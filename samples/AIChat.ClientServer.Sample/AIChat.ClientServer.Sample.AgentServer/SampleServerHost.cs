using AIChat.ClientServer.Sample.Shared;
using AGUI.Server;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

namespace AIChat.ClientServer.Sample.AgentServer;

public static class SampleServerHost
{
    public static WebApplication Build(
        string[] args,
        Action<WebApplicationBuilder>? configure = null,
        Func<FoundryOptions, Microsoft.Extensions.AI.IChatClient>? reasoningClientFactory = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.TypeInfoResolverChain.Add(SampleSerializerContext.Default));
        builder.Services.AddAGUIServer();
        builder.Services.AddHealthChecks();
        builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, null);
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.MapGet("/", () => Results.Ok(new { service = "AIChat AG-UI agent server", endpoints = ScenarioIds.All }))
            .AllowAnonymous();
        app.MapHealthChecks("/health").AllowAnonymous();

        var apiKey = app.Configuration["AGUI_API_KEY"] ?? app.Configuration["AGUI:ApiKey"];
        var replay = app.Configuration.GetValue<bool>("AI:Replay");
        if (!replay && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AGUI_API_KEY must be configured for live agent execution.");

        var options = replay ? null : Foundry.ReadOptions(app.Configuration);
        var chatClient = options is null
            ? Foundry.CreateReplayChatClient()
            : Foundry.CreateChatClient(options);
        var reasoningClient = options is null
            ? Foundry.CreateReplayChatClient()
            : (reasoningClientFactory ?? Foundry.CreateReasoningChatClient)(options);
        var catalog = new AgentCatalog(chatClient, reasoningClient);
        foreach (var scenario in ScenarioIds.All)
        {
            var route = "/" + scenario.Id;
            var endpoint = app.MapAGUIServer(route, catalog.Create(scenario.Id)).RequireAuthorization();
            if (scenario.Id == ScenarioIds.AgenticGenerativeUi)
                endpoint.WithMetadata(new AGUIStreamOptions()
                    .MapResultAsStateSnapshot("create_plan")
                    .MapResultAsStateDelta("update_plan_step"));
            else if (scenario.Id == ScenarioIds.SharedState)
                endpoint.WithMetadata(new AGUIStreamOptions().MapResultAsStateSnapshot("generate_recipe"));
        }
        return app;
    }
}
