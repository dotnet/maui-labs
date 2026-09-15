using System.Text.Json;
using System.Net.Http.Json;
using AIChat.ClientServer.Sample.AgentServer;
using AIChat.ClientServer.Sample.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace AIChat.ClientServer.Sample.Tests;

public sealed class SampleContractTests
{
    [Fact]
    public void Catalog_ContainsAllProtectedScenarioRoutes()
    {
        Assert.Equal(11, ScenarioIds.All.Count);
        Assert.Contains(ScenarioIds.All, descriptor => descriptor.Id == ScenarioIds.SelectiveApproval);
    }

    [Fact]
    public void ApiKey_ValidatesBearerValueWithoutDisclosingExpectedKey()
    {
        Assert.True(ApiKeyAuthenticationHandler.IsValid("secret", "secret"));
        Assert.False(ApiKeyAuthenticationHandler.IsValid("secret", "wrong"));
        Assert.False(ApiKeyAuthenticationHandler.IsValid("secret", null));
    }

    [Fact]
    public void SharedDtos_RoundTripThroughGeneratedSerializerContext()
    {
        var plan = new Plan { Steps = [new PlanStep { Description = "Research" }] };
        var json = JsonSerializer.Serialize(plan, SampleSerializerContext.Default.Plan);
        var result = JsonSerializer.Deserialize(json, SampleSerializerContext.Default.Plan);
        Assert.Equal("Research", Assert.Single(result!.Steps).Description);
    }

    [Fact]
    public void PlanDelta_UsesJsonPatchShapeExpectedByAgui()
    {
        var delta = new JsonPatchOperation { Op = "replace", Path = "/steps/0/status", Value = "completed" };
        var json = JsonSerializer.Serialize(delta, SampleSerializerContext.Default.JsonPatchOperation);
        Assert.Contains("\"op\":\"replace\"", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"/steps/0/status\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientToolDeclarations_ExposeGenerativeAndProposalTools()
    {
        Assert.Contains(ClientToolDeclarations.All, tool => tool.Name == "render_plan");
        Assert.Contains(ClientToolDeclarations.All, tool => tool.Name == "propose_document");
    }

    [Fact]
    public void FoundryOptions_RequireEndpointForLiveExecution()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Assert.Throws<InvalidOperationException>(() => Foundry.ReadOptions(configuration));
    }

    [Fact]
    public void Host_ProtectsEveryScenarioAndMapsPlanStateEvents()
    {
        using var app = SampleServerHost.Build(
            ["--AI:Endpoint=https://example.openai.azure.com/", "--AGUI_API_KEY=test-key"],
            reasoningClientFactory: Foundry.CreateChatClient);
        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToList();

        foreach (var scenario in ScenarioIds.All)
        {
            var route = Assert.Single(routes, endpoint => endpoint.RoutePattern.RawText == "/" + scenario.Id);
            Assert.NotEmpty(route.Metadata.GetOrderedMetadata<IAuthorizeData>());
        }

        var plan = Assert.Single(routes, endpoint => endpoint.RoutePattern.RawText == "/agentic_generative_ui");
        Assert.NotNull(plan.Metadata.GetMetadata<AGUI.Server.AGUIStreamOptions>());
        var predictive = Assert.Single(
            routes,
            endpoint => endpoint.RoutePattern.RawText == "/predictive_state");
        Assert.NotNull(
            predictive.Metadata.GetMetadata<AGUI.Server.AGUIStreamOptions>());
    }

    [Fact]
    public void Host_RejectsLiveConfigurationWithoutApiKey()
    {
        Assert.Throws<InvalidOperationException>(() => SampleServerHost.Build(
            ["--AI:Endpoint=https://example.openai.azure.com/"]));
    }

    [Fact]
    public void Host_ReplayConfiguration_DoesNotRequireAzureEndpoint()
    {
        using var app = SampleServerHost.Build(
            ["--AI:Replay=true", "--AGUI_API_KEY=test-key"]);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        Assert.Contains(routes, endpoint => endpoint.RoutePattern.RawText == "/agentic_chat");
    }

    [Fact]
    public void Host_ReplayConfiguration_StillRequiresApiKey()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SampleServerHost.Build(["--AI:Replay=true"]));
    }

    [Fact]
    public async Task AgenticChat_RejectsMissingBearerKeyAndAcceptsValidBearerKey()
    {
        await using var app = SampleServerHost.Build(
            ["--AI:Replay=true", "--AGUI_API_KEY=test-key", "--urls=http://127.0.0.1:0"]);
        await app.StartAsync();

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

            using var unauthorized = await client.PostAsync("/agentic_chat", content: null);
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/agentic_chat")
            {
                Content = JsonContent.Create(new
                {
                    threadId = "thread-1",
                    runId = "run-1",
                    messages = new[]
                    {
                        new { id = "message-1", role = "user", content = "Hello" }
                    }
                })
            };
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-key");
            using var authorized = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(System.Net.HttpStatusCode.OK, authorized.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
