using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);
var sharedAiSecretsPath = OperatingSystem.IsWindows()
    ? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Microsoft",
        "UserSecrets",
        "ai-attributes-secrets",
        "secrets.json")
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".microsoft",
        "usersecrets",
        "ai-attributes-secrets",
        "secrets.json");
var sharedAiSecrets = new ConfigurationBuilder()
    .AddJsonFile(sharedAiSecretsPath, optional: true)
    .Build();

var endpoint = builder.AddParameter(
    "ai-endpoint",
    value: builder.Configuration["AI:Endpoint"]
        ?? sharedAiSecrets["AI:Endpoint"]
        ?? builder.Configuration["Parameters:ai-endpoint"]
        ?? string.Empty,
    secret: true);
var providerApiKey = builder.AddParameter(
    "ai-api-key",
    value: builder.Configuration["AI:ApiKey"] ?? sharedAiSecrets["AI:ApiKey"] ?? string.Empty,
    secret: true);
var model = builder.AddParameter("ai-model", builder.Configuration["Parameters:ai-model"] ?? "gpt-5.4-mini");
var replay = builder.AddParameter(
    "ai-replay",
    builder.Configuration["AI:Replay"] ?? builder.Configuration["Parameters:ai-replay"] ?? "false");
var apiKey = builder.AddParameter("agui-api-key", secret: true);

builder.AddProject<Projects.AIChat_ClientServer_Sample_AgentServer>("agentserver")
    .WithHttpEndpoint(port: 5018, targetPort: 5018, name: "agui", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://127.0.0.1:5018")
    .WithEnvironment("AI__Endpoint", endpoint)
    .WithEnvironment("AI__ApiKey", providerApiKey)
    .WithEnvironment("AI__Model", model)
    .WithEnvironment("AI__Replay", replay)
    .WithEnvironment("AGUI_API_KEY", apiKey);

builder.Build().Run();
