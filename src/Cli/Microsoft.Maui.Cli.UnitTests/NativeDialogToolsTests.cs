using System.Collections.Concurrent;
using System.Net;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class NativeDialogToolsTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Respond_ThroughMcpTransport_RequiresHostApprovalDespiteCallerFlag(
        bool supportsConfirmation, bool approve)
    {
        await using var agent = await DialogAgent.StartAsync();
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var input = toServer.Reader.AsStream();
        using var output = toClient.Writer.AsStream();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(CreateSession(agent.Port));
        services.AddMcpServer().WithStreamServerTransport(input, output).WithTools<NativeDialogTools>();
        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<McpServer>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var serverTask = server.RunAsync(timeout.Token);
        var confirmationRequests = 0;
        try
        {
            var options = new McpClientOptions
            {
                Capabilities = new ClientCapabilities
                {
                    Elicitation = supportsConfirmation ? new ElicitationCapability { Form = new() } : null
                },
                Handlers = new McpClientHandlers
                {
                    ElicitationHandler = supportsConfirmation ? async (request, token) =>
                    {
                        Assert.NotNull(request);
                        confirmationRequests++;
                        Assert.Contains("Allow", request.Message);
                        Assert.Contains("Test permission", request.Message);
                        Assert.Contains("TestApp", request.Message);
                        if (!approve)
                            return new ElicitResult { Action = "decline" };
                        return await Approve(request, token);
                    } : null
                }
            };
            await using var client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()),
                options, cancellationToken: timeout.Token);
            var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
            var responseTool = Assert.Single(tools, tool => tool.Name == "maui_native_dialog_respond");
            var properties = responseTool.JsonSchema.GetProperty("properties");
            Assert.False(properties.TryGetProperty("server", out _));
            Assert.False(properties.TryGetProperty("confirmedByUser", out _));

            var detection = await client.CallToolAsync(
                "maui_native_dialog_detect", cancellationToken: timeout.Token);
            using var detected = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(detection.Content)).Text);
            var promptId = detected.RootElement.GetProperty("promptId").GetString();
            var result = await client.CallToolAsync("maui_native_dialog_respond",
                new Dictionary<string, object?>
                {
                    ["promptId"] = promptId,
                    ["buttonLabel"] = "Allow",
                    ["confirmedByUser"] = true
                }, cancellationToken: timeout.Token);
            using var response = JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
            Assert.Equal(supportsConfirmation && approve, response.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(supportsConfirmation ? 1 : 0, confirmationRequests);
            Assert.Equal(supportsConfirmation && approve ? 1 : 0, agent.Taps.Count);
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await serverTask;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
            }
        }
    }

    [Theory]
    [InlineData("", "Allow")]
    [InlineData("reviewed-prompt", "")]
    public async Task Respond_MissingPromptOrButton_RequiresUserAction(string promptId, string buttonLabel)
    {
        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            null!, promptId, buttonLabel));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.True(response.RootElement.GetProperty("userActionRequired").GetBoolean());
    }

    [Fact]
    public async Task Respond_WithoutHostConfirmation_RequiresManualAction()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        var json = await NativeDialogTools.Respond(
            session, promptId, "Allow");
        using var document = JsonDocument.Parse(json);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.True(document.RootElement.GetProperty("userActionRequired").GetBoolean());
        Assert.Equal("host-confirmation-unavailable", document.RootElement.GetProperty("reason").GetString());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_UnknownPrompt_RejectsWithoutConnecting()
    {
        using var response = JsonDocument.Parse(await NativeDialogTools.Respond(
            null!, Guid.NewGuid().ToString("N"), "Allow"));

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
        using var response = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));
        Assert.True(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("allow-button", Assert.Single(agent.Taps));

        using var repeated = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));
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

        using var response = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));

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

        using var response = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));

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

        using var response = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));

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

        using var rejected = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            CreateSession(agent.Port), promptId, "Allow", requestConfirmation: Approve));
        Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(agent.Taps);

        using var accepted = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            owner, promptId, "Allow", requestConfirmation: Approve));
        Assert.True(accepted.RootElement.GetProperty("success").GetBoolean());
        Assert.Single(agent.Taps);
    }

    [Theory]
    [InlineData("decline", "true")]
    [InlineData("cancel", "true")]
    [InlineData("accept", "false")]
    [InlineData("accept", "\"true\"")]
    [InlineData("accept", "null")]
    public async Task Respond_HostDoesNotApprove_ConsumesTokenWithoutActing(string action, string value)
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);

        using var rejected = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: (_, _) => Task.FromResult(new ElicitResult
            {
                Action = action,
                Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.Deserialize<JsonElement>(value) }
            })));
        Assert.False(rejected.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(agent.Taps);

        using var retried = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: Approve));
        Assert.False(retried.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_DialogChangesDuringHostConfirmation_RejectsWithoutActing()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        using var result = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: (request, token) =>
            {
                Assert.Contains("TestApp", request.Message);
                Assert.Contains("Test permission", request.Message);
                Assert.Contains("Allow", request.Message);
                Assert.Contains(promptId, request.Message);
                Assert.Contains("confirm", request.RequestedSchema!.Required!);
                agent.DialogTitle = "Changed while awaiting user input";
                return Approve(request, token);
            }));

        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("changed", result.RootElement.GetProperty("instruction").GetString());
        Assert.Empty(agent.Taps);
    }

    private static Task<ElicitResult> Approve(ElicitRequestParams request, CancellationToken token)
        => Task.FromResult(new ElicitResult
        {
            Action = "accept",
            Content = new Dictionary<string, JsonElement> { ["confirm"] = JsonSerializer.SerializeToElement(true) }
        });

    [Fact]
    public async Task Respond_UnknownButton_DoesNotRequestHostApprovalOrAct()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        using var response = JsonDocument.Parse(await NativeDialogTools.RespondCore(
            session, promptId, "Invented button",
            requestConfirmation: (_, _) => throw new InvalidOperationException("Unknown buttons must not reach host confirmation.")));

        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("dialog-button-not-found-or-ambiguous", response.RootElement.GetProperty("reason").GetString());
        Assert.Empty(agent.Taps);
    }

    [Fact]
    public async Task Respond_CanceledDuringHostConfirmation_DoesNotAct()
    {
        await using var agent = await DialogAgent.StartAsync();
        var session = CreateSession(agent.Port);
        var promptId = await DetectPromptAsync(session);
        using var canceled = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NativeDialogTools.RespondCore(
            session, promptId, "Allow", requestConfirmation: async (request, token) =>
            {
                canceled.Cancel();
                await Task.Delay(Timeout.Infinite, token);
                return await Approve(request, token);
            }, cancellationToken: canceled.Token));

        Assert.Empty(agent.Taps);
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
