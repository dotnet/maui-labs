using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed record AzureOpenAIAdviceConfiguration
{
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string DeploymentName { get; init; } = "gpt-4.1-mini";
    public string ApiVersion { get; init; } = "2024-10-21";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(DeploymentName) &&
        !string.IsNullOrWhiteSpace(ApiVersion);

    public static AzureOpenAIAdviceConfiguration FromEnvironment() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_ENDPOINT"),
        ApiKey = Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_API_KEY"),
        DeploymentName =
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_DEPLOYMENT")
            ?? "gpt-4.1-mini",
        ApiVersion =
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_API_VERSION")
            ?? "2024-10-21"
    };
}

public sealed partial class AzureOpenAIAdviceAdapter : IAIAdviceAdapter
{
    private const string SourceName = "via Azure OpenAI";

    private readonly HttpClient _httpClient;
    private readonly AzureOpenAIAdviceConfiguration _configuration;

    public AzureOpenAIAdviceAdapter(
        HttpClient httpClient,
        AzureOpenAIAdviceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public bool IsConfigured => _configuration.IsConfigured;

    public async Task<AIAdviceResponseDto> GetShotAdviceAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var prompt = AIPromptBuilder.BuildPrompt(request);
        var content = await CompleteAsync(
            AIPromptBuilder.BuildAdviceSystemPrompt(request.CurrentShot.BrewMethod),
            prompt,
            structuredJson: true,
            cancellationToken);
        var payload = Deserialize(content, JsonContext.Default.ShotAdvicePayload);
        return new AIAdviceResponseDto
        {
            Success = payload.Adjustments.Count > 0,
            Adjustments = payload.Adjustments,
            Reasoning = payload.Reasoning,
            Source = SourceName,
            PromptSent = prompt,
            HistoricalShotsCount = request.HistoricalShots.Count,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<string?> GetPassiveInsightAsync(
        AIAdviceRequestDto request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var content = await CompleteAsync(
            AIPromptBuilder.BuildPassiveAdviceSystemPrompt(request.CurrentShot.BrewMethod),
            AIPromptBuilder.BuildPassivePrompt(request),
            structuredJson: false,
            cancellationToken);
        return string.IsNullOrWhiteSpace(content) ? null : content.Trim();
    }

    public async Task<AIRecommendationDto> GetBeanRecommendationAsync(
        BeanRecommendationContextDto context,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var content = await CompleteAsync(
            "You are an expert coffee brewing coach. Return JSON with numeric dose, output, " +
            "and duration fields, a grindSetting string, and optional confidence string.",
            JsonSerializer.Serialize(context, JsonContext.Default.BeanRecommendationContextDto),
            structuredJson: true,
            cancellationToken);
        var payload = Deserialize(content, JsonContext.Default.BeanRecommendationPayload);
        return new AIRecommendationDto
        {
            Success =
                payload.Dose > 0 &&
                payload.Output > 0 &&
                payload.Duration > 0 &&
                !string.IsNullOrWhiteSpace(payload.GrindSetting),
            Dose = payload.Dose,
            GrindSetting = payload.GrindSetting ?? string.Empty,
            Output = payload.Output,
            Duration = payload.Duration,
            RecommendationType = context.HasHistory
                ? RecommendationType.ReturningBean
                : RecommendationType.NewBean,
            Confidence = payload.Confidence,
            Source = SourceName
        };
    }

    private async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        bool structuredJson,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(_configuration.Endpoint!.TrimEnd('/') + "/");
        var deployment = Uri.EscapeDataString(_configuration.DeploymentName);
        var apiVersion = Uri.EscapeDataString(_configuration.ApiVersion);
        var requestUri = new Uri(
            endpoint,
            $"openai/deployments/{deployment}/chat/completions?api-version={apiVersion}");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_configuration.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.TryAddWithoutValidation("api-key", _configuration.ApiKey);
        request.Content = new StringContent(
            SerializeChatRequest(systemPrompt, userPrompt, structuredJson),
            Encoding.UTF8,
            "application/json");
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new AIProviderRateLimitException();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure OpenAI returned HTTP {(int)response.StatusCode}.",
                inner: null,
                response.StatusCode);
        }

        var responseJson = await response.Content.ReadAsStringAsync(timeout.Token);
        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            string.IsNullOrWhiteSpace(content.GetString()))
        {
            throw new InvalidDataException("Azure OpenAI returned no message content.");
        }

        return content.GetString()!;
    }

    private static T Deserialize<T>(string content, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(content, typeInfo)
                ?? throw new InvalidDataException("AI response was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("AI response was not valid JSON.", exception);
        }
    }

    private static string SerializeChatRequest(
        string systemPrompt,
        string userPrompt,
        bool structuredJson)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            WriteMessage(writer, "system", systemPrompt);
            WriteMessage(writer, "user", userPrompt);
            writer.WriteEndArray();
            writer.WriteNumber("temperature", 0.2);
            if (structuredJson)
            {
                writer.WritePropertyName("response_format");
                writer.WriteStartObject();
                writer.WriteString("type", "json_object");
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Azure OpenAI is not configured.");
    }

    private sealed record ShotAdvicePayload
    {
        public List<ShotAdjustment> Adjustments { get; init; } = [];
        public string? Reasoning { get; init; }
    }

    private sealed record BeanRecommendationPayload
    {
        public decimal Dose { get; init; }
        public string? GrindSetting { get; init; }
        public decimal Output { get; init; }
        public decimal Duration { get; init; }
        public string? Confidence { get; init; }
    }

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(BeanRecommendationContextDto))]
    [JsonSerializable(typeof(ShotAdvicePayload))]
    [JsonSerializable(typeof(BeanRecommendationPayload))]
    private sealed partial class JsonContext : JsonSerializerContext;
}

public sealed class AIProviderRateLimitException()
    : Exception("The AI provider rate limit was exceeded.");
