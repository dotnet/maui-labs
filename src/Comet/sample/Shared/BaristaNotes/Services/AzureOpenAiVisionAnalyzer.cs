#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CometBaristaNotes.Services.DTOs;

namespace CometBaristaNotes.Services;

public sealed record BaristaVisionConfiguration(
    string? Endpoint,
    string? ApiKey,
    string VisionDeployment = "gpt-4o",
    string ExtractionDeployment = "gpt-4o-mini",
    string ApiVersion = "2024-10-21")
{
    public static BaristaVisionConfiguration FromEnvironment() =>
        new(
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_ENDPOINT"),
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_API_KEY"),
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_VISION_DEPLOYMENT") ?? "gpt-4o",
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_EXTRACTION_DEPLOYMENT") ?? "gpt-4o-mini",
            Environment.GetEnvironmentVariable("BARISTANOTES_AZURE_OPENAI_API_VERSION") ?? "2024-10-21");
}

public sealed class AzureOpenAiVisionAnalyzer : IBaristaVisionAnalyzer, IVisionService
{
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    const string ClassificationPrompt = """
        Classify a photo from a coffee journal app. Return only JSON:
        {"intent":"coffee|profile|room|unknown","isObvious":true|false,"rationale":"short sentence",
         "name":null,"roaster":null,"origin":null,"roastDate":null,"notes":null}
        coffee means a coffee bag or readable coffee label; profile means one clear portrait;
        room means a group or scene intended for counting people. Use unknown and isObvious=false
        for mixed or uncertain subjects. Never guess. For coffee, include only visible label fields.
        """;

    const string ExtractionPrompt = """
        Read the coffee bag label and return only JSON:
        {"name":null,"roaster":null,"origin":null,"roastDate":null,"notes":null}
        roastDate must be YYYY-MM-DD and only when explicitly printed. Never guess.
        """;

    const string RoomPrompt = """
        Count every visible person and return only JSON:
        {"peopleCount":0,"message":"one concise helpful sentence"}
        Each person needs one cup and each cup needs approximately 18 grams of coffee.
        If people cannot be counted reliably, return {"error":"short explanation"}.
        """;

    readonly BaristaVisionConfiguration _configuration;
    readonly HttpClient _httpClient;

    public AzureOpenAiVisionAnalyzer(
        BaristaVisionConfiguration configuration,
        HttpClient? httpClient = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpClient = httpClient ?? new HttpClient();

        if (string.IsNullOrWhiteSpace(configuration.Endpoint)
            || string.IsNullOrWhiteSpace(configuration.ApiKey))
        {
            Availability = new VisionAvailability(
                VisionAvailabilityState.NotConfigured,
                "Set BARISTANOTES_AZURE_OPENAI_ENDPOINT and BARISTANOTES_AZURE_OPENAI_API_KEY.");
        }
        else if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http"))
        {
            Availability = new VisionAvailability(
                VisionAvailabilityState.NotConfigured,
                "BARISTANOTES_AZURE_OPENAI_ENDPOINT must be an absolute HTTP or HTTPS URL.");
        }
        else
        {
            Availability = new VisionAvailability(VisionAvailabilityState.Ready, null);
        }
    }

    public VisionAvailability Availability { get; }

    public static AzureOpenAiVisionAnalyzer FromEnvironment(HttpClient? httpClient = null) =>
        new(BaristaVisionConfiguration.FromEnvironment(), httpClient);

