using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Experimental native AppKit lifecycle. This fixture is intentionally selected only by the
/// <c>macos</c> platform key; it never represents Mac Catalyst coverage. Reset is restricted to
/// the exact Debug fixture process and its test-only in-memory seed extension.
/// </summary>
public sealed class MacOSFixture : AppFixtureBase, IPlatformFlowTestLifecycle
{
    const string PackageId = "com.companyname.mauitodo.appkit";
    const string TargetFramework = "net10.0-macos";
    const string ResetIdentity = "appkit-safe-test-relaunch-v1";
    const string IntegrationTestSeedId = "devflow-sample-v1";
    const int MaximumDiagnosticCharacters = 64 * 1024;

    readonly StringBuilder _standardOutput = new();
    readonly StringBuilder _standardError = new();
    HashSet<string> _crashReportsBeforeLaunch = new(StringComparer.OrdinalIgnoreCase);
    Process? _appProcess;
    int? _ownedProcessId;
    DateTimeOffset _launchedAt;
    PlatformBuildResult? _build;
    MauiFlowResetResult? _reset;
    PlatformSeedResult? _seed;
    PlatformLaunchResult? _launch;
    PlatformAgentIdentity? _agent;

    public override string Platform => MacOSFlowCapabilities.PlatformKey;
    public override bool SupportsFlowLifecycle => true;
    internal override IPlatformFlowTestLifecycle FlowLifecycle => this;
    protected override bool CanReuseExistingAgent => false;

    internal MacOSFlowTestHost CreateFlowTestHost() => new(this);

