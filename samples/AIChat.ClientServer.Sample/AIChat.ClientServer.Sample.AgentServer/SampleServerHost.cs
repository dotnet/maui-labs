using System.Runtime.CompilerServices;
using System.Text.Json;
using AGUI.Abstractions;
using AIChat.ClientServer.Sample.Shared;
using AGUI.Server;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
        {
            var resolvers = options.SerializerOptions.TypeInfoResolverChain;
            resolvers.Add(AgentAbstractionsJsonUtilities.DefaultOptions.TypeInfoResolver!);
            resolvers.Add(AGUIJsonSerializerContext.Default.Options.TypeInfoResolver!);
            resolvers.Add(SampleSerializerContext.Default);
        });
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
            var endpoint = MapAgentEndpoint(app, route, catalog.Create(scenario.Id))
                .RequireAuthorization();
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

    // Keep the sample on the public AGUI.Server pipeline instead of the preview ASP.NET host adapter.
    private static IEndpointConventionBuilder MapAgentEndpoint(
        WebApplication app,
        string route,
        AIAgent agent)
    {
        var hostAgent = new AIHostAgent(agent, new NoopAgentSessionStore());
        return app.MapPost(route, async (
            [FromBody] RunAgentInput? input,
            [FromServices] IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (input is null)
                return Results.BadRequest();

            var streamOptions = context.GetEndpoint()?.Metadata.GetMetadata<AGUIStreamOptions>();
            var request = input.ToChatRequestContext(
                jsonOptions.Value.SerializerOptions,
                streamOptions);
            var threadId = string.IsNullOrWhiteSpace(request.Input.ThreadId)
                ? Guid.NewGuid().ToString("N")
                : request.Input.ThreadId;
            request.Input.ThreadId = threadId;
            var session = await hostAgent.GetOrCreateSessionAsync(
                threadId,
                cancellationToken).ConfigureAwait(false);
            var events = hostAgent
                .RunStreamingAsync(
                    request.Messages,
                    session,
                    new ChatClientAgentRunOptions { ChatOptions = request.ChatOptions },
                    cancellationToken)
                .AsChatResponseUpdatesAsync()
                .AsAGUIEventStreamAsync(request, cancellationToken);

            return TypedResults.ServerSentEvents(
                SaveSessionAfterStreamingAsync(
                    events,
                    hostAgent,
                    threadId,
                    session,
                    cancellationToken));
        });
    }

    private static async IAsyncEnumerable<BaseEvent> SaveSessionAfterStreamingAsync(
        IAsyncEnumerable<BaseEvent> events,
        AIHostAgent hostAgent,
        string threadId,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in events.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return item;

        await hostAgent.SaveSessionAsync(
            threadId,
            session,
            cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<BaseEvent> MapDocumentProposal(
        FunctionCallContent call)
    {
        if (call.Arguments is null ||
            !(call.Arguments.TryGetValue("proposal", out var value)
                || call.Arguments.TryGetValue("document", out value)))
            return [];

        var proposal = value switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("document", out _) =>
                element.Deserialize(SampleSerializerContext.Default.DocumentProposal),
            JsonElement element => new DocumentProposal
                {
                    Document = element.Deserialize(SampleSerializerContext.Default.DocumentState) ?? new DocumentState(),
                },
            DocumentState document => new DocumentProposal { Document = document },
            DocumentProposal existing => existing,
            _ => throw new InvalidOperationException(
                $"Unsupported document proposal payload type '{value?.GetType().FullName ?? "null"}'."),
        };

        if (proposal is null)
            return [];

        return [new StateSnapshotEvent
        {
            Snapshot = JsonSerializer.SerializeToElement(
                new DocumentProposalSnapshot { Proposal = proposal },
                SampleSerializerContext.Default.DocumentProposalSnapshot),
        }];
    }
}
