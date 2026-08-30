using System.Runtime.InteropServices;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal abstract class AppleFlowExecutionAdapterBase : IFlowExecutionPlatformAdapter
{
    protected readonly IFlowExecutionHostEnvironment Host;
    protected readonly IAppleAppBundleInspector BundleInspector;

    protected AppleFlowExecutionAdapterBase(
        IFlowExecutionHostEnvironment host,
        IAppleAppBundleInspector bundleInspector)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        BundleInspector = bundleInspector ?? throw new ArgumentNullException(nameof(bundleInspector));
    }

    public abstract FlowExecutionPlatformDescriptor Descriptor { get; }

    public void ValidateHost()
    {
        if (!Host.IsMacOS)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-host-required",
                $"{Descriptor.DisplayName} flow execution requires a macOS host.");
        }
        _ = GetDefaultRuntimeIdentifier();
    }

    public abstract string? GetDefaultRuntimeIdentifier();

    public abstract Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default);

    public abstract Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
        FlowExecutionPlatformRequest request,
        CancellationToken cancellationToken = default);

    public virtual Task EstablishAgentForwardingAsync(
        FlowExecutionPlatformSession session,
        int agentPort,
        int brokerPort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public abstract Task<FlowExecutionCleanupResult> CleanupAsync(
        FlowExecutionPlatformSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default);

    protected string ArchitectureRuntimeIdentifier(string arm64, string x64)
        => Host.ProcessArchitecture switch
        {
            Architecture.Arm64 => arm64,
            Architecture.X64 => x64,
            _ => throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-host-architecture-unsupported",
                $"{Descriptor.DisplayName} flow execution supports arm64 and x64 macOS hosts only."),
        };

    internal static async Task<AppleAppBundleInfo> ValidateBundleIdentityAsync(
        ResolvedAppArtifact artifact,
        IAppleAppBundleInspector inspector,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(artifact.OwnedOutputRoot))
        {
            ExecutionPathSafety.ValidateConfinedArtifactPath(
                artifact.OwnedOutputRoot,
                artifact.Path);
        }
        if (!string.Equals(artifact.LaunchIdentityKind, "apple-bundle-id", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(artifact.LaunchIdentity) ||
            (!string.IsNullOrWhiteSpace(artifact.ApplicationId) &&
             !string.Equals(artifact.ApplicationId, artifact.LaunchIdentity, StringComparison.Ordinal)))
        {
            throw FlowExecutionException.Invalid(
                "apple-launch-identity-invalid",
                "The AppProjectReference launch identity does not match the Apple bundle identity.");
        }

        var bundle = await inspector.InspectAsync(artifact.Path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(bundle.BundleIdentifier, artifact.LaunchIdentity, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "apple-bundle-identity-mismatch",
                "Info.plist does not match the AppProjectReference Apple bundle identity.");
        }
        return bundle;
    }

    internal static Device CreateDesktopDevice(
        IFlowExecutionHostEnvironment host,
        string platform)
        => new()
        {
            Id = host.MachineName,
            Name = host.MachineName,
            Platforms = [platform],
            Version = host.OsVersion,
            Architecture = host.ProcessArchitecture.ToString().ToLowerInvariant(),
            Idiom = DeviceIdiom.Desktop,
            IsEmulator = false,
            IsRunning = true,
            ConnectionType = ConnectionType.Local,
            Type = DeviceType.Physical,
            State = DeviceState.Connected,
        };
}

internal sealed record IosSimulatorFlowExecutionPreflightState
{
    public required SimulatorInfo Simulator { get; init; }
}

internal sealed record IosSimulatorFlowExecutionSessionState
{
    public required string SimulatorUdid { get; init; }
    public required string BundleIdentifier { get; init; }
    public bool WasInstalledBefore { get; init; }
    public bool WasRunningBefore { get; init; }
    public bool SimulatorBootedByInvocation { get; init; }
}

internal sealed class IosSimulatorFlowExecutionAdapter : AppleFlowExecutionAdapterBase
{
    private readonly IAppleProvider _appleProvider;
    private readonly IAppleSimulatorAppInspector _appInspector;

