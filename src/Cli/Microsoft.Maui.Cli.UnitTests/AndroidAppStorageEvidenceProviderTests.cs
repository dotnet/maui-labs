using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Covers the repository's first concrete <c>IFlowStateEvidenceProvider</c>. The behaviour that
/// matters most here is the split between a failed oracle (the app did not commit the record) and
/// an unusable evidence channel (nothing can be certified either way) - collapsing those two would
/// let an infrastructure fault read as a business verdict.
/// </summary>
public class AndroidAppStorageEvidenceProviderTests : IDisposable
{
    private readonly string _sdkRoot;
    private readonly string _adbPath;

    public AndroidAppStorageEvidenceProviderTests()
    {
        _sdkRoot = Path.Combine(Path.GetTempPath(), "devflow-oracle-tests-" + Guid.NewGuid().ToString("N"));
        var platformTools = Path.Combine(_sdkRoot, "platform-tools");
        Directory.CreateDirectory(platformTools);
        _adbPath = Path.Combine(platformTools, OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        File.WriteAllText(_adbPath, string.Empty);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            if (Directory.Exists(_sdkRoot))
                Directory.Delete(_sdkRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Supports_RequiresAndroidArtifactAndDeclaredEvidenceKind()
    {
        var provider = CreateProvider(new QueueRunner());

        Assert.True(provider.Supports(CreateRequest(CreatePlan(), "android")));
        Assert.False(provider.Supports(CreateRequest(CreatePlan(), "ios")));
        Assert.False(provider.Supports(CreateRequest(CreatePlan(evidenceKind: "other-kind"), "android")));
    }

    [Fact]
    public void Supports_IgnoresOraclesThatAreNotRequiredAndIndependent()
    {
        var provider = CreateProvider(new QueueRunner());

        Assert.False(provider.Supports(CreateRequest(CreatePlan(required: false), "android")));
        Assert.False(provider.Supports(CreateRequest(CreatePlan(independent: false), "android")));
    }

    [Fact]
    public async Task PrepareAsync_AddsNoAdmissionObligation()
    {
        var provider = CreateProvider(new QueueRunner());

        var result = await provider.PrepareAsync(CreateRequest(CreatePlan(), "android"));

        Assert.True(result.Supported);
        var context = Assert.IsType<MauiFlowRunContext>(result.RunContext);
        Assert.Equal(MauiFlowReplayIntents.OrdinaryReplay, context.Intent);
        Assert.Empty(context.BusinessOracles ?? []);
        Assert.True(context.PriorMutationCompletionCertain);
    }

    [Theory]
    [InlineData("/data/user/0/pkg/files/ledger.jsonl", "android-app-storage-reference-invalid")]
    [InlineData("../files/ledger.jsonl", "android-app-storage-reference-invalid")]
    [InlineData("files/ledger.jsonl; rm -rf /", "android-app-storage-reference-invalid")]
    [InlineData("files/$(whoami).jsonl", "android-app-storage-reference-invalid")]
    // `cat --help` and `cat -` both exit zero without reading app storage, which would let an
    // absent-only oracle pass vacuously, so option-like references are refused outright.
    [InlineData("--help", "android-app-storage-reference-invalid")]
    [InlineData("-", "android-app-storage-reference-invalid")]
    [InlineData("-n", "android-app-storage-reference-invalid")]
    [InlineData("files/-n", "android-app-storage-reference-invalid")]
    [InlineData("etc/passwd", "android-app-storage-reference-invalid")]
    [InlineData("files", "android-app-storage-reference-invalid")]
    [InlineData("files/ledger.jsonl\n", "android-app-storage-reference-invalid")]
    public async Task PrepareAsync_RefusesUnsafeReference(string reference, string expectedCode)
    {
        var provider = CreateProvider(new QueueRunner());

        var ex = await Assert.ThrowsAsync<FlowExecutionException>(
            () => provider.PrepareAsync(CreateRequest(CreatePlan(reference: reference), "android")));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Theory]
    [InlineData("""{ "notContains": ["x"], "contains": ["\"id\":\"todo-0001\""] }""")]
    [InlineData("""{ "absnet": ["x"], "contains": ["\"id\":\"todo-0001\""] }""")]
    [InlineData("""{ "equals": ["x"], "contains": ["\"id\":\"todo-0001\""] }""")]
    public async Task PrepareAsync_RefusesUnknownExpectationSoAPlanCannotReadStricterThanItChecks(string expect)
    {
        var provider = CreateProvider(new QueueRunner());

        var ex = await Assert.ThrowsAsync<FlowExecutionException>(
            () => provider.PrepareAsync(CreateRequest(CreatePlan(expect: expect), "android")));

        Assert.Equal("android-app-storage-expectation-invalid", ex.Code);
    }

    [Fact]
    public async Task PrepareAsync_RefusesMultiLinePredicate()
    {
        var provider = CreateProvider(new QueueRunner());
        var plan = CreatePlan(expect: """{ "contains": ["first\nsecond"] }""");

        var ex = await Assert.ThrowsAsync<FlowExecutionException>(
            () => provider.PrepareAsync(CreateRequest(plan, "android")));

        Assert.Equal("android-app-storage-expectation-invalid", ex.Code);
    }

    [Fact]
    public async Task PrepareAsync_RefusesOracleThatChecksNothing()
    {
        var provider = CreateProvider(new QueueRunner());
        var plan = CreatePlan(expect: """{ "contains": [] }""");

        var ex = await Assert.ThrowsAsync<FlowExecutionException>(
            () => provider.PrepareAsync(CreateRequest(plan, "android")));

        Assert.Equal("android-app-storage-expectation-missing", ex.Code);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_SucceedsWhenCommittedRecordIsPresent()
    {
        var runner = new QueueRunner(Ok("""{"event":"todo-added","id":"todo-0001"}"""));
        var provider = CreateProvider(runner);

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.True(result.Supported);
        var oracle = Assert.Single(result.BusinessOracles);
        Assert.True(oracle.Succeeded);
        Assert.True(oracle.Independent);
        Assert.Equal("todo-ledger-record", oracle.OracleId);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(_adbPath, call.FileName);
        Assert.Equal(
            ["-s", "emulator-5554", "shell", "run-as", "com.example.app", "cat", "files/ledger.jsonl"],
            call.Arguments);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_FailsWhenRequiredRecordIsAbsent()
    {
        var provider = CreateProvider(new QueueRunner(Ok("""{"event":"todo-added","id":"todo-9999"}""")));

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.True(result.Supported);
        var oracle = Assert.Single(result.BusinessOracles);
        Assert.False(oracle.Succeeded);
        Assert.Contains("expect.contains[0]", oracle.Message);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_FailsWhenForbiddenRecordIsPresent()
    {
        var plan = CreatePlan(expect: """{ "absent": ["todo-removed"] }""");
        var provider = CreateProvider(new QueueRunner(Ok("""{"event":"todo-removed","id":"todo-0001"}""")));

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(plan));

        var oracle = Assert.Single(result.BusinessOracles);
        Assert.False(oracle.Succeeded);
        Assert.Contains("expect.absent[0]", oracle.Message);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_NeverEchoesFileContentIntoTheReport()
    {
        const string Secret = "user-private-note-value";
        var provider = CreateProvider(new QueueRunner(Ok(Secret)));

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        var oracle = Assert.Single(result.BusinessOracles);
        Assert.False(oracle.Succeeded);
        Assert.DoesNotContain(Secret, oracle.Message);
        Assert.DoesNotContain(Secret, oracle.EvidenceReference);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_TreatsMissingFileAsFailedOracleNotBrokenChannel()
    {
        var runner = new QueueRunner(new ProcessResult
        {
            ExitCode = 1,
            StandardError = "cat: files/ledger.jsonl: No such file or directory",
        });
        var provider = CreateProvider(runner);

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.True(result.Supported);
        var oracle = Assert.Single(result.BusinessOracles);
        Assert.False(oracle.Succeeded);
        Assert.Contains("did not commit", oracle.Message);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_DoesNotBlameTheAppWhenAdbItselfCouldNotStart()
    {
        // ProcessRunner reports a launch failure as exit -1 carrying the OS message, which on Unix
        // is literally "No such file or directory" - the app must not be blamed for that.
        var runner = new QueueRunner(new ProcessResult
        {
            ExitCode = -1,
            StandardError = "An error occurred trying to start process 'adb'. No such file or directory",
        });
        var provider = CreateProvider(runner);

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-read-failed", result.DetailCode);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_DoesNotTreatAppContentAsAMissingFileDiagnostic()
    {
        var runner = new QueueRunner(new ProcessResult
        {
            ExitCode = 1,
            StandardOutput = """{"event":"error","detail":"No such file or directory"}""",
            StandardError = "read interrupted",
        });
        var provider = CreateProvider(runner);

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-read-failed", result.DetailCode);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_ReportsUnsupportedWhenEvidenceChannelIsUnusable()
    {
        var runner = new QueueRunner(new ProcessResult
        {
            ExitCode = 1,
            StandardError = "run-as: package not debuggable: com.example.app",
        });
        var provider = CreateProvider(runner);

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-read-failed", result.DetailCode);
        Assert.Empty(result.BusinessOracles);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_RefusesNonAndroidRun()
    {
        var provider = CreateProvider(new QueueRunner());
        var request = CreateEvaluationRequest(CreatePlan()) with { Platform = "ios" };

        var result = await provider.EvaluatePostRunAsync(request);

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-platform-mismatch", result.DetailCode);
    }

    [Theory]
    [InlineData("emulator-5554 && echo pwned", "com.example.app")]
    [InlineData("emulator-5554", "com.example.app; rm -rf /")]
    [InlineData("emulator-5554", "notapackage")]
    [InlineData("-foo", "com.example.app")]
    [InlineData("emulator-5554\n", "com.example.app")]
    [InlineData("emulator-5554", "com.example.app\n")]
    [InlineData("emulator-5554", "")]
    public async Task EvaluatePostRunAsync_RefusesUnsafeAdbTarget(string serial, string packageId)
    {
        var runner = new QueueRunner();
        var provider = CreateProvider(runner);
        var request = CreateEvaluationRequest(CreatePlan()) with
        {
            DeviceSerial = serial,
            PackageId = packageId,
        };

        var result = await provider.EvaluatePostRunAsync(request);

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-target-invalid", result.DetailCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_MatchesPredicatesAgainstDeviceLineEndings()
    {
        // ProcessRunner rejoins device output with the host newline, so on Windows every device
        // "\n" arrives as "\r\n". Predicates are single-line by construction, and normalizing keeps
        // a record that ends at a line boundary matching identically on every host.
        var plan = CreatePlan(expect: """{ "contains": ["{\"id\":\"a\"}"], "absent": ["{\"id\":\"c\"}"] }""");
        var provider = CreateProvider(new QueueRunner(Ok("{\"id\":\"a\"}\r\n{\"id\":\"b\"}\r\n")));

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(plan));

        var oracle = Assert.Single(result.BusinessOracles);
        Assert.True(oracle.Succeeded);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_EchoesRunBindingSoTheEvidenceCannotBeReattributed()
    {
        var provider = CreateProvider(new QueueRunner(Ok("""{"event":"todo-added","id":"todo-0001"}""")));
        var request = CreateEvaluationRequest(CreatePlan());

        var result = await provider.EvaluatePostRunAsync(request);

        Assert.Equal(request.RunId, result.RunId);
        Assert.Equal(request.FlowDigest, result.FlowDigest);
        Assert.Equal(request.DeviceIdentityFingerprint, result.DeviceIdentityFingerprint);
        Assert.Equal(request.AppBuildFingerprint, result.AppBuildFingerprint);
        Assert.Equal(request.PackageDigest, result.PackageDigest);
        Assert.Equal(request.StartedAt, result.StartedAt);
        Assert.Equal(request.EndedAt, result.EndedAt);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_ReportsUnsupportedWhenAdbIsNotInstalled()
    {
        var provider = new AndroidAppStorageEvidenceProvider(
            new FakeAndroidProvider { SdkPath = Path.Combine(_sdkRoot, "missing") },
            new QueueRunner());

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.False(result.Supported);
        Assert.Equal("android-adb-not-found", result.DetailCode);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_RefusesEvidenceLargerThanTheBoundedReadLimit()
    {
        var provider = CreateProvider(new QueueRunner(Ok(new string('x', (256 * 1024) + 1))));

        var result = await provider.EvaluatePostRunAsync(CreateEvaluationRequest(CreatePlan()));

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-evidence-too-large", result.DetailCode);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_RefusesToReadAfterTheEvaluationWindowClosed()
    {
        var runner = new QueueRunner();
        var provider = CreateProvider(runner);
        var request = CreateEvaluationRequest(CreatePlan()) with
        {
            EvaluationDeadline = DateTimeOffset.UnixEpoch,
        };

        var result = await provider.EvaluatePostRunAsync(request);

        Assert.False(result.Supported);
        Assert.Equal("android-app-storage-deadline-elapsed", result.DetailCode);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task EvaluatePostRunAsync_BoundsTheReadByTheRemainingEvaluationWindow()
    {
        var runner = new QueueRunner(Ok("""{"event":"todo-added","id":"todo-0001"}"""));
        var provider = CreateProvider(runner);
        var request = CreateEvaluationRequest(CreatePlan()) with
        {
            EvaluationDeadline = TimeProvider.System.GetUtcNow().AddSeconds(2),
        };

        var result = await provider.EvaluatePostRunAsync(request);

        Assert.True(result.Supported);
        Assert.True(runner.Timeouts[0] <= TimeSpan.FromSeconds(2));
    }

    private AndroidAppStorageEvidenceProvider CreateProvider(IExecutionProcessRunner runner)
        => new(new FakeAndroidProvider { SdkPath = _sdkRoot }, runner);

    private AttachedRunOracleTarget CreateAttachedTarget(MauiTestPlan plan) => new()
    {
        Plan = plan,
        Platform = "android",
        PackageId = "com.example.app",
        DeviceIdentity = "platform=android;avd=test-avd",
        Deadline = DateTimeOffset.MaxValue,
    };

    /// <summary>
    /// The reads a successful attached evaluation performs, in order: list devices, ask the one
    /// emulator its AVD name, then read the declared evidence file.
    /// </summary>
    private static ProcessResult[] AttachedReads(string evidence) =>
    [
        Ok("List of devices attached\nemulator-5554\tdevice\n"),
        Ok("test-avd\nOK\n"),
        Ok(evidence),
    ];

    // ── Attached runs ───────────────────────────────────────────────────────────────────────────
    //
    // A run the broker attached to did not install the app, so app-private storage was not empty
    // when it started. Only the difference between a baseline taken before the run and what is
    // there afterwards can be attributed to that run.

    [Fact]
    public async Task Attached_RecordWrittenDuringTheRun_Verifies()
    {
        var runner = new QueueRunner([.. AttachedReads(string.Empty), .. AttachedReads("""{"id":"todo-0001"}""")]);
        var provider = CreateProvider(runner);
        var target = CreateAttachedTarget(CreatePlan());

        var baseline = await provider.ObserveAttachedBaselineAsync(target);
        var results = await provider.EvaluateAttachedAsync(target, baseline);

        Assert.True(baseline.Observed);
        var result = Assert.Single(results);
        Assert.True(result.Succeeded);
        Assert.True(result.Independent);
    }

    [Fact]
    public async Task Attached_RecordThatPredatesTheRun_DoesNotVerify()
    {
        // Identical content before and after means the app committed nothing during this run.
        // Certifying it would attribute a stale record to a run that never produced it.
        var runner = new QueueRunner([.. AttachedReads("""{"id":"todo-0001"}"""), .. AttachedReads("""{"id":"todo-0001"}""")]);
        var provider = CreateProvider(runner);
        var target = CreateAttachedTarget(CreatePlan());

        var baseline = await provider.ObserveAttachedBaselineAsync(target);
        var results = await provider.EvaluateAttachedAsync(target, baseline);

        var result = Assert.Single(results);
        Assert.False(result.Succeeded);
        Assert.Contains("already existed before this run", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attached_RecordNeverWritten_DoesNotVerify()
    {
        var runner = new QueueRunner([.. AttachedReads(string.Empty), .. AttachedReads("""{"id":"todo-0002"}""")]);
        var provider = CreateProvider(runner);
        var target = CreateAttachedTarget(CreatePlan());

        var baseline = await provider.ObserveAttachedBaselineAsync(target);
        var results = await provider.EvaluateAttachedAsync(target, baseline);

        var result = Assert.Single(results);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Attached_WithoutABaseline_CertifiesNothing()
    {
        // No baseline means no evidence that anything read now was produced by this run. Returning
        // no result leaves the run unverified, which is correct; a successful one would be a claim
        // the provider cannot support.
        var runner = new QueueRunner([.. AttachedReads("""{"id":"todo-0001"}""")]);
        var provider = CreateProvider(runner);
        var target = CreateAttachedTarget(CreatePlan());

        var results = await provider.EvaluateAttachedAsync(
            target,
            AttachedRunOracleBaseline.Unavailable("android-adb-not-found", "unreachable"));

        Assert.Empty(results);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Attached_UnreadableEvidenceChannel_CertifiesNothing()
    {
        // An unusable channel is not a business verdict. It must not be reported as either a
        // verified or a failed oracle, because neither is known.
        var provider = CreateProvider(new QueueRunner(
            Ok("List of devices attached\nemulator-5554\tdevice\n"),
            Ok("test-avd\nOK\n"),
            new ProcessResult { ExitCode = 13, StandardError = "device offline" }));
        var target = CreateAttachedTarget(CreatePlan());

        var baseline = await provider.ObserveAttachedBaselineAsync(target);

        Assert.False(baseline.Observed);
        Assert.Equal("android-app-storage-read-failed", baseline.UnavailableCode);
    }

    [Fact]
    public void Attached_IsRefusedForNonAndroidTargetsAndUndeclaredOracles()
    {
        var provider = CreateProvider(new QueueRunner());

        Assert.True(provider.SupportsAttachedRun(CreatePlan(), "android"));
        Assert.False(provider.SupportsAttachedRun(CreatePlan(), "ios"));
        Assert.False(provider.SupportsAttachedRun(CreatePlan(evidenceKind: "other-kind"), "android"));
        Assert.False(provider.SupportsAttachedRun(plan: null, "android"));
    }

    [Fact]
    public async Task Attached_AmbiguousDevice_CertifiesNothing()
    {
        // Two emulators are attached and neither answers to the AVD the agent named. Reading a
        // guessed device would certify another app's storage as this run's business outcome.
        var provider = CreateProvider(new QueueRunner(
            Ok("List of devices attached\nemulator-5554\tdevice\nemulator-5556\tdevice\n"),
            Ok("other-avd\nOK\n"),
            Ok("another-avd\nOK\n")));

        var baseline = await provider.ObserveAttachedBaselineAsync(CreateAttachedTarget(CreatePlan()));

        Assert.False(baseline.Observed);
        Assert.Equal("android-app-storage-device-unresolved", baseline.UnavailableCode);
    }

    [Fact]
    public async Task Attached_UnbootedDevice_IsNotUsed()
    {
        // An offline device cannot serve a run-as read, so it must not be counted as the single
        // attached candidate for an agent that recognised nothing about its host.
        var provider = CreateProvider(new QueueRunner(
            Ok("List of devices attached\nemulator-5554\toffline\n")));
        var target = CreateAttachedTarget(CreatePlan()) with { DeviceIdentity = null };

        var baseline = await provider.ObserveAttachedBaselineAsync(target);

        Assert.False(baseline.Observed);
        Assert.Equal("android-app-storage-device-unresolved", baseline.UnavailableCode);
    }

    private static ProcessResult Ok(string standardOutput)
        => new() { ExitCode = 0, StandardOutput = standardOutput };

    private static MauiTestPlan CreatePlan(
        string evidenceKind = "android-app-storage",
        string reference = "files/ledger.jsonl",
        bool required = true,
        bool independent = true,
        string expect = """{ "contains": ["\"id\":\"todo-0001\""] }""")
    {
        var declaration = new MauiIndependentBusinessOracleDeclaration
        {
            OracleId = "todo-ledger-record",
            Required = required,
            Independent = independent,
            EvidenceKind = evidenceKind,
            Reference = reference,
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["expect"] = JsonDocument.Parse(expect).RootElement.Clone(),
            },
        };

        return new MauiTestPlan
        {
            Schema = 1,
            PlanId = "plan-oracle-test",
            IndependentBusinessOracles = [declaration],
        };
    }

    private static FlowStateEvidenceRequest CreateRequest(MauiTestPlan plan, string platform) => new()
    {
        Plan = plan,
        Flow = new MauiFlow { Schema = 2, Name = "flow" },
        Artifact = CreateArtifact(platform),
    };

    private static FlowPostRunOracleEvaluationRequest CreateEvaluationRequest(MauiTestPlan plan) => new()
    {
        Plan = plan,
        Flow = new MauiFlow { Schema = 2, Name = "flow" },
        Artifact = CreateArtifact("android"),
        RunId = "run-1",
        FlowDigest = "digest-1",
        DeviceIdentityFingerprint = "device-fingerprint",
        AppBuildFingerprint = "build-fingerprint",
        PackageDigest = "package-digest",
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(5),
        EvaluationDeadline = DateTimeOffset.MaxValue,
        Report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome { Status = "passed", Terminal = true },
        },
        Platform = "android",
        DeviceSerial = "emulator-5554",
        PackageId = "com.example.app",
    };

    private static ResolvedAppArtifact CreateArtifact(string platform) => new()
    {
        Path = "app.apk",
        ProjectPath = "app.csproj",
        AgentSessionId = "session",
        TargetFramework = "net10.0-" + platform,
        TargetPlatformIdentifier = platform,
        Configuration = "Debug",
        ArtifactType = "apk",
        PackageDigest = "package-digest",
    };

    private sealed class QueueRunner(params ProcessResult[] results) : IExecutionProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<(string FileName, string[] Arguments)> Calls { get; } = [];

        public List<TimeSpan?> Timeouts { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory = null,
            TimeSpan? timeout = null,
            IEnumerable<string>? environmentVariablesToRemove = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToArray()));
            Timeouts.Add(timeout);
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new ProcessResult { ExitCode = 0 });
        }
    }
}
