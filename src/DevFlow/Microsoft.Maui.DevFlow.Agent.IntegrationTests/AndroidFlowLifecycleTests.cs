using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using System.Text.Json;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public sealed class AndroidDeviceLifecycleOperationsTests
{
    [Fact]
    public void SampleIntegrationExtension_UsesValidReverseDomainNamespace()
    {
        var source = File.ReadAllText(Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "samples",
            "DevFlow.Sample",
            "MauiProgram.cs"));

        Assert.Contains("\"com.example.devflow.integrationtest\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"com.example.devflow.integration-test\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HardReset_PmClear_StopsClearsThenInstalls()
    {
        var runner = new ScriptedProcessRunner(
            Success(),
            Success("Success\n"),
            Success());
        var operations = CreateOperations(runner);

        var reset = await operations.HardResetAsync(AndroidResetStrategy.PmClear, null);
        await operations.InstallAsync(@"C:\build\sample.apk", replaceExisting: true);

        Assert.True(reset.AppStateSucceeded);
        Assert.Equal(
            [
                "-s emulator-5554 shell am force-stop com.companyname.mauitodo",
                "-s emulator-5554 shell pm clear com.companyname.mauitodo",
                "-s emulator-5554 install -r -t \"C:\\build\\sample.apk\"",
            ],
            runner.Arguments);
    }

    [Fact]
    public async Task HardReset_UninstallReinstall_UninstallsBeforeFreshInstall()
    {
        var runner = new ScriptedProcessRunner(
            Success(),
            Success("Success\n"),
            Success());
        var operations = CreateOperations(runner);

        var reset = await operations.HardResetAsync(AndroidResetStrategy.UninstallReinstall, null);
        await operations.InstallAsync(@"C:\build\sample.apk", replaceExisting: false);

        Assert.Equal("uninstall-reinstall", reset.Strategy);
        Assert.Equal(
            [
                "-s emulator-5554 shell am force-stop com.companyname.mauitodo",
                "-s emulator-5554 uninstall com.companyname.mauitodo",
                "-s emulator-5554 install -t \"C:\\build\\sample.apk\"",
            ],
            runner.Arguments);
    }

    [Fact]
    public async Task EnsureAgentPortForward_RecreatesAndVerifiesMapping()
    {
        var runner = new ScriptedProcessRunner(
            new PlatformProcessResult("", "", 1),
            Success(),
            Success("emulator-5554 tcp:49152 tcp:49152\n"));
        var operations = CreateOperations(runner);

        await operations.EnsureAgentPortForwardAsync();

        Assert.Equal(
            [
                "-s emulator-5554 forward --remove tcp:49152",
                "-s emulator-5554 forward tcp:49152 tcp:49152",
                "-s emulator-5554 forward --list",
            ],
            runner.Arguments);
    }

    [Fact]
    public async Task Install_AdbExitFailure_RetainsSafeActionAndExitContext()
    {
        const string secret = "ANDROID-ADB-SECRET-SENTINEL";
        var runner = new ScriptedProcessRunner(
            new PlatformProcessResult(
                "",
                $"adb protocol fault (couldn't read status length); token={secret}",
                1));
        var operations = CreateOperations(runner);

        var exception = await Assert.ThrowsAsync<PlatformFlowLifecycleException>(
            () => operations.InstallAsync(@"C:\build\sample.apk", replaceExisting: true));

        Assert.Equal(PlatformFlowLifecycleFailureKind.Infrastructure, exception.Kind);
        Assert.Equal("install the Android Debug APK", exception.Details?.ActionName);
        Assert.Equal("install", exception.Details?.AdbCommandCategory);
        Assert.Equal(1, exception.Details?.ExitCode);
        Assert.Equal(120, exception.Details?.TimeoutSeconds);
        Assert.False(exception.Details?.TimedOut ?? true);
        Assert.False(exception.Details?.CancellationRequested ?? true);
        Assert.Contains("adb protocol fault", exception.Details?.SafeErrorText ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, exception.Details?.SafeErrorText ?? string.Empty, StringComparison.Ordinal);
        Assert.IsType<PlatformAdbCommandException>(exception.InnerException);
    }

    [Fact]
    public async Task WaitForExpectedAgent_RejectsStaleIdentityBeforeAcceptingNewProcess()
    {
        var statuses = new Queue<AgentStatus?>([
            Status(processId: 42, instanceId: "old"),
            Status(processId: 42, instanceId: "new"),
        ]);

        var ready = await PlatformAgentReadiness.WaitForExpectedAsync(
            _ => Task.FromResult(statuses.Dequeue()),
            new PlatformAgentExpectation
            {
                PackageId = "com.companyname.mauitodo",
                ExpectedProcessId = 42,
                PreviousAgent = new PlatformAgentIdentity("old", 42),
                Timeout = TimeSpan.FromSeconds(1),
            },
            pollInterval: TimeSpan.Zero);

        Assert.Equal(42, ready.Agent.ProcessId);
        Assert.Equal("new", ready.Agent.InstanceId);
    }

    static AndroidDeviceLifecycleOperations CreateOperations(IPlatformProcessRunner runner)
        => new(
            runner,
            "adb",
            "emulator-5554",
            "com.companyname.mauitodo",
            49152);

    static PlatformProcessResult Success(string output = "")
        => new(output, "", 0);

    static AgentStatus Status(int processId, string instanceId)
        => new()
        {
            Agent = new AgentDescriptor { InstanceId = instanceId },
            App = new AppDescriptor { PackageId = "com.companyname.mauitodo", ProcessId = processId },
            Running = true,
        };
}

public sealed class AndroidFlowTestHostTests
{
    [Fact]
    public async Task CheckpointMismatch_BlocksBeforeRunnerMutation()
    {
        var lifecycle = new FakeLifecycle
        {
            VerifyException = PlatformFlowLifecycleException.Precondition("seed fingerprint mismatch"),
        };
        var driver = new FakeFlowDriver();
        await using var host = new AndroidFlowTestHost(lifecycle, driver);

        var result = await host.RunAsync(TapFlow(), Plan());

        Assert.Equal(MauiFlowRunOutcomes.Failed, result.Report.Outcome?.Status);
        Assert.Equal(MauiFlowFailureClasses.PreconditionUnsatisfied, result.Report.Failure?.Code);
        Assert.Equal(0, driver.MutationCalls);
        Assert.Contains("verify", lifecycle.Calls);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("launch")]
    [InlineData("install")]
    [InlineData("agent-readiness")]
    public async Task LifecycleFailure_IsClassifiedAsInfrastructure(string phase)
    {
        var lifecycle = new FakeLifecycle();
        if (phase == "reset")
            lifecycle.ResetException = PlatformFlowLifecycleException.Infrastructure("adb pm clear failed");
        else if (phase == "launch")
            lifecycle.LaunchException = PlatformFlowLifecycleException.Infrastructure("adb launch failed");
        else if (phase == "install")
            lifecycle.InstallException = PlatformFlowLifecycleException.Infrastructure("adb install failed");
        else
            lifecycle.WaitException = PlatformFlowLifecycleException.Infrastructure("agent readiness failed");

        await using var host = new AndroidFlowTestHost(lifecycle, new FakeFlowDriver());
        var result = await host.RunAsync(TapFlow(), Plan());

        Assert.Equal(MauiFlowRunOutcomes.InfrastructureError, result.Report.Outcome?.Status);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, result.Report.Failure?.Code);
        Assert.Equal("android-lifecycle", result.Report.Failure?.Phase);
        Assert.False(result.Report.Failure?.RepairEligible ?? true);
    }

    [Fact]
    public async Task AdbExitFailure_DoesNotRetryOrLaunchAgain_AndRemainsInfrastructure()
    {
        var lifecycle = new FakeLifecycle
        {
            LaunchException = PlatformFlowLifecycleException.Infrastructure(
                "Failed to launch the Android app (adb exit 1).",
                new PlatformAdbCommandException(new PlatformFlowLifecycleFailureDetails
                {
                    LifecyclePhase = "android-device-lifecycle",
                    ActionName = "launch the Android app",
                    AdbCommandCategory = "activity",
                    ExitCode = 1,
                    TimeoutSeconds = 45,
                    TimedOut = false,
                    CancellationRequested = false,
                    SafeErrorText = "adb protocol fault (couldn't read status length)",
                })),
        };
        var driver = new FakeFlowDriver();
        await using var host = new AndroidFlowTestHost(lifecycle, driver);

        var result = await host.RunAsync(TapFlow(), Plan());

        Assert.Equal(MauiFlowRunOutcomes.InfrastructureError, result.Report.Outcome?.Status);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, result.Report.Failure?.Code);
        Assert.Equal(1, lifecycle.Calls.Count(call => call == "launch"));
        Assert.DoesNotContain("wait", lifecycle.Calls);
        Assert.Equal(0, driver.MutationCalls);
    }

    [Fact]
    public async Task TestTenantPolicy_PropagatesResetProofAndPreventsMutation()
    {
        var lifecycle = new FakeLifecycle
        {
            Reset = new MauiFlowResetResult
            {
                Requested = true,
                Succeeded = true,
                AppStateSucceeded = true,
                BackendTestDataSucceeded = false,
                SeedFingerprint = "seed",
                BackendStateFingerprint = "backend",
            },
        };
        var driver = new FakeFlowDriver();
        await using var host = new AndroidFlowTestHost(lifecycle, driver);

        var result = await host.RunAsync(
            TapFlow(),
            Plan(MauiFlowSideEffectPolicies.TestTenantResettable));

        Assert.Equal(MauiFlowSideEffectPolicies.TestTenantResettable, result.Report.SideEffectPolicy);
        Assert.False(result.Report.ReplayEligibility?.OrdinaryReplayAllowed);
        Assert.Equal(MauiFlowFailureClasses.PreconditionUnsatisfied, result.Report.Failure?.Code);
        Assert.Equal(0, driver.MutationCalls);
    }

    [Fact]
    public async Task SuccessfulRun_WritesReportAndHostDiagnostics()
    {
        var root = CreateArtifactRoot();
        try
        {
            var lifecycle = new FakeLifecycle();
            var driver = new FakeFlowDriver();
            await using var host = new AndroidFlowTestHost(lifecycle, driver);

            var result = await host.RunAsync(
                TapFlow(),
                Plan(),
                new AndroidFlowRunRequest
                {
                    ArtifactRoot = root,
                    RunId = "artifact-output",
                });

            Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome?.Status);
            Assert.NotNull(result.Report.ReportPath);
            Assert.True(File.Exists(result.Report.ReportPath));
            Assert.Contains(result.Artifacts, artifact => artifact.Kind == "host-diagnostics");
            Assert.Equal(1, driver.MutationCalls);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_DisablesMutatingTransportRetriesDuringReplay()
    {
        using var client = new AgentClient("127.0.0.1", 1)
        {
            RetryMutatingRequests = true,
        };
        bool? retryDuringMutation = null;
        var driver = new FakeFlowDriver
        {
            OnMutation = () => retryDuringMutation = client.RetryMutatingRequests,
        };
        await using var host = new AndroidFlowTestHost(new FakeLifecycle(), driver, client);

        var result = await host.RunAsync(TapFlow(), Plan());

        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome?.Status);
        Assert.False(retryDuringMutation);
        Assert.True(client.RetryMutatingRequests);
    }

    [Fact]
    public async Task Dispose_StopsAndDisposesLifecycle()
    {
        var lifecycle = new FakeLifecycle();
        var host = new AndroidFlowTestHost(lifecycle, new FakeFlowDriver());

        await host.DisposeAsync();

        Assert.True(lifecycle.Stopped);
        Assert.True(lifecycle.Disposed);
    }

    static MauiFlow TapFlow() => new()
    {
        Name = "android-host-tap",
        Steps =
        [
            new FlowStep
            {
                Seq = 1,
                Action = FlowActions.Tap,
                Args = new FlowStepArgs { Selector = new FlowSelector { AutomationId = "submit" } },
            },
        ],
    };

    static MauiTestPlan Plan(string policy = MauiFlowSideEffectPolicies.None)
    {
        var flow = TapFlow();
        return new()
        {
            PlanId = "android-test-plan",
            Revision = 1,
            Goal = "Exercise the Android flow host contract.",
            Flow = new MauiFlowReference
            {
                Path = "android-host-tap.md",
                FlowId = "android-host-tap",
                Revision = 1,
                Digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
            },
            SideEffectPolicy = policy,
            Reset = new MauiTestResetRequirement
            {
                Required = policy == MauiFlowSideEffectPolicies.TestTenantResettable,
                SeedFingerprint = "seed",
                BackendStateFingerprint = "backend",
            },
            Checkpoint = new MauiFlowCheckpointRequirements
            {
                Route = "//native",
                SeedFingerprint = "seed",
                BackendStateFingerprint = "backend",
            },
            Provenance = new MauiActorProvenance
            {
                ActorKind = "test",
                ActorId = "android-flow-lifecycle",
                Channel = "integration-test",
                Provider = "xunit",
            },
        };
    }

    static string CreateArtifactRoot()
        => Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "android-lifecycle-tests",
            Guid.NewGuid().ToString("N"));
}

