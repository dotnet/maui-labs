using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

/// <summary>
/// Evaluates independent business oracles by reading a file from the app's private Android
/// storage over adb, outside the DevFlow agent channel the flow itself drove.
/// </summary>
/// <remarks>
/// <para>
/// Independence here is channel independence. The flow talks to the in-app DevFlow agent and
/// asserts on what the UI reports about itself; this provider reaches the same device through a
/// different transport (adb) and reads what the app durably committed. An app that renders a
/// success state without committing anything satisfies the flow assertion and fails this oracle.
/// </para>
/// <para>
/// Freshness rests on an admission rule this provider does not own: the Android adapter refuses a
/// run when the package is already installed (<c>android-preexisting-app-unsafe</c>), so
/// app-private storage is necessarily empty when the run starts and anything read afterwards was
/// written by this run. The provider therefore refuses any non-Android request rather than
/// inheriting a weaker guarantee. Reaching storage at all also requires a debuggable build, which
/// <c>flow run</c> separately enforces by refusing a non-Debug configuration.
/// </para>
/// <para>
/// It cannot make a run verified on its own. The plan must still declare the oracle as required
/// and independent, and the flow must still cover its scenarios and acceptance criteria with hard
/// assertions. This provider only supplies the post-run evidence half of that contract, bound to
/// the exact run, device, build, flow, and evaluation window.
/// </para>
/// </remarks>
internal sealed partial class AndroidAppStorageEvidenceProvider : IFlowStateEvidenceProvider, IAttachedRunOracleEvaluator
{
    /// <summary>The <c>evidenceKind</c> a plan oracle declares to select this provider.</summary>
    public const string AndroidAppStorageEvidenceKind = "android-app-storage";

    private const int MaximumEvidenceCharacters = 256 * 1024;
    private const int MaximumPredicates = 32;
    private const int MaximumPredicateCharacters = 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Directories Android actually exposes under an app's private data root.</summary>
    private static readonly string[] AppStorageRoots =
        ["files", "cache", "databases", "shared_prefs", "no_backup"];

    private static readonly string[] ExpectationNames = ["contains", "absent"];

    private readonly IAndroidProvider _androidProvider;
    private readonly IExecutionProcessRunner _processRunner;
    private readonly TimeProvider _clock;

    public AndroidAppStorageEvidenceProvider(
        IAndroidProvider androidProvider,
        IExecutionProcessRunner processRunner,
        TimeProvider? clock = null)
    {
        _androidProvider = androidProvider ?? throw new ArgumentNullException(nameof(androidProvider));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _clock = clock ?? TimeProvider.System;
    }

    public string ProviderId => "android-app-storage";

    public bool Supports(FlowStateEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return IsAndroidArtifact(request.Artifact) && ReadDeclarations(request.Plan).Count > 0;
    }

    public Task<FlowStateEvidenceResult> PrepareAsync(
        FlowStateEvidenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var declaration in ReadDeclarations(request.Plan))
            _ = ParseDeclaration(declaration);