    internal static readonly FlowExecutionPlatformDescriptor PlatformDescriptor = new()
    {
        Platform = "ios",
        DisplayName = "iOS Simulator",
        CommandAliases = ["ios", "ios-simulator"],
        FlowPlatformAliases = ["ios", "ios-simulator"],
        AgentPlatformAliases = ["ios"],
        TargetFrameworkPlatformIdentifiers = ["ios"],
        CandidateArtifactTypes = ["app", "ipa"],
        UnsupportedArtifactTypes = ["ipa"],
        UnsupportedArtifactCode = "ios-physical-artifact-unsupported",
        UnsupportedArtifactMessage = "iOS flow run v1 supports Simulator .app bundles only; IPA and physical iOS deployment are unsupported.",
    };

    public IosSimulatorFlowExecutionAdapter(
        IAppleProvider appleProvider,
        IAppleSimulatorAppInspector appInspector,
        IFlowExecutionHostEnvironment host,
        IAppleAppBundleInspector bundleInspector)
        : base(host, bundleInspector)
    {
        _appleProvider = appleProvider ?? throw new ArgumentNullException(nameof(appleProvider));
        _appInspector = appInspector ?? throw new ArgumentNullException(nameof(appInspector));
    }

    public override FlowExecutionPlatformDescriptor Descriptor => PlatformDescriptor;

    public override string GetDefaultRuntimeIdentifier()
        => ArchitectureRuntimeIdentifier("iossimulator-arm64", "iossimulator-x64");

