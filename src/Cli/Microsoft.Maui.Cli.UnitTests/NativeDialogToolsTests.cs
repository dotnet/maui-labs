using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class NativeDialogToolsTests
{
    [Theory]
    [InlineData("", "Allow")]
    [InlineData("reviewed-prompt", "")]
    public async Task Respond_MissingPromptOrButton_RequiresUserAction(string promptId, string buttonLabel)
    {
        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            null!, promptId, buttonLabel, confirmedByUser: true));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.True(response.RootElement.GetProperty("userActionRequired").GetBoolean());
    }

    [Fact]
    public async Task Respond_WithoutUserConfirmation_RequiresUserAction()
    {
        var json = await NativeDialogTools.Respond(
            null!,
            "reviewed-prompt",
            "Allow",
            confirmedByUser: false);
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("userActionRequired").GetBoolean());
        Assert.Contains(
            "confirm",
            document.RootElement.GetProperty("instruction").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Respond_UnknownPrompt_RejectsWithoutConnecting()
    {
        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            null!, Guid.NewGuid().ToString("N"), "Allow", confirmedByUser: true));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("expired", response.RootElement.GetProperty("instruction").GetString());
    }

    [Fact]
    public async Task Respond_ConfirmedLinuxDialog_PreservesMutationLeaseAndUsesTokenOnce()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        using var client = await session.GetAgentClientAsync();
        Assert.True((await client.ControlMutationLeaseAsync("claim")).YouHold);

        var promptId = await DetectPromptAsync(session);
        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("allow-button", Assert.Single(agent.Taps));

        using var repeated = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));
        Assert.False(repeated.RootElement.GetProperty("success").GetBoolean());
        Assert.Single(agent.Taps);
    }

    [Fact]
    public async Task Respond_ChangedDialog_RejectsWithoutTapping()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        agent.DialogTitle = "A different permission";

        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("changed", response.RootElement.GetProperty("instruction").GetString());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_AppControlledByAnotherClient_RejectsWithoutTapping()
    {
        await using var agent = await DialogAgent.StartAsync();
        using var otherClient = new AgentClient("127.0.0.1", agent.Port);
        Assert.True((await otherClient.ControlMutationLeaseAsync("claim")).YouHold);
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);

        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "mutation-lease-held-by-other",
            response.RootElement.GetProperty("reason").GetString());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_ChangedTarget_RejectsWithoutTapping()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        agent.ProcessId++;

        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("app changed", response.RootElement.GetProperty("instruction").GetString());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_DifferentMcpSession_CannotRedeemAnotherSessionsToken()
    {
        await using var agent = await DialogAgent.StartAsync();
        var owner = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(owner);

        using var rejected = JsonDocument.Parse(await NativeDialogTools.Respond(
            CreateSession(agent.Port), promptId, "Allow", confirmedByUser: true));
        Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(agent.Taps);

        using var accepted = JsonDocument.Parse(await NativeDialogTools.Respond(
            owner, promptId, "Allow", confirmedByUser: true));
        Assert.True(accepted.RootElement.GetProperty("success").GetBoolean());
        Assert.Single(agent.Taps);
    }

    [Fact]
    public async Task Respond_WithoutConfirmation_DoesNotConsumeReviewedToken()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);

        using var rejected = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: false));
        Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(agent.Taps);

        using var accepted = JsonDocument.Parse(await NativeDialogTools.Respond(
            session, promptId, "Allow", confirmedByUser: true));
        Assert.True(accepted.RootElement.GetProperty("success").GetBoolean());
        Assert.Single(agent.Taps);
    }

    private static McpAgentSession CreateSession(int port)
        => new() { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = port };

    private static async Task<string> DetectPromptAsync(McpAgentSession session)
    {
        using var response = JsonDocument.Parse(await NativeDialogTools.Detect(session));
        Assert.True(response.RootElement.GetProperty("detected").GetBoolean());
        return response.RootElement.GetProperty("promptId").GetString()!;
    }

    private sealed class DialogAgent(WebApplication app) : IAsyncDisposable
    {
        public int Port => new Uri(app.Urls.Single()).Port;
        public int ProcessId { get; set; } = 42;
        public string DialogTitle { get; set; } = "Test permission";
        public ConcurrentQueue<string> Taps { get; } = new();
        private string? _leaseId;

        public static async Task<DialogAgent> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Logging.ClearProviders();
            var app = builder.Build();
            var agent = new DialogAgent(app);
            app.MapGet("/api/v1/agent/status", () => Results.Json(new
            {
                device = new { platform = "Linux" },
                app = new { name = "TestApp", processId = agent.ProcessId }
            }));
            app.MapGet("/api/v1/ui/tree", () => Results.Json(new[]
            {
                new
                {
                    id = "permission-dialog",
                    type = "AlertDialog",
                    text = agent.DialogTitle,
                    children = new[] { new { id = "allow-button", type = "Button", text = "Allow" } }
                }
            }));
            app.MapPost("/api/v1/agent/lease", async (HttpContext context) =>
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                var requestedId = body.RootElement.GetProperty("leaseId").GetString();
                agent._leaseId ??= requestedId;
                return Results.Json(new { ok = true, youHold = agent._leaseId == requestedId });
            });
            app.MapPost("/api/v1/ui/actions/tap", async (HttpContext context) =>
            {
                using var body = await JsonDocument.ParseAsync(context.Request.Body);
                agent.Taps.Enqueue(body.RootElement.GetProperty("elementId").GetString()!);
                return Results.Json(new { success = true });
            });

            await app.StartAsync();
            return agent;
        }

        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