        // The oracle only reads app storage, so it adds no reset or seed obligation of its own.
        // Admission stays exactly what the plan's own side-effect policy already established.
        return Task.FromResult(new FlowStateEvidenceResult
        {
            RunContext = new MauiFlowRunContext
            {
                Intent = MauiFlowReplayIntents.OrdinaryReplay,
                Preconditions = new MauiFlowReplayPreconditions
                {
                    Expected = new MauiFlowCheckpoint(),
                    ObservationDeferredUntilLaunch = true,
                },
                BusinessOracles = [],
                PriorMutationCompletionCertain = true,
            },
        });
    }

    public async Task<FlowPostRunOracleEvidenceResult> EvaluatePostRunAsync(
        FlowPostRunOracleEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.Platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            return new FlowPostRunOracleEvidenceResult
            {
                Supported = false,
                DetailCode = "android-app-storage-platform-mismatch",
                Message = "The Android app-storage oracle can only read evidence from an Android run.",
            };
        }

        var serial = request.DeviceSerial;
        var packageId = request.PackageId;
        if (!IsValidSerial(serial) || string.IsNullOrEmpty(packageId) || !PackagePattern().IsMatch(packageId))
        {
            return new FlowPostRunOracleEvidenceResult
            {
                Supported = false,
                DetailCode = "android-app-storage-target-invalid",
                Message = "The Android device serial or package identity is not a safe adb argument.",
            };
        }

        var adbPath = ResolveAdbPath();
        if (adbPath is null)
        {
            return new FlowPostRunOracleEvidenceResult
            {
                Supported = false,
                DetailCode = "android-adb-not-found",
                Message = "ADB was not found in the configured Android SDK, so app-storage evidence cannot be read.",
            };
        }

        var results = new List<MauiIndependentBusinessOracleResult>();
        foreach (var declaration in ReadDeclarations(request.Plan))
        {
            var parsed = ParseDeclaration(declaration);

            // Evidence observed after the deadline would fail the run binding, so each read is
            // bounded by whatever is left of the window rather than by its own timeout alone.
            var remaining = request.EvaluationDeadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return new FlowPostRunOracleEvidenceResult
                {
                    Supported = false,
                    DetailCode = "android-app-storage-deadline-elapsed",
                    Message = "The bounded post-run evaluation window closed before app-storage evidence could be read.",
                };
            }

            var read = await ReadAppStorageFileAsync(
                adbPath,
                serial,
                packageId,
                parsed.RelativePath,
                remaining < ReadTimeout ? remaining : ReadTimeout,
                cancellationToken).ConfigureAwait(false);
            if (read.ChannelFailureCode is not null)
            {
                return new FlowPostRunOracleEvidenceResult
                {
                    Supported = false,
                    DetailCode = read.ChannelFailureCode,
                    Message = read.ChannelFailureMessage,
                };
            }

            results.Add(BuildResult(parsed, read.Content));
        }

        return new FlowPostRunOracleEvidenceResult
        {
            RunId = request.RunId,
            FlowDigest = request.FlowDigest,
            DeviceIdentityFingerprint = request.DeviceIdentityFingerprint,
            AppBuildFingerprint = request.AppBuildFingerprint,
            PackageDigest = request.PackageDigest,
            StartedAt = request.StartedAt,
            EndedAt = request.EndedAt,
            ObservedAt = _clock.GetUtcNow(),
            BusinessOracles = results,
        };
    }

    // ── Attached runs ───────────────────────────────────────────────────────────────────────────

    public bool SupportsAttachedRun(MauiTestPlan? plan, string? platform)
        => string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase) &&
           ReadDeclarations(plan).Count > 0;

    public async Task<AttachedRunOracleBaseline> ObserveAttachedBaselineAsync(
        AttachedRunOracleTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!SupportsAttachedRun(target.Plan, target.Platform))
        {
            return AttachedRunOracleBaseline.Unavailable(
                "android-app-storage-attached-unsupported",
                "The Android app-storage oracle can only read evidence from an attached Android run.");
        }

        var reads = await ReadDeclaredEvidenceAsync(target, cancellationToken).ConfigureAwait(false);
        if (reads.FailureCode is not null)
            return AttachedRunOracleBaseline.Unavailable(reads.FailureCode, reads.FailureMessage!);

        var preExisting = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        foreach (var observation in reads.Observations)
        {
            var content = observation.Content;
            preExisting[observation.Oracle.OracleId] = content is null
                ? []
                : [.. Enumerable.Range(0, observation.Oracle.Contains.Count)
                    .Where(index => content.Contains(observation.Oracle.Contains[index], StringComparison.Ordinal))];
        }

        return new AttachedRunOracleBaseline { Observed = true, PreExistingContains = preExisting };
    }

    public async Task<IReadOnlyList<MauiIndependentBusinessOracleResult>> EvaluateAttachedAsync(
        AttachedRunOracleTarget target,
        AttachedRunOracleBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(baseline);

        // Without a baseline there is no evidence that anything read now was produced by this run.
        // Reporting no result leaves the run unverified, which is the correct outcome; reporting a
        // successful one would certify a record that may have predated the run entirely.
        if (!baseline.Observed)
            return [];

        var reads = await ReadDeclaredEvidenceAsync(target, cancellationToken).ConfigureAwait(false);
        if (reads.FailureCode is not null)
            return [];

        return
        [
            .. reads.Observations.Select(observation => BuildResult(
                observation.Oracle,
                observation.Content,
                baseline.PreExistingContains.TryGetValue(observation.Oracle.OracleId, out var pre)
                    ? pre
                    : [])),
        ];
    }

    /// <summary>Reads every declared oracle's evidence file once, bounded by the target deadline.</summary>
    private async Task<DeclaredEvidenceReads> ReadDeclaredEvidenceAsync(
        AttachedRunOracleTarget target,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target.PackageId) || !PackagePattern().IsMatch(target.PackageId))
        {
            return DeclaredEvidenceReads.Failure(
                "android-app-storage-target-invalid",
                "The Android package identity is not a safe adb argument.");
        }

        var adbPath = ResolveAdbPath();
        if (adbPath is null)
        {
            return DeclaredEvidenceReads.Failure(
                "android-adb-not-found",
                "ADB was not found in the configured Android SDK, so app-storage evidence cannot be read.");
        }

        var serial = await ResolveAttachedSerialAsync(adbPath, target, cancellationToken).ConfigureAwait(false);
        if (serial is null)
        {
            return DeclaredEvidenceReads.Failure(
                "android-app-storage-device-unresolved",
                "The exact Android device running the attached app could not be identified, so its " +
                "app-private storage cannot be read without guessing which device to reach.");
        }

        var observations = new List<AppStorageObservation>();
        foreach (var declaration in ReadDeclarations(target.Plan))
        {
            var parsed = ParseDeclaration(declaration);
            var remaining = target.Deadline - _clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return DeclaredEvidenceReads.Failure(
                    "android-app-storage-deadline-elapsed",
                    "The bounded evaluation window closed before app-storage evidence could be read.");
            }

            var read = await ReadAppStorageFileAsync(
                adbPath,
                serial,
                target.PackageId,
                parsed.RelativePath,
                remaining < ReadTimeout ? remaining : ReadTimeout,
                cancellationToken).ConfigureAwait(false);
            if (read.ChannelFailureCode is not null)
                return DeclaredEvidenceReads.Failure(read.ChannelFailureCode, read.ChannelFailureMessage!);

            observations.Add(new AppStorageObservation(parsed, read.Content));
        }

        return new DeclaredEvidenceReads { Observations = observations };
    }

    /// <summary>
    /// Finds the adb serial of the device the attached app is running on, or null when that cannot
    /// be established without guessing.
    /// </summary>
    /// <remarks>
    /// Reaching the wrong device would read another app's storage and certify a business outcome
    /// that never happened here, so every ambiguous case resolves to null. An agent that named its
    /// AVD must match exactly one attached emulator; an agent that recognised nothing about its
    /// host is accepted only when exactly one device is attached at all.
    /// </remarks>
    private async Task<string?> ResolveAttachedSerialAsync(
        string adbPath,
        AttachedRunOracleTarget target,
        CancellationToken cancellationToken)
    {
        var attached = await ListAttachedSerialsAsync(adbPath, cancellationToken).ConfigureAwait(false);
        if (attached.Count == 0)
            return null;

        var avdName = ReadAvdName(target.DeviceIdentity);
        if (avdName is null)
            return attached.Count == 1 ? attached[0] : null;

        string? matched = null;
        foreach (var serial in attached)
        {
            var name = await ReadEmulatorAvdNameAsync(adbPath, serial, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(name, avdName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (matched is not null)
                return null;
            matched = serial;
        }

        return matched;
    }

    private async Task<IReadOnlyList<string>> ListAttachedSerialsAsync(
        string adbPath,
        CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                adbPath,
                ["devices"],
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        if (!result.Success)
            return [];

        return
        [
            .. (result.StandardOutput ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => line.Split('\t', StringSplitOptions.TrimEntries))
                // Only a fully booted 'device' state can serve a run-as read; 'offline' and
                // 'unauthorized' would fail later with a channel error that reads like a defect.
                .Where(static parts => parts.Length == 2 &&
                    string.Equals(parts[1], "device", StringComparison.Ordinal) &&
                    IsValidSerial(parts[0]))
                .Select(static parts => parts[0]),
        ];
    }

    private async Task<string?> ReadEmulatorAvdNameAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _processRunner.RunAsync(
                adbPath,
                ["-s", serial, "emu", "avd", "name"],
                timeout: TimeSpan.FromSeconds(15),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return null;

            // The emulator console answers with the name followed by its own OK acknowledgement.
            return (result.StandardOutput ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(static line => !string.Equals(line, "OK", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static string? ReadAvdName(string? deviceIdentity)
        => deviceIdentity?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(static parts => parts.Length == 2 &&
                string.Equals(parts[0], "avd", StringComparison.OrdinalIgnoreCase))
            .Select(static parts => parts[1])
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private sealed record AppStorageObservation(AppStorageOracle Oracle, string? Content);

    private sealed record DeclaredEvidenceReads
    {
        public IReadOnlyList<AppStorageObservation> Observations { get; init; } = [];
        public string? FailureCode { get; init; }
        public string? FailureMessage { get; init; }

        public static DeclaredEvidenceReads Failure(string code, string message)
            => new() { FailureCode = code, FailureMessage = message };
    }

    /// <summary>
    /// Evaluates the declared predicates. The reason never repeats file content, because app
    /// storage can hold user data that must not reach a report.
    /// </summary>
    private MauiIndependentBusinessOracleResult BuildResult(AppStorageOracle oracle, string? content)
        => BuildResult(oracle, content, preExistingContains: []);

    private MauiIndependentBusinessOracleResult BuildResult(
        AppStorageOracle oracle,
        string? content,
        IReadOnlyList<int> preExistingContains)
    {
        var succeeded = false;
        string message;
        if (content is null)
        {
            message = "The app did not commit the declared evidence file to its private storage.";
        }
        else if (preExistingContains.Count > 0)
        {
            // The record was already there before the run, so its presence afterwards says nothing
            // about what this run did. Failing closed is the only honest reading.
            message =
                $"The record required by expect.contains[{preExistingContains[0]}] already existed before this run, " +
                "so this evidence does not show that this run committed it.";
        }
        else if (FirstUnsatisfiedPredicate(oracle, content) is { } unsatisfied)
        {
            message = unsatisfied;
        }
        else
        {
            succeeded = true;
            message = "The app committed the declared business record to its private storage.";
        }

        return new MauiIndependentBusinessOracleResult
        {
            OracleId = oracle.OracleId,
            Independent = true,
            Succeeded = succeeded,
            ObservedAt = _clock.GetUtcNow(),
            // The path is redacted to a hash by the report serializer because it contains '/', so
            // the oracle id is used instead: it survives redaction and identifies which check ran.
            EvidenceReference = AndroidAppStorageEvidenceKind + ":" + oracle.OracleId,
            Message = message,
        };
    }

    private static string? FirstUnsatisfiedPredicate(AppStorageOracle oracle, string content)
    {
        for (var index = 0; index < oracle.Contains.Count; index++)
        {
            if (!content.Contains(oracle.Contains[index], StringComparison.Ordinal))
                return $"The evidence file does not contain the record required by expect.contains[{index}].";
        }
        for (var index = 0; index < oracle.Absent.Count; index++)
        {
            if (content.Contains(oracle.Absent[index], StringComparison.Ordinal))
                return $"The evidence file contains the record forbidden by expect.absent[{index}].";
        }
        return null;
    }

    private async Task<AppStorageRead> ReadAppStorageFileAsync(
        string adbPath,
        string serial,
        string packageId,
        string relativePath,
        TimeSpan readTimeout,
        CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                adbPath,
                ["-s", serial, "shell", "run-as", packageId, "cat", relativePath],
                timeout: readTimeout,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return AppStorageRead.ChannelFailure(
                "android-app-storage-read-failed",
                "ADB could not read app-private storage for the independent business oracle.");
        }

        if (result.Success)
        {
            // ProcessRunner rejoins device output with the host newline, so predicates are matched
            // against the device's own line endings rather than a Windows-only rewrite.
            var content = (result.StandardOutput ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
            return content.Length > MaximumEvidenceCharacters
                ? AppStorageRead.ChannelFailure(
                    "android-app-storage-evidence-too-large",
                    "The declared evidence file exceeds the bounded read limit for oracle evaluation.")
                : AppStorageRead.Found(content);
        }

        // A missing file is a business outcome: the app did not commit what the plan requires.
        // Anything else means the evidence channel itself is unusable and nothing can be certified.
        // The device-side exit code and empty stdout are required as well as the diagnostic text,
        // because ProcessRunner reports a failure to launch adb at all as exit -1 carrying the same
        // 'No such file or directory' wording, and app storage may legitimately contain it.
        var missing = result.ExitCode == 1 &&
            string.IsNullOrEmpty(result.StandardOutput) &&
            (result.StandardError ?? string.Empty).Contains(
                "No such file or directory",
                StringComparison.OrdinalIgnoreCase);

        return missing
            ? AppStorageRead.Missing()
            : AppStorageRead.ChannelFailure(
                "android-app-storage-read-failed",
                "ADB could not read app-private storage for the independent business oracle. " +
                "The app build must be debuggable for 'adb shell run-as' to reach its storage.");
    }

    private string? ResolveAdbPath()
    {
        var sdkPath = _androidProvider.SdkPath;
        if (string.IsNullOrWhiteSpace(sdkPath))
            return null;

        var executable = OperatingSystem.IsWindows() ? "adb.exe" : "adb";
        var path = Path.Combine(sdkPath, "platform-tools", executable);
        return File.Exists(path) ? path : null;
    }

    private static bool IsAndroidArtifact(ResolvedAppArtifact artifact)
        => artifact is not null &&
           (string.Equals(artifact.TargetPlatformIdentifier, "android", StringComparison.OrdinalIgnoreCase) ||
            artifact.TargetFramework.Contains("-android", StringComparison.OrdinalIgnoreCase));

    private static List<MauiIndependentBusinessOracleDeclaration> ReadDeclarations(MauiTestPlan? plan)
        => (plan?.IndependentBusinessOracles ?? [])
            .Where(static oracle =>
                oracle is not null &&
                oracle.Required &&
                oracle.Independent &&
                string.Equals(oracle.EvidenceKind, AndroidAppStorageEvidenceKind, StringComparison.Ordinal))
            .ToList();

    private static AppStorageOracle ParseDeclaration(MauiIndependentBusinessOracleDeclaration declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration.OracleId))
        {
            throw FlowExecutionException.Invalid(
                "android-app-storage-oracle-id-missing",
                $"An '{AndroidAppStorageEvidenceKind}' business oracle requires an oracleId.");
        }

        var relativePath = declaration.Reference;
        if (string.IsNullOrWhiteSpace(relativePath) || !IsSafeRelativeDevicePath(relativePath))
        {
            throw FlowExecutionException.Invalid(
                "android-app-storage-reference-invalid",
                $"Business oracle '{declaration.OracleId}' must set reference to a relative app-storage path " +
                $"under {string.Join(", ", AppStorageRoots)}, such as 'files/todo-ledger.jsonl'. " +
                "Absolute paths, parent traversal, option-like segments, and shell metacharacters are refused.");
        }

        var contains = ReadPredicates(declaration, "contains");
        var absent = ReadPredicates(declaration, "absent");
        if (contains.Count == 0 && absent.Count == 0)
        {
            throw FlowExecutionException.Invalid(
                "android-app-storage-expectation-missing",
                $"Business oracle '{declaration.OracleId}' must declare expect.contains or expect.absent. " +
                "An oracle that checks nothing cannot verify a business result.");
        }

        ValidateDeclaredKeys(declaration);
        return new AppStorageOracle(declaration.OracleId!, relativePath!, contains, absent);
    }

    /// <summary>
    /// Refuses keys this provider does not evaluate. Silently ignoring a misspelled predicate such
    /// as <c>notContains</c> would let a plan read as stricter than the check it actually performs,
    /// and a reviewer would have no way to see the difference.
    /// </summary>
    private static void ValidateDeclaredKeys(MauiIndependentBusinessOracleDeclaration declaration)
    {
        foreach (var key in declaration.ExtensionData!.Keys)
        {
            if (key is not "expect")
            {
                throw FlowExecutionException.Invalid(
                    "android-app-storage-expectation-invalid",
                    $"Business oracle '{declaration.OracleId}' declares unsupported key '{key}'. " +
                    $"An '{AndroidAppStorageEvidenceKind}' oracle accepts only 'expect'.");
            }
        }

        foreach (var property in declaration.ExtensionData["expect"].EnumerateObject())
        {
            if (!ExpectationNames.Contains(property.Name, StringComparer.Ordinal))
            {
                throw FlowExecutionException.Invalid(
                    "android-app-storage-expectation-invalid",
                    $"Business oracle '{declaration.OracleId}' declares unsupported expectation " +
                    $"'expect.{property.Name}'. Only expect.contains and expect.absent are evaluated.");
            }
        }
    }

    private static List<string> ReadPredicates(
        MauiIndependentBusinessOracleDeclaration declaration,
        string name)
    {
        if (declaration.ExtensionData is null ||
            !declaration.ExtensionData.TryGetValue("expect", out var expect) ||
            expect.ValueKind != JsonValueKind.Object ||
            !expect.TryGetProperty(name, out var values))
        {
            return [];
        }

        if (values.ValueKind != JsonValueKind.Array)
        {
            throw FlowExecutionException.Invalid(
                "android-app-storage-expectation-invalid",
                $"Business oracle '{declaration.OracleId}' declares expect.{name} that is not an array of strings.");
        }

        var predicates = new List<string>();
        foreach (var value in values.EnumerateArray())
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrEmpty(text) ||
                text.Length > MaximumPredicateCharacters ||
                text.Contains('\n', StringComparison.Ordinal) ||
                text.Contains('\r', StringComparison.Ordinal))
            {
                throw FlowExecutionException.Invalid(
                    "android-app-storage-expectation-invalid",
                    $"Business oracle '{declaration.OracleId}' declares an expect.{name} entry that is not a bounded, " +
                    "single-line, non-empty string. Match one committed record per predicate.");
            }
            predicates.Add(text);
        }
        if (predicates.Count > MaximumPredicates)
        {
            throw FlowExecutionException.Invalid(
                "android-app-storage-expectation-invalid",
                $"Business oracle '{declaration.OracleId}' declares more than {MaximumPredicates} expect.{name} entries.");
        }
        return predicates;
    }

    /// <summary>
    /// <c>adb shell</c> reassembles its arguments into a device-side shell command and <c>cat</c>
    /// reads leading-dash arguments as options, so the path is constrained to a real app-storage
    /// subtree rather than merely screened for metacharacters. Without the root allow-list a
    /// reference such as <c>--help</c> makes <c>cat</c> print usage and exit zero, which would let
    /// an oracle pass without app storage ever being read.
    /// </summary>
    private static bool IsSafeRelativeDevicePath(string path)
    {
        if (path.Length > 512 || !DevicePathPattern().IsMatch(path))
            return false;

        var segments = path.Split('/');
        if (segments.Length < 2 || !AppStorageRoots.Contains(segments[0]))
            return false;

        return segments.All(static segment =>
            segment is not ("" or "." or "..") && !segment.StartsWith('-'));
    }

    private static bool IsValidSerial(string? serial)
        => !string.IsNullOrWhiteSpace(serial) &&
           serial.Length <= 256 &&
           !serial.StartsWith('-') &&
           serial.All(static character =>
               char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':');

    [GeneratedRegex(@"\A[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex PackagePattern();

    [GeneratedRegex(@"\A[A-Za-z0-9._/-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePathPattern();

    private sealed record AppStorageOracle(
        string OracleId,
        string RelativePath,
        IReadOnlyList<string> Contains,
        IReadOnlyList<string> Absent);

    private sealed record AppStorageRead(
        string? Content,
        string? ChannelFailureCode,
        string? ChannelFailureMessage)
    {
        public static AppStorageRead Found(string content) => new(content, null, null);
        public static AppStorageRead Missing() => new(null, null, null);
        public static AppStorageRead ChannelFailure(string code, string message) => new(null, code, message);
    }
}
