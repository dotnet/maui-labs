using System.Text.Json;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal enum WindowsAppBackend
{
    Unknown,
    WinUI,
    Wpf,
}

internal sealed record WindowsAppProjectFacts
{
    public required WindowsAppBackend Backend { get; init; }
    public required bool ExplicitlyUnpackaged { get; init; }
}

internal interface IWindowsAppProjectInspector
{
    Task<WindowsAppProjectFacts> InspectAsync(
        ResolvedAppArtifact artifact,
        CancellationToken cancellationToken = default);
}

internal sealed class WindowsAppProjectInspector : IWindowsAppProjectInspector
{
    private const int MaximumEvaluationCharacters = 1024 * 1024;
    private static readonly string[] RequiredProperties =
    [
        "TargetFramework",
        "Configuration",
        "RuntimeIdentifier",
        "UseWPF",
        "UseMaui",
        "WindowsPackageType",
        "GenerateAppxPackageOnBuild",
    ];
    private static readonly string[] MsBuildEnvironmentVariablesToRemove =
    [
        "MSBuildSDKsPath",
        "MSBUILD_EXE_PATH",
        "MSBuildExtensionsPath",
        "MSBuildToolsPath",
    ];
    private readonly IExecutionProcessRunner _processRunner;

    public WindowsAppProjectInspector(IExecutionProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<WindowsAppProjectFacts> InspectAsync(
        ResolvedAppArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var arguments = new List<string>
        {
            "msbuild",
            artifact.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            $"-property:TargetFramework={artifact.TargetFramework}",
            $"-property:Configuration={artifact.Configuration}",
        };
        if (!string.IsNullOrWhiteSpace(artifact.RuntimeIdentifier))
            arguments.Add($"-property:RuntimeIdentifier={artifact.RuntimeIdentifier}");
        arguments.Add("-getProperty:" + string.Join(',', RequiredProperties));

        var result = await _processRunner.RunAsync(
            "dotnet",
            arguments,
            workingDirectory: Path.GetDirectoryName(artifact.ProjectPath),
            timeout: TimeSpan.FromMinutes(2),
            environmentVariablesToRemove: MsBuildEnvironmentVariablesToRemove,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw FlowExecutionException.Infrastructure(
                "windows-project-evaluation-failed",
                $"MSBuild could not evaluate the Windows app project before launch (exit code {result.ExitCode}).");
        }
        if (result.StandardOutput.Length > MaximumEvaluationCharacters)
        {
            throw FlowExecutionException.Infrastructure(
                "windows-project-evaluation-too-large",
                "The evaluated Windows app project properties exceeded the bounded response size.");
        }

        var properties = ParseProperties(result.StandardOutput);
        EnsureExactBuildContext(properties, artifact);
        var useWpf = ParseBoolean(properties, "UseWPF");
        var useMaui = ParseBoolean(properties, "UseMaui");
        var packageType = properties["WindowsPackageType"].Trim();
        var generatePackage = ParseBoolean(properties, "GenerateAppxPackageOnBuild");
        var explicitlyUnpackaged = string.Equals(
            packageType,
            "None",
            StringComparison.OrdinalIgnoreCase);
        var explicitlyPackaged =
            (!string.IsNullOrWhiteSpace(packageType) && !explicitlyUnpackaged) ||
            generatePackage;

        return new WindowsAppProjectFacts
        {
            Backend = useWpf
                ? WindowsAppBackend.Wpf
                : useMaui
                    ? WindowsAppBackend.WinUI
                    : WindowsAppBackend.Unknown,
            ExplicitlyUnpackaged = useWpf
                ? !explicitlyPackaged
                : explicitlyUnpackaged,
        };
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(
                output.Trim(),
                new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Properties", out var propertiesElement) ||
                propertiesElement.ValueKind != JsonValueKind.Object)
            {
                throw InvalidEvaluation();
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in RequiredProperties)
            {
                if (!propertiesElement.TryGetProperty(name, out var value) ||
                    value.ValueKind != JsonValueKind.String)
                {
                    throw InvalidEvaluation();
                }
                properties[name] = value.GetString() ?? "";
            }
            return properties;
        }
        catch (JsonException ex)
        {
            throw FlowExecutionException.Infrastructure(
                "windows-project-evaluation-invalid",
                "MSBuild returned invalid evaluated Windows app project properties.",
                ex);
        }
    }

