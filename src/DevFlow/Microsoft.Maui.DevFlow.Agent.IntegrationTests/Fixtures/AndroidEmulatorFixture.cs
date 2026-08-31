using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on an Android emulator.
/// </summary>
public sealed class AndroidEmulatorFixture : AppFixtureBase, IPlatformFlowTestLifecycle
{
    const string PackageId = "com.companyname.mauitodo";

    readonly IPlatformProcessRunner _processRunner;
    Process? _emulatorProcess;
    AndroidDeviceLifecycleOperations? _deviceLifecycle;
    CancellationTokenSource? _appMonitorCts;
    Task? _appMonitorTask;
    bool _weStartedEmulator;
    bool _appSeenRunning;
    string? _lastKnownPid;
    string? _diagnosticsDir;
    string? _serialNumber;
    int _apiLevel;
    string _sdkRoot = null!;
    PlatformBuildResult? _build;
    MauiFlowResetResult? _reset;
    PlatformSeedResult? _seed;
    PlatformLaunchResult? _launch;
    PlatformAgentIdentity? _agent;
    bool _requiresReinstall;

    public override string Platform => "android";
    public override bool SupportsFlowLifecycle => true;
    internal override IPlatformFlowTestLifecycle FlowLifecycle => this;
    protected override bool CanReuseExistingAgent => false;

    internal AndroidFlowTestHost CreateFlowTestHost() => new(this);

    public AndroidEmulatorFixture()
        : this(new SystemPlatformProcessRunner())
    {
    }

    internal AndroidEmulatorFixture(IPlatformProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    protected override async Task InitializePlatformAsync()
    {
        _sdkRoot = ResolveSdkRoot();
        _apiLevel = GetTargetApiLevel();
        var avdName = GetTargetAvdName(_apiLevel);

        // If the caller pinned a non-emulator (i.e. physical) device that is already
        // online, skip AVD provisioning entirely so the physical-device path works
        // even when cmdline-tools / system-images aren't installed locally.
        var skipAvdSetup = await IsPhysicalDeviceReadyAsync();

        if (!skipAvdSetup)
            await EnsureAvdExistsAsync(avdName, _apiLevel);

        _serialNumber = await EnsureEmulatorRunningAsync(avdName);
        _deviceLifecycle = new AndroidDeviceLifecycleOperations(
            _processRunner,
            AdbPath(),
            _serialNumber,
            PackageId,
            AgentPort);

        try
        {
            // Start with a clean log buffer so crash dumps are focused on this run.
            await RunProcessAsync(AdbPath(), $"-s {_serialNumber} logcat -c", timeoutSeconds: 10);
        }
        catch
        {
            // Best-effort only; some emulator states reject logcat clear briefly.
        }

        await BuildAsync();
        await InstallAsync();
        await HardResetAsync(new PlatformFlowResetRequest());
        if (_requiresReinstall)
            await InstallAsync();
        await LaunchAsync();
        await WaitForExpectedAgentAsync(new PlatformAgentExpectation
        {
            PackageId = PackageId,
            ExpectedProcessId = _launch?.ProcessId,
            PreviousAgent = _launch?.PreviousAgent,
        });
        await SeedAsync(new PlatformFlowSeedRequest());
        await VerifyCheckpointAsync(new PlatformCheckpointRequest
        {
            Expected = new MauiFlowCheckpoint { Route = "//native" },
            Reset = _reset!,
            Seed = _seed!,
        });

        StartAppMonitor();
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_appMonitorCts != null)
        {
            try
            {
                await _appMonitorCts.CancelAsync();
            }
            catch
            {
                // No-op
            }
        }

        if (_appMonitorTask != null)
        {
            try
            {
                await _appMonitorTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // No-op
            }
        }

        _appMonitorTask = null;
        _appMonitorCts?.Dispose();
        _appMonitorCts = null;

        if (_serialNumber != null)
        {
            try { await StopAsync(); } catch { }
        }

        if (_weStartedEmulator && _emulatorProcess is { HasExited: false })
        {
            try { await AdbAsync("emu kill", timeoutSeconds: 10); } catch { }

            try
            {
                _emulatorProcess.Kill(entireProcessTree: true);
                await _emulatorProcess.WaitForExitAsync(new CancellationTokenSource(10000).Token);
            }
            catch
            {
            }
        }

        _emulatorProcess?.Dispose();
    }

