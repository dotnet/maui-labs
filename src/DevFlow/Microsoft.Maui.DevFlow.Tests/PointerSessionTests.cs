using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class PointerSessionTests
{
    [Fact]
    public async Task PointerStreamCapability_ReportsLimitsAndBackendFidelity()
    {
        await WithServiceAsync(async (_, client) =>
        {
            var response = await client.GetCapabilitiesAsync();
            var capability = response.GetProperty("capabilities").GetProperty("ui.pointerStream");

            Assert.True(capability.GetProperty("supported").GetBoolean());
            Assert.Equal("test", capability.GetProperty("fidelity").GetString());
            Assert.Equal(64, capability.GetProperty("maxSamplesPerMove").GetInt32());
            Assert.Equal("windowLogical", capability.GetProperty("coordinateSpaces")[0].GetString());
        });
    }

    [Fact]
    public async Task PointerSession_OrderedBatches_DuplicateRetryAndTerminalTombstone()
    {
        await WithServiceAsync(async (service, client) =>
        {
            var start = await client.StartPointerSessionAsync(Start("ordered"));
            Assert.True(start.Success);
            Assert.Equal(0, start.LastSequence);

            var samples = new[]
            {
                Sample(10, 11, 5),
                Sample(12, 13, 8)
            };
            var move = await client.MovePointerSessionAsync("ordered", 1, samples);
            var duplicate = await client.MovePointerSessionAsync("ordered", 1, samples);
            Assert.True(move.Success);
            Assert.True(duplicate.Success);
            Assert.Equal(1, duplicate.LastSequence);

            var endSample = Sample(14, 15, 12);
            var end = await client.EndPointerSessionAsync("ordered", 2, endSample);
            var duplicateEnd = await client.EndPointerSessionAsync("ordered", 2, endSample);
            Assert.True(end.Success);
            Assert.True(duplicateEnd.Success);
            Assert.Equal("ended", duplicateEnd.State);

            var late = await client.MovePointerSessionAsync("ordered", 3, [Sample(16, 17, 15)]);
            Assert.False(late.Success);
            Assert.Equal(410, late.StatusCode);
            Assert.Equal("session-gone", late.Reason);
            Assert.Equal(["start", "move:2", "end"], service.Events);
        });
    }

    [Fact]
    public async Task PointerSession_SequenceGapAndConflictingDuplicate_AreRejected()
    {
        await WithServiceAsync(async (service, client) =>
        {
            Assert.True((await client.StartPointerSessionAsync(Start("sequence"))).Success);

            var gap = await client.MovePointerSessionAsync("sequence", 2, [Sample(1, 1, 1)]);
            Assert.False(gap.Success);
            Assert.Equal("sequence-gap", gap.Reason);
            Assert.Equal(0, gap.LastSequence);

            Assert.True((await client.MovePointerSessionAsync("sequence", 1, [Sample(2, 2, 2)])).Success);
            var conflict = await client.MovePointerSessionAsync("sequence", 1, [Sample(3, 3, 3)]);
            Assert.False(conflict.Success);
            Assert.Equal("sequence-conflict", conflict.Reason);
            Assert.Equal(["start", "move:1"], service.Events);

            Assert.True((await client.CancelPointerSessionAsync("sequence", "test-cleanup")).Success);
            Assert.Equal(["start", "move:1", "cancel:test-cleanup"], service.Events);
        });
    }

    [Fact]
    public async Task PointerSession_IdenticalStartRetry_DoesNotReconsumeCapture()
    {
        await WithServiceAsync(async (service, client) =>
        {
            service.RejectRepeatedPrepare = true;
            var request = Start("start-retry");

            Assert.True((await client.StartPointerSessionAsync(request)).Success);
            Assert.True((await client.StartPointerSessionAsync(request)).Success);
            Assert.Equal(1, service.PrepareStartCalls);
            Assert.True((await client.CancelPointerSessionAsync("start-retry")).Success);
        });
    }

    [Fact]
    public async Task PointerSession_TooManySamples_IsRejectedWithoutEndingSession()
    {
        await WithServiceAsync(
            async (service, client) =>
            {
                Assert.True((await client.StartPointerSessionAsync(Start("bounded"))).Success);
                var result = await client.MovePointerSessionAsync(
                    "bounded",
                    1,
                    [Sample(1, 1, 1), Sample(2, 2, 2), Sample(3, 3, 3)]);

                Assert.False(result.Success);
                Assert.Equal(413, result.StatusCode);
                Assert.Equal("too-many-samples", result.Reason);
                Assert.True((await client.CancelPointerSessionAsync("bounded")).Success);
                Assert.Equal(["start", "cancel:client-cancel"], service.Events);
            },
            new AgentOptions
            {
                RequireMutationLease = false,
                PointerSessionMaxSamplesPerMove = 2
            });
    }

    [Fact]
    public async Task PointerSession_TimestampsMustIncreaseWithinCoalescedBatch()
    {
        await WithServiceAsync(async (service, client) =>
        {
            Assert.True((await client.StartPointerSessionAsync(Start("timestamps"))).Success);
            var result = await client.MovePointerSessionAsync(
                "timestamps",
                1,
                [Sample(1, 1, 100), Sample(2, 2, 50)]);

            Assert.False(result.Success);
            Assert.Equal("timestamp-regression", result.Reason);
            Assert.DoesNotContain(service.Events, value => value.StartsWith("move:", StringComparison.Ordinal));
            Assert.True((await client.CancelPointerSessionAsync("timestamps")).Success);
        });
    }

    [Fact]
    public async Task PointerSession_RetryIdentityIncludesOperationKind()
    {
        await WithServiceAsync(async (_, client) =>
        {
            Assert.True((await client.StartPointerSessionAsync(Start("operation-kind"))).Success);
            const string body = """
                {
                  "sequence": 1,
                  "samples": [{ "x": 1, "y": 2, "elapsedMs": 3 }],
                  "sample": { "x": 1, "y": 2, "elapsedMs": 3 }
                }
                """;
            using var http = new HttpClient();
            using var move = await http.PostAsync(
                $"{client.BaseUrl}/api/v1/ui/actions/pointer-sessions/operation-kind/move",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
            using var end = await http.PostAsync(
                $"{client.BaseUrl}/api/v1/ui/actions/pointer-sessions/operation-kind/end",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, end.StatusCode);
            Assert.True((await client.CancelPointerSessionAsync("operation-kind")).Success);
        });
    }

    [Fact]
    public async Task PointerSession_IdleTimeout_SendsBackendCancel()
    {
        await WithServiceAsync(
            async (service, client) =>
            {
                Assert.True((await client.StartPointerSessionAsync(Start("timeout"))).Success);
                var reason = await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("idle-timeout", reason);

                var late = await client.MovePointerSessionAsync("timeout", 1, [Sample(1, 1, 1)]);
                Assert.False(late.Success);
                Assert.Equal("session-gone", late.Reason);
            },
            new AgentOptions
            {
                RequireMutationLease = false,
                PointerSessionIdleTimeoutMs = 500,
                PointerSessionMaxDurationMs = 5_000
            });
    }

    [Fact]
    public async Task PointerSession_BlocksOtherMutationsUntilTerminal()
    {
        await WithServiceAsync(async (_, client) =>
        {
            Assert.True((await client.StartPointerSessionAsync(Start("exclusive"))).Success);

            using var http = new HttpClient();
            using var response = await http.PostAsync(
                $"{client.BaseUrl}/api/v1/invoke/actions/test",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("pointer-session-active", body);
            Assert.True((await client.CancelPointerSessionAsync("exclusive")).Success);
        });
    }

    [Fact]
    public async Task PointerSession_NavigationCancelsBeforeNavigating()
    {
        await WithServiceAsync(async (service, client) =>
        {
            Assert.True((await client.StartPointerSessionAsync(Start("navigation"))).Success);

            Assert.True(await client.BackAsync());
            Assert.Equal(1, service.BackCalls);
            Assert.Contains("cancel:navigation", service.Events);
        });
    }

    [Fact]
    public async Task PointerSession_CancelPreemptsInFlightMove_AndEmitsOneTerminalCancel()
    {
        await WithServiceAsync(async (service, client) =>
        {
            service.BlockMoves = true;
            Assert.True((await client.StartPointerSessionAsync(Start("race"))).Success);

            var moveTask = client.MovePointerSessionAsync("race", 1, [Sample(10, 10, 10)]);
            await service.MoveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var cancelTask = client.CancelPointerSessionAsync("race", "pointercancel");

            var move = await moveTask.WaitAsync(TimeSpan.FromSeconds(5));
            var cancel = await cancelTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(move.Success);
            Assert.Equal("cancelled", cancel.State);
            Assert.Single(service.Events, value => value == "cancel:pointer-session-cancelled");
        });
    }

    [Fact]
    public async Task PointerSession_CancelPreemptsPendingStart()
    {
        await WithServiceAsync(async (service, client) =>
        {
            service.BlockStarts = true;
            var startTask = client.StartPointerSessionAsync(Start("pending-start"));
            await service.StartStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var cancel = await client.CancelPointerSessionAsync("pending-start", "pointercancel")
                .WaitAsync(TimeSpan.FromSeconds(5));
            var start = await startTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(start.Success);
            Assert.Equal("pointer-start-cancelled", start.Reason);
            Assert.True(cancel.Success);
            Assert.Equal("unknown", cancel.State);
        });
    }

    [Fact]
    public async Task PointerSession_ForcedLeaseTakeover_CancelsOldPointer()
    {
        var options = new AgentOptions
        {
            Port = FreePort(),
            RequireMutationLease = true,
            MutationLeaseTimeoutMs = 10_000
        };
        using var service = new PointerTestService(options);
        service.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
        using var owner = new AgentClient("localhost", options.Port)
        {
            MutationLeaseId = "pointer-owner",
            MutationLeaseHolderKind = "test"
        };
        using var takeover = new AgentClient("localhost", options.Port)
        {
            MutationLeaseId = "pointer-takeover",
            MutationLeaseHolderKind = "test"
        };
        try
        {
            Assert.True((await owner.StartPointerSessionAsync(Start("lease"))).Success);
            var status = await takeover.ControlMutationLeaseAsync("claim", force: true);
            Assert.True(status.YouHold);
            var hijack = await takeover.MovePointerSessionAsync("lease", 1, [Sample(10, 10, 10)]);
            Assert.False(hijack.Success);
            Assert.DoesNotContain(service.Events, value => value.StartsWith("move:", StringComparison.Ordinal));
            Assert.Equal("lease-lost", await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task PointerSession_NonOwnerRelease_DoesNotCancelActivePointer()
    {
        var options = new AgentOptions
        {
            Port = FreePort(),
            RequireMutationLease = true,
            MutationLeaseTimeoutMs = 10_000
        };
        using var service = new PointerTestService(options);
        service.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
        using var owner = new AgentClient("localhost", options.Port)
        {
            MutationLeaseId = "release-owner",
            MutationLeaseHolderKind = "test"
        };
        using var other = new AgentClient("localhost", options.Port)
        {
            MutationLeaseId = "release-other",
            MutationLeaseHolderKind = "test",
            AutoAcquireMutationLease = false
        };
        try
        {
            Assert.True((await owner.StartPointerSessionAsync(Start("release"))).Success);
            var status = await other.ControlMutationLeaseAsync("release");
            Assert.False(status.YouHold);
            await Task.Delay(750);
            Assert.False(service.Cancelled.Task.IsCompleted);
            Assert.True((await owner.CancelPointerSessionAsync("release")).Success);
        }
        finally
        {
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task PointerSession_WatchdogCheck_DoesNotRenewAbandonedLease()
    {
        var options = new AgentOptions
        {
            Port = FreePort(),
            RequireMutationLease = true,
            MutationLeaseTimeoutMs = 1_000,
            PointerSessionIdleTimeoutMs = 5_000,
            PointerSessionMaxDurationMs = 10_000
        };
        using var service = new PointerTestService(options);
        service.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
        using var owner = new AgentClient("localhost", options.Port)
        {
            MutationLeaseId = "abandoned-owner",
            MutationLeaseHolderKind = "test"
        };
        try
        {
            Assert.True((await owner.StartPointerSessionAsync(Start("abandoned"))).Success);
            Assert.Equal("lease-lost", await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(4)));
        }
        finally
        {
            await service.StopAsync();
        }
    }

    [Fact]
    public async Task PointerSession_Dispose_SendsBackendCancel()
    {
        var options = new AgentOptions { Port = FreePort(), RequireMutationLease = false };
        var service = new PointerTestService(options);
        service.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
        using var client = new AgentClient("localhost", options.Port) { AutoAcquireMutationLease = false };

        Assert.True((await client.StartPointerSessionAsync(Start("dispose"))).Success);
        service.Dispose();

        Assert.Equal("agent-disposed", await service.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private static PointerSessionStartRequest Start(string sessionId) => new()
    {
        SessionId = sessionId,
        PointerId = 1,
        PointerType = "touch",
        CoordinateSpace = "windowLogical",
        X = 5,
        Y = 6,
        ElapsedMs = 0
    };

    private static PointerSessionSample Sample(double x, double y, double elapsedMs) => new()
    {
        X = x,
        Y = y,
        ElapsedMs = elapsedMs
    };

    private static async Task WithServiceAsync(
        Func<PointerTestService, AgentClient, Task> test,
        AgentOptions? options = null)
    {
        options ??= new AgentOptions { RequireMutationLease = false };
        options.Port = options.Port == 9223 ? FreePort() : options.Port;
        using var service = new PointerTestService(options);
        service.StartServerOnly(new DelegateAgentDispatcher(() => false, action => action()));
        using var client = new AgentClient("localhost", options.Port) { AutoAcquireMutationLease = false };
        try
        {
            await test(service, client);
        }
        finally
        {
            await service.StopAsync();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class PointerTestService(AgentOptions options) : DevFlowAgentService(options)
    {
        public ConcurrentQueue<string> Events { get; } = new();
        public TaskCompletionSource<string> Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource MoveStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockStarts { get; set; }
        public bool BlockMoves { get; set; }
        public bool RejectRepeatedPrepare { get; set; }
        public int PrepareStartCalls { get; private set; }
        public int BackCalls { get; private set; }

        protected override PointerStreamSupport GetPointerStreamSupport()
            => new(
                Supported: true,
                Fidelity: "test",
                RequiresOptIn: false,
                SupportsCoalescedSamples: true,
                SupportsPressure: false,
                SupportsButtons: false,
                PointerTypes: ["touch"],
                Reason: null);

        protected override Task<HttpResponse?> PreparePointerSessionStartAsync(
            HttpRequest request,
            PointerSessionStartRequest body)
        {
            PrepareStartCalls++;
            return Task.FromResult<HttpResponse?>(
                RejectRepeatedPrepare && PrepareStartCalls > 1
                    ? HttpResponse.Error("capture already consumed", 409, "stale-capture-epoch")
                    : null);
        }

        protected override async Task<PointerSessionBackendStartResult> StartPointerSessionBackendAsync(
            PointerSessionStartRequest request,
            CancellationToken cancellationToken)
        {
            Events.Enqueue("start");
            if (BlockStarts)
            {
                StartStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return PointerSessionBackendStartResult.Started(new object(), "test start");
        }

        protected override async Task<PointerSessionBackendResult> MovePointerSessionBackendAsync(
            object state,
            IReadOnlyList<PointerSessionSample> samples,
            CancellationToken cancellationToken)
        {
            Events.Enqueue($"move:{samples.Count}");
            if (BlockMoves)
            {
                MoveStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return PointerSessionBackendResult.Applied();
        }

        protected override Task<PointerSessionBackendResult> EndPointerSessionBackendAsync(
            object state,
            PointerSessionSample sample,
            CancellationToken cancellationToken)
        {
            Events.Enqueue("end");
            return Task.FromResult(PointerSessionBackendResult.Applied());
        }

        protected override Task<PointerSessionBackendResult> CancelPointerSessionBackendAsync(
            object state,
            string reason)
        {
            Events.Enqueue($"cancel:{reason}");
            Cancelled.TrySetResult(reason);
            return Task.FromResult(PointerSessionBackendResult.Applied());
        }

        protected override Task<HttpResponse> HandleBack(HttpRequest request)
        {
            BackCalls++;
            return Task.FromResult(HttpResponse.Ok("back"));
        }
    }
}