    public override async Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHost();
        ValidateArtifact(request.Artifact);
        ValidateExactRuntimeIdentifier(request.Artifact, GetDefaultRuntimeIdentifier(), Descriptor);
        await ValidateBundleIdentityAsync(
            request.Artifact,
            BundleInspector,
            cancellationToken).ConfigureAwait(false);
        var simulator = ResolveSimulator(request.DeviceSerial);
        var device = CreateSimulatorDevice(simulator);
        return new FlowExecutionPlatformPreflight
        {
            Device = device,
            DeviceSerial = simulator.Udid,
            PackageId = request.Artifact.LaunchIdentity!,
            State = new IosSimulatorFlowExecutionPreflightState { Simulator = simulator },
        };
    }

    public override async Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
        FlowExecutionPlatformRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateArtifact(request.Artifact);
        await ValidateBundleIdentityAsync(
            request.Artifact,
            BundleInspector,
            cancellationToken).ConfigureAwait(false);
        if (request.Preflight.State is not IosSimulatorFlowExecutionPreflightState state)
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-preflight-state-missing",
                "The exact iOS Simulator preflight state is unavailable.");
        }

        var simulator = state.Simulator;
        var bootedByInvocation = !simulator.IsBooted;
        if (bootedByInvocation && !_appleProvider.BootSimulator(simulator.Udid))
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-boot-failed",
                "The exact iOS Simulator could not be booted.");
        }

        AppleSimulatorAppState priorState;
        try
        {
            await _appInspector.WaitForBootReadinessAsync(
                simulator.Udid,
                cancellationToken).ConfigureAwait(false);
            priorState = await _appInspector.InspectAsync(
                simulator.Udid,
                request.Preflight.PackageId,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RollbackPreSessionBoot(simulator.Udid, bootedByInvocation);
            throw;
        }
        if (priorState.Installed || priorState.Running)
        {
            RollbackPreSessionBoot(simulator.Udid, bootedByInvocation);
            throw FlowExecutionException.Unsupported(
                "ios-simulator-preexisting-app-unsafe",
                "The exact iOS Simulator already contains or is running this app. Flow run v1 refuses to replace or control app state it does not own.");
        }

        var session = new FlowExecutionPlatformSession
        {
            Device = request.Preflight.Device with
            {
                State = DeviceState.Booted,
                IsRunning = true,
            },
            DeviceSerial = request.Preflight.DeviceSerial,
            PackageId = request.Preflight.PackageId,
            Platform = Descriptor.Platform,
            RuntimeKind = "ios-simulator",
            DeviceProfile = "ios-simulator",
            RequireAgentDeviceIdentity = true,
            InstalledByInvocation = false,
            LaunchedByInvocation = false,
            State = new IosSimulatorFlowExecutionSessionState
            {
                SimulatorUdid = simulator.Udid,
                BundleIdentifier = request.Preflight.PackageId,
                WasInstalledBefore = priorState.Installed,
                WasRunningBefore = priorState.Running,
                SimulatorBootedByInvocation = bootedByInvocation,
            },
        };
        if (!_appleProvider.InstallApp(simulator.Udid, request.Artifact.Path))
        {
            throw new FlowExecutionPlatformLaunchException(
                FlowExecutionException.Infrastructure(
                    "ios-simulator-install-failed",
                    "The app bundle could not be installed on the exact iOS Simulator."),
                session);
        }
        session = session with { InstalledByInvocation = true };
        if (!_appleProvider.LaunchApp(simulator.Udid, request.Preflight.PackageId))
        {
            throw new FlowExecutionPlatformLaunchException(
                FlowExecutionException.Infrastructure(
                    "ios-simulator-launch-failed",
                    "The installed app could not be launched on the exact iOS Simulator."),
                session);
        }

        return session with { LaunchedByInvocation = true };
    }

    public override Task<FlowExecutionCleanupResult> CleanupAsync(
        FlowExecutionPlatformSession session,
        string cleanupPolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        if (session.State is not IosSimulatorFlowExecutionSessionState state)
        {
            return Task.FromResult(new FlowExecutionCleanupResult
            {
                Succeeded = false,
                DetailCode = "ios-simulator-session-missing",
            });
        }
        if (string.Equals(cleanupPolicy, FlowExecutionCleanupPolicies.None, StringComparison.Ordinal))
        {
            return Task.FromResult(new FlowExecutionCleanupResult
            {
                Succeeded = true,
                DetailCode = "ios-simulator-cleanup-none",
            });
        }

        var canStop = session.LaunchedByInvocation && !state.WasRunningBefore;
        var stopped = !canStop ||
            _appleProvider.TerminateApp(state.SimulatorUdid, state.BundleIdentifier);
        var uninstallRequested = string.Equals(
            cleanupPolicy,
            FlowExecutionCleanupPolicies.Uninstall,
            StringComparison.Ordinal);
        var uninstalled = false;
        var canUninstall = session.InstalledByInvocation && !state.WasInstalledBefore;
        var uninstallSkipped = uninstallRequested && !canUninstall;
        if (uninstallRequested && canUninstall)
            uninstalled = _appleProvider.UninstallApp(state.SimulatorUdid, state.BundleIdentifier);
        var simulatorShutdown = !state.SimulatorBootedByInvocation ||
            _appleProvider.ShutdownSimulator(state.SimulatorUdid);
        var succeeded =
            stopped &&
            (!uninstallRequested || uninstallSkipped || uninstalled) &&
            simulatorShutdown;
        return Task.FromResult(new FlowExecutionCleanupResult
        {
            Succeeded = succeeded,
            PackageStopped = stopped && canStop,
            PackageUninstalled = uninstalled,
            UninstallSkippedNotOwned = uninstallSkipped,
            DetailCode = succeeded
                ? uninstalled
                    ? state.SimulatorBootedByInvocation
                        ? "ios-simulator-stopped-uninstalled-and-shutdown"
                        : "ios-simulator-stopped-and-uninstalled"
                    : uninstallSkipped
                        ? "ios-simulator-uninstall-skipped-not-owned"
                        : state.SimulatorBootedByInvocation
                            ? "ios-simulator-stopped-and-shutdown"
                            : "ios-simulator-stopped"
                : "ios-simulator-cleanup-failed",
        });
    }

    internal static void ValidateArtifact(ResolvedAppArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (string.Equals(artifact.ArtifactType, "ipa", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(artifact.Path), ".ipa", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                "ios-physical-artifact-unsupported",
                "iOS flow run v1 supports Simulator .app bundles only; IPA and physical iOS deployment are unsupported.");
        }
        if (!string.Equals(artifact.TargetPlatformIdentifier, "ios", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.TargetRuntimeKind, "ios-simulator", StringComparison.OrdinalIgnoreCase) ||
            !artifact.TargetFramework.Contains("-ios", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(artifact.RuntimeIdentifier) ||
            !artifact.RuntimeIdentifier.StartsWith("iossimulator-", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                "ios-simulator-artifact-target-unsupported",
                "The resolved app artifact does not target an iOS Simulator runtime.");
        }
        if (!string.Equals(artifact.ArtifactType, "app", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(artifact.Path), ".app", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.ArtifactContractVersion, "1", StringComparison.Ordinal) ||
            !string.Equals(artifact.ArtifactRole, "deployable", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.DeploymentModel, "simulator-bundle", StringComparison.OrdinalIgnoreCase) ||
            !artifact.Installable ||
            !artifact.Launchable)
        {
            throw FlowExecutionException.Unsupported(
                "ios-simulator-artifact-unsupported",
                "iOS flow run v1 supports installable Simulator .app bundles only.");
        }
    }

    private static void ValidateExactRuntimeIdentifier(
        ResolvedAppArtifact artifact,
        string expectedRuntimeIdentifier,
        FlowExecutionPlatformDescriptor descriptor)
    {
        if (!string.Equals(
            artifact.RuntimeIdentifier,
            expectedRuntimeIdentifier,
            StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{descriptor.Platform}-artifact-architecture-mismatch",
                $"The resolved app artifact runtime identifier must exactly match the {descriptor.DisplayName} host architecture.");
        }
    }

    private SimulatorInfo ResolveSimulator(string? requestedUdid)
    {
        var candidates = _appleProvider.GetSimulators(availableOnly: true)
            .Where(static simulator =>
                string.Equals(simulator.Platform, "iOS", StringComparison.OrdinalIgnoreCase) ||
                simulator.RuntimeIdentifier?.Contains(".iOS-", StringComparison.OrdinalIgnoreCase) == true ||
                simulator.RuntimeIdentifier?.Contains("iOS", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(requestedUdid))
        {
            var matches = candidates
                .Where(simulator => string.Equals(simulator.Udid, requestedUdid, StringComparison.Ordinal))
                .ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 when LooksLikePhysicalDeviceIdentifier(requestedUdid) =>
                    throw FlowExecutionException.Unsupported(
                        "ios-physical-device-unsupported",
                        "The requested Apple device identifier is not an iOS Simulator UDID. Physical iOS deployment is unsupported in flow run v1."),
                0 => throw FlowExecutionException.Invalid(
                    "ios-simulator-not-found",
                    "The requested exact iOS Simulator UDID is unavailable."),
                _ => throw FlowExecutionException.Invalid(
                    "ios-simulator-ambiguous",
                    "Multiple available iOS Simulators reported the requested UDID."),
            };
        }

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw FlowExecutionException.Invalid(
                "ios-simulator-missing",
                "No available iOS Simulator was found."),
            _ => throw FlowExecutionException.Invalid(
                "ios-simulator-ambiguous",
                "Multiple iOS Simulators are available. Specify the exact UDID with --device."),
        };
    }

    private void RollbackPreSessionBoot(string simulatorUdid, bool bootedByInvocation)
    {
        if (bootedByInvocation && !_appleProvider.ShutdownSimulator(simulatorUdid))
        {
            throw FlowExecutionException.Infrastructure(
                "ios-simulator-boot-rollback-failed",
                "The invocation booted the exact iOS Simulator but could not roll it back after pre-session failure.");
        }
    }

    private static bool LooksLikePhysicalDeviceIdentifier(string value)
    {
        if (Guid.TryParse(value, out _))
            return false;
        return value.Length is >= 20 and <= 64 &&
            value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static Device CreateSimulatorDevice(SimulatorInfo simulator)
        => new()
        {
            Id = simulator.Udid,
            Name = simulator.Name,
            Platforms = ["ios"],
            Version = simulator.OSVersion,
            VersionName = simulator.Platform is null
                ? simulator.OSVersion
                : $"{simulator.Platform} {simulator.OSVersion}",
            Manufacturer = "Apple",
            Model = simulator.DeviceTypeIdentifier,
            Architecture = simulator.RuntimeIdentifier?.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase) == true
                ? "arm64"
                : simulator.RuntimeIdentifier?.EndsWith("-x64", StringComparison.OrdinalIgnoreCase) == true
                    ? "x64"
                    : null,
            Idiom = simulator.DeviceTypeIdentifier?.Contains("iPad", StringComparison.OrdinalIgnoreCase) == true
                ? DeviceIdiom.Tablet
                : DeviceIdiom.Phone,
            IsEmulator = true,
            IsRunning = simulator.IsBooted,
            ConnectionType = ConnectionType.Local,
            EmulatorId = simulator.Udid,
            Type = DeviceType.Simulator,
            State = simulator.IsBooted ? DeviceState.Booted : DeviceState.Shutdown,
        };
}