    protected override async Task InitializePlatformAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                "The experimental AppKit fixture requires a macOS host.");
        }

        await BuildAsync().ConfigureAwait(false);
        await InstallAsync().ConfigureAwait(false);
        await HardResetAsync(new PlatformFlowResetRequest { Strategy = "safe-test-relaunch" }).ConfigureAwait(false);
        await LaunchAsync().ConfigureAwait(false);
        await WaitForExpectedAgentAsync(new PlatformAgentExpectation
        {
            PackageId = PackageId,
            ExpectedProcessId = _launch?.ProcessId,
            PreviousAgent = _launch?.PreviousAgent,
        }).ConfigureAwait(false);
        await SeedAsync(new PlatformFlowSeedRequest()).ConfigureAwait(false);
        await VerifyCheckpointAsync(new PlatformCheckpointRequest
        {
            Expected = new MauiFlowCheckpoint { Route = "//native" },
            Reset = _reset!,
            Seed = _seed!,
        }).ConfigureAwait(false);
    }

    protected override async Task DisposePlatformAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _appProcess?.Dispose();
            _appProcess = null;
            _ownedProcessId = null;
        }
    }

    internal async Task<PlatformBuildResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        if (_build is { } cached && Directory.Exists(cached.ArtifactPath))
            return cached;

        PlatformBuildResult? build = null;
        await WithBuildLockAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = GetAppKitSampleProjectPath();
            await BuildSampleAsync(
                projectPath,
                TargetFramework,
                $"-p:MauiDevFlowPort={AgentPort} -p:DevFlowIntegrationTest=true").ConfigureAwait(false);
            var appBundle = FindAppBundle();
            build = new PlatformBuildResult
            {
                ArtifactPath = appBundle,
                AppBuildFingerprint = await AppleFlowLifecycleSupport
                    .ComputeFileFingerprintAsync(Path.Combine(appBundle, "Contents", "Info.plist"), cancellationToken)
                    .ConfigureAwait(false),
                PackageId = PackageId,
            };
        }).ConfigureAwait(false);

        _build = build ?? throw PlatformFlowLifecycleException.Infrastructure(
            "The experimental AppKit build did not produce a launchable app bundle.");
        return _build;
    }

    internal async Task<PlatformInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var build = _build ?? await BuildAsync(cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(build.ArtifactPath))
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                "The experimental AppKit app bundle is unavailable for launch.");
        }

        // AppKit test builds are directly launched from their fixture-owned bundle. There is no
        // package install, user-container reset, Keychain deletion, or system-wide state change.
        return new PlatformInstallResult
        {
            PackageId = build.PackageId,
            ArtifactPath = build.ArtifactPath,
            Reinstalled = false,
        };
    }

    internal async Task<MauiFlowResetResult> HardResetAsync(
        PlatformFlowResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var strategy = request.Strategy ?? request.Requirement?.Strategy;
        if (!string.IsNullOrWhiteSpace(strategy) &&
            strategy is not "safe-test-relaunch" and not "app-private-state")
        {
            throw PlatformFlowLifecycleException.Precondition(
                "Experimental AppKit requires the safe-test-relaunch reset strategy.");
        }

        _agent ??= await TryGetAgentIdentityAsync(cancellationToken).ConfigureAwait(false);
        await StopOwnedProcessAsync(cancellationToken).ConfigureAwait(false);
        _reset = new MauiFlowResetResult
        {
            Requested = true,
            Succeeded = true,
            AppStateSucceeded = true,
            BackendTestDataSucceeded = true,
            Strategy = "safe-test-relaunch",
            ResetIdentity = request.Requirement?.ResetIdentity ?? ResetIdentity,
            Reference = request.Requirement?.Reference ?? new MauiFlowResetReference
            {
                Strategy = "safe-test-relaunch",
                ResetId = ResetIdentity,
                Scope = "owned-debug-appkit-fixture-process-memory",
                Version = "1",
                EvidenceReference = "appkit-test-seed-hook",
            },
            Outcome = new MauiFlowResetOutcome
            {
                Requested = true,
                Succeeded = true,
                AppStateSucceeded = true,
                BackendTestDataSucceeded = true,
                CompletedAt = DateTimeOffset.UtcNow,
                EvidenceReference = "appkit-test-seed-hook",
                Message = "Only the exact fixture-owned AppKit process was terminated; no user state was deleted.",
            },
            Message = "Experimental AppKit test-only process reset completed.",
        };
        return _reset;
    }

    internal async Task<PlatformSeedResult> SeedAsync(
        PlatformFlowSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        _seed = await AppleFlowLifecycleSupport.SeedAsync(Client, request, _reset, cancellationToken)
            .ConfigureAwait(false);
        return _seed;
    }

    internal async Task<PlatformLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var build = _build ?? await BuildAsync(cancellationToken).ConfigureAwait(false);
        var previous = _agent ?? await TryGetAgentIdentityAsync(cancellationToken).ConfigureAwait(false);
        await StopOwnedProcessAsync(cancellationToken).ConfigureAwait(false);

        _standardOutput.Clear();
        _standardError.Clear();
        _crashReportsBeforeLaunch = SnapshotCrashReports();
        _launchedAt = DateTimeOffset.UtcNow;
        var executable = FindExecutable(build.ArtifactPath);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString(CultureInfo.InvariantCulture);
        start.Environment["DEVFLOW_INTEGRATION_TEST_SEED"] = IntegrationTestSeedId;
        start.Environment["DEVFLOW_TEST_APPKIT_LIFECYCLE"] = "1";

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => AppendDiagnosticLine(_standardOutput, args.Data);
        process.ErrorDataReceived += (_, args) => AppendDiagnosticLine(_standardError, args.Data);
        if (!process.Start())
        {
            process.Dispose();
            throw PlatformFlowLifecycleException.Infrastructure(
                "The experimental AppKit app process could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _appProcess = process;
        _ownedProcessId = process.Id;
        _launch = new PlatformLaunchResult
        {
            PackageId = build.PackageId,
            ProcessId = _ownedProcessId,
            PreviousAgent = previous,
        };
        return _launch;
    }

    internal async Task<PlatformAgentReadyResult> WaitForExpectedAgentAsync(
        PlatformAgentExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readiness = PlatformAgentReadiness.WaitForExpectedAsync(
            _ => Client.GetStatusAsync(),
            expectation,
            cancellationToken: linkedCancellation.Token);

        while (!readiness.IsCompleted)
        {
            if (_appProcess is { HasExited: true } exited)
            {
                linkedCancellation.Cancel();
                try
                {
                    await readiness.ConfigureAwait(false);
                }
                catch
                {
                    // The owned-process exit fact below is the actionable failure.
                }

                var exitCode = TryGetExitCode(exited);
                await CaptureProcessExitDiagnosticsAsync(exitCode, CancellationToken.None).ConfigureAwait(false);
                throw PlatformFlowLifecycleException.Infrastructure(
                    $"The fixture-owned AppKit process {_ownedProcessId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"} " +
                    $"exited before a new DevFlow agent became ready (exit code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"}).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        var ready = await readiness.ConfigureAwait(false);
        _agent = ready.Agent;
        return ready;
    }

    internal async Task<PlatformCheckpointVerification> VerifyCheckpointAsync(
        PlatformCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var build = _build ?? throw PlatformFlowLifecycleException.Precondition(
            "The experimental AppKit app has not been built.");
        var seed = _seed ?? request.Seed;
        var agent = _agent ?? throw PlatformFlowLifecycleException.Precondition(
            "A new experimental AppKit DevFlow agent has not been verified.");

        var status = await Client.GetStatusAsync().ConfigureAwait(false)
            ?? throw PlatformFlowLifecycleException.Infrastructure(
                "The experimental AppKit DevFlow agent stopped responding during checkpoint verification.");
        if (!string.Equals(status.Device?.Platform, Platform, StringComparison.OrdinalIgnoreCase))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected AppKit platform '{Platform}', observed '{status.Device?.Platform ?? "<none>"}'.");
        }
        if (!string.Equals(status.App?.PackageId, build.PackageId, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected AppKit package '{build.PackageId}', observed '{status.App?.PackageId ?? "<none>"}'.");
        }
        if (status.App?.ProcessId != agent.ProcessId || status.App.ProcessId != _ownedProcessId)
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"The AppKit agent process does not match the fixture-owned process. Expected " +
                $"{_ownedProcessId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}, observed " +
                $"{status.App?.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "<none>"}.");
        }
        await EnsureOwnedExecutableMatchesBuildAsync(build, cancellationToken).ConfigureAwait(false);

        var state = await new SampleIntegrationTestControlClient(Client).GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(seed.ProcessInstanceId, state.ProcessInstanceId, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                "The AppKit test-only state endpoint belongs to a different process than the seeded fixture.");
        }
        AppleFlowLifecycleSupport.EnsureExpectedFingerprint("seed", seed.SeedFingerprint, state.SeedFingerprint);
        AppleFlowLifecycleSupport.EnsureExpectedFingerprint("backend state", seed.BackendStateFingerprint, state.BackendStateFingerprint);

        var observed = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = build.AppBuildFingerprint,
            AgentInstanceId = agent.StableId,
            SeedFingerprint = state.SeedFingerprint,
            BackendStateFingerprint = state.BackendStateFingerprint,
            Route = status.Route,
            Window = status.Window,
            Modal = status.Modal,
            Locale = status.Locale,
            Theme = status.Theme,
            Orientation = status.Orientation,
            DisplayProfile = status.DisplayProfile,
        };
        AppleFlowLifecycleSupport.EnsureObserved("route", observed.Route, "experimental AppKit");
        AppleFlowLifecycleSupport.EnsureObserved("window", observed.Window, "experimental AppKit");
        AppleFlowLifecycleSupport.EnsureObserved("locale", observed.Locale, "experimental AppKit");
        AppleFlowLifecycleSupport.EnsureObserved("theme", observed.Theme, "experimental AppKit");
        AppleFlowLifecycleSupport.EnsureObserved("display profile", observed.DisplayProfile, "experimental AppKit");
        var expected = AppleFlowLifecycleSupport.MergeCheckpoint(request.Expected, observed);
        AppleFlowLifecycleSupport.EnsureCheckpointMatches(expected, observed, "experimental AppKit");

        return new PlatformCheckpointVerification
        {
            Expected = expected,
            Observed = observed,
            Target = new MauiFlowRunTarget
            {
                TargetId = agent.StableId,
                Platform = Platform,
                DeviceProfile = observed.DisplayProfile,
                AppId = build.PackageId,
                AppBuildFingerprint = build.AppBuildFingerprint,
                PackageDigest = build.AppBuildFingerprint,
                AgentId = status.Agent?.Name,
                AgentInstanceId = agent.StableId,
                Locale = observed.Locale,
                Theme = observed.Theme,
                Orientation = observed.Orientation,
                DisplayProfile = observed.DisplayProfile,
            },
            RunContext = new MauiFlowRunContext
            {
                Intent = MauiFlowReplayIntents.OrdinaryReplay,
                Preconditions = new MauiFlowReplayPreconditions
                {
                    Expected = expected,
                    Observed = observed,
                    CheckedAt = DateTimeOffset.UtcNow,
                    EvidenceReference = "appkit-lifecycle-preflight",
                },
                Reset = request.Reset,
                BusinessOracles = seed.StateOracle is null ? [] : [seed.StateOracle],
            },
        };
    }

    internal async Task<PlatformHostDiagnostics> CaptureHostDiagnosticsAsync(
        PlatformDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = EnsureRepositoryArtifactRoot(request.ArtifactRoot);
        var runDirectory = Path.Combine(root, AppleFlowLifecycleSupport.SanitizeFileName(request.RunId));
        Directory.CreateDirectory(runDirectory);

        var facts = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["reason"] = request.Reason,
            ["platform"] = Platform,
            ["backend"] = MacOSFlowCapabilities.Backend,
            ["experimental"] = "true",
            ["packageId"] = PackageId,
            ["agentPort"] = AgentPort.ToString(CultureInfo.InvariantCulture),
            ["ownedProcessId"] = _ownedProcessId?.ToString(CultureInfo.InvariantCulture),
            ["buildFingerprint"] = _build?.AppBuildFingerprint,
            ["agentInstanceId"] = _agent?.StableId,
            ["seedFingerprint"] = _seed?.SeedFingerprint,
            ["backendStateFingerprint"] = _seed?.BackendStateFingerprint,
            ["resetIdentity"] = _reset?.ResetIdentity,
        };
        try
        {
            var status = await Client.GetStatusAsync().ConfigureAwait(false);
            facts["agentProcessId"] = status?.App?.ProcessId?.ToString(CultureInfo.InvariantCulture);
            facts["route"] = RedactRoute(status?.Route);
            facts["window"] = status?.Window;
            facts["modal"] = status?.Modal;
            facts["locale"] = status?.Locale;
            facts["theme"] = status?.Theme;
            facts["displayProfile"] = status?.DisplayProfile;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            facts["agentStatus"] = $"{ex.GetType().Name}: unavailable";
        }

        var xcode = await RunProcessAsync("xcodebuild", "-version", timeoutSeconds: 15).ConfigureAwait(false);
        facts["xcodeExitCode"] = xcode.ExitCode.ToString(CultureInfo.InvariantCulture);
        var diagnostics = new PlatformHostDiagnostics();
        await AddTextArtifactAsync(
            diagnostics,
            runDirectory,
            $"{Platform}-host-diagnostics.json",
            $"{Platform}-host-diagnostics-{AppleFlowLifecycleSupport.SanitizeFileName(request.RunId)}",
            "host-diagnostics",
            JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        var processLog = RedactDiagnosticText(
            $"[stdout]{Environment.NewLine}{_standardOutput}{Environment.NewLine}[stderr]{Environment.NewLine}{_standardError}");
        if (!string.IsNullOrWhiteSpace(processLog))
        {
            await AddTextArtifactAsync(
                diagnostics,
                runDirectory,
                $"{Platform}-owned-process.log",
                $"{Platform}-owned-process-log-{AppleFlowLifecycleSupport.SanitizeFileName(request.RunId)}",
                "host-log",
                processLog,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var crashReport in FindNewCrashReports().Take(4))
        {
            try
            {
                var content = await File.ReadAllTextAsync(crashReport, cancellationToken).ConfigureAwait(false);
                await AddTextArtifactAsync(
                    diagnostics,
                    runDirectory,
                    $"{Platform}-crash-{AppleFlowLifecycleSupport.SanitizeFileName(Path.GetFileName(crashReport))}.txt",
                    $"{Platform}-crash-report-{AppleFlowLifecycleSupport.SanitizeFileName(Path.GetFileNameWithoutExtension(crashReport))}",
                    "crash-report",
                    RedactDiagnosticText(content),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                // A crash report that cannot be safely read is omitted without hiding the primary result.
            }
        }

        return diagnostics;
    }

    internal Task StopAsync(CancellationToken cancellationToken = default)
        => StopOwnedProcessAsync(cancellationToken);

    Task<PlatformBuildResult> IPlatformFlowTestLifecycle.BuildAsync(CancellationToken cancellationToken) => BuildAsync(cancellationToken);
    Task<PlatformInstallResult> IPlatformFlowTestLifecycle.InstallAsync(CancellationToken cancellationToken) => InstallAsync(cancellationToken);
    Task<MauiFlowResetResult> IPlatformFlowTestLifecycle.HardResetAsync(PlatformFlowResetRequest request, CancellationToken cancellationToken) => HardResetAsync(request, cancellationToken);
    Task<PlatformSeedResult> IPlatformFlowTestLifecycle.SeedAsync(PlatformFlowSeedRequest request, CancellationToken cancellationToken) => SeedAsync(request, cancellationToken);
    Task<PlatformLaunchResult> IPlatformFlowTestLifecycle.LaunchAsync(CancellationToken cancellationToken) => LaunchAsync(cancellationToken);
    Task<PlatformAgentReadyResult> IPlatformFlowTestLifecycle.WaitForExpectedAgentAsync(PlatformAgentExpectation expectation, CancellationToken cancellationToken) => WaitForExpectedAgentAsync(expectation, cancellationToken);
    Task<PlatformCheckpointVerification> IPlatformFlowTestLifecycle.VerifyCheckpointAsync(PlatformCheckpointRequest request, CancellationToken cancellationToken) => VerifyCheckpointAsync(request, cancellationToken);
    Task<PlatformHostDiagnostics> IPlatformFlowTestLifecycle.CaptureHostDiagnosticsAsync(PlatformDiagnosticsRequest request, CancellationToken cancellationToken) => CaptureHostDiagnosticsAsync(request, cancellationToken);
    Task IPlatformFlowTestLifecycle.StopAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);
    async ValueTask IAsyncDisposable.DisposeAsync() => await StopAsync().ConfigureAwait(false);

    async Task StopOwnedProcessAsync(CancellationToken cancellationToken)
    {
        var process = _appProcess;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
            {
                if (_ownedProcessId is null || process.Id != _ownedProcessId.Value)
                {
                    throw PlatformFlowLifecycleException.Precondition(
                        "Refusing to terminate an AppKit process that is not owned by this fixture.");
                }

                process.Kill(entireProcessTree: true);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (PlatformFlowLifecycleException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                $"The fixture-owned AppKit process {_ownedProcessId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"} did not exit within 15 seconds.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                $"The fixture-owned AppKit process {_ownedProcessId?.ToString(CultureInfo.InvariantCulture) ?? "<unknown>"} could not be terminated.",
                ex);
        }
        finally
        {
            process.Dispose();
            _appProcess = null;
            _ownedProcessId = null;
        }
    }

    async Task<PlatformAgentIdentity?> TryGetAgentIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlatformAgentIdentity.FromStatus(await Client.GetStatusAsync().ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }

    async Task EnsureOwnedExecutableMatchesBuildAsync(
        PlatformBuildResult build,
        CancellationToken cancellationToken)
    {
        var process = _appProcess;
        if (process is null || _ownedProcessId is null || process.HasExited || process.Id != _ownedProcessId.Value)
        {
            throw PlatformFlowLifecycleException.Precondition(
                "The experimental AppKit test process is no longer owned by this fixture.");
        }

        string? executablePath;
        try
        {
            executablePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                "The fixture could not inspect its owned AppKit process executable.",
                ex);
        }
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !string.Equals(
                Path.GetFullPath(executablePath),
                Path.GetFullPath(FindExecutable(build.ArtifactPath)),
                StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                "The AppKit agent process was not launched from the fixture-built app bundle.");
        }

        var currentBuildFingerprint = await AppleFlowLifecycleSupport
            .ComputeFileFingerprintAsync(Path.Combine(build.ArtifactPath, "Contents", "Info.plist"), cancellationToken)
            .ConfigureAwait(false);
        AppleFlowLifecycleSupport.EnsureExpectedFingerprint(
            "AppKit app build",
            build.AppBuildFingerprint,
            currentBuildFingerprint);
    }

    async Task CaptureProcessExitDiagnosticsAsync(int? exitCode, CancellationToken cancellationToken)
    {
        try
        {
            var configuredRoot = Environment.GetEnvironmentVariable("DEVFLOW_FLOW_QA_ARTIFACT_ROOT");
            var root = string.IsNullOrWhiteSpace(configuredRoot)
                ? Path.Combine(FindRepoRoot(), "artifacts", "TestResults", "devflow-flow", Platform)
                : EnsureRepositoryArtifactRoot(configuredRoot);
            var directory = Path.Combine(root, "host-diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(
                directory,
                $"{Platform}-process-exit-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{_ownedProcessId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.json");
            var facts = new Dictionary<string, string?>
            {
                ["event"] = "fixture-owned-appkit-process-exited-before-agent-ready",
                ["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["processId"] = _ownedProcessId?.ToString(CultureInfo.InvariantCulture),
                ["exitCode"] = exitCode?.ToString(CultureInfo.InvariantCulture),
                ["buildFingerprint"] = _build?.AppBuildFingerprint,
                ["agentPort"] = AgentPort.ToString(CultureInfo.InvariantCulture),
                ["stderr"] = RedactDiagnosticText(_standardError.ToString()),
            };
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort diagnostics must not obscure the owned-process exit failure.
        }
    }

    static string GetAppKitSampleProjectPath()
    {
        var path = Path.Combine(FindRepoRoot(), "samples", "DevFlow.Sample.MacOS", "DevFlow.Sample.MacOS.csproj");
        if (!File.Exists(path))
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                $"The experimental AppKit fixture project was not found at '{path}'.");
        }

        return path;
    }

    static string GetAppKitSampleBuildOutputRoot()
    {
        var root = FindRepoRoot();
        var candidates = new[]
        {
            Path.Combine(root, "artifacts", "bin", "DevFlow.Sample.MacOS", "Debug"),
            Path.Combine(root, "samples", "DevFlow.Sample.MacOS", "bin", "Debug"),
        };
        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw PlatformFlowLifecycleException.Infrastructure(
                "No experimental AppKit sample build output directory was found.");
    }

    static string FindAppBundle()
    {
        var root = GetAppKitSampleBuildOutputRoot();
        var app = Directory.GetDirectories(root, "*.app", SearchOption.AllDirectories)
            .Where(path => path.Contains(TargetFramework, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return app ?? throw PlatformFlowLifecycleException.Infrastructure(
            $"No experimental AppKit .app bundle was found under '{root}'.");
    }

    static string FindExecutable(string appBundlePath)
    {
        var directory = Path.Combine(appBundlePath, "Contents", "MacOS");
        var executable = Directory.Exists(directory)
            ? Directory.GetFiles(directory)
                .FirstOrDefault(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            : null;
        return executable ?? throw PlatformFlowLifecycleException.Infrastructure(
            "The experimental AppKit bundle did not contain a launchable executable.");
    }

    static string EnsureRepositoryArtifactRoot(string path)
    {
        var repositoryRoot = Path.GetFullPath(FindRepoRoot());
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(repositoryRoot, fullPath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw PlatformFlowLifecycleException.Precondition(
                "Experimental AppKit QA diagnostics must remain under the repository artifact root.");
        }

        return fullPath;
    }

    static void AppendDiagnosticLine(StringBuilder destination, string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        lock (destination)
        {
            if (destination.Length >= MaximumDiagnosticCharacters)
                return;
            var available = MaximumDiagnosticCharacters - destination.Length;
            destination.AppendLine(line.Length <= available ? line : line[..available]);
        }
    }

    static async Task AddTextArtifactAsync(
        PlatformHostDiagnostics diagnostics,
        string directory,
        string fileName,
        string artifactId,
        string kind,
        string content,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, fileName);
        var bounded = content.Length <= MaximumDiagnosticCharacters
            ? content
            : content[..MaximumDiagnosticCharacters] + Environment.NewLine + "[truncated]";
        await File.WriteAllTextAsync(path, bounded, cancellationToken).ConfigureAwait(false);
        diagnostics.Artifacts.Add(new MauiFlowArtifactReference
        {
            ArtifactId = artifactId,
            Kind = kind,
            Path = path,
            Digest = await AppleFlowLifecycleSupport.ComputeFileFingerprintAsync(path, cancellationToken).ConfigureAwait(false),
            MediaType = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? "application/json"
                : "text/plain",
            Redacted = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    HashSet<string> SnapshotCrashReports()
        => GetCrashReportCandidates().Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    IEnumerable<string> FindNewCrashReports()
        => GetCrashReportCandidates().Where(path =>
        {
            var fullPath = Path.GetFullPath(path);
            return !_crashReportsBeforeLaunch.Contains(fullPath) &&
                   File.GetLastWriteTimeUtc(fullPath) >= _launchedAt.UtcDateTime.AddMinutes(-1);
        });

    static IEnumerable<string> GetCrashReportCandidates()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Logs",
            "DiagnosticReports");
        if (!Directory.Exists(directory))
            return [];

        return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path).StartsWith("DevFlow.Sample.MacOS", StringComparison.OrdinalIgnoreCase));
    }

    static string RedactDiagnosticText(string value)
    {
        var bounded = value.Length <= MaximumDiagnosticCharacters
            ? value
            : value[..MaximumDiagnosticCharacters] + Environment.NewLine + "[truncated]";
        var redacted = Regex.Replace(
            bounded,
            @"(?i)\b(token|password|secret|authorization|api[_-]?key)\s*([:=])\s*\S+",
            "$1$2[REDACTED]");
        return Regex.Replace(redacted, @"/Users/[^/\s]+", "/Users/[REDACTED]");
    }

    static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    static string? RedactRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return route;
        var end = route.IndexOfAny(['?', '#']);
        return end >= 0 ? route[..end] : route;
    }
}
