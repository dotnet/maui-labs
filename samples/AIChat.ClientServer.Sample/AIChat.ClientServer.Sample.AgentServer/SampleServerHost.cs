using System.Text.Json;
using AGUI.Abstractions;
using AIChat.ClientServer.Sample.Shared;
using AGUI.Server;
using Microsoft.Extensions.AI;
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
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AGUI_API_KEY must be configured.");

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
            else if (scenario.Id == ScenarioIds.PredictiveState)
                endpoint.WithMetadata(new AGUIStreamOptions().MapCall(
                    "propose_document",
                    MapDocumentProposal));
        }
        return app;
    }

    private static IEnumerable<BaseEvent> MapDocumentProposal(
        FunctionCallContent call)
    {
        if (call.Arguments?.TryGetValue("document", out var value) != true)
            return [];

        var snapshot = value switch
        {
            JsonElement element => element,
            DocumentState document => JsonSerializer.SerializeToElement(
                document,
                SampleSerializerContext.Default.DocumentState),
            DocumentProposal proposal => JsonSerializer.SerializeToElement(
                proposal.Document,
                SampleSerializerContext.Default.DocumentState),
            _ => JsonSerializer.SerializeToElement(
                value,
                SampleSerializerContext.Default.Options),
        };

        return [new StateSnapshotEvent { Snapshot = snapshot }];
    }
}