internal abstract class AppleDesktopFlowExecutionAdapterBase : DesktopFlowExecutionAdapterBase
{
    protected readonly IFlowExecutionHostEnvironment Host;
    private readonly IAppleAppBundleInspector _bundleInspector;
    private readonly string _targetPlatformIdentifier;
    private readonly string _targetRuntimeKind;
    private readonly string _targetFrameworkSuffix;
    private readonly string _runtimeIdentifierPrefix;

    protected AppleDesktopFlowExecutionAdapterBase(
        IFlowExecutionHostEnvironment host,
        IAppleAppBundleInspector bundleInspector,
        IFlowExecutionProcessController processController,
        string targetPlatformIdentifier,
        string targetRuntimeKind,
        string targetFrameworkSuffix,
        string runtimeIdentifierPrefix)
        : base(processController)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _bundleInspector = bundleInspector ?? throw new ArgumentNullException(nameof(bundleInspector));
        _targetPlatformIdentifier = targetPlatformIdentifier;
        _targetRuntimeKind = targetRuntimeKind;
        _targetFrameworkSuffix = targetFrameworkSuffix;
        _runtimeIdentifierPrefix = runtimeIdentifierPrefix;
    }

    public override void ValidateHost()
    {
        if (!Host.IsMacOS)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-host-required",
                $"{Descriptor.DisplayName} flow execution requires a macOS host.");
        }
        _ = GetDefaultRuntimeIdentifier();
    }

    public override async Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHost();
        ValidateArtifact(request.Artifact);
        if (!string.IsNullOrWhiteSpace(request.DeviceSerial) &&
            !string.Equals(request.DeviceSerial, Host.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                $"{Descriptor.Platform}-desktop-target-mismatch",
                $"The requested desktop target does not match the current {Descriptor.DisplayName} host.");
        }

        var bundle = await AppleFlowExecutionAdapterBase.ValidateBundleIdentityAsync(
            request.Artifact,
            _bundleInspector,
            cancellationToken).ConfigureAwait(false);
        var expectedExecutableRoot = Path.Combine(
            Path.GetFullPath(request.Artifact.Path),
            "Contents",
            "MacOS");
        var relativeExecutable = Path.GetRelativePath(expectedExecutableRoot, bundle.ExecutablePath);
        if (relativeExecutable.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeExecutable))
        {
            throw FlowExecutionException.Invalid(
                $"{Descriptor.Platform}-bundle-executable-layout-invalid",
                $"{Descriptor.DisplayName} requires an exact executable under Contents/MacOS.");
        }

        var device = AppleFlowExecutionAdapterBase.CreateDesktopDevice(Host, Descriptor.Platform);
        return new FlowExecutionPlatformPreflight
        {
            Device = device,
            DeviceSerial = device.Id,
            PackageId = request.Artifact.LaunchIdentity!,
            State = new DesktopFlowExecutionPreflightState
            {
                ExecutablePath = bundle.ExecutablePath,
                OwnedBuildRoot = request.Artifact.OwnedOutputRoot,
                RuntimeKind = _targetRuntimeKind,
                DeviceProfile = Descriptor.Platform + "-desktop",
            },
        };
    }

    private void ValidateArtifact(ResolvedAppArtifact artifact)
    {
        if (!string.Equals(artifact.TargetPlatformIdentifier, _targetPlatformIdentifier, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.TargetRuntimeKind, _targetRuntimeKind, StringComparison.OrdinalIgnoreCase) ||
            !artifact.TargetFramework.Contains(_targetFrameworkSuffix, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(artifact.RuntimeIdentifier) ||
            !artifact.RuntimeIdentifier.StartsWith(_runtimeIdentifierPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-target-unsupported",
                $"The resolved app artifact does not target the supported {Descriptor.DisplayName} runtime.");
        }
        var expectedRuntimeIdentifier = GetDefaultRuntimeIdentifier();
        if (!string.Equals(
            artifact.RuntimeIdentifier,
            expectedRuntimeIdentifier,
            StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-architecture-mismatch",
                $"The resolved app artifact runtime identifier must exactly match the {Descriptor.DisplayName} host architecture.");
        }
        if (!string.Equals(artifact.ArtifactType, "app", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(artifact.Path), ".app", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.ArtifactContractVersion, "1", StringComparison.Ordinal) ||
            !string.Equals(artifact.ArtifactRole, "launcher", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.DeploymentModel, "desktop-bundle", StringComparison.OrdinalIgnoreCase) ||
            !artifact.Launchable)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-unsupported",
                $"{Descriptor.DisplayName} flow execution supports directly launchable desktop .app bundles only.");
        }
    }
}