public sealed class AndroidFixtureInitializationDiagnosticsTests
{
    [Fact]
    public void Write_BoundsAndRedactsFixtureInitializationExceptionChain()
    {
        const string secret = "ANDROID-DIAGNOSTIC-SECRET-SENTINEL";
        var root = Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "artifacts",
            "TestResults",
            "android-fixture-diagnostics-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var details = new PlatformFlowLifecycleFailureDetails
            {
                LifecyclePhase = "android-device-lifecycle",
                ActionName = "install Android Debug APK",
                AdbCommandCategory = "install",
                ExitCode = 1,
                TimeoutSeconds = 120,
                TimedOut = false,
                CancellationRequested = false,
                SafeErrorText =
                    $"adb protocol fault (couldn't read status length) -s emulator-5554 token={secret} " +
                    $"PATH=C:\\Users\\qa-user\\android-sdk {new string('x', 2_048)}",
            };
            var exception = PlatformFlowLifecycleException.Infrastructure(
                "Android fixture initialization failed.",
                new PlatformAdbCommandException(
                    details,
                    new InvalidOperationException($"serial=emulator-5554 authorization={secret}")),
                details);

            var write = AndroidFixtureInitializationDiagnostics.Write(root, exception);

            Assert.True(write.Ok, write.Error);
            Assert.NotNull(write.Artifact);
            Assert.True(File.Exists(write.Artifact!.Path));
            using var document = JsonDocument.Parse(File.ReadAllText(write.Artifact.Path));
            var diagnostic = document.RootElement;
            Assert.Equal("android-fixture-initialization", diagnostic.GetProperty("lifecyclePhase").GetString());
            Assert.Equal("infrastructure", diagnostic.GetProperty("classification").GetString());

            var chain = diagnostic.GetProperty("exceptionChain").EnumerateArray().ToArray();
            Assert.InRange(chain.Length, 2, AndroidFixtureInitializationDiagnostics.MaxExceptionChainEntries);
            var adb = Assert.Single(
                chain,
                entry => entry.GetProperty("exceptionType").GetString() == "adb-command");
            Assert.Equal("android-device-lifecycle", adb.GetProperty("lifecyclePhase").GetString());
            Assert.Equal("install Android Debug APK", adb.GetProperty("actionName").GetString());
            Assert.Equal("install", adb.GetProperty("adbCommandCategory").GetString());
            Assert.Equal(1, adb.GetProperty("exitCode").GetInt32());
            Assert.Equal(120, adb.GetProperty("timeoutSeconds").GetInt32());
            Assert.False(adb.GetProperty("timedOut").GetBoolean());
            Assert.False(adb.GetProperty("cancellationRequested").GetBoolean());
            Assert.True(
                adb.GetProperty("safeErrorText").GetString()!.Length <=
                AndroidFixtureInitializationDiagnostics.MaxSafeErrorTextCharacters);

            var text = File.ReadAllText(write.Artifact.Path);
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain("emulator-5554", text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"C:\Users\qa-user", text, StringComparison.Ordinal);
            Assert.True(new FileInfo(write.Artifact.Path).Length < 8_192);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

public sealed class SampleFlowContractTests
{
    [Fact]
    public void TierOneSampleFlows_ParseAndValidate()
    {
        var directory = Path.Combine(
            AppFixtureBase.FindRepoRoot(),
            "samples",
            "DevFlow.Sample",
            "maui-tests");
        var files = Directory.GetFiles(directory, "*.md")
            .Where(static path => !string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.InRange(files.Length, 6, 12);
        foreach (var file in files)
        {
            var parsed = FlowMarkdown.Parse(File.ReadAllText(file), file);
            Assert.True(parsed.Ok, parsed.Error);
            var validation = FlowValidator.Validate(parsed.Flow!);
            Assert.True(validation.Ok, $"{Path.GetFileName(file)}: {string.Join("; ", validation.Errors)}");
        }
    }
}

public sealed class AndroidFlowEnvironmentTests
{
    readonly ITestOutputHelper _output;

    public AndroidFlowEnvironmentTests(ITestOutputHelper output)
        => _output = output;

    [Fact]
    public async Task SampleBaseline_RunsWhenAndroidLifecyclePrerequisitesAreExplicitlyEnabled()
    {
        if (!CanRun())
        {
            _output.WriteLine(
                "Android smoke flow not requested. Set DEVFLOW_RUN_ANDROID_FLOW_TESTS=1 with a configured Android SDK/emulator to run it.");
            return;
        }

        var fixture = new AndroidEmulatorFixture();
        await fixture.InitializeAsync();
        try
        {
            var flowPath = Path.Combine(
                AppFixtureBase.FindRepoRoot(),
                "samples",
                "DevFlow.Sample",
                "maui-tests",
                "native-baseline.md");
            var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(flowPath), flowPath);
            Assert.True(parsed.Ok, parsed.Error);

            await using var host = fixture.CreateFlowTestHost();
            var result = await host.RunAsync(
                parsed.Flow!,
                new MauiTestPlan
                {
                    PlanId = "android-sample-baseline",
                    SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                    Checkpoint = new MauiFlowCheckpointRequirements { Route = "//native" },
                    IndependentBusinessOracles =
                    [
                        new MauiIndependentBusinessOracleDeclaration
                        {
                            OracleId = "sample-integration-state",
                            Required = true,
                            Independent = true,
                            EvidenceKind = "sample-test-state",
                        },
                    ],
                });

            Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report.Outcome?.Status);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    static bool CanRun()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DEVFLOW_RUN_ANDROID_FLOW_TESTS"), "1", StringComparison.Ordinal))
            return false;

        var sdkRoot = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        return !string.IsNullOrWhiteSpace(sdkRoot) && Directory.Exists(sdkRoot);
    }
}

internal sealed class ScriptedProcessRunner(params PlatformProcessResult[] results) : IPlatformProcessRunner
{
    readonly Queue<PlatformProcessResult> _results = new(results);

    public List<string> Arguments { get; } = [];

    public Task<PlatformProcessResult> RunAsync(
        string fileName,
        string arguments,
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        Arguments.Add(arguments);
        if (_results.Count == 0)
            throw new InvalidOperationException($"No fake process response remains for '{fileName} {arguments}'.");
        return Task.FromResult(_results.Dequeue());
    }
}

internal sealed class FakeLifecycle : IPlatformFlowTestLifecycle
{
    public List<string> Calls { get; } = [];
    public PlatformFlowLifecycleException? ResetException { get; set; }
    public PlatformFlowLifecycleException? LaunchException { get; set; }
    public PlatformFlowLifecycleException? InstallException { get; set; }
    public PlatformFlowLifecycleException? WaitException { get; set; }
    public PlatformFlowLifecycleException? VerifyException { get; set; }
    public MauiFlowResetResult Reset { get; set; } = new()
    {
        Requested = true,
        Succeeded = true,
        AppStateSucceeded = true,
        BackendTestDataSucceeded = false,
        Strategy = "pm-clear",
        SeedFingerprint = "seed",
        BackendStateFingerprint = "backend",
    };
    public bool Stopped { get; private set; }
    public bool Disposed { get; private set; }

    public Task<PlatformBuildResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("build");
        return Task.FromResult(new PlatformBuildResult
        {
            ArtifactPath = "sample.apk",
            AppBuildFingerprint = "sha256:build",
            PackageId = "com.companyname.mauitodo",
        });
    }

    public Task<PlatformInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("install");
        if (InstallException is not null)
            throw InstallException;
        return Task.FromResult(new PlatformInstallResult
        {
            ArtifactPath = "sample.apk",
            PackageId = "com.companyname.mauitodo",
        });
    }

