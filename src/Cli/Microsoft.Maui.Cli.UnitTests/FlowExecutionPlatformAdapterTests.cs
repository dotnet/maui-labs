using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class FlowExecutionPlatformAdapterTests
{
    private static readonly string MacCatalystBundlePath =
        Path.Combine(Path.GetTempPath(), "build", "App.app");

    [Fact]
    public void WindowsAdapter_NonWindowsHost_IsTypedUnsupported()
    {
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(
            host: new FakeHost { IsWindows = false },
            processes: processes);

        var exception = Assert.Throws<FlowExecutionException>(adapter.ValidateHost);

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("windows-host-required", exception.Code);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WindowsAdapter_DisconnectedDesktop_IsUnsupportedBeforeLaunch()
    {
        var probe = new FakeAdmissionProbe(RejectedDesktop(WindowsWtsConnectionState.Disconnected));
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(probe: probe, processes: processes);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = WindowsArtifact(),
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("windows-interactive-desktop-required", exception.Code);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WindowsAdapter_PackagedArtifact_IsUnsupportedBeforeAdmissionOrLaunch()
    {
        var probe = new FakeAdmissionProbe(AllowedDesktop());
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(probe: probe, processes: processes);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = WindowsArtifact() with
                {
                    Path = @"C:\build\App.msix",
                    ArtifactType = "msix",
                    ArtifactRole = "deployable",
                    DeploymentModel = "package",
                    LaunchIdentityKind = "windows-package-identity",
                    LaunchIdentity = "com.example.app",
                    Installable = true,
                },
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("windows-packaged-artifact-unsupported", exception.Code);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WindowsAdapter_WrongRuntime_IsUnsupportedBeforeLaunch()
    {
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(processes: processes);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = WindowsArtifact() with
                {
                    TargetFramework = "net10.0-windows10.0.19041.0",
                    TargetRuntimeKind = "windows-wpf",
                },
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("windows-artifact-target-unsupported", exception.Code);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WindowsAdapter_RuntimeIdentifierMustMatchHostArchitectureExactly()
    {
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(
            host: new FakeHost
            {
                IsWindows = true,
                ProcessArchitecture = Architecture.Arm64,
            },
            processes: processes);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = WindowsArtifact() with { RuntimeIdentifier = "win-x64" },
            }));

        Assert.Equal("windows-artifact-architecture-mismatch", exception.Code);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WindowsAdapter_OfficialWinUiRejectsWpfBackendBeforeLaunch()
    {
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(
            inspector: new FakeWindowsProjectInspector(WindowsAppBackend.Wpf, unpackaged: true),
            processes: processes);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = WindowsArtifact(),
            }));

        Assert.Equal("windows-backend-mismatch", exception.Code);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task WpfAdapter_IsExperimentalAndUsesOnlyItsOwnedProcess()
    {
        var processes = new FakeProcessController();
        var adapter = new WpfFlowExecutionAdapter(
            new FakeHost { IsWindows = true },
            new FakeAdmissionProbe(AllowedDesktop()),
            new FakeWindowsProjectInspector(WindowsAppBackend.Wpf, unpackaged: true),
            processes);

        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = WindowsArtifact(),
        });
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = WindowsArtifact(),
            Preflight = preflight,
            BrokerPort = 19223,
        });
        var cleanup = await adapter.CleanupAsync(session, FlowExecutionCleanupPolicies.Uninstall);

        Assert.True(adapter.Descriptor.Experimental);
        Assert.Equal("wpf", session.Platform);
        Assert.Equal("windows-wpf", session.RuntimeKind);
        Assert.False(session.InstalledByInvocation);
        Assert.True(cleanup.Succeeded);
        Assert.True(cleanup.UninstallSkippedNotOwned);
        Assert.Same(processes.StartedProcess, processes.StoppedProcess);
    }

    [Fact]
    public async Task WindowsAdapter_CleanupNone_ReleasesHandleWithoutStoppingOwnedProcess()
    {
        var processes = new FakeProcessController();
        var adapter = CreateWindowsAdapter(processes: processes);
        var artifact = WindowsArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
        });
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = artifact,
            Preflight = preflight,
            BrokerPort = 19223,
        });

        var cleanup = await adapter.CleanupAsync(
            session,
            FlowExecutionCleanupPolicies.None);

        Assert.True(cleanup.Succeeded);
        Assert.Null(processes.StoppedProcess);
        Assert.Same(processes.StartedProcess, processes.ReleasedProcess);
        Assert.True(((FakeFlowExecutionProcessHandle)processes.StartedProcess!.Handle).RootAlive);
        Assert.True(FlowExecutionCoordinator.ShouldRetainOwnedArtifactRoot(
            session,
            FlowExecutionCleanupPolicies.None));
    }

    [Fact]
    public async Task WindowsProjectInspector_EvaluatesImportsAndBuildConditions()
    {
        using var workspace = new ExecutionPlatformTestWorkspace();
        var project = workspace.WriteFile(
            "ImportedWindowsApp.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
                <EnableWindowsTargeting>true</EnableWindowsTargeting>
              </PropertyGroup>
              <Import Project="Backend.props" />
            </Project>
            """);
        workspace.WriteFile(
            "Directory.Build.props",
            """
            <Project>
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <UseMaui>true</UseMaui>
                <WindowsPackageType Condition="'$(RuntimeIdentifier)' == 'win-x64'">None</WindowsPackageType>
              </PropertyGroup>
            </Project>
            """);
        workspace.WriteFile(
            "Directory.Build.targets",
            """
            <Project />
            """);
        workspace.WriteFile(
            "Backend.props",
            """
            <Project>
              <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                <UseWPF>true</UseWPF>
                <GenerateAppxPackageOnBuild>true</GenerateAppxPackageOnBuild>
              </PropertyGroup>
            </Project>
            """);
        var inspector = new WindowsAppProjectInspector(new ExecutionProcessRunner());

        var debug = await inspector.InspectAsync(WindowsArtifact() with
        {
            ProjectPath = project,
            Configuration = "Debug",
            RuntimeIdentifier = "win-x64",
        });
        var release = await inspector.InspectAsync(WindowsArtifact() with
        {
            ProjectPath = project,
            Configuration = "Release",
            RuntimeIdentifier = "win-arm64",
        });

        Assert.Equal(WindowsAppBackend.WinUI, debug.Backend);
        Assert.True(debug.ExplicitlyUnpackaged);
        Assert.Equal(WindowsAppBackend.Wpf, release.Backend);
        Assert.False(release.ExplicitlyUnpackaged);
    }

    [Fact]
    public async Task WindowsProjectInspector_MissingEvaluatedPropertyFailsClosed()
    {
        var runner = new StaticExecutionProcessRunner(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = """
                {
                  "Properties": {
                    "TargetFramework": "net10.0-windows10.0.19041.0",
                    "Configuration": "Debug",
                    "RuntimeIdentifier": "win-x64",
                    "UseWPF": "",
                    "UseMaui": "true",
                    "WindowsPackageType": "None"
                  }
                }
                """,
        });
        var inspector = new WindowsAppProjectInspector(runner);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            inspector.InspectAsync(WindowsArtifact()));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, failure.ExitCategory);
        Assert.Equal("windows-project-evaluation-invalid", failure.Code);
    }

    [Fact]
    public async Task SystemProcessController_StopTerminatesOnlyOwnedRootProcess()
    {
        var handle = new FakeFlowExecutionProcessHandle(4242);
        var owned = new FlowExecutionOwnedProcess
        {
            ProcessId = 4242,
            ExecutablePath = @"C:\build\App.exe",
            Handle = handle,
        };
        var controller = new SystemFlowExecutionProcessController();

        var result = await controller.StopAsync(owned);

        Assert.True(result.Succeeded);
        Assert.False(handle.RootAlive);
        Assert.True(handle.ChildAlive);
        Assert.True(handle.Disposed);
    }

    [Fact]
    public async Task AndroidAdapter_CleanupRemovesOnlyInvocationAddedPortMappings()
    {
        var device = new Device
        {
            Id = "emulator-5554",
            EmulatorId = "Pixel_8_API_35",
            Name = "Pixel 8",
            Platforms = ["android"],
            IsEmulator = true,
            IsRunning = true,
            State = DeviceState.Booted,
            Type = DeviceType.Emulator,
            Idiom = DeviceIdiom.Phone,
        };
        var portManager = new FakeAndroidFlowPortManager(
            ForwardingReport(
                brokerReverseAdded: true,
                brokerReversePresent: true),
            ForwardingReport(
                agentPort: 9223,
                agentPresentBefore: false,
                agentAdded: true),
            ForwardingReport(
                agentPort: 9224,
                agentPresentBefore: true,
                agentAdded: false));
        var deployment = new FakeAndroidAppDeployment();
        var adapter = new AndroidFlowExecutionAdapter(
            new FakeAndroidProvider(),
            deployment,
            portManager,
            new StaticExecutionProcessRunner());
        var preflight = new FlowExecutionPlatformPreflight
        {
            Device = device,
            DeviceSerial = device.Id,
            PackageId = "com.example.app",
        };

        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = AndroidArtifact(),
            Preflight = preflight,
            BrokerPort = 19223,
        });
        await adapter.EstablishAgentForwardingAsync(session, 9223, 19223);
        await adapter.EstablishAgentForwardingAsync(session, 9224, 19223);
        var cleanup = await adapter.CleanupAsync(session, FlowExecutionCleanupPolicies.None);

        Assert.True(cleanup.Succeeded);
        Assert.True(session.RequireAgentDeviceIdentity);
        Assert.Collection(
            portManager.RemovedForwards,
            mapping => Assert.Equal(("emulator-5554", 9223), mapping));
        Assert.Collection(
            portManager.RemovedReverses,
            mapping => Assert.Equal(("emulator-5554", 19223), mapping));
        Assert.DoesNotContain(portManager.RemovedForwards, mapping => mapping.Port == 9224);
        Assert.Equal(1, deployment.CleanupCalls);
    }

    [Fact]
    public async Task AndroidAdapter_CleanupPreservesPreexistingBrokerReverse()
    {
        var device = new Device
        {
            Id = "emulator-5554",
            EmulatorId = "Pixel_8_API_35",
            Name = "Pixel 8",
            Platforms = ["android"],
            IsEmulator = true,
            IsRunning = true,
            State = DeviceState.Booted,
            Type = DeviceType.Emulator,
            Idiom = DeviceIdiom.Phone,
        };
        var portManager = new FakeAndroidFlowPortManager(
            ForwardingReport(
                brokerReverseAdded: false,
                brokerReversePresent: true));
        var adapter = new AndroidFlowExecutionAdapter(
            new FakeAndroidProvider(),
            new FakeAndroidAppDeployment(),
            portManager,
            new StaticExecutionProcessRunner());
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = AndroidArtifact(),
            Preflight = new FlowExecutionPlatformPreflight
            {
                Device = device,
                DeviceSerial = device.Id,
                PackageId = "com.example.app",
            },
            BrokerPort = 19223,
        });

        var cleanup = await adapter.CleanupAsync(session, FlowExecutionCleanupPolicies.None);

        Assert.True(cleanup.Succeeded);
        Assert.Empty(portManager.RemovedReverses);
        Assert.Empty(portManager.RemovedForwards);
    }

    [Fact]
    public async Task AndroidAdapter_PhysicalDevice_IsRejectedBeforeDeploymentOrForwarding()
    {
        var physical = new Device
        {
            Id = "RZ8T123456A",
            Name = "Phone",
            Platforms = ["android"],
            IsEmulator = false,
            IsRunning = true,
            State = DeviceState.Connected,
            Type = DeviceType.Physical,
            Idiom = DeviceIdiom.Phone,
        };
        var provider = new FakeAndroidProvider { Devices = [physical] };
        var deployment = new FakeAndroidAppDeployment();
        var portManager = new FakeAndroidFlowPortManager();
        var adapter = new AndroidFlowExecutionAdapter(
            provider,
            deployment,
            portManager,
            new StaticExecutionProcessRunner());

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = AndroidArtifact(),
                DeviceSerial = physical.Id,
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, failure.ExitCategory);
        Assert.Equal("android-physical-device-unsupported", failure.Code);
        Assert.Equal(0, deployment.DeployCalls);
        Assert.Equal(0, portManager.EnsureCalls);
    }

    [Fact]
    public void IosSimulatorAdapter_NonMacHost_IsTypedUnsupported()
    {
        var adapter = CreateIosAdapter(
            new FakeAppleProvider(),
            new FakeHost { IsMacOS = false });

        var exception = Assert.Throws<FlowExecutionException>(adapter.ValidateHost);

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("ios-host-required", exception.Code);
    }

    [Fact]
    public async Task AppleSimulatorAppInspector_ConvertsListAppsPlistAndMatchesExactBundle()
    {
        var processes = new StaticExecutionProcessRunner(
            new ProcessResult { ExitCode = 0 },
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = """
                    {
                        "com.example.app" = {
                            CFBundleName = App;
                        };
                    }
                    """,
            },
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = """
                    PID	Status	Label
                    501	0	UIKitApplication:com.example.app.beta[abc]
                    777	0	UIKitApplication:com.example.app[def][rb-legacy]
                    """,
            });
        var converter = new StaticStandardInputProcessRunner(new ProcessResult
        {
            ExitCode = 0,
            StandardOutput = """{"com.example.app":{"CFBundleName":"App"}}""",
        });
        var inspector = new AppleSimulatorAppInspector(processes, converter);

        await inspector.WaitForBootReadinessAsync("SIM-UDID");
        var state = await inspector.InspectAsync("SIM-UDID", "com.example.app");

        Assert.True(state.Installed);
        Assert.True(state.Running);
        Assert.Equal(
            ["simctl", "bootstatus", "SIM-UDID", "-b"],
            processes.Calls[0].Arguments);
        Assert.Equal(
            ["simctl", "listapps", "SIM-UDID"],
            processes.Calls[1].Arguments);
        Assert.Equal(
            ["simctl", "spawn", "SIM-UDID", "launchctl", "list"],
            processes.Calls[2].Arguments);
        Assert.Equal(
            ["-convert", "json", "-o", "-", "-"],
            Assert.Single(converter.Calls).Arguments);
        Assert.Contains("com.example.app", converter.Calls[0].StandardInput);
    }

    [Fact]
    public async Task AppleSimulatorAppInspector_ChecksRunningStateIndependentlyOfInstallationList()
    {
        var processes = new StaticExecutionProcessRunner(
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "{}",
            },
            new ProcessResult
            {
                ExitCode = 0,
                StandardOutput = "501\t0\tUIKitApplication:com.example.app[opaque]\n",
            });
        var inspector = new AppleSimulatorAppInspector(
            processes,
            new StaticStandardInputProcessRunner(new ProcessResult { ExitCode = 0 }));

        var state = await inspector.InspectAsync("SIM-UDID", "com.example.app");

        Assert.False(state.Installed);
        Assert.True(state.Running);
        Assert.Equal(2, processes.Calls.Count);
    }

    [Fact]
    public async Task IosSimulatorAdapter_MultipleSimulatorsRequireExactUdidBeforeMutation()
    {
        var apple = new FakeAppleProvider
        {
            Simulators =
            [
                Simulator("sim-one"),
                Simulator("sim-two"),
            ],
        };
        var adapter = CreateIosAdapter(apple);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = IosSimulatorArtifact(),
            }));

        Assert.Equal(FlowExecutionExitCategories.InvalidConfiguration, exception.ExitCategory);
        Assert.Equal("ios-simulator-ambiguous", exception.Code);
        Assert.Empty(apple.BootedSimulators);
        Assert.Empty(apple.InstalledApps);
        Assert.Empty(apple.LaunchedApps);
    }

    [Fact]
    public async Task IosSimulatorAdapter_PhysicalArtifactIsUnsupportedBeforeProviderMutation()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var adapter = CreateIosAdapter(apple);

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = IosSimulatorArtifact() with
                {
                    Path = @"C:\build\App.ipa",
                    ArtifactType = "ipa",
                    ArtifactRole = "distribution",
                    TargetRuntimeKind = "ios-device",
                    RuntimeIdentifier = "ios-arm64",
                    DeploymentModel = "physical-device-archive",
                    Installable = false,
                    Launchable = false,
                },
                DeviceSerial = "sim-one",
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, exception.ExitCategory);
        Assert.Equal("ios-physical-artifact-unsupported", exception.Code);
        Assert.Empty(apple.BootedSimulators);
        Assert.Empty(apple.InstalledApps);
        Assert.Empty(apple.LaunchedApps);
    }

    [Fact]
    public async Task IosSimulatorAdapter_PhysicalDeviceIdentifier_IsExplicitlyUnsupported()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var adapter = CreateIosAdapter(apple);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = IosSimulatorArtifact(),
                DeviceSerial = "00008110-001234567890001E",
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, failure.ExitCategory);
        Assert.Equal("ios-physical-device-unsupported", failure.Code);
        Assert.Empty(apple.BootedSimulators);
        Assert.Empty(apple.InstalledApps);
    }

    [Fact]
    public async Task IosSimulatorCleanup_UninstallsInvocationOwnedInstall()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var adapter = CreateIosAdapter(
            apple,
            appInspector: new FakeAppleSimulatorAppInspector(installed: false));
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = artifact,
            Preflight = preflight,
            BrokerPort = 19223,
        });

        var cleanup = await adapter.CleanupAsync(
            session,
            FlowExecutionCleanupPolicies.Uninstall);

        Assert.True(session.InstalledByInvocation);
        Assert.True(cleanup.Succeeded);
        Assert.True(cleanup.PackageUninstalled);
        Assert.Single(apple.UninstalledApps);
        Assert.False(cleanup.UninstallSkippedNotOwned);
        Assert.Single(apple.TerminatedApps);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IosSimulatorAdapter_UnsafePriorAppStateFailsBeforeAppMutation(
        bool installed,
        bool running)
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var inspector = new FakeAppleSimulatorAppInspector(
            installed: installed,
            running: running);
        var adapter = CreateIosAdapter(apple, appInspector: inspector);
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
            {
                Artifact = artifact,
                Preflight = preflight,
                BrokerPort = 19223,
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, failure.ExitCategory);
        Assert.Equal("ios-simulator-preexisting-app-unsafe", failure.Code);
        Assert.Equal(1, inspector.BootReadinessCalls);
        Assert.Equal(1, inspector.InspectCalls);
        Assert.Empty(apple.InstalledApps);
        Assert.Empty(apple.LaunchedApps);
        Assert.Empty(apple.TerminatedApps);
        Assert.Empty(apple.UninstalledApps);
    }

    [Fact]
    public async Task IosSimulatorAdapter_WaitsForBootReadinessBeforeInstall()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one") with { IsBooted = false }],
        };
        var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inspector = new FakeAppleSimulatorAppInspector(
            installed: false,
            bootReadiness: readiness.Task);
        var adapter = CreateIosAdapter(apple, appInspector: inspector);
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });

        var launch = adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = artifact,
            Preflight = preflight,
            BrokerPort = 19223,
        });
        await inspector.BootReadinessObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(apple.BootedSimulators);
        Assert.Empty(apple.InstalledApps);
        Assert.Equal(0, inspector.InspectCalls);

        readiness.SetResult(true);
        var session = await launch;

        Assert.True(session.LaunchedByInvocation);
        Assert.Single(apple.InstalledApps);
        Assert.Single(apple.LaunchedApps);
    }

    [Fact]
    public async Task IosSimulatorAdapter_PreSessionFailure_RollsBackInvocationBoot()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one") with { IsBooted = false }],
        };
        var adapter = CreateIosAdapter(
            apple,
            appInspector: new FakeAppleSimulatorAppInspector(
                installed: false,
                failure: FlowExecutionException.Infrastructure(
                    "ios-simulator-installation-query-failed",
                    "query failed")));
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
            {
                Artifact = artifact,
                Preflight = preflight,
                BrokerPort = 19223,
            }));

        Assert.Equal("ios-simulator-installation-query-failed", failure.Code);
        Assert.Equal(["sim-one"], apple.BootedSimulators);
        Assert.Equal(["sim-one"], apple.ShutdownSimulators);
        Assert.Empty(apple.InstalledApps);
    }

    [Fact]
    public async Task IosSimulatorCleanup_ShutsDownSimulatorBootedByInvocation()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one") with { IsBooted = false }],
        };
        var adapter = CreateIosAdapter(
            apple,
            appInspector: new FakeAppleSimulatorAppInspector(installed: false));
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = artifact,
            Preflight = preflight,
            BrokerPort = 19223,
        });

        var cleanup = await adapter.CleanupAsync(
            session,
            FlowExecutionCleanupPolicies.Stop);

        Assert.True(cleanup.Succeeded);
        Assert.Equal(["sim-one"], apple.ShutdownSimulators);
    }

    [Fact]
    public async Task IosSimulatorAdapter_FailedLaunchPreservesInstallOwnershipForCleanup()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
            LaunchAppResult = false,
        };
        var adapter = CreateIosAdapter(
            apple,
            appInspector: new FakeAppleSimulatorAppInspector(installed: false));
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });

        var failure = await Assert.ThrowsAsync<FlowExecutionPlatformLaunchException>(() =>
            adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
            {
                Artifact = artifact,
                Preflight = preflight,
                BrokerPort = 19223,
            }));
        var cleanup = await adapter.CleanupAsync(
            failure.Session,
            FlowExecutionCleanupPolicies.Uninstall);

        Assert.True(failure.Session.InstalledByInvocation);
        Assert.False(failure.Session.LaunchedByInvocation);
        Assert.True(cleanup.PackageUninstalled);
        Assert.Single(apple.UninstalledApps);
    }

    [Fact]
    public async Task IosSimulatorAdapter_UnknownInstallationStateFailsClosedBeforeInstall()
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var adapter = CreateIosAdapter(
            apple,
            appInspector: new FakeAppleSimulatorAppInspector(
                installed: false,
                failure: FlowExecutionException.Infrastructure(
                    "ios-simulator-installation-query-failed",
                    "query failed")));
        var artifact = IosSimulatorArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
            DeviceSerial = "sim-one",
        });

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
            {
                Artifact = artifact,
                Preflight = preflight,
                BrokerPort = 19223,
            }));

        Assert.Equal("ios-simulator-installation-query-failed", exception.Code);
        Assert.Empty(apple.InstalledApps);
        Assert.Empty(apple.LaunchedApps);
    }

    [Theory]
    [InlineData(Architecture.Arm64, "iossimulator-x64")]
    [InlineData(Architecture.X64, "iossimulator-arm64")]
    public async Task IosSimulatorAdapter_OppositeArchitectureArtifactIsUnsupported(
        Architecture hostArchitecture,
        string artifactRuntimeIdentifier)
    {
        var apple = new FakeAppleProvider
        {
            Simulators = [Simulator("sim-one")],
        };
        var bundleInspector = new FakeAppleBundleInspector("com.example.app");
        var adapter = new IosSimulatorFlowExecutionAdapter(
            apple,
            new FakeAppleSimulatorAppInspector(installed: false),
            new FakeHost { IsMacOS = true, ProcessArchitecture = hostArchitecture },
            bundleInspector);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = IosSimulatorArtifact() with
                {
                    RuntimeIdentifier = artifactRuntimeIdentifier,
                },
                DeviceSerial = "sim-one",
            }));

        Assert.Equal("ios-artifact-architecture-mismatch", failure.Code);
        Assert.Equal(0, bundleInspector.Calls);
        Assert.Empty(apple.BootedSimulators);
        Assert.Empty(apple.InstalledApps);
    }

    [Fact]
    public async Task MacCatalystAndAppKit_RejectEachOthersRuntimeBeforeLaunch()
    {
        var host = new FakeHost { IsMacOS = true, ProcessArchitecture = Architecture.Arm64 };
        var processes = new FakeProcessController();
        var bundleInspector = new FakeAppleBundleInspector("com.example.app");
        var catalyst = new MacCatalystFlowExecutionAdapter(host, bundleInspector, processes);
        var appKit = new AppKitFlowExecutionAdapter(host, bundleInspector, processes);

        var catalystFailure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            catalyst.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = AppKitArtifact(),
            }));
        var appKitFailure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            appKit.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = MacCatalystArtifact(),
            }));

        Assert.Equal("maccatalyst-artifact-target-unsupported", catalystFailure.Code);
        Assert.Equal("macos-artifact-target-unsupported", appKitFailure.Code);
        Assert.Equal(0, bundleInspector.Calls);
        Assert.Equal(0, processes.StartCalls);
        Assert.True(appKit.Descriptor.Experimental);
        Assert.False(appKit.Descriptor.MatchesFlowPlatform("maccatalyst"));
        Assert.False(catalyst.Descriptor.MatchesFlowPlatform("macos"));
    }

    [Theory]
    [InlineData("maccatalyst", Architecture.Arm64, "maccatalyst-x64")]
    [InlineData("macos", Architecture.X64, "osx-arm64")]
    public async Task AppleDesktopAdapter_OppositeArchitectureArtifactIsUnsupported(
        string platform,
        Architecture hostArchitecture,
        string artifactRuntimeIdentifier)
    {
        var host = new FakeHost { IsMacOS = true, ProcessArchitecture = hostArchitecture };
        var processes = new FakeProcessController();
        var bundleInspector = new FakeAppleBundleInspector("com.example.app");
        IFlowExecutionPlatformAdapter adapter = platform == "maccatalyst"
            ? new MacCatalystFlowExecutionAdapter(host, bundleInspector, processes)
            : new AppKitFlowExecutionAdapter(host, bundleInspector, processes);
        var artifact = (platform == "maccatalyst"
            ? MacCatalystArtifact()
            : AppKitArtifact()) with
        {
            RuntimeIdentifier = artifactRuntimeIdentifier,
        };

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
            {
                Artifact = artifact,
            }));

        Assert.Equal($"{platform}-artifact-architecture-mismatch", failure.Code);
        Assert.Equal(0, bundleInspector.Calls);
        Assert.Equal(0, processes.StartCalls);
    }

    [Fact]
    public async Task MacCatalystAdapter_LaunchesAndTerminatesExactOwnedBundleProcess()
    {
        var host = new FakeHost { IsMacOS = true, ProcessArchitecture = Architecture.Arm64 };
        var processes = new FakeProcessController();
        var bundleInspector = new FakeAppleBundleInspector(
            "com.example.app",
            Path.Combine(MacCatalystBundlePath, "Contents", "MacOS", "App"));
        var adapter = new MacCatalystFlowExecutionAdapter(host, bundleInspector, processes);
        var artifact = MacCatalystArtifact();
        var preflight = await adapter.PreflightAsync(new FlowExecutionPlatformPreflightRequest
        {
            Artifact = artifact,
        });
        var session = await adapter.PrepareAndLaunchAsync(new FlowExecutionPlatformRequest
        {
            Artifact = artifact,
            Preflight = preflight,
            BrokerPort = 19223,
        });

        var cleanup = await adapter.CleanupAsync(session, FlowExecutionCleanupPolicies.Stop);

        Assert.Equal("maccatalyst", session.Platform);
        Assert.Equal("mac-catalyst", session.RuntimeKind);
        Assert.False(session.InstalledByInvocation);
        Assert.True(session.LaunchedByInvocation);
        Assert.True(cleanup.Succeeded);
        Assert.Same(processes.StartedProcess, processes.StoppedProcess);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(MacCatalystBundlePath, "Contents", "MacOS", "App")),
            processes.LastStartRequest?.ExecutablePath);
    }

    [Fact]
    public void CoordinatorSelection_UsesAliasesWithoutCrossingOfficialBackendIdentities()
    {
        IFlowExecutionPlatformAdapter[] adapters =
        [
            new DescriptorOnlyAdapter(WindowsFlowExecutionAdapter.PlatformDescriptor),
            new DescriptorOnlyAdapter(WpfFlowExecutionAdapter.PlatformDescriptor),
            new DescriptorOnlyAdapter(MacCatalystFlowExecutionAdapter.PlatformDescriptor),
            new DescriptorOnlyAdapter(AppKitFlowExecutionAdapter.PlatformDescriptor),
        ];

        Assert.Equal(
            "windows",
            FlowExecutionCoordinator.SelectAdapter(adapters, "winui").Descriptor.Platform);
        Assert.Equal(
            "maccatalyst",
            FlowExecutionCoordinator.SelectAdapter(adapters, "mac-catalyst").Descriptor.Platform);
        Assert.Equal(
            "wpf",
            FlowExecutionCoordinator.SelectAdapter(adapters, "wpf").Descriptor.Platform);
        Assert.Equal(
            "macos",
            FlowExecutionCoordinator.SelectAdapter(adapters, "appkit").Descriptor.Platform);

        Assert.Throws<FlowExecutionException>(() =>
            FlowExecutionCoordinator.ValidateBundleTarget(
                Bundle("windows"),
                WpfFlowExecutionAdapter.PlatformDescriptor));
        Assert.Throws<FlowExecutionException>(() =>
            FlowExecutionCoordinator.ValidateBundleTarget(
                Bundle("maccatalyst"),
                AppKitFlowExecutionAdapter.PlatformDescriptor));
    }

    [Fact]
    public void BundleTarget_MultiPlatformTagsAndAliasesUseSharedParser()
    {
        var windowsBundle = Bundle(
            "android, winui",
            "android|windows");
        var catalystBundle = Bundle(
            "ios; mac catalyst",
            "ios, mac-catalyst");

        FlowExecutionCoordinator.ValidateBundleTarget(
            windowsBundle,
            WindowsFlowExecutionAdapter.PlatformDescriptor);
        FlowExecutionCoordinator.ValidateBundleTarget(
            catalystBundle,
            MacCatalystFlowExecutionAdapter.PlatformDescriptor);

        var failure = Assert.Throws<FlowExecutionException>(() =>
            FlowExecutionCoordinator.ValidateBundleTarget(
                Bundle("android, ios", "android|ios"),
                WindowsFlowExecutionAdapter.PlatformDescriptor));
        Assert.Equal("flow-platform-mismatch", failure.Code);
    }

    [Fact]
    public void ExactAgentBinding_WindowsAliasesRejectWpfAgent()
    {
        var expectation = new ExactAgentBindingExpectation
        {
            SessionId = "flowsession",
            TargetFramework = "net10.0-windows10.0.19041.0",
            Platform = "windows",
            PlatformAliases = WindowsFlowExecutionAdapter.PlatformDescriptor.AgentPlatformAliases,
            PackageId = "com.example.app",
            ProcessId = 44,
        };
        var wpf = DesktopAgent("wpf", "WPF");
        var winui = DesktopAgent("winui", "WinUI");

        var selection = ExactAgentBindingResolver.SelectNewMatch([], [wpf, winui], expectation);

        Assert.Equal(ExactAgentBindingSelectionKind.Matched, selection.Kind);
        Assert.Equal("winui", selection.Agent?.InstanceId);
    }

    [Fact]
    public void ExactAgentBinding_IosSimulatorRequiresExactAgentDeviceIdentity()
    {
        var expectation = new ExactAgentBindingExpectation
        {
            SessionId = "flowsession",
            TargetFramework = "net10.0-ios",
            Platform = "ios",
            PlatformAliases = IosSimulatorFlowExecutionAdapter.PlatformDescriptor.AgentPlatformAliases,
            PackageId = "com.example.app",
            DeviceSerial = "sim-one",
            RequireDeviceIdentityMatch = true,
        };
        var missingIdentity = new AgentRegistration
        {
            Id = "missing",
            InstanceId = "missing",
            Project = "App.csproj",
            SessionId = expectation.SessionId,
            Tfm = expectation.TargetFramework,
            Platform = "iOS",
            PackageId = expectation.PackageId,
            Port = 9301,
        };
        var exactIdentity = missingIdentity with
        {
            Id = "exact",
            InstanceId = "exact",
            DeviceId = "platform=ios;udid=sim-one",
            Port = 9302,
        };

        var missing = ExactAgentBindingResolver.SelectNewMatch([], [missingIdentity], expectation);
        var exact = ExactAgentBindingResolver.SelectNewMatch([], [exactIdentity], expectation);

        Assert.Equal(ExactAgentBindingSelectionKind.Pending, missing.Kind);
        Assert.Equal(ExactAgentBindingSelectionKind.Matched, exact.Kind);
        Assert.Equal("exact", exact.Agent?.InstanceId);
    }

    [Fact]
    public void ExactAgentBinding_AndroidEmulatorMatchesAvdInsteadOfBuildSerial()
    {
        var expectation = new ExactAgentBindingExpectation
        {
            SessionId = "flowsession",
            TargetFramework = "net10.0-android",
            Platform = "android",
            PlatformAliases = AndroidFlowExecutionAdapter.PlatformDescriptor.AgentPlatformAliases,
            PackageId = "com.example.app",
            DeviceSerial = "emulator-5554",
            DeviceEmulatorId = "Pixel_8_API_35",
            RequireDeviceIdentityMatch = true,
        };
        var registration = new AgentRegistration
        {
            Id = "agent",
            InstanceId = "agent",
            Project = "App.csproj",
            SessionId = expectation.SessionId,
            Tfm = expectation.TargetFramework,
            Platform = "Android",
            PackageId = expectation.PackageId,
            DeviceId = "platform=android;avd=Pixel_8_API_35;serial=unknown-build-serial",
            Port = 9303,
        };
        var wrongAvd = registration with
        {
            InstanceId = "wrong",
            DeviceId = "platform=android;avd=Pixel_9_API_35;serial=emulator-5554",
            Port = 9304,
        };

        var realistic = ExactAgentBindingResolver.SelectNewMatch([], [registration], expectation);
        var serialOnly = ExactAgentBindingResolver.SelectNewMatch([], [wrongAvd], expectation);

        Assert.Equal(ExactAgentBindingSelectionKind.Matched, realistic.Kind);
        Assert.Equal("agent", realistic.Agent?.InstanceId);
        Assert.Equal(ExactAgentBindingSelectionKind.Pending, serialOnly.Kind);
    }

    [Fact]
    public void ServiceConfiguration_RegistersAllDistinctExecutionAdapters()
    {
        using var provider = ServiceConfiguration.CreateServiceProvider() as ServiceProvider;

        var adapters = provider!.GetServices<IFlowExecutionPlatformAdapter>().ToArray();

        Assert.Equal(
            ["android", "ios", "maccatalyst", "macos", "windows", "wpf"],
            adapters.Select(adapter => adapter.Descriptor.Platform).Order().ToArray());
    }

    private static WindowsFlowExecutionAdapter CreateWindowsAdapter(
        FakeHost? host = null,
        FakeAdmissionProbe? probe = null,
        IWindowsAppProjectInspector? inspector = null,
        FakeProcessController? processes = null)
        => new(
            host ?? new FakeHost { IsWindows = true },
            probe ?? new FakeAdmissionProbe(AllowedDesktop()),
            inspector ?? new FakeWindowsProjectInspector(WindowsAppBackend.WinUI, unpackaged: true),
            processes ?? new FakeProcessController());

    private static IosSimulatorFlowExecutionAdapter CreateIosAdapter(
        FakeAppleProvider apple,
        FakeHost? host = null,
        IAppleSimulatorAppInspector? appInspector = null)
        => new(
            apple,
            appInspector ?? new FakeAppleSimulatorAppInspector(installed: false),
            host ?? new FakeHost { IsMacOS = true, ProcessArchitecture = Architecture.Arm64 },
            new FakeAppleBundleInspector("com.example.app", @"C:\build\App.app\App"));

    private static WindowsDesktopSessionAdmission AllowedDesktop()
        => new(
            1,
            WindowsWtsConnectionState.Active,
            WindowsDesktopLockState.Unlocked,
            WindowsDesktopSessionAdmissionResult.Allowed,
            "active-unlocked-desktop");

    private static WindowsDesktopSessionAdmission RejectedDesktop(WindowsWtsConnectionState state)
        => new(
            2,
            state,
            null,
            WindowsDesktopSessionAdmissionResult.Rejected,
            "wts-connection-state-disconnected");

    private static ResolvedAppArtifact WindowsArtifact() => new()
    {
        Path = @"C:\build\App.exe",
        ProjectPath = @"C:\src\App.csproj",
        AgentSessionId = "flowsession",
        TargetFramework = "net10.0-windows10.0.19041.0",
        TargetPlatformIdentifier = "windows",
        RuntimeIdentifier = "win-x64",
        Configuration = "Debug",
        ApplicationId = "com.example.app",
        ArtifactType = "exe",
        ArtifactContractVersion = "1",
        ArtifactRole = "launcher",
        TargetRuntimeKind = "windows",
        DeploymentModel = "executable",
        LaunchIdentityKind = "file-path",
        LaunchIdentity = @"C:\build\App.exe",
        Installable = false,
        Launchable = true,
        PackageDigest = "sha256:" + new string('a', 64),
    };

    private static ResolvedAppArtifact AndroidArtifact() => new()
    {
        Path = @"C:\build\App.apk",
        ProjectPath = @"C:\src\App.csproj",
        AgentSessionId = "flowsession",
        TargetFramework = "net10.0-android",
        TargetPlatformIdentifier = "android",
        Configuration = "Debug",
        ApplicationId = "com.example.app",
        ArtifactType = "apk",
        ArtifactContractVersion = "1",
        ArtifactRole = "deployable",
        TargetRuntimeKind = "android",
        DeploymentModel = "package",
        LaunchIdentityKind = "android-package-name",
        LaunchIdentity = "com.example.app",
        Installable = true,
        Launchable = true,
        PackageDigest = "sha256:" + new string('d', 64),
    };

    private static ResolvedAppArtifact IosSimulatorArtifact() => new()
    {
        Path = @"C:\build\App.app",
        ProjectPath = @"C:\src\App.csproj",
        AgentSessionId = "flowsession",
        TargetFramework = "net10.0-ios",
        TargetPlatformIdentifier = "ios",
        RuntimeIdentifier = "iossimulator-arm64",
        Configuration = "Debug",
        ApplicationId = "com.example.app",
        ArtifactType = "app",
        ArtifactContractVersion = "1",
        ArtifactRole = "deployable",
        TargetRuntimeKind = "ios-simulator",
        DeploymentModel = "simulator-bundle",
        LaunchIdentityKind = "apple-bundle-id",
        LaunchIdentity = "com.example.app",
        Installable = true,
        Launchable = true,
        PackageDigest = "sha256:" + new string('b', 64),
    };

    private static ResolvedAppArtifact MacCatalystArtifact() => new()
    {
        Path = MacCatalystBundlePath,
        ProjectPath = @"C:\src\App.csproj",
        AgentSessionId = "flowsession",
        TargetFramework = "net10.0-maccatalyst",
        TargetPlatformIdentifier = "maccatalyst",
        RuntimeIdentifier = "maccatalyst-arm64",
        Configuration = "Debug",
        ApplicationId = "com.example.app",
        ArtifactType = "app",
        ArtifactContractVersion = "1",
        ArtifactRole = "launcher",
        TargetRuntimeKind = "mac-catalyst",
        DeploymentModel = "desktop-bundle",
        LaunchIdentityKind = "apple-bundle-id",
        LaunchIdentity = "com.example.app",
        Installable = false,
        Launchable = true,
        PackageDigest = "sha256:" + new string('c', 64),
    };

    private static ResolvedAppArtifact AppKitArtifact() => MacCatalystArtifact() with
    {
        TargetFramework = "net10.0-macos",
        TargetPlatformIdentifier = "macos",
        RuntimeIdentifier = "osx-arm64",
        TargetRuntimeKind = "macos-appkit",
    };

    private static SimulatorInfo Simulator(string udid) => new()
    {
        Name = "iPhone 17",
        Udid = udid,
        Platform = "iOS",
        OSVersion = "18.0",
        RuntimeIdentifier = "com.apple.CoreSimulator.SimRuntime.iOS-18-0",
        DeviceTypeIdentifier = "com.apple.CoreSimulator.SimDeviceType.iPhone-17",
        IsAvailable = true,
        IsBooted = true,
    };

    private static AndroidDevFlowForwardingReport ForwardingReport(
        bool brokerReverseAdded = false,
        bool brokerReversePresent = true,
        int? agentPort = null,
        bool agentPresentBefore = false,
        bool agentAdded = false)
        => new()
        {
            Status = brokerReverseAdded || agentAdded
                ? AndroidDevFlowForwardingStatus.Repaired
                : AndroidDevFlowForwardingStatus.Ok,
            SelectedSerial = "emulator-5554",
            BrokerPort = 19223,
            BrokerReversePresent = brokerReversePresent,
            BrokerReverseChecked = true,
            BrokerReverseAdded = brokerReverseAdded,
            AgentPorts = agentPort is null ? [] : [agentPort.Value],
            AgentForwards = agentPort is null
                ? []
                :
                [
                    new AndroidDevFlowPortForward
                    {
                        Port = agentPort.Value,
                        PresentBefore = agentPresentBefore,
                        Added = agentAdded,
                        PresentAfter = true,
                    },
                ],
            RepairRequested = true,
        };

    private static AgentRegistration DesktopAgent(string instanceId, string platform) => new()
    {
        Id = instanceId,
        InstanceId = instanceId,
        Project = @"C:\src\App.csproj",
        Tfm = "net10.0-windows10.0.19041.0",
        Platform = platform,
        AppName = "App",
        PackageId = "com.example.app",
        SessionId = "flowsession",
        ProcessId = 44,
        Port = instanceId == "wpf" ? 9201 : 9202,
    };

    private static CommittedFlowBundle Bundle(
        string platform,
        params string[] requiredPlatforms)
        => new()
        {
            FlowPath = "flow.md",
            PlanPath = "flow.maui-plan.json",
            Flow = new MauiFlow
            {
                Name = "flow",
                App = "App",
                Platform = platform,
                Steps = [],
            },
            Plan = new MauiTestPlan
            {
                PlanId = "plan",
                Revision = 1,
                Flow = new MauiFlowReference { Path = "flow.md", Digest = "digest" },
                Goal = "goal",
                Reset = new MauiTestResetRequirement(),
                RequiredPlatforms = requiredPlatforms.Length == 0
                    ? [platform]
                    : [.. requiredPlatforms],
                SideEffectPolicy = MauiFlowSideEffectPolicies.None,
                Provenance = new MauiActorProvenance
                {
                    ActorKind = "human",
                    Channel = "unit-test",
                },
            },
            FlowDigest = "digest",
        };

    private sealed class FakeAndroidAppDeployment : IAndroidAppDeployment
    {
        public int DeployCalls { get; private set; }
        public int CleanupCalls { get; private set; }
        public int PackageQueries { get; private set; }
        public bool PackageInstalled { get; set; }

        public Task<bool> IsPackageInstalledAsync(
            string deviceSerial,
            string packageId,
            CancellationToken cancellationToken = default)
        {
            PackageQueries++;
            return Task.FromResult(PackageInstalled);
        }

        public Task<AndroidAppDeploymentSession> DeployAndLaunchAsync(
            AndroidAppDeploymentRequest request,
            CancellationToken cancellationToken = default)
        {
            DeployCalls++;
            return Task.FromResult(new AndroidAppDeploymentSession
            {
                DeviceSerial = request.DeviceSerial,
                PackageId = request.PackageId,
                Activity = request.PackageId + "/.MainActivity",
                ProcessId = 321,
                InstalledByInvocation = true,
                InstallAttempted = true,
                LaunchedByInvocation = true,
            });
        }

        public Task<AndroidAppDeploymentCleanupResult> CleanupAsync(
            AndroidAppDeploymentSession session,
            string cleanupPolicy,
            CancellationToken cancellationToken = default)
        {
            CleanupCalls++;
            return Task.FromResult(new AndroidAppDeploymentCleanupResult
            {
                Succeeded = true,
                DetailCode = "cleanup-complete",
            });
        }
    }

    private sealed class FakeAndroidFlowPortManager(params AndroidDevFlowForwardingReport[] reports)
        : IAndroidFlowPortManager
    {
        private readonly Queue<AndroidDevFlowForwardingReport> _reports = new(reports);
        public int EnsureCalls { get; private set; }
        public List<(string Serial, int Port)> RemovedForwards { get; } = [];
        public List<(string Serial, int Port)> RemovedReverses { get; } = [];

        public Task<AndroidDevFlowForwardingReport> EnsureAsync(
            AndroidDevFlowForwardingRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            return Task.FromResult(_reports.Dequeue());
        }

        public Task<bool> RemoveReverseAsync(
            string deviceSerial,
            int port,
            CancellationToken cancellationToken = default)
        {
            RemovedReverses.Add((deviceSerial, port));
            return Task.FromResult(true);
        }

        public Task<bool> RemoveForwardAsync(
            string deviceSerial,
            int port,
            CancellationToken cancellationToken = default)
        {
            RemovedForwards.Add((deviceSerial, port));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeHost : IFlowExecutionHostEnvironment
    {
        public bool IsWindows { get; init; }
        public bool IsMacOS { get; init; }
        public Architecture ProcessArchitecture { get; init; } = Architecture.X64;
        public string MachineName { get; init; } = "test-host";
        public string OsVersion { get; init; } = "test-os";
    }

    private sealed class FakeAdmissionProbe(WindowsDesktopSessionAdmission admission)
        : IWindowsDesktopSessionAdmissionProbe
    {
        public int Calls { get; private set; }

        public WindowsDesktopSessionAdmission Probe()
        {
            Calls++;
            return admission;
        }
    }

    private sealed class FakeWindowsProjectInspector(
        WindowsAppBackend backend,
        bool unpackaged) : IWindowsAppProjectInspector
    {
        public Task<WindowsAppProjectFacts> InspectAsync(
            ResolvedAppArtifact artifact,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WindowsAppProjectFacts
            {
                Backend = backend,
                ExplicitlyUnpackaged = unpackaged,
            });
    }

    private sealed class FakeProcessController : IFlowExecutionProcessController
    {
        public int StartCalls { get; private set; }
        public FlowExecutionProcessStartRequest? LastStartRequest { get; private set; }
        public FlowExecutionOwnedProcess? StartedProcess { get; private set; }
        public FlowExecutionOwnedProcess? StoppedProcess { get; private set; }
        public FlowExecutionOwnedProcess? ReleasedProcess { get; private set; }

        public FlowExecutionOwnedProcess Start(FlowExecutionProcessStartRequest request)
        {
            StartCalls++;
            LastStartRequest = request;
            StartedProcess = new FlowExecutionOwnedProcess
            {
                ProcessId = 4242,
                ExecutablePath = request.ExecutablePath,
                Handle = new FakeFlowExecutionProcessHandle(4242),
            };
            return StartedProcess;
        }

        public Task<FlowExecutionProcessStopResult> StopAsync(
            FlowExecutionOwnedProcess process,
            CancellationToken cancellationToken = default)
        {
            StoppedProcess = process;
            return Task.FromResult(new FlowExecutionProcessStopResult
            {
                Succeeded = true,
                DetailCode = "desktop-process-stopped",
            });
        }

        public void Release(FlowExecutionOwnedProcess process)
        {
            ReleasedProcess = process;
        }
    }

    private sealed class FakeAppleBundleInspector(
        string bundleIdentifier,
        string executablePath = @"C:\build\App.app\App") : IAppleAppBundleInspector
    {
        public int Calls { get; private set; }

        public Task<AppleAppBundleInfo> InspectAsync(
            string appBundlePath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AppleAppBundleInfo
            {
                BundleIdentifier = bundleIdentifier,
                ExecutablePath = executablePath,
            });
        }
    }

    private sealed class FakeAppleSimulatorAppInspector(
        bool installed,
        bool running = false,
        Task? bootReadiness = null,
        FlowExecutionException? failure = null)
        : IAppleSimulatorAppInspector
    {
        public int BootReadinessCalls { get; private set; }
        public int InspectCalls { get; private set; }
        public TaskCompletionSource<bool> BootReadinessObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitForBootReadinessAsync(
            string simulatorUdid,
            CancellationToken cancellationToken = default)
        {
            BootReadinessCalls++;
            BootReadinessObserved.TrySetResult(true);
            if (bootReadiness is not null)
                await bootReadiness.WaitAsync(cancellationToken);
        }

        public Task<AppleSimulatorAppState> InspectAsync(
            string simulatorUdid,
            string bundleIdentifier,
            CancellationToken cancellationToken = default)
        {
            InspectCalls++;
            if (failure is not null)
                throw failure;
            return Task.FromResult(new AppleSimulatorAppState
            {
                Installed = installed,
                Running = running,
            });
        }
    }

    private sealed class StaticExecutionProcessRunner(params ProcessResult[] results)
        : IExecutionProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);
        public List<(string FileName, string[] Arguments)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory = null,
            TimeSpan? timeout = null,
            IEnumerable<string>? environmentVariablesToRemove = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class FakeFlowExecutionProcessHandle(int processId) : IFlowExecutionProcessHandle
    {
        public int ProcessId { get; } = processId;
        public bool RootAlive { get; private set; } = true;
        public bool ChildAlive { get; private set; } = true;
        public bool HasExited => !RootAlive;
        public bool Disposed { get; private set; }

        public void Kill() => RootAlive = false;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }

    private sealed class StaticStandardInputProcessRunner(ProcessResult result)
        : IExecutionStandardInputProcessRunner
    {
        public List<(string FileName, string[] Arguments, string StandardInput)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((fileName, arguments.ToArray(), standardInput));
            return Task.FromResult(result);
        }
    }

    private sealed class ExecutionPlatformTestWorkspace : IDisposable
    {
        public ExecutionPlatformTestWorkspace()
        {
            Root = Path.Combine(
                FindRepositoryRoot(),
                "artifacts",
                "TestResults",
                "flow-platform-execution-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static string FindRepositoryRoot()
        {
            for (var current = new DirectoryInfo(Environment.CurrentDirectory);
                 current is not null;
                 current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, "MauiLabs.slnx")))
                    return current.FullName;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }

    private sealed class DescriptorOnlyAdapter(FlowExecutionPlatformDescriptor descriptor)
        : IFlowExecutionPlatformAdapter
    {
        public FlowExecutionPlatformDescriptor Descriptor => descriptor;
        public void ValidateHost() => throw new NotSupportedException();
        public string? GetDefaultRuntimeIdentifier() => null;
        public Task<FlowExecutionPlatformPreflight> PreflightAsync(
            FlowExecutionPlatformPreflightRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
            FlowExecutionPlatformRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EstablishAgentForwardingAsync(
            FlowExecutionPlatformSession session,
            int agentPort,
            int brokerPort,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FlowExecutionCleanupResult> CleanupAsync(
            FlowExecutionPlatformSession session,
            string cleanupPolicy,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public void AndroidAgentForwardFailure_WithReservedLocalPort_NamesThePortAndTheReservation()
    {
        var report = new AndroidDevFlowForwardingReport
        {
            Status = AndroidDevFlowForwardingStatus.Error,
            Message = "adb forward tcp:9224 tcp:9224 failed: cannot bind to 127.0.0.1:9224: " +
                      "An attempt was made to access a socket in a way forbidden by its access permissions. (10013)",
            Suggestions = ["adb -s emulator-5554 forward tcp:9224 tcp:9224"],
        };

        var message = AndroidFlowExecutionAdapter.DescribeAgentForwardFailure(9224, report);

        Assert.Contains("port 9224", message, StringComparison.Ordinal);
        Assert.Contains("10013", message, StringComparison.Ordinal);
        Assert.Contains("excludedportrange", message, StringComparison.Ordinal);
        Assert.Contains("adb -s emulator-5554 forward tcp:9224 tcp:9224", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AndroidAgentForwardFailure_WithPortInUse_BlamesAnotherInstanceNotAReservation()
    {
        var report = new AndroidDevFlowForwardingReport
        {
            Status = AndroidDevFlowForwardingStatus.Error,
            Message = "adb forward tcp:9225 tcp:9225 failed: cannot bind to 127.0.0.1:9225: " +
                      "Only one usage of each socket address (protocol/network address/port) is normally permitted. (10048)",
        };

        var message = AndroidFlowExecutionAdapter.DescribeAgentForwardFailure(9225, report);

        Assert.Contains("already bound by another process", message, StringComparison.Ordinal);
        Assert.Contains("maui devflow list", message, StringComparison.Ordinal);
        // A port that is in use is not a reserved range; suggesting netsh would send the reader nowhere.
        Assert.DoesNotContain("excludedportrange", message, StringComparison.Ordinal);
    }
    [Fact]
    public void AndroidAgentForwardFailure_WithUnrelatedError_DoesNotClaimAPortReservation()
    {
        var report = new AndroidDevFlowForwardingReport
        {
            Status = AndroidDevFlowForwardingStatus.Error,
            Message = "device offline",
        };

        var message = AndroidFlowExecutionAdapter.DescribeAgentForwardFailure(9223, report);

        Assert.Contains("device offline", message, StringComparison.Ordinal);
        Assert.DoesNotContain("excludedportrange", message, StringComparison.Ordinal);
    }
}