    public async Task<PhotoClassificationResult> ClassifyPhotoAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            _configuration.VisionDeployment,
            ClassificationPrompt,
            "Choose the next workflow.",
            [photo],
            cancellationToken);

        if (response.Status != VisionRequestStatus.Success)
            return new(
                response.Status,
                FailedClassification(response.ErrorMessage),
                null,
                response.ErrorMessage);

        try
        {
            using var document = JsonDocument.Parse(ExtractJson(response.Content!));
            var root = document.RootElement;
            var intent = ParseIntent(GetString(root, "intent"));
            var obvious = GetBoolean(root, "isObvious") && intent != PhotoWorkflowIntent.Unknown;
            if (!obvious)
                intent = PhotoWorkflowIntent.Unknown;

            var details = intent == PhotoWorkflowIntent.Coffee
                ? ParseBeanLabel(root)
                : null;
            return new(
                VisionRequestStatus.Success,
                new PhotoWorkflowAnalysis(
                    true,
                    intent,
                    obvious,
                    GetString(root, "rationale"),
                    null),
                details,
                null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            var message = $"Vision classification returned invalid JSON: {ex.Message}";
            return new(VisionRequestStatus.Error, FailedClassification(message), null, message);
        }
    }

    public async Task<BeanLabelResult> ExtractBeanLabelAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            _configuration.ExtractionDeployment,
            ExtractionPrompt,
            "Extract the visible coffee label fields.",
            [photo],
            cancellationToken);
        if (response.Status != VisionRequestStatus.Success)
            return new(response.Status, null, response.ErrorMessage);

        try
        {
            using var document = JsonDocument.Parse(ExtractJson(response.Content!));
            return new(
                VisionRequestStatus.Success,
                ParseBeanLabel(document.RootElement),
                null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new(
                VisionRequestStatus.Error,
                null,
                $"Bean label extraction returned invalid JSON: {ex.Message}");
        }
    }

    public async Task<RoomAnalysisResult> AnalyzeRoomAsync(
        PhotoAsset photo,
        CancellationToken cancellationToken = default)
    {
        var response = await RequestAsync(
            _configuration.VisionDeployment,
            RoomPrompt,
            "Count the people and calculate the coffee required.",
            [photo],
            cancellationToken);
        if (response.Status != VisionRequestStatus.Success)
            return new(response.Status, null, response.ErrorMessage);

        try
        {
            using var document = JsonDocument.Parse(ExtractJson(response.Content!));
            var root = document.RootElement;
            if (GetString(root, "error") is { } modelError)
                return new(VisionRequestStatus.Error, null, modelError);
            if (!root.TryGetProperty("peopleCount", out var countElement)
                || !countElement.TryGetInt32(out var peopleCount)
                || peopleCount < 0)
            {
                return new(VisionRequestStatus.Error, null, "Vision response did not contain a valid peopleCount.");
            }

            var message = GetString(root, "message")
                ?? $"I see {peopleCount} {(peopleCount == 1 ? "person" : "people")}. "
                + $"Plan for {peopleCount} {(peopleCount == 1 ? "cup" : "cups")} and {peopleCount * 18}g of beans.";
            return new(
                VisionRequestStatus.Success,
                new VisionAnalysisResult(true, peopleCount, peopleCount, peopleCount * 18, message, null),
                null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new(
                VisionRequestStatus.Error,
                null,
                $"Room analysis returned invalid JSON: {ex.Message}");
        }
    }

    public async Task<ProfileMatchResult> MatchProfileAsync(
        PhotoAsset photo,
        IReadOnlyList<PersonIdentificationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0)
        {
            var noCandidates = new PersonIdentificationResult(
                true, null, null, "No candidate avatars supplied.", null);
            return new(VisionRequestStatus.Success, noCandidates, null);
        }

        var images = new List<PhotoAsset> { photo };
        images.AddRange(candidates.Select(candidate =>
            new PhotoAsset(candidate.AvatarBytes, "image/jpeg", candidate.Name, PhotoSource.Gallery)));
        var labels = string.Join(
            ", ",
            candidates.Select((candidate, index) => $"image {index + 2}=ID {candidate.ProfileId} ({candidate.Name})"));
        var prompt = """
            The first image is a target portrait. Remaining images are candidate profile avatars.
            Return only JSON: {"matchedId":number|null,"rationale":"short sentence"}.
            Match only when clearly the same person; otherwise matchedId must be null. Never guess.
            """;
        var response = await RequestAsync(
            _configuration.VisionDeployment,
            prompt,
            $"Candidate mapping: {labels}",
            images,
            cancellationToken);
        if (response.Status != VisionRequestStatus.Success)
            return new(response.Status, null, response.ErrorMessage);

        try
        {
            using var document = JsonDocument.Parse(ExtractJson(response.Content!));
            var root = document.RootElement;
            var rationale = GetString(root, "rationale");
            if (!root.TryGetProperty("matchedId", out var idElement)
                || idElement.ValueKind == JsonValueKind.Null)
            {
                return new(
                    VisionRequestStatus.Success,
                    new PersonIdentificationResult(true, null, null, rationale, null),
                    null);
            }

            if (!idElement.TryGetInt32(out var id))
                return new(VisionRequestStatus.Error, null, "Profile match returned an invalid matchedId.");
            var candidate = candidates.FirstOrDefault(item => item.ProfileId == id);
            if (candidate is null)
                return new(VisionRequestStatus.Error, null, "Profile match returned an unknown candidate ID.");
            return new(
                VisionRequestStatus.Success,
                new PersonIdentificationResult(true, id, candidate.Name, rationale, null),
                null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new(
                VisionRequestStatus.Error,
                null,
                $"Profile match returned invalid JSON: {ex.Message}");
        }
    }

    public async Task<VisionAnalysisResult> AnalyzeImageAsync(
        Stream imageStream,
        string userQuestion,
        CancellationToken cancellationToken = default)
    {
        var photo = await ReadPhotoAsync(imageStream, cancellationToken);
        var result = await AnalyzeRoomAsync(photo, cancellationToken);
        return result.Analysis
            ?? new VisionAnalysisResult(false, 0, 0, 0, null, result.ErrorMessage);
    }

    public async Task<BeanLabelExtraction> ExtractBeanLabelAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        var photo = await ReadPhotoAsync(imageStream, cancellationToken);
        var result = await ExtractBeanLabelAsync(photo, cancellationToken);
        return result.Extraction
            ?? new BeanLabelExtraction { Success = false, ErrorMessage = result.ErrorMessage };
    }

    public async Task<PhotoWorkflowAnalysis> ClassifyPhotoAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default)
    {
        var photo = await ReadPhotoAsync(imageStream, cancellationToken);
        var result = await ClassifyPhotoAsync(photo, cancellationToken);
        return result.Analysis;
    }

    public async Task<PersonIdentificationResult> IdentifyPersonFromPhotoAsync(
        byte[] targetPhoto,
        IReadOnlyList<PersonIdentificationCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        var result = await MatchProfileAsync(
            new PhotoAsset(targetPhoto, "image/jpeg", null, PhotoSource.Camera),
            candidates,
            cancellationToken);
        return result.Match
            ?? new PersonIdentificationResult(false, null, null, null, result.ErrorMessage);
    }

    public Task<bool> IsAvailableAsync() => Task.FromResult(Availability.IsAvailable);

    async Task<RawVisionResponse> RequestAsync(
        string deployment,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<PhotoAsset> images,
        CancellationToken cancellationToken)
    {
        if (!Availability.IsAvailable)
            return new(VisionRequestStatus.Unavailable, null, Availability.Reason);
        if (images.Count == 0 || images.Any(image => image.Bytes.Length == 0))
            return new(VisionRequestStatus.Error, null, "At least one non-empty image is required.");
        if (images.Any(image => image.Bytes.Length > BaristaPhotoNormalization.MaximumUploadBytes))
            return new(VisionRequestStatus.Error, null, "An image exceeds the 12 MB vision request limit.");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildRequestUri(deployment));
        request.Headers.Add("api-key", _configuration.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        request.Content = new StringContent(
            SerializeRequest(systemPrompt, userPrompt, images),
            Encoding.UTF8,
            "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new(
                    VisionRequestStatus.Error,
                    null,
                    $"Vision request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            using var responseDocument = JsonDocument.Parse(responseText);
            var root = responseDocument.RootElement;
            var text = root.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(text)
                ? new(VisionRequestStatus.Error, null, "Vision service returned an empty response.")
                : new(VisionRequestStatus.Success, text, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(VisionRequestStatus.Cancelled, null, "Vision request was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return new(VisionRequestStatus.Error, null, "Vision request timed out after 30 seconds.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            return new(VisionRequestStatus.Error, null, $"Vision request failed: {ex.Message}");
        }
    }

    static string SerializeRequest(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<PhotoAsset> images)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", systemPrompt);
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", userPrompt);
            writer.WriteEndObject();
            foreach (var image in images)
            {
                writer.WriteStartObject();
                writer.WriteString("type", "image_url");
                writer.WritePropertyName("image_url");
                writer.WriteStartObject();
                writer.WriteString(
                    "url",
                    $"data:{NormalizeContentType(image.ContentType)};base64,{Convert.ToBase64String(image.Bytes)}");
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_object");
            writer.WriteEndObject();
            writer.WriteNumber("temperature", 0);
            writer.WriteNumber("max_tokens", 600);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    Uri BuildRequestUri(string deployment)
    {
        var endpoint = _configuration.Endpoint!.TrimEnd('/');
        return new Uri(
            $"{endpoint}/openai/deployments/{Uri.EscapeDataString(deployment)}/chat/completions"
            + $"?api-version={Uri.EscapeDataString(_configuration.ApiVersion)}");
    }

    static async Task<PhotoAsset> ReadPhotoAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        if (stream.CanSeek)
            stream.Position = 0;
        var block = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + read > BaristaPhotoNormalization.MaximumUploadBytes)
                throw new IOException("The image exceeds the 12 MB vision request limit.");
            await buffer.WriteAsync(block, 0, read, cancellationToken);
        }
        return new PhotoAsset(buffer.ToArray(), "image/jpeg", null, PhotoSource.Gallery);
    }

    static BeanLabelExtraction ParseBeanLabel(JsonElement root) =>
        new()
        {
            Success = true,
            Name = GetString(root, "name"),
            Roaster = GetString(root, "roaster"),
            Origin = GetString(root, "origin"),
            RoastDate = GetDate(root, "roastDate"),
            Notes = GetString(root, "notes"),
        };

    static PhotoWorkflowIntent ParseIntent(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "coffee" => PhotoWorkflowIntent.Coffee,
            "profile" => PhotoWorkflowIntent.Profile,
            "room" => PhotoWorkflowIntent.Room,
            _ => PhotoWorkflowIntent.Unknown,
        };

    static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
            return null;
        var value = property.GetString()?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    static bool GetBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False
        && property.GetBoolean();

    static DateTime? GetDate(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var date)
            ? date.Date
            : null;
    }

    static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            throw new JsonException("No JSON object was present.");
        return text.Substring(start, end - start + 1);
    }

    static string NormalizeContentType(string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? contentType
            : "image/jpeg";

    static PhotoWorkflowAnalysis FailedClassification(string? message) =>
        new(false, PhotoWorkflowIntent.Unknown, false, null, message);

    sealed record RawVisionResponse(
        VisionRequestStatus Status,
        string? Content,
        string? ErrorMessage);
}