internal sealed class MacCatalystFlowExecutionAdapter : AppleDesktopFlowExecutionAdapterBase
{
    internal static readonly FlowExecutionPlatformDescriptor PlatformDescriptor = new()
    {
        Platform = "maccatalyst",
        DisplayName = "Mac Catalyst",
        CommandAliases = ["maccatalyst", "mac-catalyst"],
        FlowPlatformAliases = ["maccatalyst", "mac-catalyst"],
        AgentPlatformAliases = ["maccatalyst", "mac-catalyst"],
        TargetFrameworkPlatformIdentifiers = ["maccatalyst"],
        CandidateArtifactTypes = ["app"],
    };

    public MacCatalystFlowExecutionAdapter(
        IFlowExecutionHostEnvironment host,
        IAppleAppBundleInspector bundleInspector,
        IFlowExecutionProcessController processController)
        : base(
            host,
            bundleInspector,
            processController,
            "maccatalyst",
            "mac-catalyst",
            "-maccatalyst",
            "maccatalyst-")
    {
    }

    public override FlowExecutionPlatformDescriptor Descriptor => PlatformDescriptor;

    public override string GetDefaultRuntimeIdentifier()
        => HostArchitectureRuntimeIdentifier("maccatalyst-arm64", "maccatalyst-x64");