    private static void EnsureExactBuildContext(
        IReadOnlyDictionary<string, string> properties,
        ResolvedAppArtifact artifact)
    {
        if (!string.Equals(
                properties["TargetFramework"],
                artifact.TargetFramework,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                properties["Configuration"],
                artifact.Configuration,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                properties["RuntimeIdentifier"],
                artifact.RuntimeIdentifier ?? "",
                StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Infrastructure(
                "windows-project-evaluation-context-mismatch",
                "The evaluated Windows app project properties did not match the resolved artifact build context.");
        }
    }

    private static bool ParseBoolean(
        IReadOnlyDictionary<string, string> properties,
        string name)
    {
        var value = properties[name].Trim();
        if (value.Length == 0)
            return false;
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw InvalidEvaluation();
    }

    private static FlowExecutionException InvalidEvaluation()
        => FlowExecutionException.Infrastructure(
            "windows-project-evaluation-invalid",
            "MSBuild returned incomplete or invalid evaluated Windows app project properties.");
}

internal abstract class WindowsDesktopFlowExecutionAdapterBase : DesktopFlowExecutionAdapterBase
{
    private readonly IFlowExecutionHostEnvironment _host;
    private readonly IWindowsDesktopSessionAdmissionProbe _desktopSessionProbe;
    private readonly IWindowsAppProjectInspector _projectInspector;
    private readonly WindowsAppBackend _expectedBackend;

    protected WindowsDesktopFlowExecutionAdapterBase(
        IFlowExecutionHostEnvironment host,
        IWindowsDesktopSessionAdmissionProbe desktopSessionProbe,
        IWindowsAppProjectInspector projectInspector,
        IFlowExecutionProcessController processController,
        WindowsAppBackend expectedBackend)
        : base(processController)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _desktopSessionProbe = desktopSessionProbe ?? throw new ArgumentNullException(nameof(desktopSessionProbe));
        _projectInspector = projectInspector ?? throw new ArgumentNullException(nameof(projectInspector));
        _expectedBackend = expectedBackend;
    }

