using Azure.AI.OpenAI;
using Azure.Identity;
using System.ClientModel;
using Microsoft.Extensions.AI;

namespace AIChat.ClientServer.Sample.AgentServer;

public sealed class FoundryOptions
{
    public const string DefaultModel = "gpt-5.4-mini";
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public string Model { get; set; } = DefaultModel;
}

public static class Foundry
{
    public static FoundryOptions ReadOptions(IConfiguration configuration)
    {
        var options = new FoundryOptions();
        configuration.GetSection("AI").Bind(options);
        options.Endpoint = configuration["AI:Endpoint"] ?? configuration["AI_ENDPOINT"] ?? options.Endpoint;
        options.ApiKey = configuration["AI:ApiKey"] ?? configuration["AI_APIKEY"] ?? options.ApiKey;
        options.Model = configuration["AI:Model"] ?? configuration["AI_MODEL"] ?? options.Model;
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new InvalidOperationException("AI:Endpoint must be configured for live agent execution.");
        return options;
    }

    public static IChatClient CreateChatClient(FoundryOptions options) =>
        CreateAzureClient(options)
            .GetChatClient(options.Model)
            .AsIChatClient();

    public static IChatClient CreateReasoningChatClient(FoundryOptions options) =>
        CreateAzureClient(options)
            .GetResponsesClient().AsIChatClient(options.Model);

    public static IChatClient CreateReplayChatClient() => new ReplayChatClient();

    private static AzureOpenAIClient CreateAzureClient(FoundryOptions options) =>
        string.IsNullOrWhiteSpace(options.ApiKey)
            ? new AzureOpenAIClient(new Uri(options.Endpoint!, UriKind.Absolute), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(options.Endpoint!, UriKind.Absolute), new ApiKeyCredential(options.ApiKey));
}