    internal async Task<PlatformBuildResult> BuildAsync(CancellationToken cancellationToken = default)
    {
        // Clean pilot repetitions reset/install/seed the app; rebuilding the unchanged APK for
        // every repetition only extends device time and does not strengthen that contract.
        if (_build is { } cached && File.Exists(cached.ArtifactPath))
            return cached;

        PlatformBuildResult? build = null;
        await WithBuildLockAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = GetSampleProjectPath();
            await BuildSampleAsync(
                projectPath,
                "net10.0-android",
                $"-p:EmbedAssembliesIntoApk=true -p:MauiDevFlowPort={AgentPort} -p:DevFlowIntegrationTest=true");
            var apkPath = FindApk();
            build = new PlatformBuildResult
            {
                ArtifactPath = apkPath,
                AppBuildFingerprint = await ComputeFileFingerprintAsync(apkPath, cancellationToken),
                PackageId = PackageId,
            };
        });

        _build = build ?? throw PlatformFlowLifecycleException.Infrastructure("Android build did not produce an APK.");
        return _build;
    }

    internal async Task<PlatformInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        var build = _build ?? await BuildAsync(cancellationToken);
        var lifecycle = RequireDeviceLifecycle();
        await lifecycle.InstallAsync(build.ArtifactPath, replaceExisting: !_requiresReinstall, cancellationToken);
        var result = new PlatformInstallResult
        {
            PackageId = PackageId,
            ArtifactPath = build.ArtifactPath,
            Reinstalled = _requiresReinstall,
        };
        _requiresReinstall = false;
        return result;
    }

    internal async Task<MauiFlowResetResult> HardResetAsync(
        PlatformFlowResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _agent ??= await TryGetAgentIdentityAsync(cancellationToken);
        var strategy = ParseResetStrategy(request.Strategy ?? request.Requirement?.Strategy);
        _reset = await RequireDeviceLifecycle()
            .HardResetAsync(strategy, request.Requirement, cancellationToken)
            .ConfigureAwait(false);
        _requiresReinstall = strategy == AndroidResetStrategy.UninstallReinstall;
        return _reset;
    }

    internal async Task<PlatformSeedResult> SeedAsync(
        PlatformFlowSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var retries = Client.TransientFailureRetryCount;
        var retryMutations = Client.RetryMutatingRequests;
        Client.TransientFailureRetryCount = 0;
        Client.RetryMutatingRequests = false;
        try
        {
            var state = await new SampleIntegrationTestControlClient(Client)
                .SeedAsync(request.SeedId, cancellationToken)
                .ConfigureAwait(false);
            EnsureExpectedFingerprint("seed", request.ExpectedSeedFingerprint, state.SeedFingerprint);
            EnsureExpectedFingerprint("backend state", request.ExpectedBackendStateFingerprint, state.BackendStateFingerprint);

            _seed = new PlatformSeedResult
            {
                SeedId = state.SeedId,
                SeedFingerprint = state.SeedFingerprint,
                BackendStateFingerprint = state.BackendStateFingerprint,
                StateFingerprint = state.StateFingerprint,
                ProcessInstanceId = state.ProcessInstanceId,
                AppStateSeed = new MauiFlowAppStateSeedFingerprint
                {
                    SeedId = state.SeedId,
                    Fingerprint = state.SeedFingerprint,
                    Version = "1",
                    Source = "sample-integration-test-extension",
                },
                BackendTestDataSeed = new MauiFlowBackendTestDataSeedFingerprint
                {
                    SeedId = state.SeedId,
                    Fingerprint = state.BackendStateFingerprint,
                    Dataset = "none",
                    Version = "1",
                    Source = "sample-no-external-backend",
                },
                StateOracle = new MauiIndependentBusinessOracleResult
                {
                    OracleId = "sample-integration-state",
                    Succeeded = true,
                    Independent = true,
                    ObservedAt = DateTimeOffset.UtcNow,
                    EvidenceReference = "sample-test-state",
                    Message = "A separate test-only state endpoint returned the deterministic sample fingerprint.",
                },
            };

            if (_reset is not null)
            {
                _reset.SeedFingerprint = _seed.SeedFingerprint;
                _reset.BackendStateFingerprint = _seed.BackendStateFingerprint;
                _reset.AppStateSeed = _seed.AppStateSeed;
                _reset.BackendTestDataSeed = _seed.BackendTestDataSeed;
            }

            return _seed;
        }
        finally
        {
            Client.TransientFailureRetryCount = retries;
            Client.RetryMutatingRequests = retryMutations;
        }
    }

    internal async Task<PlatformLaunchResult> LaunchAsync(CancellationToken cancellationToken = default)
    {
        var previous = _agent ?? await TryGetAgentIdentityAsync(cancellationToken).ConfigureAwait(false);
        var lifecycle = RequireDeviceLifecycle();
        await lifecycle.EnsureAgentPortForwardAsync(cancellationToken).ConfigureAwait(false);
        var processId = await lifecycle.LaunchAsync(cancellationToken).ConfigureAwait(false);
        _launch = new PlatformLaunchResult
        {
            PackageId = PackageId,
            ProcessId = processId,
            PreviousAgent = previous,
        };
        return _launch;
    }

    internal async Task<PlatformAgentReadyResult> WaitForExpectedAgentAsync(
        PlatformAgentExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        var ready = await PlatformAgentReadiness.WaitForExpectedAsync(
            _ => Client.GetStatusAsync(),
            expectation,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        _agent = ready.Agent;
        return ready;
    }

    internal async Task<PlatformCheckpointVerification> VerifyCheckpointAsync(
        PlatformCheckpointRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var build = _build ?? throw PlatformFlowLifecycleException.Precondition("The Android app has not been built.");
        var seed = _seed ?? request.Seed;
        var agent = _agent ?? throw PlatformFlowLifecycleException.Precondition("A process-scoped Android agent has not been verified.");

        var status = await Client.GetStatusAsync().ConfigureAwait(false)
            ?? throw PlatformFlowLifecycleException.Infrastructure("The DevFlow agent stopped responding during checkpoint verification.");
        if (!string.Equals(status.App?.PackageId, PackageId, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected package '{PackageId}', observed '{status.App?.PackageId ?? "<none>"}'.");
        }
        if (status.App?.ProcessId != agent.ProcessId)
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"The agent process changed before replay. Expected {agent.ProcessId}, observed {status.App?.ProcessId?.ToString() ?? "<none>"}.");
        }

        var installed = await RequireDeviceLifecycle().GetInstalledPackageInfoAsync(cancellationToken).ConfigureAwait(false);
        EnsureExpectedFingerprint("installed APK", build.AppBuildFingerprint, installed.ApkFingerprint);
        EnsureEqual("installed app version", installed.VersionName, status.App?.Version);
        EnsureEqual("installed app build", installed.VersionCode, status.App?.Build);

        var state = await new SampleIntegrationTestControlClient(Client).GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(seed.ProcessInstanceId, state.ProcessInstanceId, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                "The test-only state endpoint belongs to a different app process than the seeded instance.");
        }
        EnsureExpectedFingerprint("seed", seed.SeedFingerprint, state.SeedFingerprint);
        EnsureExpectedFingerprint("backend state", seed.BackendStateFingerprint, state.BackendStateFingerprint);

        var theme = await Client.GetThemeAsync().ConfigureAwait(false);
        var observed = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = build.AppBuildFingerprint,
            AgentInstanceId = agent.StableId,
            SeedFingerprint = state.SeedFingerprint,
            BackendStateFingerprint = state.BackendStateFingerprint,
            Route = status.Route,
            // A plan may declare window/modal as clean-state preconditions. Admission compares the
            // declared value against what the host observed, so a host that never reports them makes
            // any plan that declares them permanently unsatisfiable rather than merely mismatched.
            Window = status.Window,
            Modal = status.Modal,
            Locale = status.Locale ??
                await RequireDeviceLifecycle().GetLocaleAsync(cancellationToken).ConfigureAwait(false),
            Theme = status.Theme?.ToLowerInvariant() ??
                theme?.Theme.ToString().ToLowerInvariant(),
            Orientation = status.Orientation ??
                await RequireDeviceLifecycle().GetOrientationAsync(cancellationToken).ConfigureAwait(false),
            DisplayProfile = status.DisplayProfile ??
                await RequireDeviceLifecycle().GetDisplayProfileAsync(cancellationToken).ConfigureAwait(false),
        };
        EnsureObserved("route", observed.Route);
        EnsureObserved("locale", observed.Locale);
        EnsureObserved("theme", observed.Theme);
        EnsureObserved("orientation", observed.Orientation);
        EnsureObserved("display profile", observed.DisplayProfile);
        var expected = MergeCheckpoint(request.Expected, observed);
        var mismatches = CompareCheckpoint(expected, observed);
        if (mismatches.Count > 0)
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Android replay preconditions did not match: {string.Join("; ", mismatches)}");
        }

        var target = new MauiFlowRunTarget
        {
            TargetId = agent.StableId,
            Platform = Platform,
            DeviceId = _serialNumber,
            DeviceProfile = observed.DisplayProfile,
            AppId = PackageId,
            AppBuildFingerprint = build.AppBuildFingerprint,
            AgentId = status.Agent?.Name,
            AgentInstanceId = agent.StableId,
            Locale = observed.Locale,
            Theme = observed.Theme,
            Orientation = observed.Orientation,
            DisplayProfile = observed.DisplayProfile,
        };
        var context = new MauiFlowRunContext
        {
            Intent = MauiFlowReplayIntents.OrdinaryReplay,
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = expected,
                Observed = observed,
                CheckedAt = DateTimeOffset.UtcNow,
                EvidenceReference = "android-lifecycle-preflight",
            },
            Reset = request.Reset,
            BusinessOracles = seed.StateOracle is null ? [] : [seed.StateOracle],
        };

        return new PlatformCheckpointVerification
        {
            Expected = expected,
            Observed = observed,
            Target = target,
            RunContext = context,
        };
    }

    internal async Task<PlatformHostDiagnostics> CaptureHostDiagnosticsAsync(
        PlatformDiagnosticsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var fullRoot = Path.GetFullPath(request.ArtifactRoot);
        var runDirectory = Path.Combine(fullRoot, SanitizeFileName(request.RunId));
        Directory.CreateDirectory(runDirectory);

        var facts = await RequireDeviceLifecycle().CollectHostFactsAsync(cancellationToken).ConfigureAwait(false);
        facts["reason"] = AndroidLifecycleDiagnosticRedactor.Sanitize(
            request.Reason,
            AndroidFixtureInitializationDiagnostics.MaxSafeErrorTextCharacters,
            _serialNumber);
        facts["capturedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        facts["buildFingerprint"] = _build?.AppBuildFingerprint ?? string.Empty;
        facts["agentInstanceId"] = _agent?.StableId ?? string.Empty;
        facts["seedFingerprint"] = _seed?.SeedFingerprint ?? string.Empty;
        facts["backendStateFingerprint"] = _seed?.BackendStateFingerprint ?? string.Empty;

        var path = Path.Combine(runDirectory, "android-host-diagnostics.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        var diagnostics = new PlatformHostDiagnostics();
        diagnostics.Artifacts.Add(new MauiFlowArtifactReference
        {
            ArtifactId = $"android-host-diagnostics-{SanitizeFileName(request.RunId)}",
            Kind = "android-host-diagnostics",
            Path = path,
            Digest = await ComputeFileFingerprintAsync(path, cancellationToken).ConfigureAwait(false),
            MediaType = "application/json",
            Redacted = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return diagnostics;
    }

    internal async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_deviceLifecycle is not null)
            await _deviceLifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    Task<PlatformBuildResult> IPlatformFlowTestLifecycle.BuildAsync(CancellationToken cancellationToken)
        => BuildAsync(cancellationToken);

    Task<PlatformInstallResult> IPlatformFlowTestLifecycle.InstallAsync(CancellationToken cancellationToken)
        => InstallAsync(cancellationToken);

    Task<MauiFlowResetResult> IPlatformFlowTestLifecycle.HardResetAsync(
        PlatformFlowResetRequest request,
        CancellationToken cancellationToken)
        => HardResetAsync(request, cancellationToken);

    Task<PlatformSeedResult> IPlatformFlowTestLifecycle.SeedAsync(
        PlatformFlowSeedRequest request,
        CancellationToken cancellationToken)
        => SeedAsync(request, cancellationToken);

    Task<PlatformLaunchResult> IPlatformFlowTestLifecycle.LaunchAsync(CancellationToken cancellationToken)
        => LaunchAsync(cancellationToken);

    Task<PlatformAgentReadyResult> IPlatformFlowTestLifecycle.WaitForExpectedAgentAsync(
        PlatformAgentExpectation expectation,
        CancellationToken cancellationToken)
        => WaitForExpectedAgentAsync(expectation, cancellationToken);

    Task<PlatformCheckpointVerification> IPlatformFlowTestLifecycle.VerifyCheckpointAsync(
        PlatformCheckpointRequest request,
        CancellationToken cancellationToken)
        => VerifyCheckpointAsync(request, cancellationToken);

    Task<PlatformHostDiagnostics> IPlatformFlowTestLifecycle.CaptureHostDiagnosticsAsync(
        PlatformDiagnosticsRequest request,
        CancellationToken cancellationToken)
        => CaptureHostDiagnosticsAsync(request, cancellationToken);

    Task IPlatformFlowTestLifecycle.StopAsync(CancellationToken cancellationToken)
        => StopAsync(cancellationToken);

    async ValueTask IAsyncDisposable.DisposeAsync()
        => await StopAsync().ConfigureAwait(false);

    AndroidDeviceLifecycleOperations RequireDeviceLifecycle()
        => _deviceLifecycle ?? throw PlatformFlowLifecycleException.Infrastructure(
            "Android device lifecycle has not been initialized.");

    async Task<PlatformAgentIdentity?> TryGetAgentIdentityAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PlatformAgentIdentity.FromStatus(await Client.GetStatusAsync().ConfigureAwait(false));
        }
        catch
        {
            return null;
        }
    }

    static AndroidResetStrategy ParseResetStrategy(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "pm-clear" or "clear" or "app-private-state" => AndroidResetStrategy.PmClear,
            "uninstall-reinstall" or "uninstall" => AndroidResetStrategy.UninstallReinstall,
            _ => throw PlatformFlowLifecycleException.Precondition(
                $"Unsupported Android reset strategy '{value}'. Use 'pm-clear' or 'uninstall-reinstall'."),
        };

    static void EnsureExpectedFingerprint(string kind, string? expected, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, observed, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected {kind} fingerprint '{expected}', observed '{observed ?? "<none>"}'.");
        }
    }

    static void EnsureEqual(string kind, string? expected, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, observed, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected {kind} '{expected}', observed '{observed ?? "<none>"}'.");
        }
    }

    static void EnsureObserved(string kind, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw PlatformFlowLifecycleException.Precondition($"Android checkpoint did not provide {kind}.");
    }

    static MauiFlowCheckpoint MergeCheckpoint(MauiFlowCheckpoint requested, MauiFlowCheckpoint observed)
        => new()
        {
            AppBuildFingerprint = requested.AppBuildFingerprint ?? observed.AppBuildFingerprint,
            AgentInstanceId = requested.AgentInstanceId ?? observed.AgentInstanceId,
            SeedFingerprint = requested.SeedFingerprint ?? observed.SeedFingerprint,
            BackendStateFingerprint = requested.BackendStateFingerprint ?? observed.BackendStateFingerprint,
            Route = requested.Route ?? observed.Route,
            Window = requested.Window ?? observed.Window,
            Modal = requested.Modal ?? observed.Modal,
            Locale = requested.Locale ?? observed.Locale,
            Theme = requested.Theme ?? observed.Theme,
            Orientation = requested.Orientation ?? observed.Orientation,
            DisplayProfile = requested.DisplayProfile ?? observed.DisplayProfile,
            CollectionItemKey = requested.CollectionItemKey ?? observed.CollectionItemKey,
        };

    static List<string> CompareCheckpoint(MauiFlowCheckpoint expected, MauiFlowCheckpoint observed)
    {
        var mismatches = new List<string>();
        AddMismatch(mismatches, "app build", expected.AppBuildFingerprint, observed.AppBuildFingerprint);
        AddMismatch(mismatches, "agent instance", expected.AgentInstanceId, observed.AgentInstanceId);
        AddMismatch(mismatches, "seed", expected.SeedFingerprint, observed.SeedFingerprint);
        AddMismatch(mismatches, "backend state", expected.BackendStateFingerprint, observed.BackendStateFingerprint);
        AddMismatch(mismatches, "route", expected.Route, observed.Route);
        AddMismatch(mismatches, "locale", expected.Locale, observed.Locale);
        AddMismatch(mismatches, "theme", expected.Theme, observed.Theme);
        AddMismatch(mismatches, "orientation", expected.Orientation, observed.Orientation);
        AddMismatch(mismatches, "display", expected.DisplayProfile, observed.DisplayProfile);
        return mismatches;
    }

    static void AddMismatch(List<string> mismatches, string name, string? expected, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, observed, StringComparison.Ordinal))
        {
            mismatches.Add($"{name} expected '{expected}', observed '{observed ?? "<none>"}'");
        }
    }

    static async Task<string> ComputeFileFingerprintAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "run" : sanitized[..Math.Min(sanitized.Length, 96)];
    }

    void StartAppMonitor()
    {
        _diagnosticsDir = Path.Combine(
            FindRepoRoot(),
            "artifacts",
            "TestResults",
            "devflow-integration",
            "android",
            "diagnostics");

        Directory.CreateDirectory(_diagnosticsDir);

        _appMonitorCts?.Dispose();
        _appMonitorCts = new CancellationTokenSource();
        _appMonitorTask = Task.Run(() => MonitorAppProcessAsync(_appMonitorCts.Token));

        Console.WriteLine($"[AndroidFixture] App crash diagnostics enabled. Output: {_diagnosticsDir}");
    }

    async Task MonitorAppProcessAsync(CancellationToken cancellationToken)
    {
        var missingProcessChecks = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnsureAgentPortForwardAsync();

                var pid = await TryGetAppPidAsync();
                if (!string.IsNullOrWhiteSpace(pid))
                {
                    _appSeenRunning = true;
                    _lastKnownPid = pid.Trim();
                    missingProcessChecks = 0;
                }
                else if (_appSeenRunning)
                {
                    missingProcessChecks++;
                    Console.WriteLine(
                        $"[AndroidFixture] App process probe missed '{PackageId}' " +
                        $"({missingProcessChecks}/3). Last known pid: {_lastKnownPid ?? "<unknown>"}");

                    if (missingProcessChecks >= 3)
                    {
                        _appSeenRunning = false;
                        missingProcessChecks = 0;
                        var reason = $"App process '{PackageId}' disappeared after repeated probes. Last known pid: {_lastKnownPid ?? "<unknown>"}";
                        await CaptureCrashDiagnosticsAsync(reason);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AndroidFixture] App monitor warning: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task EnsureAgentPortForwardAsync()
    {
        if (string.IsNullOrWhiteSpace(_serialNumber))
            return;

        if (_deviceLifecycle is not null)
        {
            if (!await _deviceLifecycle.IsAgentPortForwardEstablishedAsync())
                await _deviceLifecycle.EnsureAgentPortForwardAsync();
            return;
        }

        if (await IsAgentForwardEstablishedAsync())
            return;

        Console.WriteLine($"[AndroidFixture] Re-establishing missing ADB forward tcp:{AgentPort} tcp:{AgentPort}.");

        // Best-effort remove first so a stale or partial mapping (e.g. left
        // behind by a previous adb-server crash) doesn't block the re-add.
        try { await AdbAsync($"forward --remove tcp:{AgentPort}", timeoutSeconds: 5); } catch { }

        await AdbCheckedAsync($"forward tcp:{AgentPort} tcp:{AgentPort}", timeoutSeconds: 15);

        // Verify the mapping actually landed - `adb forward` can return 0 while
        // the daemon is in an inconsistent state, and silently failing here
        // amplifies into a slow, opaque retry storm in the agent client.
        if (!await IsAgentForwardEstablishedAsync())
        {
            throw new InvalidOperationException(
                $"adb forward tcp:{AgentPort} tcp:{AgentPort} reported success on '{_serialNumber}' " +
                "but the mapping is not visible in `adb forward --list`.");
        }
    }

    async Task<bool> IsAgentForwardEstablishedAsync()
    {
        // Scope the listing to this device so we get the 2-column
        // `<local> <remote>` form regardless of how many devices are attached.
        var (output, _, exitCode) = await AdbAsync("forward --list", timeoutSeconds: 5);
        if (exitCode != 0)
            return false;

        var expectedLocal = $"tcp:{AgentPort}";
        var expectedRemote = $"tcp:{AgentPort}";

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            // Tolerate both `<serial> <local> <remote>` and `<local> <remote>`
            // shapes - older `adb` releases emit the 3-column form even with `-s`.
            string? local;
            string? remote;
            if (parts.Length >= 3)
            {
                // Defensive: even though `AdbAsync` already scopes the listing to
                // this device via `-s {_serialNumber}`, older adb releases can emit
                // 3-column output. Skip rows whose serial does not match so we
                // never accept a forward belonging to a different attached device.
                if (!string.Equals(parts[0], _serialNumber, StringComparison.OrdinalIgnoreCase))
                    continue;

                local = parts[1];
                remote = parts[2];
            }
            else if (parts.Length == 2)
            {
                local = parts[0];
                remote = parts[1];
            }
            else
            {
                continue;
            }

            if (string.Equals(local, expectedLocal, StringComparison.OrdinalIgnoreCase)
                && string.Equals(remote, expectedRemote, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    async Task<string?> TryGetAppPidAsync(int timeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(_serialNumber))
            return null;

        var (output, _, exitCode) = await RunProcessAsync(
            AdbPath(),
            $"-s {_serialNumber} shell pidof {PackageId}",
            timeoutSeconds: timeoutSeconds);

        if (exitCode == 0)
        {
            var pid = output.Trim();
            if (!string.IsNullOrWhiteSpace(pid))
                return pid;
        }

        return await TryGetAppPidFromPsAsync(timeoutSeconds);
    }

    async Task<string?> TryGetAppPidFromPsAsync(int timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(_serialNumber))
            return null;

        var (output, _, exitCode) = await RunProcessAsync(
            AdbPath(),
            $"-s {_serialNumber} shell ps -A",
            timeoutSeconds: timeoutSeconds);

        if (exitCode != 0)
            return null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (columns.Length < 2)
                continue;

            var processName = columns[^1];
            if (!processName.Equals(PackageId, StringComparison.Ordinal)
                && !line.EndsWith($" {PackageId}", StringComparison.Ordinal))
            {
                continue;
            }

            // Standard `ps -A` output is `USER PID PPID VSZ RSS ...`, so the PID is
            // always column index 1. Avoid scanning for the first integer-parseable
            // column because some ROMs use numeric UIDs in the USER column.
            if (int.TryParse(columns[1], out _))
                return columns[1];
        }

        return null;
    }

    async Task CaptureCrashDiagnosticsAsync(string reason)
    {
        if (string.IsNullOrWhiteSpace(_serialNumber))
            return;

        _diagnosticsDir ??= Path.Combine(
            FindRepoRoot(),
            "artifacts",
            "TestResults",
            "devflow-integration",
            "android",
            "diagnostics");
        Directory.CreateDirectory(_diagnosticsDir);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var prefix = Path.Combine(_diagnosticsDir, $"android-app-loss-{stamp}");

        try
        {
            var metadata = string.Join(Environment.NewLine, new[]
            {
                $"timestamp_utc={DateTime.UtcNow:O}",
                $"reason={reason}",
                $"serial={_serialNumber}",
                $"package={PackageId}",
            });
            await File.WriteAllTextAsync($"{prefix}.meta.txt", metadata);
        }
        catch
        {
            // Continue collecting best-effort diagnostics.
        }

        try
        {
            var (stdout, stderr, _) = await RunProcessAsync(
                AdbPath(),
                $"-s {_serialNumber} logcat -d -v threadtime",
                timeoutSeconds: 30);
            await File.WriteAllTextAsync($"{prefix}.logcat.txt", $"{stdout}{Environment.NewLine}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync($"{prefix}.logcat.error.txt", ex.ToString());
        }

        try
        {
            var (stdout, stderr, _) = await RunProcessAsync(
                AdbPath(),
                $"-s {_serialNumber} shell dumpsys activity top",
                timeoutSeconds: 30);
            await File.WriteAllTextAsync($"{prefix}.activity-top.txt", $"{stdout}{Environment.NewLine}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync($"{prefix}.activity-top.error.txt", ex.ToString());
        }

        try
        {
            var (stdout, stderr, _) = await RunProcessAsync(
                AdbPath(),
                $"-s {_serialNumber} forward --list",
                timeoutSeconds: 10);
            await File.WriteAllTextAsync($"{prefix}.forward-list.txt", $"{stdout}{Environment.NewLine}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync($"{prefix}.forward-list.error.txt", ex.ToString());
        }

        try
        {
            var (stdout, stderr, _) = await RunProcessAsync(
                AdbPath(),
                $"-s {_serialNumber} reverse --list",
                timeoutSeconds: 10);
            await File.WriteAllTextAsync($"{prefix}.reverse-list.txt", $"{stdout}{Environment.NewLine}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync($"{prefix}.reverse-list.error.txt", ex.ToString());
        }

        try
        {
            using var response = await Http.GetAsync("/api/v1/agent/status");
            var body = await response.Content.ReadAsStringAsync();
            await File.WriteAllTextAsync($"{prefix}.agent-status.txt", $"HTTP {(int)response.StatusCode}{Environment.NewLine}{body}");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync($"{prefix}.agent-status.error.txt", ex.ToString());
        }

        Console.WriteLine($"[AndroidFixture] Captured app-loss diagnostics at '{prefix}.*'");
    }

    static string ResolveSdkRoot()
    {
        var root = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");

        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, "Library", "Android", "sdk"),
                "/usr/local/lib/android/sdk",
                Path.Combine(home, "Android", "Sdk"),
                @"C:\Users\" + Environment.UserName + @"\AppData\Local\Android\Sdk",
            };

            root = candidates.FirstOrDefault(Directory.Exists)
                ?? throw new InvalidOperationException(
                    "Android SDK not found. Set ANDROID_HOME or ANDROID_SDK_ROOT.");
        }

        if (!Directory.Exists(root))
            throw new InvalidOperationException($"Android SDK directory not found at: {root}");

        return root;
    }

    string AdbPath() => Path.Combine(_sdkRoot, "platform-tools", OperatingSystem.IsWindows() ? "adb.exe" : "adb");
    string EmulatorPath() => Path.Combine(_sdkRoot, "emulator", OperatingSystem.IsWindows() ? "emulator.exe" : "emulator");

    string AvdManagerPath()
    {
        var cmdlineToolsDir = Path.Combine(_sdkRoot, "cmdline-tools");
        if (!Directory.Exists(cmdlineToolsDir))
            throw new InvalidOperationException($"cmdline-tools not found at: {cmdlineToolsDir}");

        var latestVersion = Directory.GetDirectories(cmdlineToolsDir)
            .Select(Path.GetFileName)
            .Where(n => n != "latest")
            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "latest";

        return Path.Combine(cmdlineToolsDir, latestVersion, "bin", OperatingSystem.IsWindows() ? "avdmanager.bat" : "avdmanager");
    }

    string SdkManagerPath()
    {
        var cmdlineToolsDir = Path.Combine(_sdkRoot, "cmdline-tools");
        var latestVersion = Directory.GetDirectories(cmdlineToolsDir)
            .Select(Path.GetFileName)
            .Where(n => n != "latest")
            .OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "latest";

        return Path.Combine(cmdlineToolsDir, latestVersion, "bin", OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager");
    }

    Task<(string Stdout, string Stderr, int ExitCode)> AdbAsync(string arguments, int timeoutSeconds = 30) =>
        RunProcessAsync(AdbPath(), $"-s {_serialNumber} {arguments}", timeoutSeconds: timeoutSeconds);

    Task<string> AdbCheckedAsync(string arguments, int timeoutSeconds = 30) =>
        RunProcessCheckedAsync(AdbPath(), $"-s {_serialNumber} {arguments}", timeoutSeconds: timeoutSeconds);

    static int GetTargetApiLevel()
    {
        var apiStr = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_API") ?? "35";
        var first = apiStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return int.Parse(first);
    }

    static string GetTargetAvdName(int apiLevel) =>
        Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_AVD")
        ?? $"devflow-tests-api{apiLevel}";

    async Task<bool> IsPhysicalDeviceReadyAsync()
    {
        var requestedSerial = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_SERIAL");
        if (string.IsNullOrWhiteSpace(requestedSerial))
            return false;

        if (requestedSerial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var (stateOutput, _, stateExitCode) = await RunProcessAsync(
                AdbPath(),
                $"-s {requestedSerial} get-state",
                timeoutSeconds: 10);

            return stateExitCode == 0
                && stateOutput.Trim().Equals("device", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    async Task EnsureAvdExistsAsync(string avdName, int apiLevel)
    {
        var (stdout, _, _) = await RunProcessAsync(AvdManagerPath(), "list avd -c");
        var existingAvds = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (existingAvds.Any(a => a.Equals(avdName, StringComparison.OrdinalIgnoreCase)))
            return;

        var systemImage = $"system-images;android-{apiLevel};google_apis;{GetSystemImageAbi()}";
        await RunProcessCheckedAsync(SdkManagerPath(), $"--install \"{systemImage}\"", timeoutSeconds: 600);

        var psi = new ProcessStartInfo(AvdManagerPath(), $"create avd -n {avdName} -k \"{systemImage}\" -d pixel_6 --force")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start avdmanager");

        await process.StandardInput.WriteLineAsync("no");
        process.StandardInput.Close();

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"avdmanager create failed: {stderr}");
        }
    }

    async Task<string> EnsureEmulatorRunningAsync(string avdName)
    {
        var adb = AdbPath();
        var requestedSerial = Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_SERIAL");

        if (!string.IsNullOrWhiteSpace(requestedSerial))
        {
            var (avdOutput, _, exitCode) = await RunProcessAsync(adb,
                $"-s {requestedSerial} emu avd name", timeoutSeconds: 10);

            if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
            {
                _weStartedEmulator = false;
                return requestedSerial;
            }

            if (!requestedSerial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
            {
                var (stateOutput, _, stateExitCode) = await RunProcessAsync(adb,
                    $"-s {requestedSerial} get-state", timeoutSeconds: 10);

                if (stateExitCode == 0 && stateOutput.Trim().Equals("device", StringComparison.OrdinalIgnoreCase))
                {
                    _weStartedEmulator = false;
                    return requestedSerial;
                }
            }

            throw new InvalidOperationException(
                $"Requested Android serial '{requestedSerial}' is not running AVD '{avdName}'.");
        }

        var (devicesOutput, _, _) = await RunProcessAsync(adb, "devices");
        var runningEmulators = devicesOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.StartsWith("emulator-") && l.Contains("device"))
            .Select(l => l.Split('\t')[0].Trim())
            .ToList();

        foreach (var serial in runningEmulators)
        {
            var (avdOutput, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} emu avd name", timeoutSeconds: 5);
            if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
            {
                _weStartedEmulator = false;
                return serial;
            }
        }

        foreach (var serial in runningEmulators)
        {
            var (apiOutput, _, exitCode) = await RunProcessAsync(adb,
                $"-s {serial} shell getprop ro.build.version.sdk", timeoutSeconds: 5);
            if (exitCode == 0 && int.TryParse(apiOutput.Trim(), out var runningApi) && runningApi == _apiLevel)
            {
                _weStartedEmulator = false;
                return serial;
            }
        }

        var emulatorPort = GetEmulatorConsolePort();
        var expectedSerial = $"emulator-{emulatorPort}";

        var psi = new ProcessStartInfo(EmulatorPath(), $"-avd {avdName} -port {emulatorPort} -no-snapshot -no-audio -no-window -gpu swiftshader_indirect")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        _emulatorProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start emulator for AVD {avdName}");
        _emulatorProcess.OutputDataReceived += static (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Console.WriteLine($"[AndroidEmulator] {e.Data}");
        };
        _emulatorProcess.ErrorDataReceived += static (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                Console.WriteLine($"[AndroidEmulator] {e.Data}");
        };
        _emulatorProcess.BeginOutputReadLine();
        _emulatorProcess.BeginErrorReadLine();
        _weStartedEmulator = true;

        var newSerial = await WaitForEmulatorSerialAsync(adb, expectedSerial, avdName, timeoutSeconds: 120);
        await WaitForDeviceBootAsync(adb, newSerial, timeoutSeconds: 180);
        return newSerial;
    }

    static int GetEmulatorConsolePort()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_ANDROID_EMULATOR_PORT"), out var configuredPort))
            return configuredPort;

        // Android emulator console ports must be even and reserve the next odd port too.
        for (var port = 5580; port <= 5680; port += 2)
        {
            if (IsPortAvailable(port) && IsPortAvailable(port + 1))
                return port;
        }

        throw new InvalidOperationException("Could not find a free Android emulator port in the 5580-5680 range.");
    }

    static string GetSystemImageAbi() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64-v8a" : "x86_64";

    static bool IsPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    async Task<string> WaitForEmulatorSerialAsync(string adb, string expectedSerial, string avdName, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (_emulatorProcess is { HasExited: true })
            {
                var stdout = await _emulatorProcess.StandardOutput.ReadToEndAsync();
                var stderr = await _emulatorProcess.StandardError.ReadToEndAsync();
                throw new InvalidOperationException(
                    $"Emulator process exited with code {_emulatorProcess.ExitCode} before becoming ready." +
                    $"\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
            }

            var (output, _, _) = await RunProcessAsync(adb, "devices");
            var emulatorSerials = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.StartsWith("emulator-") && l.Contains("device"))
                .Select(l => l.Split('\t')[0].Trim())
                .ToList();

            if (emulatorSerials.Contains(expectedSerial, StringComparer.OrdinalIgnoreCase))
                return expectedSerial;

            foreach (var serial in emulatorSerials)
            {
                var (avdOutput, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} emu avd name", timeoutSeconds: 5);
                if (exitCode == 0 && avdOutput.Trim().StartsWith(avdName, StringComparison.OrdinalIgnoreCase))
                    return serial;
            }

            await Task.Delay(2000);
        }

        throw new TimeoutException(
            $"Emulator for AVD '{avdName}' did not appear in 'adb devices' within {timeoutSeconds}s.");
    }

    static async Task WaitForDeviceBootAsync(string adb, string serial, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var (output, _, exitCode) = await RunProcessAsync(adb, $"-s {serial} shell getprop sys.boot_completed", timeoutSeconds: 5);
            if (exitCode == 0 && output.Trim() == "1")
                return;

            await Task.Delay(3000);
        }

        throw new TimeoutException($"Device {serial} did not finish booting within {timeoutSeconds}s.");
    }

    static string FindApk()
    {
        var binDir = Path.Combine(GetSampleBuildOutputRoot(), "net10.0-android");

        if (!Directory.Exists(binDir))
            throw new InvalidOperationException($"Android build output not found at: {binDir}");

        var apks = Directory.GetFiles(binDir, "*-Signed.apk", SearchOption.AllDirectories);
        if (apks.Length == 0)
            apks = Directory.GetFiles(binDir, "*.apk", SearchOption.AllDirectories);

        if (apks.Length == 0)
            throw new InvalidOperationException($"No APK found under {binDir}");

        return apks[0];
    }

    Task InstallApkAsync(string apkPath) =>
        AdbCheckedAsync($"install -r \"{apkPath}\"", timeoutSeconds: 120);

    async Task LaunchAppAsync()
    {
        var output = await AdbCheckedAsync(
            $"shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {PackageId}",
            timeoutSeconds: 10);

        var activityLine = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(l => l.Contains('/'));

        if (string.IsNullOrEmpty(activityLine))
            throw new InvalidOperationException(
                $"Could not resolve launcher activity for {PackageId}. Output: {output}");

        await AdbCheckedAsync($"shell am force-stop {PackageId}", timeoutSeconds: 10);
        var launchOutput = await AdbCheckedAsync($"shell am start -W -n {activityLine}", timeoutSeconds: 30);

        if (launchOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Failed to launch {PackageId}: {launchOutput}");

        await WaitForAppProcessAsync(timeoutSeconds: 30);
    }

    async Task WaitForAppProcessAsync(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var (output, _, exitCode) = await AdbAsync($"shell pidof {PackageId}", timeoutSeconds: 5);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return;

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Android app process '{PackageId}' did not appear within {timeoutSeconds}s.");
    }
}