    public override void ValidateHost()
    {
        if (!_host.IsWindows)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-host-required",
                $"{Descriptor.DisplayName} flow execution requires a Windows host.");
        }
    }

    public override string GetDefaultRuntimeIdentifier()
        => _host.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "win-x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "win-arm64",
            System.Runtime.InteropServices.Architecture.X86 => "win-x86",
            _ => throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-host-architecture-unsupported",
                $"{Descriptor.DisplayName} flow execution supports x86, x64, and arm64 Windows hosts only."),
        };

    public override async Task<FlowExecutionPlatformPreflight> PreflightAsync(
        FlowExecutionPlatformPreflightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHost();
        ValidateArtifact(request.Artifact);

        var projectFacts = await _projectInspector.InspectAsync(
            request.Artifact,
            cancellationToken).ConfigureAwait(false);
        if (projectFacts.Backend != _expectedBackend)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-backend-mismatch",
                _expectedBackend == WindowsAppBackend.WinUI
                    ? "The selected Windows platform requires the official WinUI backend; WPF must use --platform wpf."
                    : "The experimental WPF platform requires a WPF MAUI app and never represents official WinUI coverage.");
        }
        if (!projectFacts.ExplicitlyUnpackaged)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-packaged-project-unsupported",
                $"{Descriptor.DisplayName} flow run v1 requires an explicitly unpackaged executable.");
        }

        if (!string.IsNullOrWhiteSpace(request.DeviceSerial) &&
            !string.Equals(request.DeviceSerial, _host.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                $"{Descriptor.Platform}-desktop-target-mismatch",
                $"The requested desktop target does not match the current {Descriptor.DisplayName} host.");
        }

        EnsureInteractiveDesktop(_desktopSessionProbe.Probe());
        var executable = Path.GetFullPath(request.Artifact.Path);
        var device = new Device
        {
            Id = _host.MachineName,
            Name = _host.MachineName,
            Platforms = [Descriptor.Platform],
            Version = _host.OsVersion,
            Architecture = _host.ProcessArchitecture.ToString().ToLowerInvariant(),
            Idiom = DeviceIdiom.Desktop,
            IsEmulator = false,
            IsRunning = true,
            ConnectionType = ConnectionType.Local,
            Type = DeviceType.Physical,
            State = DeviceState.Connected,
        };
        return new FlowExecutionPlatformPreflight
        {
            Device = device,
            DeviceSerial = device.Id,
            PackageId = request.Artifact.ApplicationId!,
            State = new DesktopFlowExecutionPreflightState
            {
                ExecutablePath = executable,
                OwnedBuildRoot = request.Artifact.OwnedOutputRoot,
                RuntimeKind = _expectedBackend == WindowsAppBackend.WinUI
                    ? "windows-winui"
                    : "windows-wpf",
                DeviceProfile = _expectedBackend == WindowsAppBackend.WinUI
                    ? "windows-desktop"
                    : "wpf-desktop",
            },
        };
    }

    protected override void ValidateImmediatelyBeforeLaunch(FlowExecutionPlatformPreflight preflight)
        => EnsureInteractiveDesktop(_desktopSessionProbe.Probe());

    private void ValidateArtifact(ResolvedAppArtifact artifact)
    {
        if (!string.Equals(artifact.TargetPlatformIdentifier, "windows", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.TargetRuntimeKind, "windows", StringComparison.OrdinalIgnoreCase) ||
            !artifact.TargetFramework.Contains("-windows", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-target-unsupported",
                $"The resolved app artifact does not target the supported {Descriptor.DisplayName} runtime.");
        }
        if (!string.Equals(
            artifact.RuntimeIdentifier,
            GetDefaultRuntimeIdentifier(),
            StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-architecture-mismatch",
                $"The resolved app artifact runtime identifier must exactly match the {Descriptor.DisplayName} host architecture.");
        }

        if (string.Equals(artifact.ArtifactType, "msix", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(artifact.ArtifactType, "appinstaller", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(artifact.Path), ".msix", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(artifact.Path), ".appinstaller", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-packaged-artifact-unsupported",
                $"{Descriptor.DisplayName} flow run v1 does not install MSIX or AppInstaller artifacts.");
        }
        if (!string.Equals(artifact.ArtifactType, "exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(artifact.Path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-unsupported",
                $"{Descriptor.DisplayName} flow run v1 supports an unpackaged executable only.");
        }
        if (!string.Equals(artifact.ArtifactContractVersion, "1", StringComparison.Ordinal) ||
            !string.Equals(artifact.ArtifactRole, "launcher", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(artifact.DeploymentModel, "executable", StringComparison.OrdinalIgnoreCase) ||
            artifact.Installable ||
            !artifact.Launchable)
        {
            throw FlowExecutionException.Unsupported(
                $"{Descriptor.Platform}-artifact-not-launchable",
                $"The AppProjectReference metadata does not describe a directly launchable {Descriptor.DisplayName} executable.");
        }
        if (!string.Equals(artifact.LaunchIdentityKind, "file-path", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(artifact.LaunchIdentity) ||
            !PathsEqual(artifact.Path, artifact.LaunchIdentity) ||
            string.IsNullOrWhiteSpace(artifact.ApplicationId))
        {
            throw FlowExecutionException.Invalid(
                $"{Descriptor.Platform}-launch-identity-invalid",
                $"The AppProjectReference launch identity is not the exact {Descriptor.DisplayName} executable.");
        }
    }

    private void EnsureInteractiveDesktop(WindowsDesktopSessionAdmission admission)
    {
        if (admission.IsAllowed)
            return;
        throw FlowExecutionException.Unsupported(
            $"{Descriptor.Platform}-interactive-desktop-required",
            $"{Descriptor.DisplayName} flow execution requires an active, connected, unlocked interactive desktop ({admission.Reason}).");
    }

    private static bool PathsEqual(string first, string second)
        => string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class WindowsFlowExecutionAdapter : WindowsDesktopFlowExecutionAdapterBase
{
    internal static readonly FlowExecutionPlatformDescriptor PlatformDescriptor = new()
    {
        Platform = "windows",
        DisplayName = "Windows WinUI",
        CommandAliases = ["windows", "winui"],
        FlowPlatformAliases = ["windows", "winui"],
        AgentPlatformAliases = ["windows", "winui"],
        TargetFrameworkPlatformIdentifiers = ["windows"],
        CandidateArtifactTypes = ["exe", "msix", "appinstaller"],
        UnsupportedArtifactTypes = ["msix", "appinstaller"],
        UnsupportedArtifactCode = "windows-packaged-artifact-unsupported",
        UnsupportedArtifactMessage = "Windows WinUI flow run v1 does not install MSIX or AppInstaller artifacts.",
    };

    public WindowsFlowExecutionAdapter(
        IFlowExecutionHostEnvironment host,
        IWindowsDesktopSessionAdmissionProbe desktopSessionProbe,
        IWindowsAppProjectInspector projectInspector,
        IFlowExecutionProcessController processController)
        : base(
            host,
            desktopSessionProbe,
            projectInspector,
            processController,
            WindowsAppBackend.WinUI)
    {
    }

    public override FlowExecutionPlatformDescriptor Descriptor => PlatformDescriptor;
}

internal sealed class WpfFlowExecutionAdapter : WindowsDesktopFlowExecutionAdapterBase
{
    internal static readonly FlowExecutionPlatformDescriptor PlatformDescriptor = new()
    {
        Platform = "wpf",
        DisplayName = "experimental Windows WPF",
        CommandAliases = ["wpf"],
        FlowPlatformAliases = ["wpf"],
        AgentPlatformAliases = ["wpf"],
        TargetFrameworkPlatformIdentifiers = ["windows"],
        CandidateArtifactTypes = ["exe", "msix", "appinstaller"],
        UnsupportedArtifactTypes = ["msix", "appinstaller"],
        UnsupportedArtifactCode = "wpf-packaged-artifact-unsupported",
        UnsupportedArtifactMessage = "Experimental WPF flow run does not install MSIX or AppInstaller artifacts.",
        Experimental = true,
    };

    public WpfFlowExecutionAdapter(
        IFlowExecutionHostEnvironment host,
        IWindowsDesktopSessionAdmissionProbe desktopSessionProbe,
        IWindowsAppProjectInspector projectInspector,
        IFlowExecutionProcessController processController)
        : base(
            host,
            desktopSessionProbe,
            projectInspector,
            processController,
            WindowsAppBackend.Wpf)
    {
    }

    public override FlowExecutionPlatformDescriptor Descriptor => PlatformDescriptor;
}