    private string HostArchitectureRuntimeIdentifier(string arm64, string x64)
        => Host.ProcessArchitecture switch
        {
            Architecture.Arm64 => arm64,
            Architecture.X64 => x64,
            _ => throw FlowExecutionException.Unsupported(
                "maccatalyst-host-architecture-unsupported",
                "Mac Catalyst flow execution supports arm64 and x64 macOS hosts only."),
        };
}

internal sealed class AppKitFlowExecutionAdapter : AppleDesktopFlowExecutionAdapterBase
{
    internal static readonly FlowExecutionPlatformDescriptor PlatformDescriptor = new()
    {
        Platform = "macos",
        DisplayName = "experimental macOS AppKit",
        CommandAliases = ["macos", "appkit"],
        FlowPlatformAliases = ["macos", "appkit"],
        AgentPlatformAliases = ["macos", "appkit"],
        TargetFrameworkPlatformIdentifiers = ["macos"],
        CandidateArtifactTypes = ["app"],
        Experimental = true,
    };

    public AppKitFlowExecutionAdapter(
        IFlowExecutionHostEnvironment host,
        IAppleAppBundleInspector bundleInspector,
        IFlowExecutionProcessController processController)
        : base(
            host,
            bundleInspector,
            processController,
            "macos",
            "macos-appkit",
            "-macos",
            "osx-")
    {
    }

    public override FlowExecutionPlatformDescriptor Descriptor => PlatformDescriptor;

    public override string GetDefaultRuntimeIdentifier()
        => Host.ProcessArchitecture switch
        {
            Architecture.Arm64 => "osx-arm64",
            Architecture.X64 => "osx-x64",
            _ => throw FlowExecutionException.Unsupported(
                "macos-host-architecture-unsupported",
                "Experimental AppKit flow execution supports arm64 and x64 macOS hosts only."),
        };
}