    public Task<MauiFlowResetResult> HardResetAsync(PlatformFlowResetRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add("reset");
        if (ResetException is not null)
            throw ResetException;
        return Task.FromResult(Reset);
    }

    public Task<PlatformSeedResult> SeedAsync(PlatformFlowSeedRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add("seed");
        return Task.FromResult(new PlatformSeedResult
        {
            SeedId = "devflow-sample-v1",
            SeedFingerprint = "seed",
            BackendStateFingerprint = "backend",
            StateFingerprint = "state",
            ProcessInstanceId = "process-token",
            AppStateSeed = new MauiFlowAppStateSeedFingerprint { SeedId = "devflow-sample-v1", Fingerprint = "seed" },
            StateOracle = new MauiIndependentBusinessOracleResult
            {
                OracleId = "sample-integration-state",
                Succeeded = true,
                Independent = true,
            },
        });
    }

    public Task<PlatformLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("launch");
        if (LaunchException is not null)
            throw LaunchException;
        return Task.FromResult(new PlatformLaunchResult
        {
            PackageId = "com.companyname.mauitodo",
            ProcessId = 42,
            PreviousAgent = new PlatformAgentIdentity("old", 41),
        });
    }

    public Task<PlatformAgentReadyResult> WaitForExpectedAgentAsync(
        PlatformAgentExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("wait");
        if (WaitException is not null)
            throw WaitException;
        return Task.FromResult(new PlatformAgentReadyResult
        {
            Agent = new PlatformAgentIdentity("new", 42),
            Status = new AgentStatus
            {
                Agent = new AgentDescriptor { InstanceId = "new" },
                App = new AppDescriptor { PackageId = "com.companyname.mauitodo", ProcessId = 42 },
                Route = "//native",
                Running = true,
            },
        });
    }

    public Task<PlatformCheckpointVerification> VerifyCheckpointAsync(
        PlatformCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("verify");
        if (VerifyException is not null)
            throw VerifyException;

        var observed = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = "sha256:build",
            AgentInstanceId = "new",
            SeedFingerprint = "seed",
            BackendStateFingerprint = "backend",
            Route = "//native",
            Locale = "en-US",
            Theme = "light",
            Orientation = "0",
            DisplayProfile = "1080x1920",
        };
        return Task.FromResult(new PlatformCheckpointVerification
        {
            Expected = observed,
            Observed = observed,
            Target = new MauiFlowRunTarget
            {
                Platform = "android",
                AppId = "com.companyname.mauitodo",
                AppBuildFingerprint = "sha256:build",
                AgentInstanceId = "new",
            },
            RunContext = new MauiFlowRunContext
            {
                Intent = MauiFlowReplayIntents.OrdinaryReplay,
                Preconditions = new MauiFlowReplayPreconditions
                {
                    Expected = observed,
                    Observed = observed,
                },
                Reset = request.Reset,
                BusinessOracles =
                [
                    new MauiIndependentBusinessOracleResult
                    {
                        OracleId = "sample-integration-state",
                        Succeeded = true,
                        Independent = true,
                    },
                ],
            },
        });
    }

    public Task<PlatformHostDiagnostics> CaptureHostDiagnosticsAsync(
        PlatformDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Add("diagnostics");
        var diagnostics = new PlatformHostDiagnostics();
        diagnostics.Artifacts.Add(new MauiFlowArtifactReference
        {
            ArtifactId = $"diagnostics-{request.RunId}",
            Kind = "host-diagnostics",
            Path = Path.Combine(request.ArtifactRoot, request.RunId, "host.json"),
            Digest = "sha256:diagnostics",
            MediaType = "application/json",
            Redacted = true,
        });
        return Task.FromResult(diagnostics);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        Stopped = true;
        Calls.Add("stop");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        Calls.Add("dispose");
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeFlowDriver : IMauiFlowDriver
{
    readonly ElementInfo _element = new()
    {
        Id = "submit-id",
        AutomationId = "submit",
        Type = "Button",
        IsVisible = true,
        IsEnabled = true,
        Bounds = new BoundsInfo { X = 1, Y = 1, Width = 40, Height = 20 },
    };

    public int MutationCalls { get; private set; }
    public Action? OnMutation { get; init; }
    public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

    public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
        => Task.FromResult(
            automationId is null || string.Equals(automationId, _element.AutomationId, StringComparison.Ordinal)
                ? new List<ElementInfo> { _element }
                : []);

    public Task<ElementInfo?> GetElementAsync(string id)
        => Task.FromResult<ElementInfo?>(id == _element.Id ? _element : null);

    public Task<bool> TapAsync(string elementId)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> FillAsync(string elementId, string text)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> NavigateAsync(string route)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> BackAsync()
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(true);
    }

    public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme)
    {
        OnMutation?.Invoke();
        MutationCalls++;
        return Task.FromResult(new ThemeResult { Theme = theme, Success = true });
    }

    public Task<string?> GetPropertyAsync(string elementId, string propertyName)
        => Task.FromResult<string?>(null);

    public Task<AgentStatus?> GetStatusAsync()
        => Task.FromResult<AgentStatus?>(new AgentStatus
        {
            Agent = new AgentDescriptor { InstanceId = "new" },
            App = new AppDescriptor { PackageId = "com.companyname.mauitodo", ProcessId = 42 },
            Route = "//native",
            Running = true,
        });
}
