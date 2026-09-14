var builder = DistributedApplication.CreateBuilder(args);

var endpoint = builder.AddParameter(
    "ai-endpoint",
    value: builder.Configuration["AI:Endpoint"] ?? builder.Configuration["Parameters:ai-endpoint"] ?? string.Empty,
    secret: true);
var model = builder.AddParameter("ai-model", builder.Configuration["Parameters:ai-model"] ?? "gpt-5.4-mini");
var apiKey = builder.AddParameter("agui-api-key", secret: true);

builder.AddProject<Projects.AIChat_ClientServer_Sample_AgentServer>("agentserver")
    .WithHttpEndpoint(port: 5018, targetPort: 5018, name: "agui", isProxied: false)
    .WithEnvironment("ASPNETCORE_URLS", "http://+:5018")
    .WithEnvironment("AI__Endpoint", endpoint)
    .WithEnvironment("AI__Model", model)
    .WithEnvironment("AGUI_API_KEY", apiKey);

builder.Build().Run();
