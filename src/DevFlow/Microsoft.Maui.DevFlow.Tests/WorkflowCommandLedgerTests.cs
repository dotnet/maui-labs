using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class WorkflowCommandLedgerTests
{
    [Fact]
    public async Task BeginCommandEnd_HappyPath_StoresCompletedReceipt()
    {
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);

        var prepared = await ledger.PrepareAsync(CommandRequest(sequence: 1), CancellationToken.None);
        var reservation = Assert.IsType<WorkflowCommandReservation>(prepared.Reservation);
        var response = ledger.Complete(reservation, HttpResponse.Json(new { success = true }));

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("\"success\"", response.Body!, StringComparison.Ordinal);

        var ended = ledger.Control(Control("end"));
        Assert.True(ended.Ok);
        Assert.Equal("completed", ended.State);
        Assert.Equal(1, ended.Commands);

        var duplicateAfterEnd = await ledger.PrepareAsync(CommandRequest(sequence: 1), CancellationToken.None);
        Assert.Equal(200, duplicateAfterEnd.Response!.StatusCode);
    }

    [Fact]
    public async Task CommandSequence_RejectsGapRegressionAndStaleEpoch()
    {
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);

        var gap = await ledger.PrepareAsync(CommandRequest(sequence: 2), CancellationToken.None);
        Assert.Equal("workflow-sequence-gap", Reason(gap.Response));

        var first = await ledger.PrepareAsync(CommandRequest(sequence: 1), CancellationToken.None);
        ledger.Complete(Assert.IsType<WorkflowCommandReservation>(first.Reservation), HttpResponse.Ok());

        var regression = await ledger.PrepareAsync(CommandRequest(sequence: 1, commandId: "other"), CancellationToken.None);
        Assert.Equal("workflow-sequence-regression", Reason(regression.Response));

        var staleEpoch = await ledger.PrepareAsync(CommandRequest(sequence: 2, epoch: 2), CancellationToken.None);
        Assert.Equal("workflow-stale-epoch", Reason(staleEpoch.Response));
    }

    [Fact]
    public async Task DuplicateCommand_UsesCachedReceipt_WithoutSecondDispatch()
    {
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);

        var initialRequest = CommandRequest(sequence: 1);
        var initial = await ledger.PrepareAsync(initialRequest, CancellationToken.None);
        ledger.Complete(Assert.IsType<WorkflowCommandReservation>(initial.Reservation), HttpResponse.Json(new { success = true }));

        var duplicate = await ledger.PrepareAsync(initialRequest, CancellationToken.None);

        Assert.False(duplicate.ShouldExecute);
        Assert.NotNull(duplicate.Response);
        Assert.Equal(200, duplicate.Response!.StatusCode);
        Assert.Contains("\"success\"", duplicate.Response.Body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandEnvelope_RejectsDigestMismatchDuplicateConflictAndWrongInstance()
    {
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);

        var mismatch = CommandRequest(sequence: 1);
        mismatch.Headers[WorkflowCommandHeaders.ActionDigest] = new string('0', 64);
        var mismatchResult = await ledger.PrepareAsync(mismatch, CancellationToken.None);
        Assert.Equal("workflow-action-digest", Reason(mismatchResult.Response));

        var first = await ledger.PrepareAsync(CommandRequest(sequence: 1, commandId: "same", body: """{"value":"one"}"""), CancellationToken.None);
        ledger.Complete(Assert.IsType<WorkflowCommandReservation>(first.Reservation), HttpResponse.Ok());

        var conflict = await ledger.PrepareAsync(CommandRequest(sequence: 1, commandId: "same", body: """{"value":"two"}"""), CancellationToken.None);
        Assert.Equal("workflow-command-conflict", Reason(conflict.Response));

        var wrongInstance = await ledger.PrepareAsync(CommandRequest(sequence: 2, agentInstanceId: "other"), CancellationToken.None);
        Assert.Equal("workflow-agent-instance", Reason(wrongInstance.Response));
    }

    [Fact]
    public async Task UnknownCompletion_IsNeverRedispatched()
    {
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);

        var request = CommandRequest(sequence: 1);
        var prepared = await ledger.PrepareAsync(request, CancellationToken.None);
        ledger.MarkUnknownCompletion(Assert.IsType<WorkflowCommandReservation>(prepared.Reservation));

        var duplicate = await ledger.PrepareAsync(request, CancellationToken.None);

        Assert.False(duplicate.ShouldExecute);
        Assert.Equal("workflow-unknown-completion", Reason(duplicate.Response));
        Assert.Equal(0, ledger.ActiveLedgerCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateCommand_ExecutesHandlerOnceAndReturnsSameReceipt()
    {
        var port = GetFreePort();
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);
        using var server = new AgentHttpServer(port)
        {
            WorkflowCommandLedger = ledger,
            MutationLeaseValidator = _ => Task.FromResult(AllowedLease())
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        server.MapPost("/mutate", async _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;
            return HttpResponse.Json(new { success = true });
        });
        server.Start();

        try
        {
            using var client = new HttpClient();
            var request = CommandRequest(sequence: 1);
            var first = SendAsync(client, port, request);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = SendAsync(client, port, request);

            await Task.Delay(100);
            Assert.Equal(1, Volatile.Read(ref calls));

            release.TrySetResult();
            var firstResponse = await first;
            var secondResponse = await second;
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.TrySetResult();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task BrokerAuthorityLoss_FailsClosedOnlyWorkflowCommands()
    {
        var port = GetFreePort();
        var authority = true;
        var ledger = new WorkflowCommandLedger(() => "instance", () => authority);
        Assert.True(ledger.Control(Control("begin")).Ok);
        using var server = new AgentHttpServer(port)
        {
            WorkflowCommandLedger = ledger,
            MutationLeaseValidator = _ => Task.FromResult(AllowedLease())
        };
        var calls = 0;
        server.MapPost("/mutate", _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(HttpResponse.Ok());
        });
        server.Start();

        try
        {
            using var client = new HttpClient();
            var manual = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/mutate")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            using var manualResponse = await client.SendAsync(manual);
            Assert.Equal(HttpStatusCode.OK, manualResponse.StatusCode);

            authority = false;
            using var workflowResponse = await SendAsync(client, port, CommandRequest(sequence: 1));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, workflowResponse.StatusCode);
            Assert.Equal(1, Volatile.Read(ref calls));
            Assert.Equal(0, ledger.ActiveLedgerCount);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task LedgerCaps_RejectActiveAndCommandOverflow_AndBoundTerminalRetention()
    {
        var ledger = new WorkflowCommandLedger(
            () => "instance",
            () => true,
            new WorkflowCommandLedgerOptions
            {
                MaxActiveLedgers = 1,
                MaxCommandsPerRun = 1,
                MaxRetainedTerminalLedgers = 1,
                TerminalRetention = TimeSpan.FromDays(1),
                MaxStoredResponseBytes = 128
            });

        Assert.True(ledger.Control(Control("begin", runId: "run-one")).Ok);
        var rejectedActive = ledger.Control(Control("begin", runId: "run-two"));
        Assert.Equal(429, rejectedActive.StatusCode);
        Assert.Equal("workflow-active-capacity", rejectedActive.Reason);

        var first = await ledger.PrepareAsync(CommandRequest(sequence: 1, runId: "run-one"), CancellationToken.None);
        ledger.Complete(Assert.IsType<WorkflowCommandReservation>(first.Reservation), HttpResponse.Ok());
        var commandOverflow = await ledger.PrepareAsync(CommandRequest(sequence: 2, runId: "run-one"), CancellationToken.None);
        Assert.Equal("workflow-command-capacity", Reason(commandOverflow.Response));

        Assert.True(ledger.Control(Control("end", runId: "run-one")).Ok);
        Assert.True(ledger.Control(Control("begin", runId: "run-two")).Ok);
        Assert.True(ledger.Control(Control("end", runId: "run-two")).Ok);
        Assert.Equal(1, ledger.RetainedLedgerCount);

        var responseCapLedger = new WorkflowCommandLedger(
            () => "instance",
            () => true,
            new WorkflowCommandLedgerOptions { MaxStoredResponseBytes = 256 });
        Assert.True(responseCapLedger.Control(Control("begin", runId: "response-cap")).Ok);
        var oversizedRequest = CommandRequest(sequence: 1, runId: "response-cap");
        var oversized = await responseCapLedger.PrepareAsync(oversizedRequest, CancellationToken.None);
        var capped = responseCapLedger.Complete(
            Assert.IsType<WorkflowCommandReservation>(oversized.Reservation),
            HttpResponse.Json(new { value = new string('x', 256) }));
        Assert.Equal("workflow-response-too-large", Reason(capped));
        var cachedCap = await responseCapLedger.PrepareAsync(oversizedRequest, CancellationToken.None);
        Assert.Equal("workflow-response-too-large", Reason(cachedCap.Response));
    }

    [Fact]
    public async Task AgentClientWorkflowScope_AttachesDeterministicEnvelopeAndLeavesReadsUntouched()
    {
        var port = GetFreePort();
        var ledger = CreateLedger();
        Assert.True(ledger.Control(Control("begin")).Ok);
        using var server = new AgentHttpServer(port)
        {
            WorkflowCommandLedger = ledger,
            MutationLeaseValidator = _ => Task.FromResult(AllowedLease())
        };
        Dictionary<string, string>? mutationHeaders = null;
        Dictionary<string, string>? readHeaders = null;
        server.MapPost("/api/v1/ui/actions/tap", request =>
        {
            mutationHeaders = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(HttpResponse.Ok());
        });
        server.MapGet("/api/v1/agent/status", request =>
        {
            readHeaders = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(HttpResponse.Json(new
            {
                agent = new { name = "test", version = "1.0" },
                device = new { platform = "Test" },
                app = new { name = "Sample" },
                running = true
            }));
        });
        server.Start();

        try
        {
            using var client = new AgentClient("localhost", port)
            {
                AutoAcquireMutationLease = false,
                TransientFailureRetryCount = 5,
                RetryMutatingRequests = true
            };
            WorkflowCommandReceipt? receipt = null;
            using (client.UseWorkflowRun(new WorkflowRunContext
            {
                RunId = "run",
                AgentInstanceId = "instance",
                AuthorityEpoch = 1
            }))
            {
                Assert.True(await client.TapAsync("button"));
                receipt = client.LastWorkflowCommandReceipt;
                _ = await client.GetStatusAsync();
            }

            Assert.NotNull(mutationHeaders);
            Assert.Equal("run", mutationHeaders![WorkflowCommandHeaders.RunId]);
            Assert.Equal("instance", mutationHeaders[WorkflowCommandHeaders.AgentInstanceId]);
            Assert.Equal("1", mutationHeaders[WorkflowCommandHeaders.Sequence]);
            Assert.Equal(
                Microsoft.Maui.DevFlow.Driver.WorkflowCommandDigest.Compute(
                    "POST",
                    "/api/v1/ui/actions/tap",
                    """{"elementId":"button"}"""),
                mutationHeaders[WorkflowCommandHeaders.ActionDigest]);
            Assert.NotNull(receipt);
            Assert.Null(readHeaders?.GetValueOrDefault(WorkflowCommandHeaders.RunId));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task AgentClientWorkflowScope_DisablesMutatingTransportRetries()
    {
        var port = GetFreePort();
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        using var listenerCancellation = new CancellationTokenSource();
        var connections = 0;
        var firstConnection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acceptLoop = Task.Run(async () =>
        {
            while (!listenerCancellation.IsCancellationRequested)
            {
                try
                {
                    using var connection = await listener.AcceptTcpClientAsync(listenerCancellation.Token);
                    Interlocked.Increment(ref connections);
                    firstConnection.TrySetResult();
                    var buffer = new byte[4096];
                    _ = await connection.GetStream().ReadAsync(buffer, listenerCancellation.Token);
                    // Deliberately close without an HTTP response to model a response lost after
                    // the fenced command was dispatched.
                }
                catch (OperationCanceledException) when (listenerCancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException) when (listenerCancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (listenerCancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        });

        try
        {
            using var client = new AgentClient("127.0.0.1", port)
            {
                AutoAcquireMutationLease = false,
                TransientFailureRetryCount = 3,
                RetryMutatingRequests = true
            };
            using var scope = client.UseWorkflowRun(new WorkflowRunContext
            {
                RunId = "run",
                AgentInstanceId = "instance",
                AuthorityEpoch = 1
            });

            var failure = await Assert.ThrowsAsync<WorkflowCommandException>(() => client.TapAsync("button"));
            Assert.True(failure.IsUnknownCompletion);
            await firstConnection.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            Assert.Equal(1, Volatile.Read(ref connections));
        }
        finally
        {
            listenerCancellation.Cancel();
            listener.Stop();
            await acceptLoop;
        }
    }

    private static WorkflowCommandLedger CreateLedger()
        => new(() => "instance", () => true);

    private static WorkflowRunControlRequest Control(
        string action,
        string runId = "run",
        long epoch = 1)
        => new()
        {
            Action = action,
            RunId = runId,
            AgentInstanceId = "instance",
            AuthorityEpoch = epoch
        };

    private static HttpRequest CommandRequest(
        long sequence,
        string runId = "run",
        string agentInstanceId = "instance",
        long epoch = 1,
        string? commandId = null,
        string body = """{"elementId":"button"}""")
    {
        const string method = "POST";
        const string path = "/mutate";
        var request = new HttpRequest
        {
            Method = method,
            Path = path,
            Body = body
        };
        var digest = Microsoft.Maui.DevFlow.Agent.Core.WorkflowCommandDigest.Compute(method, path, body);
        request.Headers[WorkflowCommandHeaders.RunId] = runId;
        request.Headers[WorkflowCommandHeaders.AgentInstanceId] = agentInstanceId;
        request.Headers[WorkflowCommandHeaders.Sequence] = sequence.ToString();
        request.Headers[WorkflowCommandHeaders.CommandId] = commandId ??
            Microsoft.Maui.DevFlow.Driver.WorkflowCommandDigest.CreateCommandId(runId, sequence, digest);
        request.Headers[WorkflowCommandHeaders.ActionDigest] = digest;
        request.Headers[WorkflowCommandHeaders.AuthorityEpoch] = epoch.ToString();
        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, int port, HttpRequest request)
    {
        using var message = new HttpRequestMessage(
            new HttpMethod(request.Method),
            $"http://localhost:{port}{request.Path}")
        {
            Content = new StringContent(request.Body ?? string.Empty, Encoding.UTF8, "application/json")
        };
        foreach (var header in request.Headers)
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return await client.SendAsync(message);
    }

    private static Microsoft.Maui.DevFlow.Agent.Core.MutationLeaseStatus AllowedLease() => new()
    {
        Ok = true,
        Allowed = true,
        YouHold = true
    };

    private static string? Reason(HttpResponse? response)
    {
        if (response?.Body is null)
            return null;
        using var document = JsonDocument.Parse(response.Body);
        return document.RootElement.TryGetProperty("reason", out var reason)
            ? reason.GetString()
            : null;
    }

    private static int GetFreePort() => TestPorts.Reserve();
}
