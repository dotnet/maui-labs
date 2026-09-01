using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using FlowCommitCommands = Microsoft.Maui.Cli.DevFlow.Flows.FlowCommitCommands;
using FlowCommitException = Microsoft.Maui.Cli.DevFlow.Flows.FlowCommitException;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class FlowExecutionCoreTests
{
    [Fact]
    public void ArtifactResolver_MultipleExactArtifacts_RejectsAmbiguity()
    {
        var candidates = new[]
        {
            Artifact("app-one.apk"),
            Artifact("app-two.apk"),
        };

        var exception = Assert.Throws<FlowExecutionException>(() =>
            MsBuildAppArtifactResolver.SelectSingleArtifact(
                candidates,
                candidates[0].ProjectPath,
                candidates[0].TargetFramework,
                candidates[0].Configuration));

        Assert.Equal(FlowExecutionExitCategories.InvalidConfiguration, exception.ExitCategory);
        Assert.Equal("artifact-ambiguous", exception.Code);
    }

    [Fact]
    public void ArtifactResolver_SignedAndUnsignedAndroidPackages_SelectsTheSignedPackage()
    {
        var candidates = new[]
        {
            Artifact("com.example.app.apk") with { SigningState = "unsigned" },
            Artifact("com.example.app-Signed.apk") with { SigningState = "signed" },
        };

        var selected = MsBuildAppArtifactResolver.SelectSingleArtifact(
            candidates,
            candidates[0].ProjectPath,
            candidates[0].TargetFramework,
            candidates[0].Configuration,
            candidateArtifactTypes: ["apk"]);

        Assert.Equal("com.example.app-Signed.apk", selected.Path);
        Assert.Equal("signed", selected.SigningState);
    }

    [Fact]
    public void ArtifactResolver_MultipleSignedPackages_StillRejectsAmbiguity()
    {
        var candidates = new[]
        {
            Artifact("app-arm64-Signed.apk") with { SigningState = "signed" },
            Artifact("app-x64-Signed.apk") with { SigningState = "signed" },
        };

        var exception = Assert.Throws<FlowExecutionException>(() =>
            MsBuildAppArtifactResolver.SelectSingleArtifact(
                candidates,
                candidates[0].ProjectPath,
                candidates[0].TargetFramework,
                candidates[0].Configuration,
                candidateArtifactTypes: ["apk"]));

        Assert.Equal("artifact-ambiguous", exception.Code);
        Assert.Contains("signed=2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactResolver_SignedPackageBesideUnclassifiedPackage_RejectsAmbiguity()
    {
        var candidates = new[]
        {
            Artifact("app-Signed.apk") with { SigningState = "signed" },
            Artifact("app-copy.apk") with { SigningState = "unknown" },
        };

        var exception = Assert.Throws<FlowExecutionException>(() =>
            MsBuildAppArtifactResolver.SelectSingleArtifact(
                candidates,
                candidates[0].ProjectPath,
                candidates[0].TargetFramework,
                candidates[0].Configuration,
                candidateArtifactTypes: ["apk"]));

        Assert.Equal("artifact-ambiguous", exception.Code);
    }

    [Fact]
    public void ArtifactResolver_CandidateTypeAllowlist_DoesNotFallBackToForeignArtifact()
    {
        var candidate = Artifact("app.dll") with { ArtifactType = "dll" };

        var exception = Assert.Throws<FlowExecutionException>(() =>
            MsBuildAppArtifactResolver.SelectSingleArtifact(
                [candidate],
                candidate.ProjectPath,
                candidate.TargetFramework,
                candidate.Configuration,
                candidateArtifactTypes: ["apk"]));

        Assert.Equal("artifact-not-found", exception.Code);
    }

    [Fact]
    public void ArtifactResolver_RequestedRuntimeIdentifier_MustMatchExactly()
    {
        var candidate = Artifact("app.apk") with { RuntimeIdentifier = "android-arm64" };

        var exception = Assert.Throws<FlowExecutionException>(() =>
            MsBuildAppArtifactResolver.SelectSingleArtifact(
                [candidate],
                candidate.ProjectPath,
                candidate.TargetFramework,
                candidate.Configuration,
                runtimeIdentifier: "android-x64",
                candidateArtifactTypes: ["apk"]));

        Assert.Equal("artifact-not-found", exception.Code);
    }

    [Fact]
    public async Task ArtifactResolver_AppleBundleDirectory_PreservesSimulatorRuntimeAndHashesBundle()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "SimulatorApp.csproj");
        await File.WriteAllTextAsync(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-ios</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        var runner = new CallbackProcessRunner(async call =>
        {
            var hostProject = call.Arguments[1];
            var host = XDocument.Load(hostProject);
            Assert.Equal(
                "iossimulator-arm64",
                host.Descendants("RuntimeIdentifier").Single().Value);
            Assert.Contains(
                "MauiDevFlowSessionId=flowsession",
                host.Descendants("Properties").Single().Value,
                StringComparison.Ordinal);
            var outputRoot = host.Descendants("OutputRoot").Single().Value;
            var metadataPath = host.Descendants("WriteLinesToFile")
                .Single()
                .Attribute("File")!
                .Value;
            var bundlePath = Path.Combine(outputRoot, "app", "SimulatorApp.app");
            Directory.CreateDirectory(bundlePath);
            await File.WriteAllTextAsync(Path.Combine(bundlePath, "Info.plist"), "plist");
            await File.WriteAllBytesAsync(Path.Combine(bundlePath, "SimulatorApp"), [1, 2, 3, 4]);
            await File.WriteAllTextAsync(
                metadataPath,
                string.Join(
                    "|||DEVFLOW_ARTIFACT|||",
                    bundlePath,
                    "SimulatorApp",
                    project,
                    "net10.0-ios",
                    "ios",
                    "iossimulator-arm64",
                    "Debug",
                    "com.example.app",
                    "app",
                    "1",
                    "deployable",
                    "ios-simulator",
                    "simulator-bundle",
                    "apple-bundle-id",
                    "com.example.app",
                    "true",
                    "true",
                    AppArtifactSigningStates.NotApplicable));
            return Success();
        });
        var resolver = new MsBuildAppArtifactResolver(runner);

        var artifact = await resolver.ResolveAsync(new AppArtifactResolutionRequest
        {
            ProjectPath = project,
            AgentSessionId = "flowsession",
            TargetFramework = "net10.0-ios",
            Configuration = "Debug",
            WorkDirectory = workspace.Output,
            Platform = "ios",
            TargetFrameworkPlatformIdentifiers = ["ios"],
            CandidateArtifactTypes = ["app", "ipa"],
            RuntimeIdentifier = "iossimulator-arm64",
        });

        Assert.Equal("ios-simulator", artifact.TargetRuntimeKind);
        Assert.Equal("iossimulator-arm64", artifact.RuntimeIdentifier);
        Assert.StartsWith("sha256:", artifact.PackageDigest);
        Assert.True(Directory.Exists(artifact.Path));
    }

    [Fact]
    public async Task ArtifactResolver_WindowsUnpackagedExecutable_ResolvesExactLauncher()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "WindowsApp.csproj");
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, "Directory.Build.props"),
            """
            <Project>
              <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
                <AssemblyName>ImportedExactWindowsApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, "Directory.Build.targets"),
            "<Project />");
        await File.WriteAllTextAsync(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <ApplicationId>com.example.windowsapp</ApplicationId>
                <WindowsPackageType>None</WindowsPackageType>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Root, "Program.cs"),
            "System.Console.WriteLine(\"Exact Windows app\");");
        var resolver = new MsBuildAppArtifactResolver(new ExecutionProcessRunner());

        var artifact = await resolver.ResolveAsync(new AppArtifactResolutionRequest
        {
            ProjectPath = project,
            AgentSessionId = "flowsession",
            TargetFramework = "net10.0-windows10.0.19041.0",
            Configuration = "Debug",
            WorkDirectory = workspace.Output,
            Platform = "windows",
            TargetFrameworkPlatformIdentifiers = ["windows"],
            CandidateArtifactTypes = ["exe", "msix", "appinstaller"],
        });

        Assert.Equal("exe", artifact.ArtifactType);
        Assert.Equal("windows", artifact.TargetRuntimeKind);
        Assert.Equal("file-path", artifact.LaunchIdentityKind);
        Assert.Equal("ImportedExactWindowsApp.exe", Path.GetFileName(artifact.Path));
        Assert.Equal(Path.GetFullPath(artifact.Path), Path.GetFullPath(artifact.LaunchIdentity!));
        Assert.True(File.Exists(artifact.Path));
    }

    [Fact]
    public async Task ArtifactResolver_ArtifactOutsideOwnedBuildRoot_IsRejected()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var runner = new CallbackProcessRunner(async call =>
        {
            var host = XDocument.Load(call.Arguments[1]);
            var metadataPath = host.Descendants("WriteLinesToFile").Single().Attribute("File")!.Value;
            var outside = Path.Combine(workspace.Root, "outside.apk");
            await File.WriteAllBytesAsync(outside, [1, 2, 3]);
            await File.WriteAllTextAsync(
                metadataPath,
                ArtifactMetadata(outside, project, runtimeIdentifier: ""));
            return Success();
        });
        var resolver = new MsBuildAppArtifactResolver(runner);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            resolver.ResolveAsync(new AppArtifactResolutionRequest
            {
                ProjectPath = project,
                AgentSessionId = "flowsession",
                TargetFramework = "net10.0-android",
                Configuration = "Debug",
                WorkDirectory = workspace.Output,
                Platform = "android",
                TargetFrameworkPlatformIdentifiers = ["android"],
                CandidateArtifactTypes = ["apk"],
            }));

        Assert.Equal("artifact-path-outside-build-root", failure.Code);
    }

    [Fact]
    public async Task ArtifactResolver_OversizedArtifact_IsRejectedBeforeUse()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var runner = new CallbackProcessRunner(async call =>
        {
            var host = XDocument.Load(call.Arguments[1]);
            var outputRoot = host.Descendants("OutputRoot").Single().Value;
            var metadataPath = host.Descendants("WriteLinesToFile").Single().Attribute("File")!.Value;
            var artifactPath = Path.Combine(outputRoot, "package", "App.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            await File.WriteAllBytesAsync(artifactPath, [1, 2, 3, 4]);
            await File.WriteAllTextAsync(
                metadataPath,
                ArtifactMetadata(artifactPath, project, runtimeIdentifier: ""));
            return Success();
        });
        var resolver = new MsBuildAppArtifactResolver(
            runner,
            new AppArtifactInspectionLimits
            {
                MaximumFileBytes = 3,
                MaximumTotalBytes = 8,
                MaximumFiles = 8,
                MaximumDepth = 8,
                Timeout = TimeSpan.FromSeconds(5),
            });

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            resolver.ResolveAsync(new AppArtifactResolutionRequest
            {
                ProjectPath = project,
                AgentSessionId = "flowsession",
                TargetFramework = "net10.0-android",
                Configuration = "Debug",
                WorkDirectory = workspace.Output,
                Platform = "android",
                TargetFrameworkPlatformIdentifiers = ["android"],
                CandidateArtifactTypes = ["apk"],
            }));

        Assert.Equal("artifact-file-size-exceeded", failure.Code);
    }

    [Fact]
    public async Task ArtifactResolver_BuildFailureDiagnostics_AreBounded()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var resolver = new MsBuildAppArtifactResolver(
            new QueueProcessRunner(new ProcessResult
            {
                ExitCode = 1,
                StandardError = new string('x', 16_000) + "diagnostic-tail",
            }));

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            resolver.ResolveAsync(new AppArtifactResolutionRequest
            {
                ProjectPath = project,
                AgentSessionId = "flowsession",
                TargetFramework = "net10.0-android",
                Configuration = "Debug",
                WorkDirectory = workspace.Output,
                Platform = "android",
                TargetFrameworkPlatformIdentifiers = ["android"],
                CandidateArtifactTypes = ["apk"],
            }));

        Assert.Equal("app-build-failed", failure.Code);
        Assert.Contains("diagnostic-tail", failure.Message, StringComparison.Ordinal);
        Assert.True(failure.Message.Length < 2_200, failure.Message.Length.ToString());
    }

    [Fact]
    public async Task ArtifactResolver_HostProject_DeclaresInvocationOwnedIntermediatePaths()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var capturedHostProjectPath = "";
        var runner = new CallbackProcessRunner(async call =>
        {
            var hostProjectPath = call.Arguments[1];
            capturedHostProjectPath = hostProjectPath;
            var host = XDocument.Load(hostProjectPath);
            var ownedRoot = Path.GetDirectoryName(hostProjectPath)!;
            foreach (var name in new[]
                     {
                         "BaseIntermediateOutputPath",
                         "MSBuildProjectExtensionsPath",
                         "BaseOutputPath",
                     })
            {
                var value = host.Descendants(name).Single().Value;
                Assert.True(
                    ExecutionPathSafety.IsWithinRoot(ownedRoot, value),
                    $"{name}={value}");
            }

            var outputRoot = host.Descendants("OutputRoot").Single().Value;
            var metadataPath = host.Descendants("WriteLinesToFile").Single().Attribute("File")!.Value;
            var artifactPath = Path.Combine(outputRoot, "package", "App.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            await File.WriteAllBytesAsync(artifactPath, [1, 2, 3, 4]);
            await File.WriteAllTextAsync(
                metadataPath,
                ArtifactMetadata(artifactPath, project, runtimeIdentifier: ""));
            return Success();
        });
        var resolver = new MsBuildAppArtifactResolver(runner);

        var artifact = await resolver.ResolveAsync(new AppArtifactResolutionRequest
        {
            ProjectPath = project,
            AgentSessionId = "flowsession",
            TargetFramework = "net10.0-android",
            Configuration = "Debug",
            WorkDirectory = workspace.Output,
            Platform = "android",
            TargetFrameworkPlatformIdentifiers = ["android"],
            CandidateArtifactTypes = ["apk"],
        });

        Assert.Equal("apk", artifact.ArtifactType);
        // The owned build root is the run's own output directory, so nothing the host project
        // writes can reach the repository that owns the app project.
        Assert.True(
            ExecutionPathSafety.IsWithinRoot(workspace.Output, capturedHostProjectPath),
            capturedHostProjectPath);
    }

    /// <summary>
    /// A CLI paired with a targets package older than the SigningState column must still resolve.
    /// The signing state stays unspecified, which keeps an ambiguous signed/unsigned pair failing
    /// closed instead of turning a version skew into a hard metadata parse error.
    /// </summary>
    [Fact]
    public async Task ArtifactResolver_LegacyMetadataWithoutSigningState_StillResolves()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var runner = new CallbackProcessRunner(async call =>
        {
            var host = XDocument.Load(call.Arguments[1]);
            var outputRoot = host.Descendants("OutputRoot").Single().Value;
            var metadataPath = host.Descendants("WriteLinesToFile").Single().Attribute("File")!.Value;
            var artifactPath = Path.Combine(outputRoot, "package", "App.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
            await File.WriteAllBytesAsync(artifactPath, [1, 2, 3, 4]);
            var legacy = ArtifactMetadata(artifactPath, project, runtimeIdentifier: "");
            legacy = legacy[..legacy.LastIndexOf("|||DEVFLOW_ARTIFACT|||", StringComparison.Ordinal)];
            await File.WriteAllTextAsync(metadataPath, legacy);
            return Success();
        });
        var resolver = new MsBuildAppArtifactResolver(runner);

        var artifact = await resolver.ResolveAsync(new AppArtifactResolutionRequest
        {
            ProjectPath = project,
            AgentSessionId = "flowsession",
            TargetFramework = "net10.0-android",
            Configuration = "Debug",
            WorkDirectory = workspace.Output,
            Platform = "android",
            TargetFrameworkPlatformIdentifiers = ["android"],
            CandidateArtifactTypes = ["apk"],
        });

        Assert.Equal("apk", artifact.ArtifactType);
        Assert.Null(artifact.SigningState);
    }

    [Fact]
    public async Task ArtifactResolver_BuildFailure_SurfacesMsBuildErrorsAndPersistsRedactedLog()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0-android</TargetFramework></PropertyGroup></Project>");
        var restoreNoise = string.Join(
            "\n",
            Enumerable
                .Range(0, 400)
                .Select(index => $"  Restored C:\\repo\\src\\Project{index}\\Project{index}.csproj (in 1.2 sec)."));
        var errorLine =
            "C:\\sdk\\targets\\Microsoft.PackageDependencyResolution.targets(266,5): error NETSDK1005: " +
            "Assets file 'C:\\repo\\artifacts\\obj\\Agent.Core\\project.assets.json' doesn't have a target for 'net10.0'. " +
            "[C:\\repo\\src\\Agent.Core\\Agent.Core.csproj]";
        var resolver = new MsBuildAppArtifactResolver(
            new QueueProcessRunner(new ProcessResult
            {
                ExitCode = 1,
                StandardOutput = restoreNoise + "\n" + errorLine + "\n" + errorLine + "\n" + restoreNoise,
            }));

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            resolver.ResolveAsync(new AppArtifactResolutionRequest
            {
                ProjectPath = project,
                AgentSessionId = "flowsession",
                TargetFramework = "net10.0-android",
                Configuration = "Debug",
                WorkDirectory = workspace.Output,
                Platform = "android",
                TargetFrameworkPlatformIdentifiers = ["android"],
                CandidateArtifactTypes = ["apk"],
            }));

        Assert.Equal("app-build-failed", failure.Code);
        // The coordinator bounds the reported message to its first 512 characters, so the actionable
        // MSBuild diagnostic has to appear there instead of being buried in restore noise.
        var reported = failure.Message[..Math.Min(512, failure.Message.Length)];
        Assert.Contains("NETSDK1005", reported, StringComparison.Ordinal);
        Assert.Contains("app-build.log", reported, StringComparison.Ordinal);
        Assert.DoesNotContain("Restored", reported, StringComparison.Ordinal);

        var logPath = Path.Combine(workspace.Output, "app-build.log");
        Assert.True(File.Exists(logPath), logPath);
        var log = await File.ReadAllTextAsync(logPath);
        Assert.Contains("NETSDK1005", log, StringComparison.Ordinal);
        Assert.Contains("Restored", log, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\repo", log, StringComparison.Ordinal);
        Assert.NotNull(failure.DiagnosticsArtifact);
        Assert.Equal("app-build.log", failure.DiagnosticsArtifact!.FileName);
        Assert.StartsWith("sha256:", failure.DiagnosticsArtifact.Digest, StringComparison.Ordinal);
    }

    [Fact]
    public void ArtifactResolver_BuildDiagnosticSelection_PrefersErrorsAndDeduplicates()
    {
        var output = string.Join(
            "\n",
            "  Restored one.csproj (in 1.2 sec).",
            "app.csproj : warning NU1507: There are 6 package sources defined.",
            "app.csproj : error NETSDK1005: Assets file does not have a target.",
            "app.csproj : error NETSDK1005: Assets file does not have a target.",
            "  Restored two.csproj (in 0.4 sec).");

        var selected = MsBuildAppArtifactResolver.SelectMsBuildDiagnostics(output);

        Assert.NotNull(selected);
        Assert.StartsWith("app.csproj : error NETSDK1005", selected, StringComparison.Ordinal);
        Assert.Equal(
            1,
            selected!.Split("NETSDK1005", StringSplitOptions.None).Length - 1);
        Assert.Contains("NU1507", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("Restored", selected, StringComparison.Ordinal);
        Assert.Null(MsBuildAppArtifactResolver.SelectMsBuildDiagnostics("  Restored one.csproj (in 1.2 sec)."));
    }

    [Fact]
    public void ArtifactResolver_BuildLog_BoundsUnboundedProcessOutput()
    {
        // The oversize failure path persists a log precisely because the output blew the bounded
        // response size, so formatting it must never materialize the whole stream.
        var tail = "app.csproj : error NETSDK1005: Assets file does not have a target.";
        var result = new ProcessResult
        {
            ExitCode = 1,
            StandardOutput = new string('x', 8 * 1024 * 1024) + "\n" + tail,
            StandardError = new string('y', 4 * 1024 * 1024),
        };

        var log = MsBuildAppArtifactResolver.FormatBuildLog(result);

        Assert.True(log.Length < 1024 * 1024, log.Length.ToString());
        Assert.Contains(tail, log, StringComparison.Ordinal);
        Assert.Contains("[earlier output omitted]", log, StringComparison.Ordinal);
        // A single unbounded line is clamped rather than copied whole.
        Assert.DoesNotContain(new string('x', 8_192), log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_AppBuildFailure_WithoutPersistedLog_OmitsArtifactFromReportAndManifest()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter();
        var coordinator = CreateCoordinator(
            new BuildLogFailingArtifactResolver(persistLog: false),
            adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal("app-build-failed", result.Report?.Failure?.Code);
        // The report and the manifest have to agree: neither may point at a log that is not there.
        Assert.DoesNotContain(
            result.Report!.Artifacts,
            static item => item.ArtifactId == "app-build-log");
        Assert.DoesNotContain(
            result.Report.Failure!.Artifacts,
            static item => item.ArtifactId == "app-build-log");
        Assert.DoesNotContain(
            result.Manifest!.Artifacts,
            static item => item.ArtifactId == "app-build-log");
    }

    [Fact]
    public async Task Coordinator_AppBuildFailure_PublishesBuildLogArtifact()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter();
        var coordinator = CreateCoordinator(new BuildLogFailingArtifactResolver(), adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal("app-build-failed", result.Report?.Failure?.Code);
        var reported = Assert.Single(
            result.Report!.Artifacts,
            static item => item.ArtifactId == "app-build-log");
        Assert.Equal("app-build.log", reported.Path);
        Assert.Contains(
            result.Report.Failure!.Artifacts,
            static item => item.ArtifactId == "app-build-log");
        Assert.Contains(
            result.Manifest!.Artifacts,
            static item => item.ArtifactId == "app-build-log" &&
                item.RelativePath == "app-build.log" &&
                item.Role == "failure-diagnostics");
        Assert.True(File.Exists(Path.Combine(workspace.Output, "app-build.log")));
    }

    [Fact]
    public void ArtifactPath_ReparsePointInsideOwnedRoot_IsRejected()
    {
        using var workspace = new ExecutionTestWorkspace();
        var ownedRoot = Path.Combine(workspace.Root, "owned-build");
        var outside = Path.Combine(workspace.Root, "outside.apk");
        var link = Path.Combine(ownedRoot, "App.apk");
        Directory.CreateDirectory(ownedRoot);
        File.WriteAllBytes(outside, [1, 2, 3]);
        if (!TryCreateFileSymbolicLink(link, outside))
            return;

        var failure = Assert.Throws<FlowExecutionException>(() =>
            ExecutionPathSafety.ValidateConfinedArtifactPath(ownedRoot, link));

        Assert.Equal("artifact-reparse-point", failure.Code);
    }

    [Fact]
    public async Task Coordinator_AabWithLegacyInstallableTrue_IsUnsupportedBeforePlatformMutation()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(validateWithAndroidRules: true);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.aab")) with
            {
                ArtifactType = "aab",
                ArtifactRole = "distribution",
                DeploymentModel = "store-bundle",
                Installable = true,
                Launchable = false,
            }),
            adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.ExitCategory);
        Assert.Equal(0, adapter.MutationCalls);
        Assert.Equal("android-aab-unsupported", result.Report?.Failure?.Code);
    }

    [Fact]
    public async Task Coordinator_UnsignedAndroidPackage_IsUnsupportedBeforePlatformMutation()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(validateWithAndroidRules: true);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk")) with
            {
                SigningState = "unsigned",
            }),
            adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.ExitCategory);
        Assert.Equal(0, adapter.MutationCalls);
        Assert.Equal("android-artifact-unsigned", result.Report?.Failure?.Code);
    }

    [Fact]
    public async Task Coordinator_ResettablePlanWithoutProvider_RefusesBeforeMutation()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.TestTenantResettable);
        var adapter = new FakePlatformAdapter();
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.ExitCategory);
        Assert.Equal(0, adapter.MutationCalls);
        Assert.Equal("state-evidence-provider-missing", result.Report?.Failure?.Code);
    }

    [Fact]
    public async Task Coordinator_InvalidConfiguration_StillWritesCategorizedOutputs()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            new FakePlatformAdapter());
        var request = Request(bundle, workspace.Output) with { ProjectPath = "" };

        var result = await coordinator.RunAsync(request);

        Assert.Equal(FlowExecutionExitCategories.InvalidConfiguration, result.ExitCategory);
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.ReportPath));
        Assert.True(File.Exists(result.JUnitPath));
        Assert.Equal("project-path-missing", result.Report?.Failure?.Code);
        var manifestJson = await File.ReadAllTextAsync(result.ManifestPath!);
        ExecutionManifestSchemaValidator.AssertValid(manifestJson);
        using var manifest = JsonDocument.Parse(manifestJson);
        Assert.False(manifest.RootElement.TryGetProperty("build", out _));
        Assert.False(manifest.RootElement.TryGetProperty("device", out _));
    }

    [Fact]
    public async Task Coordinator_NonDebugConfiguration_IsRejectedBeforeArtifactBuild()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var resolver = new FakeArtifactResolver(
            Artifact(Path.Combine(workspace.Root, "app.apk")));
        var coordinator = CreateCoordinator(resolver, new FakePlatformAdapter());

        var result = await coordinator.RunAsync(
            Request(bundle, workspace.Output) with { Configuration = "Release" });

        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.ExitCategory);
        Assert.Equal("devflow-agent-debug-configuration-required", result.Report?.Failure?.Code);
        Assert.Equal("validate-request", result.Report?.Failure?.Phase);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Coordinator_ArtifactBuildFailure_ReportsActualLifecyclePhase()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var coordinator = CreateCoordinator(
            new ThrowingArtifactResolver(
                FlowExecutionException.Infrastructure(
                    "app-build-failed",
                    "bounded build failure")),
            new FakePlatformAdapter());

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal("app-build-failed", result.Report?.Failure?.Code);
        Assert.Equal("resolve-artifact", result.Report?.Failure?.Phase);
    }

    [Fact]
    public async Task Coordinator_ProvenAppCrash_IsAttributedToTheAppNotTheSymptom()
    {
        var adapter = new FakePlatformAdapter(allowMutation: true)
        {
            AppProcessEvidence = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "android-adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
                CrashSignature = "java.lang.NullPointerException",
            },
        };

        var result = await RunFailingFlowAsync(adapter);

        Assert.Equal(1, adapter.AppProbeCalls);
        Assert.Equal(MauiFlowFailureClasses.AppCrash, result.Report?.Failure?.Class);
        Assert.True(result.Report?.AppProcess?.ProcessExited);
        Assert.Equal(
            MauiFlowTriageDispositions.AppRegression,
            MauiFlowFailureClassifier.Project(result.Report!.Failure!.Class!));
    }

    [Fact]
    public async Task Coordinator_FailureWithoutCrashEvidence_IsNeverCalledACrash()
    {
        var adapter = new FakePlatformAdapter(allowMutation: true)
        {
            AppProcessEvidence = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "android-adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.UserRequested,
            },
        };

        var result = await RunFailingFlowAsync(adapter);

        Assert.Equal(1, adapter.AppProbeCalls);
        Assert.NotNull(result.Report?.Failure?.Class);
        Assert.NotEqual(MauiFlowFailureClasses.AppCrash, result.Report!.Failure!.Class);
        Assert.True(result.Report.AppProcess?.ProcessExited);
    }

    [Fact]
    public async Task Coordinator_ProbeThatCannotAnswer_NeverInventsACrash()
    {
        var adapter = new FakePlatformAdapter(allowMutation: true)
        {
            AppProcessEvidence = new MauiFlowAppProcessEvidence
            {
                Probed = false,
                Source = "android-adb",
                ProbeError = "ADB was not found.",
            },
        };

        var result = await RunFailingFlowAsync(adapter);

        Assert.NotEqual(MauiFlowFailureClasses.AppCrash, result.Report?.Failure?.Class);
        Assert.False(result.Report?.AppProcess?.Probed);
    }

    /// <summary>
    /// Drives a complete fake Android run whose only failure is a route assertion, so the run
    /// reaches a terminal failure with a launched platform session for the app probe to observe.
    /// </summary>
    private static async Task<FlowExecutionResult> RunFailingFlowAsync(FakePlatformAdapter adapter)
    {
        const string status = """
            {
              "agent": { "name": "Microsoft.Maui.DevFlow.Agent", "version": "test" },
              "device": { "platform": "Android", "deviceType": "Virtual", "idiom": "Phone" },
              "app": { "name": "App", "packageId": "com.example.app", "processId": 321, "build": "42" },
              "capabilities": { "ui": true, "mutations": true, "workflowCommandLedger": true },
              "route": "//home",
              "running": true
            }
            """;
        await using var server = new MockAgentServer(status);
        await server.StartAsync();
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var listCalls = 0;
        var binding = new ExactAgentBindingResolver(_ =>
        {
            listCalls++;
            return Task.FromResult<AgentRegistration[]?>(
                listCalls == 1
                    ? []
                    :
                    [
                        Agent("new-instance") with
                        {
                            Port = server.Port,
                            ProcessId = 321,
                        },
                    ]);
        }, pollInterval: TimeSpan.Zero);
        var coordinator = new FlowExecutionCoordinator(
            new CommittedFlowBundleLoader(),
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            [adapter],
            new FlowStateEvidenceProviderRegistry([]),
            binding,
            new FlowRunReportWriter(),
            new JUnitFlowExecutionWriter(),
            new ExecutionManifestWriter(),
            new ImmutableExecutionOutputWriter(),
            () => Task.FromResult<int?>(19223),
            appSourceIdentityProvider: new FakeAppSourceIdentityProvider(),
            agentSessionIdFactory: static () => "flowsession");

        return await coordinator.RunAsync(Request(bundle, workspace.Output));
    }

    [Fact]
    public async Task Coordinator_FlowWithoutExpectedEvidence_WritesNoExpectedEvidenceBlock()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            new FakePlatformAdapter(allowMutation: true));

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Null(result.Report?.ExpectedEvidence);
    }

    [Fact]
    public async Task Coordinator_DeclaredEvidence_IsReportedAsProducedOrMissing()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            expectedEvidence:
            [
                new FlowExpectedEvidence { Id = "report", Kind = MauiFlowEvidenceKinds.RunReport },
                new FlowExpectedEvidence { Id = "tree", Kind = MauiFlowEvidenceKinds.VisualTree },
            ]);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            new FakePlatformAdapter(allowMutation: true));

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        var expected = result.Report?.ExpectedEvidence;
        Assert.NotNull(expected);
        Assert.Equal(2, expected!.Declared);
        Assert.Contains(
            expected.Checks,
            check => check.ExpectationId == "report" &&
                check.State == MauiFlowEvidenceExpectationStates.Satisfied);
        Assert.Contains(
            expected.Checks,
            check => check.ExpectationId == "tree" &&
                check.State == MauiFlowEvidenceExpectationStates.Unsatisfied);
        Assert.False(expected.AllSatisfied);
        // Declared evidence is reviewer information, never a second verdict: a missing artifact
        // must not turn an infrastructure failure into a test failure or the reverse.
        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
    }

    [Fact]
    public void ExecutionOutput_RejectsReparsePointAncestor()
    {
        using var workspace = new ExecutionTestWorkspace();
        var target = Path.Combine(workspace.Root, "output-target");
        var link = Path.Combine(workspace.Root, "output-link");
        Directory.CreateDirectory(target);
        if (!TryCreateDirectorySymbolicLink(link, target))
            return;

        var failure = Assert.Throws<FlowExecutionException>(() =>
            ExecutionPathSafety.PrepareNewOrEmptyDirectory(Path.Combine(link, "run")));

        Assert.Equal("execution-output-reparse-point", failure.Code);
    }

    [Fact]
    public async Task ImmutableOutputWriter_RechecksPreparedRootBeforeWriting()
    {
        using var workspace = new ExecutionTestWorkspace();
        var output = Path.Combine(workspace.Root, "prepared-output");
        var moved = Path.Combine(workspace.Root, "moved-output");
        Directory.CreateDirectory(output);
        Directory.Move(output, moved);
        if (!TryCreateDirectorySymbolicLink(output, moved))
        {
            Directory.Move(moved, output);
            return;
        }

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new ImmutableExecutionOutputWriter().WriteAsync(
                output,
                [new ExecutionOutputFile("result.json", [1, 2, 3])]));

        Assert.Equal("execution-output-reparse-point", failure.Code);
        Assert.False(File.Exists(Path.Combine(moved, "result.json")));
    }

    [Fact]
    public async Task Coordinator_EndToEndFakeAndroidRun_UsesCanonicalRunnerAndIsUnverifiedWithoutOracle()
    {
        const string status = """
            {
              "agent": { "name": "Microsoft.Maui.DevFlow.Agent", "version": "test" },
              "device": { "platform": "Android", "deviceType": "Virtual", "idiom": "Phone" },
              "app": { "name": "App", "packageId": "com.example.app", "processId": 321, "build": "42" },
              "capabilities": { "ui": true, "mutations": true, "workflowCommandLedger": true },
              "route": "//checkout",
              "running": true
            }
            """;
        await using var server = new MockAgentServer(status);
        await server.StartAsync();
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(allowMutation: true);
        var listCalls = 0;
        var binding = new ExactAgentBindingResolver(_ =>
        {
            listCalls++;
            return Task.FromResult<AgentRegistration[]?>(
                listCalls == 1
                    ? []
                    :
                    [
                        Agent("new-instance") with
                        {
                            Port = server.Port,
                            ProcessId = 321,
                        },
                    ]);
        }, pollInterval: TimeSpan.Zero);
        var coordinator = new FlowExecutionCoordinator(
            new CommittedFlowBundleLoader(),
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            [adapter],
            new FlowStateEvidenceProviderRegistry([]),
            binding,
            new FlowRunReportWriter(),
            new JUnitFlowExecutionWriter(),
            new ExecutionManifestWriter(),
            new ImmutableExecutionOutputWriter(),
            () => Task.FromResult<int?>(19223),
            appSourceIdentityProvider: new FakeAppSourceIdentityProvider(),
            agentSessionIdFactory: static () => "flowsession");

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(FlowExecutionExitCategories.Unverified, result.ExitCategory);
        Assert.False(result.Ok);
        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report?.Outcome?.Status);
        Assert.False(result.Report?.Outcome?.Verified);
        Assert.False(result.Manifest?.Outcome?.Verified);
        Assert.Equal(2, adapter.MutationCalls);
        Assert.Equal(1, adapter.CleanupCalls);
        Assert.True(result.Manifest?.Lifecycle?.InstalledByInvocation);
        Assert.True(result.Manifest?.Lifecycle?.LaunchedByInvocation);
        Assert.True(result.Manifest?.Lifecycle?.CleanupCompleted);
        Assert.Equal("source-current", result.Report?.Target?.AppSourceFingerprint);
        Assert.StartsWith("sha256:", result.Manifest?.Build?.AppSourceFingerprint);
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", result.Manifest?.Build?.SourceRevision);
        Assert.Contains(server.RecordedRequests, request => request.Path == "/api/v1/agent/status");
        var junit = XDocument.Load(result.JUnitPath!);
        // An unverified run is not a regression in the app under test, so it must not read as a
        // JUnit failure -- every shipped flow would otherwise be red in CI while passing.
        Assert.Equal("0", junit.Root?.Attribute("failures")?.Value);
        Assert.Equal("0", junit.Root?.Attribute("errors")?.Value);
        Assert.Equal("1", junit.Root?.Attribute("skipped")?.Value);
        Assert.Empty(junit.Descendants("failure"));
        Assert.Single(junit.Descendants("skipped"));
        Assert.Equal(
            FlowExecutionExitCategories.Unverified,
            junit.Descendants("property")
                .Single(property => property.Attribute("name")?.Value == "devflow.exitCategory")
                .Attribute("value")?.Value);
        Assert.Equal(
            "false",
            junit.Descendants("property")
                .Single(property => property.Attribute("name")?.Value == "devflow.verified")
                .Attribute("value")?.Value);
        // The CLI message must lead with the verification reason, not "Flow replay passed."
        Assert.Contains("independent-oracle-absent", result.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run that passed with independent verification and then failed owned teardown keeps every
    /// fact about the app — <c>passed</c>, <c>verified</c>, no failure — while the command still
    /// exits non-zero as an infrastructure failure. Folding the two together used to report the
    /// app as broken and delete the only evidence that it was not.
    /// </summary>
    [Fact]
    public async Task Coordinator_VerifiedPassPlusCleanupFailure_KeepsThePassAndExitsInfrastructure()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            independentOracleId: "order-created");
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")),
            providers: [VerifyingOracleProvider("order-created")]);

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.Pass, result.PrimaryExitCategory);
        Assert.False(result.Ok);
        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report?.Outcome?.Status);
        Assert.True(result.Report?.Outcome?.Verified);
        Assert.True(result.Report?.Verification?.Verified);
        Assert.Null(result.Report?.Failure);

        var secondary = Assert.Single(result.Report!.SecondaryFailures);
        Assert.Equal(MauiFlowSecondaryFailurePhases.Cleanup, secondary.Phase);
        Assert.Equal("fake-cleanup-failed", secondary.Code);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, secondary.Class);
        Assert.True(secondary.Retryable);

        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Manifest?.Outcome?.Status);
        Assert.True(result.Manifest?.Outcome?.Verified);
        Assert.Equal(
            FlowExecutionExitCategories.InfrastructureFailure,
            result.Manifest?.Outcome?.ExitCategory);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            Assert.Single(result.Manifest!.Outcome!.SecondaryFailures).Phase);
        Assert.False(result.Manifest?.Lifecycle?.CleanupCompleted);

        var junit = XDocument.Load(result.JUnitPath!);
        Assert.Equal("2", junit.Root?.Attribute("tests")?.Value);
        Assert.Equal("0", junit.Root?.Attribute("failures")?.Value);
        Assert.Equal("1", junit.Root?.Attribute("errors")?.Value);
        Assert.Equal("0", junit.Root?.Attribute("skipped")?.Value);
        var flowCase = FlowCase(junit);
        Assert.Empty(flowCase.Elements("failure"));
        Assert.Empty(flowCase.Elements("error"));
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            CleanupCases(junit).Single().Attribute("name")?.Value);
        Assert.Equal(
            FlowExecutionExitCategories.Pass,
            JUnitProperty(junit, "devflow.primaryExitCategory"));
        Assert.Equal(
            FlowExecutionExitCategories.InfrastructureFailure,
            JUnitProperty(junit, "devflow.exitCategory"));

        // Both axes have to be legible in one line, or the operator reads a green run that failed.
        Assert.Contains("owned cleanup did not complete", result.Message, StringComparison.Ordinal);
        Assert.Contains("fake-cleanup-failed", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_UnverifiedRunPlusCleanupFailure_KeepsTheUnverifiedPrimaryResult()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.Unverified, result.PrimaryExitCategory);
        Assert.Equal(MauiFlowRunOutcomes.Passed, result.Report?.Outcome?.Status);
        Assert.False(result.Report?.Outcome?.Verified);
        Assert.Null(result.Report?.Failure);
        Assert.Single(result.Report!.SecondaryFailures);

        var junit = XDocument.Load(result.JUnitPath!);
        Assert.Equal("2", junit.Root?.Attribute("tests")?.Value);
        Assert.Equal("0", junit.Root?.Attribute("failures")?.Value);
        Assert.Equal("1", junit.Root?.Attribute("errors")?.Value);
        // The flow itself is still not a regression, so it is still <skipped>, not <error>.
        Assert.Equal("1", junit.Root?.Attribute("skipped")?.Value);
        Assert.Single(FlowCase(junit).Elements("skipped"));
    }

    /// <summary>
    /// A real assertion failure keeps its own category even when teardown also failed. Promoting it
    /// to <c>infrastructure-failure</c> would advertise a genuine regression as a retryable
    /// environment problem, which is exactly the retry the category exists to prevent.
    /// </summary>
    [Fact]
    public async Task Coordinator_TestFailurePlusCleanupFailure_KeepsTheTestFailureCategory()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            expectedRoute: "//different");
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(FlowExecutionExitCategories.TestFailure, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.TestFailure, result.PrimaryExitCategory);
        Assert.Equal(MauiFlowRunOutcomes.Failed, result.Report?.Outcome?.Status);
        // The symptom an author needs is preserved instead of being replaced by "cleanup failed".
        Assert.NotEqual(MauiFlowFailureClasses.Infrastructure, result.Report?.Failure?.Class);
        Assert.NotEqual("fake-cleanup-failed", result.Report?.Failure?.Code);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            Assert.Single(result.Report!.SecondaryFailures).Phase);
        Assert.False(result.Manifest?.Lifecycle?.CleanupCompleted);
        Assert.Equal(
            FlowExecutionExitCategories.TestFailure,
            result.Manifest?.Outcome?.ExitCategory);

        var junit = XDocument.Load(result.JUnitPath!);
        Assert.Equal("2", junit.Root?.Attribute("tests")?.Value);
        Assert.Equal("1", junit.Root?.Attribute("failures")?.Value);
        Assert.Equal("1", junit.Root?.Attribute("errors")?.Value);
        Assert.Single(FlowCase(junit).Elements("failure"));
        Assert.Single(CleanupCases(junit));
    }

    [Fact]
    public async Task Coordinator_UnknownCompletionPlusCleanupFailure_StaysFailClosed()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false)
        {
            CancelAtForwarding = cancellation,
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")),
            cancellationToken: cancellation.Token);

        // An unconfirmed mutation must never be relabelled as a retryable environment problem.
        Assert.Equal(FlowExecutionExitCategories.UnknownCompletion, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.UnknownCompletion, result.PrimaryExitCategory);
        Assert.Equal(MauiFlowRunOutcomes.UnknownCompletion, result.Report?.Outcome?.Status);
        Assert.True(result.Manifest?.Outcome?.UnknownCompletion);
        Assert.Equal(
            FlowExecutionExitCategories.UnknownCompletion,
            result.Manifest?.Outcome?.ExitCategory);
        Assert.Single(result.Report!.SecondaryFailures);
        Assert.Equal(1, adapter.CleanupCalls);
    }

    [Fact]
    public async Task Coordinator_PrimaryInfrastructureFailurePlusCleanupFailure_KeepsThePrimaryCode()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false)
        {
            ForwardingFailure = FlowExecutionException.Infrastructure(
                "agent-forward-failed",
                "The host could not forward the agent port."),
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.PrimaryExitCategory);
        // The primary code and phase survive: the run failed to forward, it did not fail to clean up.
        Assert.Equal("agent-forward-failed", result.Report?.Failure?.Code);
        Assert.Equal("agent-forward", result.Report?.Failure?.Phase);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            Assert.Single(result.Report!.SecondaryFailures).Phase);
    }

    /// <summary>
    /// A run that failed before a semantic report existed keeps its own primary code and phase.
    /// Cleanup is still only ever secondary, even when the synthetic report is all there is.
    /// </summary>
    [Fact]
    public async Task Coordinator_SyntheticFailurePlusCleanupFailure_KeepsTheSyntheticPrimaryFailure()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        using var ownedRoot = new UndeletableDirectory(Path.Combine(workspace.Root, "owned-build"));
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(
                Artifact(Path.Combine(workspace.Root, "app.apk")) with { OwnedOutputRoot = ownedRoot.Path }),
            new FakePlatformAdapter
            {
                PreflightFailure = FlowExecutionException.Infrastructure(
                    "app-build-failed",
                    "bounded build failure"),
            });

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal("app-build-failed", result.Report?.Failure?.Code);
        Assert.Equal(MauiFlowRunOutcomes.InfrastructureError, result.Report?.Outcome?.Status);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.ArtifactCleanup,
            Assert.Single(result.Report!.SecondaryFailures).Phase);
        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
    }

    [Fact]
    public async Task Coordinator_BothCleanupStagesFail_AreBothRecordedInDeterministicOrder()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        using var ownedRoot = new UndeletableDirectory(Path.Combine(workspace.Root, "owned-build"));
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")) with { OwnedOutputRoot = ownedRoot.Path });

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.Unverified, result.PrimaryExitCategory);
        Assert.Equal(
            [MauiFlowSecondaryFailurePhases.ArtifactCleanup, MauiFlowSecondaryFailurePhases.Cleanup],
            result.Report!.SecondaryFailures.Select(static failure => failure.Phase!).ToArray());
        // The manifest is a redacted projection, not the report's own list, so it is compared on
        // values rather than by reference.
        Assert.Equal(
            [MauiFlowSecondaryFailurePhases.ArtifactCleanup, MauiFlowSecondaryFailurePhases.Cleanup],
            result.Manifest!.Outcome!.SecondaryFailures.Select(static failure => failure.Phase!).ToArray());
        Assert.Equal(
            ["artifact-cleanup-failed", "fake-cleanup-failed"],
            result.Manifest.Outcome.SecondaryFailures.Select(static failure => failure.Code!).ToArray());
        Assert.Contains(
            result.Manifest.Lifecycle!.Stages,
            stage => stage.Name == "artifact-cleanup" && stage.Status == "failed");
        Assert.Contains(
            result.Manifest.Lifecycle.Stages,
            stage => stage.Name == "cleanup" && stage.Status == "failed");

        var junit = XDocument.Load(result.JUnitPath!);
        Assert.Equal("3", junit.Root?.Attribute("tests")?.Value);
        Assert.Equal("2", junit.Root?.Attribute("errors")?.Value);
        Assert.Equal(2, CleanupCases(junit).Count);
    }

    /// <summary>
    /// A proven crash is still attributed to the app when teardown also failed. The old rule made
    /// cleanup outrank the crash, so the one run where the app demonstrably died reported an
    /// infrastructure problem instead.
    /// </summary>
    [Fact]
    public async Task Coordinator_AppCrashPlusCleanupFailure_StillStampsTheCrash()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            expectedRoute: "//different");
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false)
        {
            AppProcessEvidence = new MauiFlowAppProcessEvidence
            {
                Probed = true,
                Source = "android-adb",
                ProcessExited = true,
                ExitReason = MauiFlowAppExitReasons.Crash,
                CrashLogPresent = true,
            },
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(MauiFlowFailureClasses.AppCrash, result.Report?.Failure?.Class);
        Assert.Equal(FlowExecutionExitCategories.TestFailure, result.ExitCategory);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            Assert.Single(result.Report!.SecondaryFailures).Phase);
    }

    /// <summary>
    /// An owned build root that cannot be deleted is recorded even when no platform session ever
    /// existed, so a run that failed before launch still reports what the host left behind.
    /// </summary>
    [Fact]
    public async Task Coordinator_ArtifactCleanupFailureWithoutASession_IsStillRecorded()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        using var ownedRoot = new UndeletableDirectory(Path.Combine(workspace.Root, "owned-build"));
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(
                Artifact(Path.Combine(workspace.Root, "app.apk")) with { OwnedOutputRoot = ownedRoot.Path }),
            new FakePlatformAdapter
            {
                PreflightFailure = FlowExecutionException.Unsupported(
                    "android-device-unsupported",
                    "The exact Android device cannot host this build."),
            });

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        // The primary refusal is untouched; only the cleanup axis is added.
        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.ExitCategory);
        Assert.Equal(FlowExecutionExitCategories.Unsupported, result.PrimaryExitCategory);
        Assert.Equal("android-device-unsupported", result.Report?.Failure?.Code);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.ArtifactCleanup,
            Assert.Single(result.Report!.SecondaryFailures).Phase);
    }

    /// <summary>
    /// A replay can finish and mark itself verified before the invocation is cancelled. Publishing
    /// that optimistic verdict would advertise an unprovable mutation as an independently verified
    /// pass, which is the exact claim <c>unknown-completion</c> exists to refuse.
    /// </summary>
    [Fact]
    public async Task Coordinator_VerifiedRunLostAfterReplay_IsRestatedAsUnknownCompletion()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            independentOracleId: "order-created");
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakePlatformAdapter(allowMutation: true);
        var provider = new FakeStateEvidenceProvider
        {
            Evaluate = _ => throw new OperationCanceledException(CancelAndCreateToken(cancellation)),
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")),
            providers: [provider],
            cancellationToken: cancellation.Token);

        Assert.Equal(FlowExecutionExitCategories.UnknownCompletion, result.ExitCategory);
        Assert.Equal(MauiFlowRunOutcomes.UnknownCompletion, result.Report?.Outcome?.Status);
        Assert.False(result.Report?.Outcome?.Verified);
        Assert.False(result.Report?.Verification?.Verified);
        Assert.False(result.Manifest?.Outcome?.Verified);
        Assert.True(result.Manifest?.Outcome?.UnknownCompletion);
        Assert.Equal(MauiFlowRunOutcomes.UnknownCompletion, result.Manifest?.Outcome?.Status);
    }

    private static CancellationToken CancelAndCreateToken(CancellationTokenSource source)
    {
        source.Cancel();
        return source.Token;
    }

    /// <summary>
    /// An adapter's cleanup detail code is an arbitrary string. One the report redactor refuses to
    /// publish must not delete the cleanup failure with it: dropping the entry would leave a
    /// passing run with no secondary failure at all and report it green.
    /// </summary>
    [Fact]
    public async Task Coordinator_UnpublishableCleanupDetailCode_StillRecordsTheCleanupFailure()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false)
        {
            // Mixed case with no separators: the redactor treats this as an opaque secret value.
            CleanupDetailCode = "AndroidUninstallTimedOutUnexpectedly",
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, result.ExitCategory);
        Assert.False(result.Ok);
        var secondary = Assert.Single(result.Report!.SecondaryFailures);
        Assert.Equal(MauiFlowSecondaryFailurePhases.Cleanup, secondary.Phase);
        Assert.Equal("cleanup-failed", secondary.Code);
        Assert.DoesNotContain("AndroidUninstall", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_CleanupFailure_SurvivesTheOnDiskReportAndManifestRoundTrip()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        var report = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(result.ReportPath!),
            MauiTestingJsonContext.Default.MauiFlowRunReport)!;
        var manifest = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(result.ManifestPath!),
            MauiTestingJsonContext.Default.MauiTestExecutionManifest)!;

        Assert.Equal(MauiFlowRunOutcomes.Passed, report.Outcome?.Status);
        Assert.Null(report.Failure);
        var secondary = Assert.Single(report.SecondaryFailures);
        Assert.Equal(MauiFlowSecondaryFailurePhases.Cleanup, secondary.Phase);
        Assert.Equal("fake-cleanup-failed", secondary.Code);
        Assert.Equal(MauiFlowFailureClasses.Infrastructure, secondary.Class);
        Assert.True(secondary.Retryable);
        Assert.Equal(
            FlowExecutionExitCategories.InfrastructureFailure,
            report.ExtensionData!["exitCategory"].GetString());
        // Old artifacts carried the pre-cleanup verdict here; new ones must not write it at all.
        Assert.False(report.ExtensionData.ContainsKey("primaryExecutionOutcome"));
        Assert.Equal(MauiFlowRunOutcomes.Passed, manifest.Outcome?.Status);
        Assert.Equal(
            FlowExecutionExitCategories.InfrastructureFailure,
            manifest.Outcome?.ExitCategory);
        Assert.Equal(
            MauiFlowSecondaryFailurePhases.Cleanup,
            Assert.Single(manifest.Outcome!.SecondaryFailures).Phase);

        // Triage has to accept the pair: a preserved pass plus a recorded cleanup failure is a
        // consistent occurrence, not a corrupt artifact.
        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Manifest = manifest,
            IsCurrentLocalRun = true,
        });
        Assert.Equal(MauiFlowTriageEvidenceStates.Sufficient, triage.Evidence?.State);
        Assert.False(triage.Retryable);
        Assert.False(triage.RepairEligible);
        Assert.DoesNotContain(MauiFlowTriageNextActions.RetryRun, triage.AllowedNextActions);
    }

    /// <summary>
    /// The shipped triage entry point has to accept a real cleanup-failed run for every primary
    /// outcome. A manifest/report pair that records a preserved verdict plus a cleanup failure is a
    /// consistent occurrence; scoring it insufficient would tell an operator to collect evidence
    /// they already have.
    /// </summary>
    [Theory]
    [InlineData("//checkout", MauiFlowRunOutcomes.Passed)]
    [InlineData("//different", MauiFlowRunOutcomes.Failed)]
    public async Task FlowTriage_AcceptsACleanupFailedRun(string expectedRoute, string expectedStatus)
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None, expectedRoute);
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false);

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            Path.Combine(workspace.Root, "triage-" + expectedStatus),
            Artifact(Path.Combine(workspace.Root, "app.apk")));

        Assert.Equal(expectedStatus, result.Report?.Outcome?.Status);
        var triage = await new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = result.ManifestPath!,
            ReportPath = result.ReportPath!,
        });

        Assert.Equal(MauiFlowTriageEvidenceStates.Sufficient, triage.Triage.Evidence?.State);
        Assert.False(triage.Triage.RepairEligible);
        Assert.Contains("owned-cleanup-incomplete", triage.Triage.RepairEligibilityCodes);
        // Imported evidence is diagnostic-only, and cleanup failure must not add a retry either.
        Assert.DoesNotContain(MauiFlowTriageNextActions.RetryRun, triage.Triage.AllowedNextActions);
    }

    [Fact]
    public async Task FlowTriage_AcceptsAnUnknownCompletionRunWhoseCleanupAlsoFailed()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakePlatformAdapter(allowMutation: true, cleanupSucceeds: false)
        {
            CancelAtForwarding = cancellation,
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            Path.Combine(workspace.Root, "triage-unknown"),
            Artifact(Path.Combine(workspace.Root, "app.apk")),
            cancellationToken: cancellation.Token);

        var triage = await new FlowTriageCoordinator().AnalyzeAsync(new FlowTriageRequest
        {
            ManifestPath = result.ManifestPath!,
            ReportPath = result.ReportPath!,
        });

        // A cancelled run has no failed step to point at, which is a property of unknown completion
        // rather than of cleanup. What must hold is that nothing about the two-axis outcome reads
        // as inconsistent: the pair describes one occurrence and says so.
        Assert.Equal(["failure-step"], triage.Triage.Evidence!.MissingFacts);
        Assert.False(triage.Triage.RepairEligible);
        Assert.Contains("owned-cleanup-incomplete", triage.Triage.RepairEligibilityCodes);
    }

    /// <summary>
    /// Runs a complete fake Android invocation whose agent reports <paramref name="route"/>, so a
    /// caller decides whether the flow's route assertion passes or fails.
    /// </summary>
    private static async Task<FlowExecutionResult> RunAndroidFlowAsync(
        FakePlatformAdapter adapter,
        (string Flow, string Plan) bundle,
        string output,
        ResolvedAppArtifact artifact,
        string route = "//checkout",
        IEnumerable<IFlowStateEvidenceProvider>? providers = null,
        CancellationToken cancellationToken = default)
    {
        var status = $$"""
            {
              "agent": { "name": "Microsoft.Maui.DevFlow.Agent", "version": "test" },
              "device": { "platform": "Android", "deviceType": "Virtual", "idiom": "Phone" },
              "app": { "name": "App", "packageId": "com.example.app", "processId": 321, "build": "42" },
              "capabilities": { "ui": true, "mutations": true, "workflowCommandLedger": true },
              "route": "{{route}}",
              "running": true
            }
            """;
        await using var server = new MockAgentServer(status);
        await server.StartAsync();
        var listCalls = 0;
        var binding = new ExactAgentBindingResolver(_ =>
        {
            listCalls++;
            return Task.FromResult<AgentRegistration[]?>(
                listCalls == 1
                    ? []
                    :
                    [
                        Agent("new-instance") with
                        {
                            Port = server.Port,
                            ProcessId = 321,
                        },
                    ]);
        }, pollInterval: TimeSpan.Zero);
        var coordinator = new FlowExecutionCoordinator(
            new CommittedFlowBundleLoader(),
            new FakeArtifactResolver(artifact),
            [adapter],
            new FlowStateEvidenceProviderRegistry(providers ?? []),
            binding,
            new FlowRunReportWriter(),
            new JUnitFlowExecutionWriter(),
            new ExecutionManifestWriter(),
            new ImmutableExecutionOutputWriter(),
            () => Task.FromResult<int?>(19223),
            appSourceIdentityProvider: new FakeAppSourceIdentityProvider(),
            agentSessionIdFactory: static () => "flowsession");

        return await coordinator.RunAsync(Request(bundle, output), cancellationToken);
    }

    private static IFlowStateEvidenceProvider VerifyingOracleProvider(string oracleId)
        => new FakeStateEvidenceProvider
        {
            Evaluate = request => new FlowPostRunOracleEvidenceResult
            {
                RunId = request.RunId,
                FlowDigest = request.FlowDigest,
                DeviceIdentityFingerprint = request.DeviceIdentityFingerprint,
                AppBuildFingerprint = request.AppBuildFingerprint,
                PackageDigest = request.PackageDigest,
                StartedAt = request.StartedAt,
                EndedAt = request.EndedAt,
                ObservedAt = request.EndedAt,
                BusinessOracles =
                [
                    new MauiIndependentBusinessOracleResult
                    {
                        OracleId = oracleId,
                        Independent = true,
                        Succeeded = true,
                        ObservedAt = request.EndedAt,
                    },
                ],
            },
        };

    private static XElement FlowCase(XDocument junit)
        => junit.Descendants("testcase")
            .Single(element => (string?)element.Attribute("classname") == "maui.devflow");

    private static List<XElement> CleanupCases(XDocument junit)
        => junit.Descendants("testcase")
            .Where(element => (string?)element.Attribute("classname") == "maui.devflow.cleanup")
            .ToList();

    private static string? JUnitProperty(XDocument junit, string name)
        => junit.Descendants("property")
            .Single(element => (string?)element.Attribute("name") == name)
            .Attribute("value")?.Value;

    /// <summary>
    /// A directory the host genuinely cannot delete, so owned artifact cleanup fails for the same
    /// reason it would on a real machine rather than through a stubbed result.
    /// </summary>
    private sealed class UndeletableDirectory : IDisposable
    {
        private readonly FileStream? _handle;
        private readonly string _child;
        private readonly UnixFileMode _originalMode;

        public UndeletableDirectory(string path)
        {
            Path = path;
            _child = System.IO.Path.Combine(path, "child");
            Directory.CreateDirectory(_child);
            var file = System.IO.Path.Combine(_child, "locked.bin");
            File.WriteAllBytes(file, [1]);
            if (OperatingSystem.IsWindows())
            {
                _handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            else
            {
                _originalMode = File.GetUnixFileMode(_child);
                File.SetUnixFileMode(_child, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            _handle?.Dispose();
            if (!OperatingSystem.IsWindows() && Directory.Exists(_child))
                File.SetUnixFileMode(_child, _originalMode);
        }
    }

    [Theory]
    [InlineData(FlowExecutionExitCategories.Pass, FlowExecutionExitCategories.InfrastructureFailure)]
    [InlineData(FlowExecutionExitCategories.Unverified, FlowExecutionExitCategories.InfrastructureFailure)]
    [InlineData(FlowExecutionExitCategories.TestFailure, FlowExecutionExitCategories.TestFailure)]
    [InlineData(FlowExecutionExitCategories.UnknownCompletion, FlowExecutionExitCategories.UnknownCompletion)]
    [InlineData(FlowExecutionExitCategories.InvalidConfiguration, FlowExecutionExitCategories.InvalidConfiguration)]
    [InlineData(FlowExecutionExitCategories.Unsupported, FlowExecutionExitCategories.Unsupported)]
    [InlineData(FlowExecutionExitCategories.InfrastructureFailure, FlowExecutionExitCategories.InfrastructureFailure)]
    public void OverallExitCategory_CleanupFailureOnlyEverPromotesAPassingRun(
        string primary,
        string expected)
    {
        MauiFlowSecondaryFailure[] cleanupFailed =
        [
            new()
            {
                Phase = MauiFlowSecondaryFailurePhases.Cleanup,
                Code = "cleanup-failed",
                Class = MauiFlowFailureClasses.Infrastructure,
                Retryable = true,
            },
        ];

        // Without a cleanup failure the primary answer is the whole answer.
        Assert.Equal(primary, FlowExecutionCoordinator.ComposeOverallExitCategory(primary, []));
        Assert.Equal(
            expected,
            FlowExecutionCoordinator.ComposeOverallExitCategory(primary, cleanupFailed));
        Assert.False(FlowExecutionExitCategories.IsSuccess(
            FlowExecutionCoordinator.ComposeOverallExitCategory(primary, cleanupFailed)));
    }

    /// <summary>
    /// Three exit categories have no honest restatement. Rewriting the report to any of them —
    /// which, before the guard, meant writing every one of them out as <c>infrastructure-error</c> —
    /// would delete the verdict the run actually observed and replace it with a retryable
    /// environment problem that never happened.
    /// </summary>
    [Theory]
    [InlineData(FlowExecutionExitCategories.Pass)]
    [InlineData(FlowExecutionExitCategories.Unverified)]
    [InlineData(FlowExecutionExitCategories.Unsupported)]
    public void RestatePrimaryOutcome_RefusesACategoryItCannotStateHonestly(string exitCategory)
    {
        var report = FailedTestReport();
        var events = report.Events.Count;

        var restated = FlowExecutionCoordinator.RestatePrimaryOutcome(
            report,
            exitCategory,
            "detail-code",
            "Message.",
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            "post-run-verification");

        Assert.False(restated);
        Assert.Equal(MauiFlowRunOutcomes.Failed, report.Outcome?.Status);
        Assert.Equal(MauiFlowFailureClasses.AssertionFailed, report.Failure?.Class);
        Assert.Equal("assertion-failed", report.Failure?.Code);
        Assert.Equal(events, report.Events.Count);
        // In particular it must not fall through to an infrastructure error, which would both hide
        // the regression and make the failure look automatically retryable.
        Assert.NotEqual(MauiFlowRunOutcomes.InfrastructureError, report.Outcome?.Status);
        Assert.Null(report.ExtensionData);
    }

    /// <summary>
    /// The refusal leaves the report saying what the run observed, so the disagreement with the
    /// command's own category is still open. It must never be resolved toward green: exiting
    /// <c>pass</c> over an artifact that records a failure turns a red run into a green build.
    /// </summary>
    [Theory]
    [InlineData(FlowExecutionExitCategories.Pass)]
    [InlineData(FlowExecutionExitCategories.Unverified)]
    public void RestatePrimaryOutcome_RefusalLeavesTheReportStricterThanTheRequestedCategory(
        string requested)
    {
        var report = FailedTestReport();

        var restated = FlowExecutionCoordinator.RestatePrimaryOutcome(
            report,
            requested,
            "detail-code",
            "Message.",
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            "post-run-verification");

        Assert.False(restated);
        // This is the category the coordinator adopts in place of the refused one.
        var reported = FlowExecutionCoordinator.ClassifyReport(report);
        Assert.Equal(FlowExecutionExitCategories.TestFailure, reported);
        Assert.False(FlowExecutionExitCategories.IsSuccess(reported));
    }

    /// <summary>
    /// A restatement is reached when a later, non-cleanup stage fails, and the displaced verdict is
    /// the thing an operator most needs. The prose event names the class, but prose is not something
    /// a dashboard can gate on, so the verdict is also preserved in machine-readable form.
    /// </summary>
    [Fact]
    public void RestatePrimaryOutcome_PreservesTheDisplacedVerdictMachineReadably()
    {
        var report = PassedVerifiedReport();

        var restated = FlowExecutionCoordinator.RestatePrimaryOutcome(
            report,
            FlowExecutionExitCategories.UnknownCompletion,
            "run-lost",
            "The invocation was lost after the replay finished.",
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            "post-run-verification");

        Assert.True(restated);
        Assert.Equal(MauiFlowRunOutcomes.UnknownCompletion, report.Outcome?.Status);
        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        // Without this, a reader still holding the object's older meaning — the verdict an owned
        // cleanup failure overwrote — would promote the displaced pass back to the real result and
        // hide the post-run fault. `secondaryFailures` cannot discriminate: the contract requires
        // absent and empty to be read identically.
        Assert.Equal(
            MauiFlowPrimaryOutcomeDisplacements.Restatement,
            primary.GetProperty("displacedBy").GetString());
        Assert.Equal(FlowExecutionExitCategories.Pass, primary.GetProperty("exitCategory").GetString());
        Assert.Equal(MauiFlowRunOutcomes.Passed, primary.GetProperty("status").GetString());
        Assert.True(primary.GetProperty("verified").GetBoolean());
        // Nothing failed before the restatement, so there is no failure to name.
        Assert.False(primary.TryGetProperty("failureClass", out _));
        // The prose record stays too: the two are written together, not instead of one another.
        Assert.Contains(
            report.Events,
            item => item.Kind == "primary-outcome-restated");
    }

    [Fact]
    public void RestatePrimaryOutcome_PreservesADisplacedFailureClassCodeAndPhase()
    {
        var report = FailedTestReport();

        FlowExecutionCoordinator.RestatePrimaryOutcome(
            report,
            FlowExecutionExitCategories.InfrastructureFailure,
            "oracle-probe-failed",
            "Post-run verification did not complete.",
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            "post-run-verification");

        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        Assert.Equal(
            FlowExecutionExitCategories.TestFailure,
            primary.GetProperty("exitCategory").GetString());
        Assert.Equal(MauiFlowRunOutcomes.Failed, primary.GetProperty("status").GetString());
        Assert.Equal(
            MauiFlowFailureClasses.AssertionFailed,
            primary.GetProperty("failureClass").GetString());
        Assert.Equal("assertion-failed", primary.GetProperty("failureCode").GetString());
        Assert.Equal("assertion", primary.GetProperty("failurePhase").GetString());
        Assert.False(primary.GetProperty("verified").GetBoolean());
    }

    /// <summary>
    /// A synthetic report is manufactured from the failure the command already knows about, so
    /// there is no earlier verdict to displace. Preserving one would claim a displaced result that
    /// never existed — and the manufactured report does carry an outcome and a failure, so only the
    /// caller can tell.
    /// </summary>
    [Fact]
    public void RestatePrimaryOutcome_WithNoVerdictToDisplace_WritesNoPrimaryExecutionOutcome()
    {
        var synthetic = FailedTestReport();
        var empty = new MauiFlowRunReport { RunId = "run-empty" };

        FlowExecutionCoordinator.RestatePrimaryOutcome(
            synthetic,
            FlowExecutionExitCategories.InfrastructureFailure,
            "launch-failed",
            "The app could not be launched.",
            DateTimeOffset.UnixEpoch,
            "launch",
            reportIsSynthetic: true);
        FlowExecutionCoordinator.RestatePrimaryOutcome(
            empty,
            FlowExecutionExitCategories.InfrastructureFailure,
            "launch-failed",
            "The app could not be launched.",
            DateTimeOffset.UnixEpoch,
            "launch");

        Assert.Null(synthetic.ExtensionData);
        Assert.Null(empty.ExtensionData);
    }

    /// <summary>
    /// The displaced verdict has to survive the projection every report goes through on the way to
    /// disk, or preserving it in memory is worth nothing to the operator reading the artifact.
    /// </summary>
    [Fact]
    public async Task Coordinator_RestatedRun_KeepsTheDisplacedVerdictInTheOnDiskReport()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(
            MauiFlowSideEffectPolicies.None,
            independentOracleId: "order-created");
        using var cancellation = new CancellationTokenSource();
        var adapter = new FakePlatformAdapter(allowMutation: true);
        var provider = new FakeStateEvidenceProvider
        {
            Evaluate = _ => throw new OperationCanceledException(CancelAndCreateToken(cancellation)),
        };

        var result = await RunAndroidFlowAsync(
            adapter,
            bundle,
            workspace.Output,
            Artifact(Path.Combine(workspace.Root, "app.apk")),
            providers: [provider],
            cancellationToken: cancellation.Token);

        var report = JsonSerializer.Deserialize(
            await File.ReadAllTextAsync(result.ReportPath!),
            MauiTestingJsonContext.Default.MauiFlowRunReport)!;

        Assert.Equal(MauiFlowRunOutcomes.UnknownCompletion, report.Outcome?.Status);
        var primary = report.ExtensionData!["primaryExecutionOutcome"];
        Assert.Equal(MauiFlowRunOutcomes.Passed, primary.GetProperty("status").GetString());
        // The discriminator has to survive the redactor's allowlist, or the artifact is ambiguous
        // exactly where it is read without the process that produced it.
        Assert.Equal(
            MauiFlowPrimaryOutcomeDisplacements.Restatement,
            primary.GetProperty("displacedBy").GetString());
        // Cleanup succeeded here, so the second axis is empty: the two are never confused.
        Assert.Empty(report.SecondaryFailures);
    }

    private static MauiFlowRunReport PassedVerifiedReport() => new()
    {
        RunId = "run-restate",
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
        Outcome = new MauiFlowRunOutcome
        {
            Status = MauiFlowRunOutcomes.Passed,
            Terminal = true,
            Verified = true,
        },
    };

    private static MauiFlowRunReport FailedTestReport() => new()
    {
        RunId = "run-restate",
        StartedAt = DateTimeOffset.UnixEpoch,
        EndedAt = DateTimeOffset.UnixEpoch.AddSeconds(1),
        Outcome = new MauiFlowRunOutcome
        {
            Status = MauiFlowRunOutcomes.Failed,
            Terminal = true,
            Verified = false,
        },
        Failure = new MauiFlowFailure
        {
            FailureId = "failure-run-restate",
            Class = MauiFlowFailureClasses.AssertionFailed,
            Code = "assertion-failed",
            Phase = "assertion",
            StepId = "step-1",
        },
    };

    [Fact]
    public void ExactBinding_RejectsStaleInstanceAndAcceptsMatchingNewInstance()
    {
        var old = Agent("old-instance");
        var expectation = Expectation();

        var stale = ExactAgentBindingResolver.SelectNewMatch([old], [old], expectation);
        var fresh = ExactAgentBindingResolver.SelectNewMatch(
            [old],
            [old, Agent("new-instance")],
            expectation);

        Assert.Equal(ExactAgentBindingSelectionKind.Pending, stale.Kind);
        Assert.True(stale.MatchingStaleAgentObserved);
        Assert.Equal(ExactAgentBindingSelectionKind.Matched, fresh.Kind);
        Assert.Equal("new-instance", fresh.Agent?.InstanceId);
    }

    [Fact]
    public void ExactBinding_MultipleNewMatchingInstances_IsAmbiguous()
    {
        var selection = ExactAgentBindingResolver.SelectNewMatch(
            [],
            [Agent("new-one"), Agent("new-two")],
            Expectation());

        Assert.Equal(ExactAgentBindingSelectionKind.Ambiguous, selection.Kind);
        Assert.Null(selection.Agent);
    }

    [Fact]
    public void ExactBinding_UsesOpaqueSessionInsteadOfProjectPathRepresentation()
    {
        var relativeProject = Agent("relative-project") with
        {
            Project = "App.csproj",
        };
        var wrongSession = relativeProject with
        {
            InstanceId = "wrong-session",
            SessionId = "different-session",
            Port = 9333,
        };

        var selection = ExactAgentBindingResolver.SelectNewMatch(
            [],
            [relativeProject, wrongSession],
            Expectation());

        Assert.Equal(ExactAgentBindingSelectionKind.Matched, selection.Kind);
        Assert.Equal("relative-project", selection.Agent?.InstanceId);
    }

    [Fact]
    public async Task CommittedBundleLoader_StaleDigest_IsRejected()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Name = "changed-after-plan-commit";
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new CommittedFlowBundleLoader().LoadAsync(bundle.Flow, bundle.Plan));

        Assert.Equal("plan-flow-digest-stale", exception.Code);
    }

    [Fact]
    public async Task CommittedBundleLoader_DraftSidecar_IsRejected()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        plan["draft"] = true;
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new CommittedFlowBundleLoader().LoadAsync(bundle.Flow, bundle.Plan));

        Assert.Equal("draft-not-executable", exception.Code);
    }

    [Fact]
    public async Task AppSourceIdentity_CleanProjectTree_UsesCommitAndRelativeProject()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var runner = new QueueProcessRunner(
            Success("0123456789abcdef0123456789abcdef01234567\n"),
            Success());
        var provider = new GitAppSourceIdentityProvider(runner);

        var identity = await provider.ResolveAsync(project);

        Assert.Equal("0123456789abcdef0123456789abcdef01234567", identity.SourceRevision);
        Assert.StartsWith("sha256:", identity.AppSourceFingerprint);
        var status = Assert.Single(runner.Calls, call => call.Arguments.Contains("status"));
        Assert.Equal("--", status.Arguments[^2]);
        Assert.Equal(
            Path.GetRelativePath(FindRepositoryRoot(), workspace.Root)
                .Replace(Path.DirectorySeparatorChar, '/'),
            status.Arguments[^1]);
    }

    [Fact]
    public async Task AppSourceIdentity_DirtyProjectTree_OmitsSourceFingerprint()
    {
        using var workspace = new ExecutionTestWorkspace();
        var project = Path.Combine(workspace.Root, "App.csproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var runner = new QueueProcessRunner(
            Success("0123456789abcdef0123456789abcdef01234567\n"),
            Success(" M App.csproj\n"));
        var provider = new GitAppSourceIdentityProvider(runner);

        var identity = await provider.ResolveAsync(project);

        Assert.Equal("0123456789abcdef0123456789abcdef01234567", identity.SourceRevision);
        Assert.Null(identity.AppSourceFingerprint);
    }

    [Fact]
    public async Task AndroidDeployment_PreexistingPackageIsRejectedBeforeOverwrite()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            Success("package:/data/app/~~token/com.example.app/base.apk\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
            {
                DeviceSerial = "emulator-5554",
                ApkPath = workspace.WriteApk(),
                PackageId = "com.example.app",
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, failure.ExitCategory);
        Assert.Equal("android-preexisting-app-unsafe", failure.Code);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("install"));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("uninstall"));
        Assert.All(runner.Calls, call => Assert.Equal(["-s", "emulator-5554"], call.Arguments.Take(2)));
    }

    [Fact]
    public async Task AndroidCleanup_UninstallRequested_RemovesOnlyNewlyInstalledPackage()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            Success(),
            Success("Success\n"),
            Success("com.example.app/.MainActivity\n"),
            Success(),
            Success("Status: ok\n"),
            Success("321\n"),
            Success(),
            Success("Success\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var session = await deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
        {
            DeviceSerial = "emulator-5554",
            ApkPath = workspace.WriteApk(),
            PackageId = "com.example.app",
        });
        var cleanup = await deployment.CleanupAsync(session, FlowExecutionCleanupPolicies.Uninstall);

        Assert.True(session.InstalledByInvocation);
        Assert.True(cleanup.PackageUninstalled);
        var uninstall = Assert.Single(runner.Calls, call => call.Arguments.Contains("uninstall"));
        Assert.Equal(["-s", "emulator-5554", "uninstall", "com.example.app"], uninstall.Arguments);
    }

    [Fact]
    public async Task AndroidDeployment_PackageNotFoundExit_IsNormalFreshInstallState()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            new ProcessResult
            {
                ExitCode = 1,
                StandardError = "Error: package com.example.app was not found\r\n",
            },
            Success("Performing Streamed Install\r\nSuccess\r\n"),
            Success("priority=0 preferredOrder=0 match=0x108000 specificIndex=-1 isDefault=true\r\ncom.example.app/.MainActivity\r\n"),
            Success(),
            Success("Status: ok\r\nLaunchState: COLD\r\n"),
            Success("321\r\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var session = await deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
        {
            DeviceSerial = "emulator-5554",
            ApkPath = workspace.WriteApk(),
            PackageId = "com.example.app",
        });

        Assert.False(session.PackageWasInstalledBefore);
        Assert.True(session.InstalledByInvocation);
        Assert.True(session.LaunchedByInvocation);
        Assert.Equal(321, session.ProcessId);
    }

    [Fact]
    public async Task AndroidDeployment_ModernBlankExitOnePackageQuery_IsNormalAbsence()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            new ProcessResult { ExitCode = 1 },
            Success("Success\n"),
            Success("com.example.app/.MainActivity\n"),
            Success(),
            Success("Status: ok\n"),
            Success("321\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var session = await deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
        {
            DeviceSerial = "emulator-5554",
            ApkPath = workspace.WriteApk(),
            PackageId = "com.example.app",
        });

        Assert.True(session.InstalledByInvocation);
        Assert.True(session.LaunchedByInvocation);
    }

    [Fact]
    public async Task AndroidDeployment_MultiUserDevice_IsRejectedBeforePackageMutation()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(Success(
            "Users:\n\tUserInfo{0:Owner:13} running\n\tUserInfo{10:Work:30}\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
            {
                DeviceSerial = "emulator-5554",
                ApkPath = workspace.WriteApk(),
                PackageId = "com.example.app",
            }));

        Assert.Equal(FlowExecutionExitCategories.Unsupported, failure.ExitCategory);
        Assert.Equal("android-multi-user-unsupported", failure.Code);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("install"));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("path"));
    }

    [Fact]
    public async Task AndroidDeployment_DeviceFailureDuringPackageQuery_IsInfrastructure()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            new ProcessResult
            {
                ExitCode = 1,
                StandardError = "adb: device 'emulator-5554' not found\r\n",
            });
        var deployment = new AndroidAppDeployment(provider, runner);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
            {
                DeviceSerial = "emulator-5554",
                ApkPath = workspace.WriteApk(),
                PackageId = "com.example.app",
            }));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, failure.ExitCategory);
        Assert.Equal("android-package-query-failed", failure.Code);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("install"));
    }

    [Fact]
    public async Task AndroidDeployment_CancelledInstall_ReconcilesNewPackageForOwnedCleanup()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var packageQueries = 0;
        var runner = new CallbackProcessRunner(call =>
        {
            if (call.Arguments.Contains("users"))
                return Task.FromResult(SingleUser());
            if (call.Arguments.Contains("pm") && call.Arguments.Contains("path"))
            {
                packageQueries++;
                return Task.FromResult(packageQueries == 1
                    ? new ProcessResult
                    {
                        ExitCode = 1,
                        StandardError = "Error: package com.example.app was not found\n",
                    }
                    : Success("package:/data/app/~~token/com.example.app/base.apk\n"));
            }
            if (call.Arguments.Contains("install"))
                throw new OperationCanceledException("adb install was cancelled after device commit");
            return Task.FromResult(Success("Success\n"));
        });
        var deployment = new AndroidAppDeployment(provider, runner);

        var failure = await Assert.ThrowsAsync<AndroidAppDeploymentException>(() =>
            deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
            {
                DeviceSerial = "emulator-5554",
                ApkPath = workspace.WriteApk(),
                PackageId = "com.example.app",
            }));
        var cleanup = await deployment.CleanupAsync(
            failure.Session,
            FlowExecutionCleanupPolicies.Uninstall);

        Assert.Equal(FlowExecutionExitCategories.UnknownCompletion, failure.Failure.ExitCategory);
        Assert.Equal("android-install-cancelled-unknown", failure.Failure.Code);
        Assert.True(failure.Session.InstallAttempted);
        Assert.True(failure.Session.InstalledByInvocation);
        Assert.False(failure.Session.InstallationCompletionUnknown);
        Assert.True(cleanup.PackageUninstalled);
        Assert.Equal(2, packageQueries);
        Assert.Single(runner.Calls, call => call.Arguments.Contains("uninstall"));
    }

    [Fact]
    public async Task AndroidCleanup_FailedLaunch_PreservesOwnershipForRequestedUninstall()
    {
        using var workspace = new ExecutionTestWorkspace();
        var provider = workspace.CreateAndroidProvider();
        var runner = new QueueProcessRunner(
            SingleUser(),
            Success(),
            Success("Success\n"),
            Success("com.other.app/.MainActivity\n"),
            Success("Success\n"));
        var deployment = new AndroidAppDeployment(provider, runner);

        var failure = await Assert.ThrowsAsync<AndroidAppDeploymentException>(() =>
            deployment.DeployAndLaunchAsync(new AndroidAppDeploymentRequest
            {
                DeviceSerial = "emulator-5554",
                ApkPath = workspace.WriteApk(),
                PackageId = "com.example.app",
            }));
        var cleanup = await deployment.CleanupAsync(
            failure.Session,
            FlowExecutionCleanupPolicies.Uninstall);

        Assert.True(failure.Session.InstalledByInvocation);
        Assert.False(failure.Session.LaunchedByInvocation);
        Assert.True(cleanup.PackageUninstalled);
        Assert.Single(runner.Calls, call => call.Arguments.Contains("uninstall"));
    }

    [Fact]
    public async Task Outputs_ManifestJUnitAndReportAgreeOnUnsupportedCategory()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.TestTenantResettable);
        var coordinator = CreateCoordinator(
            new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk"))),
            new FakePlatformAdapter());

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        var manifestJson = await File.ReadAllTextAsync(result.ManifestPath!);
        ExecutionManifestSchemaValidator.AssertValid(manifestJson);
        using var manifest = JsonDocument.Parse(manifestJson);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReportPath!));
        var junit = XDocument.Load(result.JUnitPath!);
        var junitCategory = junit
            .Descendants("property")
            .Single(property => (string?)property.Attribute("name") == "devflow.exitCategory")
            .Attribute("value")?.Value;

        Assert.Equal(result.ExitCategory, manifest.RootElement.GetProperty("outcome").GetProperty("exitCategory").GetString());
        Assert.Equal(result.ExitCategory, junitCategory);
        Assert.Equal(result.ExitCategory, report.RootElement.GetProperty("exitCategory").GetString());
        Assert.Equal("state-evidence-provider-missing", report.RootElement.GetProperty("failure").GetProperty("code").GetString());
        Assert.Equal(
            manifest.RootElement.GetProperty("runId").GetString(),
            report.RootElement.GetProperty("runId").GetString());
        Assert.DoesNotContain("emulator-5554", manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\src\App.csproj", manifestJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(MauiFlowRunOutcomes.Passed, true, null, FlowExecutionExitCategories.Pass)]
    [InlineData(MauiFlowRunOutcomes.Passed, false, null, FlowExecutionExitCategories.Unverified)]
    [InlineData(MauiFlowRunOutcomes.Failed, false, MauiFlowFailureClasses.AssertionFailed, FlowExecutionExitCategories.TestFailure)]
    [InlineData(MauiFlowRunOutcomes.InfrastructureError, false, MauiFlowFailureClasses.Infrastructure, FlowExecutionExitCategories.InfrastructureFailure)]
    [InlineData(MauiFlowRunOutcomes.UnknownCompletion, false, MauiFlowFailureClasses.UnknownCompletion, FlowExecutionExitCategories.UnknownCompletion)]
    [InlineData(MauiFlowRunOutcomes.Failed, false, MauiFlowFailureClasses.FlowInvalid, FlowExecutionExitCategories.InvalidConfiguration)]
    public void OutputCategory_MapsCanonicalReport(
        string status,
        bool verified,
        string? failureCode,
        string expected)
    {
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome
            {
                Status = status,
                Terminal = true,
                Verified = verified,
            },
            Failure = failureCode is null ? null : new MauiFlowFailure { Code = failureCode },
        };

        Assert.Equal(expected, FlowExecutionCoordinator.ClassifyReport(report));
    }

    [Fact]
    public void OutputCategory_UsesCanonicalFailureClassInsteadOfDetailCode()
    {
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Failed,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.Infrastructure,
                Code = "selector-zero-matches",
            },
        };

        Assert.Equal(
            FlowExecutionExitCategories.InfrastructureFailure,
            FlowExecutionCoordinator.ClassifyReport(report));
    }

    /// <summary>
    /// A crash reaches the runner as a dead agent channel, so the report's outcome status is
    /// <c>infrastructure-error</c>. Once the platform has proven the app itself exited abnormally,
    /// the run must stop being filed as an environment problem, because an environment problem is
    /// what CI retries.
    /// </summary>
    [Fact]
    public void OutputCategory_ProvenAppCrash_IsAttributedToTheAppNotTheHarness()
    {
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.InfrastructureError,
                Terminal = true,
                Verified = false,
            },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.AppCrash,
                Code = MauiFlowFailureClasses.AppCrash,
            },
        };

        Assert.Equal(FlowExecutionExitCategories.TestFailure, FlowExecutionCoordinator.ClassifyReport(report));
    }

    /// <summary>
    /// Knowing the app died still does not prove whether the step that was in flight committed its
    /// mutation, so an unknown-completion outcome stays unknown.
    /// </summary>
    [Theory]
    [InlineData(MauiFlowRunOutcomes.Orphaned)]
    [InlineData(MauiFlowRunOutcomes.TimedOut)]
    [InlineData(MauiFlowRunOutcomes.UnknownCompletion)]
    [InlineData(MauiFlowRunOutcomes.Cancelled)]
    public void OutputCategory_ProvenAppCrash_NeverRelaxesAnUnknownCompletion(string status)
    {
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome { Status = status, Terminal = true, Verified = false },
            Failure = new MauiFlowFailure
            {
                Class = MauiFlowFailureClasses.AppCrash,
                Code = MauiFlowFailureClasses.AppCrash,
            },
        };

        Assert.Equal(
            FlowExecutionExitCategories.UnknownCompletion,
            FlowExecutionCoordinator.ClassifyReport(report));
    }

    [Fact]
    public async Task StateEvidence_PostRunOracleIsBoundAndCanVerifyPassedExecution()
    {
        var now = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var plan = OraclePlan();
        var flow = OracleFlow();
        var provider = new FakeStateEvidenceProvider
        {
            PrepareResult = new FlowStateEvidenceResult
            {
                RunContext = AdmissionContext(now),
            },
            Evaluate = request => new FlowPostRunOracleEvidenceResult
            {
                RunId = request.RunId,
                FlowDigest = request.FlowDigest,
                DeviceIdentityFingerprint = request.DeviceIdentityFingerprint,
                AppBuildFingerprint = request.AppBuildFingerprint,
                PackageDigest = request.PackageDigest,
                StartedAt = request.StartedAt,
                EndedAt = request.EndedAt,
                ObservedAt = request.EndedAt.AddSeconds(1),
                BusinessOracles =
                [
                    new MauiIndependentBusinessOracleResult
                    {
                        OracleId = "order-created",
                        Independent = true,
                        Succeeded = true,
                        ObservedAt = request.EndedAt.AddSeconds(1),
                    },
                ],
            },
        };
        var registry = new FlowStateEvidenceProviderRegistry([provider]);
        var evidenceRequest = new FlowStateEvidenceRequest
        {
            Plan = plan,
            Flow = flow,
            Artifact = Artifact("app.apk"),
        };

        var admission = await registry.PrepareAsync(evidenceRequest);
        Assert.Empty(admission.RunContext.BusinessOracles);
        var evaluationRequest = new FlowPostRunOracleEvaluationRequest
        {
            Plan = plan,
            Flow = flow,
            Artifact = evidenceRequest.Artifact,
            RunId = "run-1",
            FlowDigest = "flow-digest",
            DeviceIdentityFingerprint = "device-fingerprint",
            AppBuildFingerprint = "build-fingerprint",
            PackageDigest = evidenceRequest.Artifact.PackageDigest,
            Platform = "android",
            DeviceSerial = "emulator-5554",
            PackageId = "com.example.app",
            StartedAt = now,
            EndedAt = now.AddSeconds(5),
            EvaluationDeadline = now.AddMinutes(1),
            Report = new MauiFlowRunReport(),
        };
        var oracles = await registry.EvaluatePostRunAsync(admission, evaluationRequest);
        admission.RunContext.BusinessOracles = oracles.ToList();
        var report = new MauiFlowRunReport
        {
            Outcome = new MauiFlowRunOutcome
            {
                Status = MauiFlowRunOutcomes.Passed,
                Terminal = true,
                Verified = false,
            },
        };

        FlowExecutionCoordinator.ApplyPostRunVerification(
            report,
            plan,
            flow,
            admission.RunContext,
            now.AddSeconds(7));

        Assert.Equal(1, provider.PrepareCalls);
        Assert.Equal(1, provider.EvaluateCalls);
        Assert.True(report.Outcome?.Verified);
        Assert.True(report.Verification?.Verified);
        Assert.Equal(FlowExecutionExitCategories.Pass, FlowExecutionCoordinator.ClassifyReport(report));
    }

    [Fact]
    public async Task StateEvidence_PreRunOracleEvidenceIsRejected()
    {
        var now = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var context = AdmissionContext(now);
        context.BusinessOracles.Add(new MauiIndependentBusinessOracleResult
        {
            OracleId = "stale",
            Independent = true,
            Succeeded = true,
            ObservedAt = now,
        });
        var registry = new FlowStateEvidenceProviderRegistry(
        [
            new FakeStateEvidenceProvider
            {
                PrepareResult = new FlowStateEvidenceResult { RunContext = context },
            },
        ]);

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            registry.PrepareAsync(new FlowStateEvidenceRequest
            {
                Plan = OraclePlan(),
                Flow = OracleFlow(),
                Artifact = Artifact("app.apk"),
            }));

        Assert.Equal(FlowExecutionExitCategories.InvalidConfiguration, failure.ExitCategory);
        Assert.Equal("pre-run-oracle-evidence-not-allowed", failure.Code);
    }

    [Fact]
    public async Task StateEvidence_PostRunOracleBindingMismatchFailsClosed()
    {
        var now = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var provider = new FakeStateEvidenceProvider
        {
            PrepareResult = new FlowStateEvidenceResult
            {
                RunContext = AdmissionContext(now),
            },
            Evaluate = request => new FlowPostRunOracleEvidenceResult
            {
                RunId = "different-run",
                FlowDigest = request.FlowDigest,
                DeviceIdentityFingerprint = request.DeviceIdentityFingerprint,
                AppBuildFingerprint = request.AppBuildFingerprint,
                PackageDigest = request.PackageDigest,
                StartedAt = request.StartedAt,
                EndedAt = request.EndedAt,
                ObservedAt = request.EndedAt,
            },
        };
        var registry = new FlowStateEvidenceProviderRegistry([provider]);
        var plan = OraclePlan();
        var flow = OracleFlow();
        var artifact = Artifact("app.apk");
        var admission = await registry.PrepareAsync(new FlowStateEvidenceRequest
        {
            Plan = plan,
            Flow = flow,
            Artifact = artifact,
        });

        var failure = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            registry.EvaluatePostRunAsync(admission, new FlowPostRunOracleEvaluationRequest
            {
                Plan = plan,
                Flow = flow,
                Artifact = artifact,
                RunId = "run-1",
                FlowDigest = "flow-digest",
                DeviceIdentityFingerprint = "device-fingerprint",
                AppBuildFingerprint = "build-fingerprint",
                PackageDigest = artifact.PackageDigest,
                Platform = "android",
                DeviceSerial = "emulator-5554",
                PackageId = "com.example.app",
                StartedAt = now,
                EndedAt = now.AddSeconds(5),
                EvaluationDeadline = now.AddMinutes(1),
                Report = new MauiFlowRunReport(),
            }));

        Assert.Equal(FlowExecutionExitCategories.InfrastructureFailure, failure.ExitCategory);
        Assert.Equal("post-run-oracle-binding-mismatch", failure.Code);
    }

    [Fact]
    public void PlanRequirements_UsesSharedValidatorForAliasesVersionsAndSemantics()
    {
        var plan = new MauiTestPlan
        {
            Requirements = new MauiFlowRequirements
            {
                RequiredCapabilities =
                [
                    new MauiCapabilityRequirement
                    {
                        Name = "agent.ui",
                        MinimumVersion = 1,
                    },
                ],
                RequiredSemantics =
                [
                    new MauiRequiredSemantic
                    {
                        Name = "stable-step-identity",
                        MinimumVersion = 1,
                    },
                ],
            },
        };
        var status = new AgentStatus
        {
            Capabilities = new AgentCapabilities
            {
                Ui = true,
            },
        };

        FlowExecutionCoordinator.ValidatePlanRequirements(plan, status);

        var unsupported = new MauiTestPlan
        {
            Requirements = new MauiFlowRequirements
            {
                RequiredSemantics =
                [
                    new MauiRequiredSemantic
                    {
                        Name = "stable-step-identity",
                        MinimumVersion = 2,
                    },
                ],
            },
        };
        var failure = Assert.Throws<FlowExecutionException>(() =>
            FlowExecutionCoordinator.ValidatePlanRequirements(unsupported, status));
        Assert.Equal("required-semantics-unsupported", failure.Code);
    }

    [Fact]
    public async Task FlowRunHelp_DescribesProductionPlatformOptions()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync("devflow", "flow", "run", "--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--project", result.StdOut);
        Assert.Contains("--cleanup", result.StdOut);
        Assert.Contains("supported local platform adapters", result.StdOut);
    }

    [Fact]
    public async Task FlowRunCommand_ParsesAndInvokesCoordinator()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);
        var fake = new FakeCoordinator();
        DevFlowCommands.CreateFlowExecutionCoordinator = () => fake;
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "flow", "run", "maui-tests\\checkout.md",
                "--project", "src\\App\\App.csproj",
                "--device", "emulator-5554",
                "--cleanup", "uninstall",
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("android", fake.Request?.Platform);
            Assert.Equal("emulator-5554", fake.Request?.DeviceSerial);
            Assert.Equal(FlowExecutionCleanupPolicies.Uninstall, fake.Request?.CleanupPolicy);
            Assert.Equal(FlowExecutionExitCategories.Pass, result.ParseJsonOutput().GetProperty("exitCategory").GetString());
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task FlowHandoffHelp_DescribesImportAndDiagnosticOnlyBoundaries()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);

        var reproduce = await cli.InvokeRawAsync("devflow", "flow", "reproduce", "--help");
        var triage = await cli.InvokeRawAsync("devflow", "flow", "triage", "--help");

        Assert.Equal(0, reproduce.ExitCode);
        Assert.Contains("--import", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("--output", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("--json", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("--no-json", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("--platform", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("--device", reproduce.StdOut, StringComparison.Ordinal);
        Assert.Contains("stop after trust evaluation", reproduce.StdOut, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, triage.ExitCode);
        Assert.Contains("--manifest", triage.StdOut, StringComparison.Ordinal);
        Assert.Contains("--report", triage.StdOut, StringComparison.Ordinal);
        Assert.Contains("--json", triage.StdOut, StringComparison.Ordinal);
        Assert.Contains("--no-json", triage.StdOut, StringComparison.Ordinal);
        Assert.Contains("diagnostic-only", triage.StdOut, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlowReproduceCommand_ParsesRunOptionsAndInvokesHandoffCoordinator()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);
        var fake = new FakeReproductionCoordinator();
        DevFlowCommands.CreateFlowReproductionCoordinator = () => fake;
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "flow", "reproduce", "maui-tests\\checkout.md",
                "--import", "downloaded\\flow-run.json",
                "--project", "src\\App\\App.csproj",
                "--device", "emulator-5554",
                "--cleanup", "uninstall",
                "--output", "artifacts\\reproduction",
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("downloaded\\flow-run.json", fake.Request?.ImportedArtifactPath);
            Assert.Equal("android", fake.Request?.Execution.Platform);
            Assert.Equal("emulator-5554", fake.Request?.Execution.DeviceSerial);
            Assert.Equal(FlowExecutionCleanupPolicies.Uninstall, fake.Request?.Execution.CleanupPolicy);
            Assert.True(result.ParseJsonOutput().GetProperty("matched").GetBoolean());
            Assert.False(result.ParseJsonOutput().GetProperty("approvalGranted").GetBoolean());

            var human = await cli.InvokeRawAsync(
                "devflow", "flow", "reproduce", "maui-tests\\checkout.md",
                "--import", "downloaded\\flow-run.json",
                "--project", "src\\App\\App.csproj",
                "--device", "emulator-5554",
                "--output", "artifacts\\reproduction-human",
                "--no-json");

            Assert.Equal(0, human.ExitCode);
            Assert.Contains("Failure correspondence: same-failure", human.StdOut, StringComparison.Ordinal);
            Assert.Contains("Developer lane:", human.StdOut, StringComparison.Ordinal);
            Assert.Contains("leave the worktree uncommitted", human.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task FlowTriageCommand_InvokesSharedCoordinator()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);
        var fake = new FakeTriageCoordinator();
        DevFlowCommands.CreateFlowTriageCoordinator = () => fake;
        var previousOutput = Environment.GetEnvironmentVariable("MAUIDEVFLOW_OUTPUT");
        Environment.SetEnvironmentVariable("MAUIDEVFLOW_OUTPUT", "json");
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "flow", "triage",
                "--manifest", "execution-manifest.json",
                "--report", "flow-run.json",
                "--format", "markdown",
                "--no-json");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("execution-manifest.json", fake.Request?.ManifestPath);
            Assert.Equal("flow-run.json", fake.Request?.ReportPath);
            Assert.Equal(FlowTriageOutputFormats.Markdown, fake.Request?.Format);
            Assert.Contains("# MAUI DevFlow triage", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAUIDEVFLOW_OUTPUT", previousOutput);
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task FlowTriageCommand_JsonAndMarkdownFormat_AreRejected()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);
        var fake = new FakeTriageCoordinator();
        DevFlowCommands.CreateFlowTriageCoordinator = () => fake;
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "flow", "triage",
                "--manifest", "execution-manifest.json",
                "--report", "flow-run.json",
                "--format", "markdown",
                "--json");

            Assert.Equal(1, result.ExitCode);
            Assert.Null(fake.Request);
            using var error = JsonDocument.Parse(result.StdErr);
            Assert.Equal("InvalidArgument", error.RootElement.GetProperty("type").GetString());
            Assert.Contains(
                "effective JSON output",
                error.RootElement.GetProperty("error").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    [Fact]
    public async Task FlowTriageCommand_RedirectedEffectiveJson_RequiresExplicitNoJsonForMarkdown()
    {
        var cli = new CliTestHarness(mockAgentPort: 1);
        var fake = new FakeTriageCoordinator();
        DevFlowCommands.CreateFlowTriageCoordinator = () => fake;
        var previousOutput = Environment.GetEnvironmentVariable("MAUIDEVFLOW_OUTPUT");
        Environment.SetEnvironmentVariable("MAUIDEVFLOW_OUTPUT", "json");
        try
        {
            var result = await cli.InvokeRawAsync(
                "devflow", "flow", "triage",
                "--manifest", "execution-manifest.json",
                "--report", "flow-run.json",
                "--format", "markdown");

            Assert.Equal(1, result.ExitCode);
            Assert.Null(fake.Request);
            using var error = JsonDocument.Parse(result.StdErr);
            Assert.Contains(
                "effective JSON output",
                error.RootElement.GetProperty("error").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MAUIDEVFLOW_OUTPUT", previousOutput);
            DevFlowCommands.ResetBrokerClientForTests();
        }
    }

    private static FlowExecutionCoordinator CreateCoordinator(
        IAppArtifactResolver resolver,
        IFlowExecutionPlatformAdapter adapter,
        IFlowStateEvidenceProviderRegistry? stateEvidenceProviders = null)
        => new(
            new CommittedFlowBundleLoader(),
            resolver,
            [adapter],
            stateEvidenceProviders ?? new FlowStateEvidenceProviderRegistry([]),
            new ExactAgentBindingResolver(_ => throw new InvalidOperationException("Broker must not be reached.")),
            new FlowRunReportWriter(),
            new JUnitFlowExecutionWriter(),
            new ExecutionManifestWriter(),
            new ImmutableExecutionOutputWriter(),
            () => throw new InvalidOperationException("Broker must not be started."));

    private static FlowExecutionRequest Request((string Flow, string Plan) bundle, string output)
        => new()
        {
            FlowPath = bundle.Flow,
            PlanPath = bundle.Plan,
            ProjectPath = @"C:\src\App.csproj",
            Platform = "android",
            TargetFramework = "net10.0-android",
            Configuration = "Debug",
            DeviceSerial = "emulator-5554",
            OutputDirectory = output,
        };

    private static ResolvedAppArtifact Artifact(string path) => new()
    {
        Path = path,
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
        PackageDigest = "sha256:" + new string('a', 64),
    };

    private static AgentRegistration Agent(string instanceId) => new()
    {
        Id = "agent",
        InstanceId = instanceId,
        Project = @"C:\src\App.csproj",
        Tfm = "net10.0-android",
        Platform = "Android",
        AppName = "App",
        PackageId = "com.example.app",
        SessionId = "flowsession",
        DeviceId = "platform=android;serial=emulator-5554",
        ProcessId = 321,
        Port = instanceId == "old-instance" ? 9223 : 9224,
    };

    private static ExactAgentBindingExpectation Expectation() => new()
    {
        SessionId = "flowsession",
        TargetFramework = "net10.0-android",
        Platform = "android",
        PlatformAliases = ["android"],
        PackageId = "com.example.app",
        DeviceSerial = "emulator-5554",
        ProcessId = 321,
    };

    private static MauiTestPlan OraclePlan() => new()
    {
        PlanId = "oracle-plan",
        Revision = 1,
        Flow = new MauiFlowReference
        {
            Path = "oracle-flow.md",
            Digest = "sha256:" + new string('1', 64),
        },
        Goal = "Verify the independent order-created business oracle.",
        Reset = new MauiTestResetRequirement(),
        RequiredPlatforms = ["android"],
        SideEffectPolicy = MauiFlowSideEffectPolicies.None,
        Provenance = new MauiActorProvenance
        {
            ActorKind = "human",
            Channel = "unit-test",
        },
        IndependentBusinessOracles =
        [
            new MauiIndependentBusinessOracleDeclaration
            {
                OracleId = "order-created",
                Required = true,
                Independent = true,
            },
        ],
    };

    private static MauiFlow OracleFlow() => new()
    {
        Name = "oracle-flow",
        Platform = "android",
        Steps = [],
    };

    private static MauiFlowRunContext AdmissionContext(DateTimeOffset checkedAt)
        => new()
        {
            Intent = MauiFlowReplayIntents.OrdinaryReplay,
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = new MauiFlowCheckpoint(),
                Observed = new MauiFlowCheckpoint(),
                CheckedAt = checkedAt,
            },
            PriorMutationCompletionCertain = true,
            BusinessOracles = [],
        };

    private static ProcessResult Success(string stdout = "") => new()
    {
        ExitCode = 0,
        StandardOutput = stdout,
    };

    private static ProcessResult SingleUser()
        => Success("Users:\n\tUserInfo{0:Owner:13} running\n");

    private static string ArtifactMetadata(
        string artifactPath,
        string projectPath,
        string runtimeIdentifier,
        string signingState = AppArtifactSigningStates.Signed)
        => string.Join(
            "|||DEVFLOW_ARTIFACT|||",
            artifactPath,
            "App",
            projectPath,
            "net10.0-android",
            "android",
            runtimeIdentifier,
            "Debug",
            "com.example.app",
            "apk",
            "1",
            "deployable",
            "android",
            "package",
            "android-package-name",
            "com.example.app",
            "true",
            "true",
            signingState);

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

    private static bool TryCreateDirectorySymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
                UnauthorizedAccessException or
                PlatformNotSupportedException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Coordinator_DeviceAdmissionRefusal_HappensBeforeTheAppBuild()
    {
        // Regression: `android-preexisting-app-unsafe` used to fire during platform-launch, after
        // a multi-minute build and artifact resolution. The refusal is correct policy, but the
        // package the flow drives is declared in the committed flow itself, so the answer is
        // available before any expensive stage runs.
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var resolver = new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk")));
        var adapter = new FakePlatformAdapter { RefuseAdmissionFor = "App" };
        var coordinator = CreateCoordinator(resolver, adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal("android-preexisting-app-unsafe", result.Report?.Failure?.Code);
        Assert.Equal(1, adapter.AdmissionCalls);
        Assert.Equal("App", adapter.AdmissionAppId);
        // The whole point: nothing expensive ran.
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, adapter.MutationCalls);
        var stages = result.Manifest?.Lifecycle?.Stages ?? [];
        var admission = Assert.Single(stages, stage => stage.Name == "device-admission");
        Assert.Equal("failed", admission.Status);
        Assert.DoesNotContain(stages, stage => stage.Name == "resolve-artifact");
    }

    [Fact]
    public async Task Coordinator_WithoutInjectedSessionFactory_PassesTheSameSessionIdToEveryBuild()
    {
        // Regression: the session id used to be "flow" + Guid.NewGuid(), which is compiled into the
        // app by Microsoft.Maui.DevFlow.Agent.targets and so gave every run a different app binary.
        // Every other coordinator test injects a fixed factory, so only this one fails if the
        // per-invocation default comes back.
        using var first = new ExecutionTestWorkspace();
        using var second = new ExecutionTestWorkspace();
        var firstResolver = new FakeArtifactResolver(Artifact(Path.Combine(first.Root, "app.apk")));
        var secondResolver = new FakeArtifactResolver(Artifact(Path.Combine(second.Root, "app.apk")));

        var firstRequest = Request(first.WriteBundle(MauiFlowSideEffectPolicies.None), first.Output);
        var secondRequest = Request(second.WriteBundle(MauiFlowSideEffectPolicies.None), second.Output);

        await CreateCoordinator(firstResolver, new FakePlatformAdapter()).RunAsync(firstRequest);
        await CreateCoordinator(secondResolver, new FakePlatformAdapter()).RunAsync(secondRequest);

        Assert.Equal(1, firstResolver.Calls);
        Assert.Equal(1, secondResolver.Calls);
        Assert.Equal(firstResolver.ObservedAgentSessionId, secondResolver.ObservedAgentSessionId);
        Assert.Equal(
            FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(firstRequest),
            firstResolver.ObservedAgentSessionId);
    }

    [Fact]
    public async Task Coordinator_DeviceAdmissionAccepted_StillRunsTheRestOfThePipeline()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var resolver = new FakeArtifactResolver(Artifact(Path.Combine(workspace.Root, "app.apk")));
        var adapter = new FakePlatformAdapter();
        var coordinator = CreateCoordinator(resolver, adapter);

        var result = await coordinator.RunAsync(Request(bundle, workspace.Output));

        Assert.Equal(1, adapter.AdmissionCalls);
        Assert.Equal(1, resolver.Calls);
        var stages = result.Manifest?.Lifecycle?.Stages ?? [];
        var admissionIndex = stages.FindIndex(stage => stage.Name == "device-admission");
        var resolveIndex = stages.FindIndex(stage => stage.Name == "resolve-artifact");
        Assert.True(admissionIndex >= 0, "device-admission must be recorded in the lifecycle");
        Assert.True(resolveIndex > admissionIndex, "device-admission must precede resolve-artifact");
    }

    [Fact]
    public async Task CommittedBundleLoader_StaleFlowDigest_NamesTheSupportedRebindingVerb()
    {
        // Regression: the refusal told the author to "commit the matching flow and plan again"
        // while no verb existed to do so, which forced reflection into the shipping assembly.
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Name = "changed-after-plan-commit";
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        var exception = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new CommittedFlowBundleLoader().LoadAsync(bundle.Flow, bundle.Plan));

        Assert.Equal("plan-flow-digest-stale", exception.Code);
        Assert.Contains("maui devflow flow commit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlowCommit_StaleSidecar_RebindsAndMakesTheBundleRunnableAgain()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        // Before: the edit is refused, which is the safety property and must not change.
        var stale = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            new CommittedFlowBundleLoader().LoadAsync(bundle.Flow, bundle.Plan));
        Assert.Equal("plan-flow-digest-stale", stale.Code);

        var check = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: true);
        Assert.False(check.Ok);
        Assert.False(check.Changed);

        var committed = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);
        Assert.True(committed.Ok);
        Assert.True(committed.Changed);
        Assert.NotEqual(committed.PreviousDigest, committed.Digest);

        // After: the author's own edit is runnable again, without reflection.
        var loaded = await new CommittedFlowBundleLoader().LoadAsync(bundle.Flow, bundle.Plan);
        Assert.Equal(committed.Digest, loaded.FlowDigest);

        var recheck = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: true);
        Assert.True(recheck.Ok);
        Assert.False(recheck.Changed);
    }

    [Fact]
    public async Task FlowCommit_CurrentSidecar_IsAnUnchangedNoOp()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var before = await File.ReadAllTextAsync(bundle.Plan);

        var result = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        Assert.True(result.Ok);
        Assert.False(result.Changed);
        Assert.Equal(before, await File.ReadAllTextAsync(bundle.Plan));
    }

    [Fact]
    public async Task FlowCommit_PreservesEveryOtherPlanField()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var before = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        var after = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        foreach (var (key, value) in before)
        {
            if (key == "flow")
                continue;
            Assert.Equal(value?.ToJsonString(), after[key]?.ToJsonString());
        }
    }

    [Fact]
    public async Task FlowCommit_MissingSidecar_RefusesInsteadOfAuthoringOne()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        File.Delete(bundle.Plan);

        var failure = await Assert.ThrowsAsync<FlowCommitException>(() =>
            FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false));

        Assert.Equal("plan-missing", failure.Code);
    }

    [Fact]
    public async Task FlowCommit_UnparsableFlow_IsRefusedBeforeAnyWrite()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var before = await File.ReadAllTextAsync(bundle.Plan);
        await File.WriteAllTextAsync(bundle.Flow, "# not a flow\n\nno maui-test block here\n");

        var failure = await Assert.ThrowsAsync<FlowCommitException>(() =>
            FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false));

        Assert.Equal("flow-invalid", failure.Code);
        Assert.Equal(before, await File.ReadAllTextAsync(bundle.Plan));
    }

    [Fact]
    public async Task FlowCommit_UnrelatedPlanPath_IsRefused()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var elsewhere = Path.Combine(workspace.Root, "someone-elses.maui-plan.json");
        File.Copy(bundle.Plan, elsewhere);

        var failure = await Assert.ThrowsAsync<FlowCommitException>(() =>
            FlowCommitCommands.ExecuteAsync(bundle.Flow, elsewhere, checkOnly: false));

        Assert.Equal("plan-sidecar-mismatch", failure.Code);
    }

    [Fact]
    public async Task FlowCommit_DropsApprovalsBoundToTheSupersededFlow()
    {
        // Re-blessing must never carry a review of the old bytes onto the new ones.
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        var staleDigest = (string?)plan["flow"]?["digest"];
        plan["approvals"] = new JsonArray(
            new JsonObject
            {
                ["approvedBy"] = "reviewer",
                ["approvedAt"] = "2026-08-15T20:00:00.0000000+00:00",
                ["digest"] = staleDigest,
            });
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());

        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        var result = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        Assert.True(result.Ok);
        Assert.Equal(1, result.RemovedApprovals);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        Assert.Empty(after["approvals"]?.AsArray() ?? []);
    }

    /// <summary>
    /// An approval that names no bytes cannot vouch for the new bytes either. Retaining it would
    /// leave the sidecar asserting a review of content nobody looked at, which is the exact failure
    /// the digest binding exists to prevent.
    /// </summary>
    [Fact]
    public async Task FlowCommit_ApprovalWithNoBoundDigest_IsAlsoDropped()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        plan["approvals"] = new JsonArray(
            new JsonObject
            {
                ["approvedBy"] = "reviewer",
                ["approvedAt"] = "2026-08-15T20:00:00.0000000+00:00",
            });
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());

        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        var result = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        Assert.Equal(1, result.RemovedApprovals);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        Assert.Empty(after["approvals"]?.AsArray() ?? []);
    }

    /// <summary>
    /// A sidecar is operator-authored text, so a wrongly typed member is an input to report, not a
    /// crash. An unhandled cast here would print a stack trace with absolute source paths and break
    /// the <c>--json</c> contract.
    /// </summary>
    [Theory]
    [InlineData("digest", "12345")]
    [InlineData("revision", "\"two\"")]
    [InlineData("path", "17")]
    public async Task FlowCommit_SidecarMemberOfTheWrongJsonType_IsReportedNotThrownRaw(
        string member,
        string rawJson)
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        plan["flow"]!.AsObject()[member] = JsonNode.Parse(rawJson);
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());

        // Neither call may escape as a raw cast failure: that would print a stack trace with
        // absolute source paths and break the --json contract for the caller.
        await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: true);
        var committed = await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        Assert.True(committed.Ok);
        var after = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        Assert.Equal(committed.Digest, (string?)after["flow"]!["digest"]);
    }

    /// <summary>
    /// Validation runs against the candidate bytes before they replace the operator's file, so a
    /// rejected re-bind leaves the sidecar exactly as it was rather than destroying plan content
    /// while reporting failure.
    /// </summary>
    [Fact]
    public async Task FlowCommit_WhenTheRewrittenSidecarIsInvalid_LeavesTheOriginalOnDisk()
    {
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        plan["sideEffectPolicy"] = "not-a-policy";
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());
        var before = await File.ReadAllTextAsync(bundle.Plan);

        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        var failure = await Assert.ThrowsAsync<FlowCommitException>(() =>
            FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false));

        Assert.Equal("plan-invalid", failure.Code);
        Assert.Equal(before, await File.ReadAllTextAsync(bundle.Plan));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(bundle.Plan)!, "*.tmp-*"));
    }

    /// <summary>
    /// Non-ASCII plan prose must survive a re-bind as itself rather than as escape sequences, so
    /// re-blessing a flow does not churn unrelated lines of the operator's sidecar.
    /// </summary>
    [Fact]
    public async Task FlowCommit_PreservesNonAsciiPlanTextWithoutEscaping()
    {
        const string goal = "Vérifier l'écran <Interactions> & le bouton";
        using var workspace = new ExecutionTestWorkspace();
        var bundle = workspace.WriteBundle(MauiFlowSideEffectPolicies.None);
        var plan = JsonNode.Parse(await File.ReadAllTextAsync(bundle.Plan))!.AsObject();
        plan["goal"] = goal;
        await File.WriteAllTextAsync(bundle.Plan, plan.ToJsonString());

        var parsed = FlowMarkdown.Parse(await File.ReadAllTextAsync(bundle.Flow)).Flow!;
        parsed.Steps[0].Target = new FlowSelector { AutomationId = "RenamedByTheAuthor" };
        await File.WriteAllTextAsync(bundle.Flow, FlowMarkdown.Serialize(parsed));

        await FlowCommitCommands.ExecuteAsync(bundle.Flow, bundle.Plan, checkOnly: false);

        Assert.Contains(goal, await File.ReadAllTextAsync(bundle.Plan), StringComparison.Ordinal);
    }

    private sealed class FakeArtifactResolver(ResolvedAppArtifact artifact) : IAppArtifactResolver
    {
        public int Calls { get; private set; }

        public string? ObservedAgentSessionId { get; private set; }

        public Task<ResolvedAppArtifact> ResolveAsync(
            AppArtifactResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ObservedAgentSessionId = request.AgentSessionId;
            return Task.FromResult(artifact with { AgentSessionId = request.AgentSessionId });
        }
    }

    private sealed class ThrowingArtifactResolver(FlowExecutionException failure) : IAppArtifactResolver
    {
        public Task<ResolvedAppArtifact> ResolveAsync(
            AppArtifactResolutionRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromException<ResolvedAppArtifact>(failure);
    }

    private sealed class BuildLogFailingArtifactResolver(bool persistLog = true) : IAppArtifactResolver
    {
        public async Task<ResolvedAppArtifact> ResolveAsync(
            AppArtifactResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            var logPath = Path.Combine(request.WorkDirectory, "app-build.log");
            var content = "# DevFlow app build output (redacted)\nerror NETSDK1005: assets file mismatch.\n";
            if (persistLog)
                await File.WriteAllTextAsync(logPath, content, cancellationToken);
            throw new FlowExecutionException(
                FlowExecutionExitCategories.InfrastructureFailure,
                "app-build-failed",
                "MSBuild could not resolve the app artifact (exit code 1). Full build output: app-build.log.")
            {
                DiagnosticsArtifact = new FlowExecutionDiagnosticsArtifact
                {
                    FileName = "app-build.log",
                    Digest = "sha256:" + new string('a', 64),
                    SizeBytes = content.Length,
                },
            };
        }
    }

    private sealed class FakeStateEvidenceProvider : IFlowStateEvidenceProvider
    {
        public string ProviderId => "fake-state-evidence";
        public FlowStateEvidenceResult PrepareResult { get; init; } = new()
        {
            RunContext = AdmissionContext(DateTimeOffset.UtcNow),
        };
        public Func<FlowPostRunOracleEvaluationRequest, FlowPostRunOracleEvidenceResult>? Evaluate { get; init; }
        public int PrepareCalls { get; private set; }
        public int EvaluateCalls { get; private set; }

        public bool Supports(FlowStateEvidenceRequest request) => true;

        public Task<FlowStateEvidenceResult> PrepareAsync(
            FlowStateEvidenceRequest request,
            CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(PrepareResult);
        }

        public Task<FlowPostRunOracleEvidenceResult> EvaluatePostRunAsync(
            FlowPostRunOracleEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            EvaluateCalls++;
            return Task.FromResult(
                Evaluate?.Invoke(request) ??
                new FlowPostRunOracleEvidenceResult
                {
                    RunId = request.RunId,
                    FlowDigest = request.FlowDigest,
                    DeviceIdentityFingerprint = request.DeviceIdentityFingerprint,
                    AppBuildFingerprint = request.AppBuildFingerprint,
                    PackageDigest = request.PackageDigest,
                    StartedAt = request.StartedAt,
                    EndedAt = request.EndedAt,
                    ObservedAt = request.EndedAt,
                });
        }
    }

    private sealed class FakePlatformAdapter(
        bool validateWithAndroidRules = false,
        bool allowMutation = false,
        bool cleanupSucceeds = true) : IFlowExecutionPlatformAdapter
    {
        public int MutationCalls { get; private set; }
        public int CleanupCalls { get; private set; }
        public int AdmissionCalls { get; private set; }
        public int AppProbeCalls { get; private set; }
        public MauiFlowAppProcessEvidence? AppProcessEvidence { get; set; }

        /// <summary>Raised from platform preflight, which runs after the owned build root exists.</summary>
        public FlowExecutionException? PreflightFailure { get; init; }

        /// <summary>Overrides the detail code a failed cleanup reports.</summary>
        public string? CleanupDetailCode { get; init; }

        /// <summary>Raised from agent forwarding, which runs after the owned session exists.</summary>
        public FlowExecutionException? ForwardingFailure { get; init; }

        /// <summary>Cancelled from agent forwarding, so the run ends unknown with a live session.</summary>
        public CancellationTokenSource? CancelAtForwarding { get; init; }

        public string? AdmissionAppId { get; private set; }
        public string? RefuseAdmissionFor { get; set; }
        public FlowExecutionPlatformDescriptor Descriptor { get; } = new()
        {
            Platform = "android",
            DisplayName = "Android",
            CommandAliases = ["android"],
            FlowPlatformAliases = ["android"],
            AgentPlatformAliases = ["android"],
            TargetFrameworkPlatformIdentifiers = ["android"],
            CandidateArtifactTypes = ["apk", "aab"],
        };

        public void ValidateHost()
        {
        }

        public string? GetDefaultRuntimeIdentifier() => null;

        public Task ValidateDeviceAdmissionAsync(
            FlowExecutionDeviceAdmissionRequest request,
            CancellationToken cancellationToken = default)
        {
            AdmissionCalls++;
            AdmissionAppId = request.DeclaredAppId;
            if (RefuseAdmissionFor is { } refused &&
                string.Equals(refused, request.DeclaredAppId, StringComparison.Ordinal))
            {
                throw FlowExecutionException.Unsupported(
                    "android-preexisting-app-unsafe",
                    "The exact Android device already contains the app.");
            }
            return Task.CompletedTask;
        }

        public Task<FlowExecutionPlatformPreflight> PreflightAsync(
            FlowExecutionPlatformPreflightRequest request,
            CancellationToken cancellationToken = default)
        {
            if (validateWithAndroidRules)
                AndroidFlowExecutionAdapter.ValidateAndroidArtifact(request.Artifact);
            if (PreflightFailure is { } failure)
                throw failure;
            return Task.FromResult(new FlowExecutionPlatformPreflight
            {
                Device = CreateDevice(),
                DeviceSerial = "emulator-5554",
                PackageId = "com.example.app",
            });
        }

        public Task<FlowExecutionPlatformSession> PrepareAndLaunchAsync(
            FlowExecutionPlatformRequest request,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            if (!allowMutation)
                throw new InvalidOperationException("Mutation should not be reached.");
            return Task.FromResult(new FlowExecutionPlatformSession
            {
                Device = request.Preflight.Device,
                DeviceSerial = "emulator-5554",
                PackageId = "com.example.app",
                Platform = "android",
                RuntimeKind = "emulator",
                DeviceProfile = "emulator",
                ProcessId = 321,
                InstalledByInvocation = true,
                LaunchedByInvocation = true,
                State = new object(),
            });
        }

        public Task<MauiFlowAppProcessEvidence?> ProbeAppProcessAsync(
            FlowExecutionAppProbeRequest request,
            CancellationToken cancellationToken = default)
        {
            AppProbeCalls++;
            return Task.FromResult(AppProcessEvidence);
        }

        public Task EstablishAgentForwardingAsync(
            FlowExecutionPlatformSession session,
            int agentPort,
            int brokerPort,
            CancellationToken cancellationToken = default)
        {
            MutationCalls++;
            if (!allowMutation)
                throw new InvalidOperationException("Mutation should not be reached.");
            if (CancelAtForwarding is { } cancellation)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (ForwardingFailure is { } failure)
                throw failure;
            return Task.CompletedTask;
        }

        public Task<FlowExecutionCleanupResult> CleanupAsync(
            FlowExecutionPlatformSession session,
            string cleanupPolicy,
            CancellationToken cancellationToken = default)
        {
            CleanupCalls++;
            return Task.FromResult(new FlowExecutionCleanupResult
            {
                Succeeded = cleanupSucceeds,
                PackageStopped = cleanupSucceeds,
                DetailCode = CleanupDetailCode ??
                    (cleanupSucceeds ? "cleanup-complete" : "fake-cleanup-failed"),
            });
        }

        private static Device CreateDevice() => new()
        {
            Id = "emulator-5554",
            Name = "Pixel",
            Platforms = ["android"],
            IsEmulator = true,
            IsRunning = true,
            State = DeviceState.Booted,
            Type = DeviceType.Emulator,
            Architecture = "x64",
            Version = "35",
            Idiom = DeviceIdiom.Phone,
        };
    }

    private sealed class QueueProcessRunner(params ProcessResult[] results) : IExecutionProcessRunner
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
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : Success());
        }
    }

    private sealed class CallbackProcessRunner(
            Func<(string FileName, string[] Arguments), Task<ProcessResult>> callback)
        : IExecutionProcessRunner
    {
        public List<(string FileName, string[] Arguments)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            string? workingDirectory = null,
            TimeSpan? timeout = null,
            IEnumerable<string>? environmentVariablesToRemove = null,
            CancellationToken cancellationToken = default)
        {
            var call = (fileName, arguments.ToArray());
            Calls.Add(call);
            return callback(call);
        }
    }

    private sealed class FakeCoordinator : IFlowExecutionCoordinator
    {
        public FlowExecutionRequest? Request { get; private set; }

        public Task<FlowExecutionResult> RunAsync(
            FlowExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new FlowExecutionResult
            {
                ExitCategory = FlowExecutionExitCategories.Pass,
                Message = "passed",
                OutputDirectory = @"C:\artifacts\run",
                ManifestPath = @"C:\artifacts\run\execution-manifest.json",
                ReportPath = @"C:\artifacts\run\flow-run.json",
                JUnitPath = @"C:\artifacts\run\report.junit.xml",
            });
        }
    }

    private sealed class FakeReproductionCoordinator : IFlowReproductionCoordinator
    {
        public FlowReproductionRequest? Request { get; private set; }

        public Task<FlowReproductionResult> ReproduceAsync(
            FlowReproductionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            var report = MauiLocalReproductionReportSerializer.CreateSafeProjection(
                new MauiLocalReproductionReport
                {
                    ImportedArtifact = MauiImportedArtifactIdentity.Create(),
                    ImportedArtifactKind = "flow-run",
                    ImportedArtifactDigest = new string('a', 64),
                    LocalRunId = "run-local",
                    LocalExitCategory = FlowExecutionExitCategories.TestFailure,
                    LocalManifestDigest = new string('b', 64),
                    LocalReportDigest = new string('c', 64),
                    Matched = true,
                    TrustState = MauiArtifactTrustStates.LocallyReproduced,
                    ReasonCodes = ["locally-reproduced"],
                });
            return Task.FromResult(new FlowReproductionResult
            {
                LocalExecution = new FlowExecutionResult
                {
                    ExitCategory = FlowExecutionExitCategories.TestFailure,
                },
                Report = report,
                ReportPath = "local-reproduction.json",
            });
        }
    }

    private sealed class FakeTriageCoordinator : IFlowTriageCoordinator
    {
        public FlowTriageRequest? Request { get; private set; }

        public Task<FlowTriageResult> AnalyzeAsync(
            FlowTriageRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new FlowTriageResult
            {
                Triage = new MauiFlowTriage
                {
                    ImportedEvidence = true,
                    RepairEligible = false,
                },
                Content = Encoding.UTF8.GetBytes("# MAUI DevFlow triage\n"),
            });
        }
    }

    private sealed class FakeAppSourceIdentityProvider : IAppSourceIdentityProvider
    {
        public Task<AppSourceIdentity> ResolveAsync(
            string projectPath,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AppSourceIdentity
            {
                SourceRevision = "0123456789abcdef0123456789abcdef01234567",
                AppSourceFingerprint = "source-current",
            });
    }

    private sealed class ExecutionTestWorkspace : IDisposable
    {
        public ExecutionTestWorkspace()
        {
            Root = Path.Combine(
                FindRepositoryRoot(),
                "artifacts",
                "TestResults",
                "flow-execution-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Output);
        }

        public string Root { get; }
        public string Output => Path.Combine(Root, "output");

        public (string Flow, string Plan) WriteBundle(
            string policy,
            string expectedRoute = "//checkout",
            List<FlowExpectedEvidence>? expectedEvidence = null,
            string? independentOracleId = null)
        {
            const string flowName = "checkout.md";
            var flow = new MauiFlow
            {
                Name = "checkout",
                App = "App",
                Platform = "android",
                ExpectedEvidence = expectedEvidence,
                Steps =
                [
                    new FlowStep
                    {
                        Seq = 1,
                        StepId = "open-checkout",
                        Action = FlowActions.Assert,
                        Asserts =
                        [
                            new FlowAssert
                            {
                                Kind = "routeIs",
                                Expected = expectedRoute,
                                Verify = true,
                            },
                        ],
                    },
                ],
            };
            var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
            var plan = new MauiTestPlan
            {
                PlanId = "plan-checkout",
                Revision = 1,
                Flow = new MauiFlowReference
                {
                    Path = flowName,
                    Digest = digest,
                },
                Goal = "Verify checkout.",
                Reset = new MauiTestResetRequirement
                {
                    Required = policy != MauiFlowSideEffectPolicies.None,
                    SeedFingerprint = policy == MauiFlowSideEffectPolicies.TestTenantResettable ? "seed" : null,
                    BackendStateFingerprint = policy == MauiFlowSideEffectPolicies.TestTenantResettable ? "backend" : null,
                },
                RequiredPlatforms = ["android"],
                SideEffectPolicy = policy,
                Provenance = new MauiActorProvenance
                {
                    ActorKind = "human",
                    Channel = "unit-test",
                },
                IndependentBusinessOracles = independentOracleId is null
                    ? []
                    :
                    [
                        new MauiIndependentBusinessOracleDeclaration
                        {
                            OracleId = independentOracleId,
                            Required = true,
                            Independent = true,
                        },
                    ],
            };
            var flowPath = Path.Combine(Root, flowName);
            var planPath = Path.Combine(Root, "checkout.maui-plan.json");
            File.WriteAllText(flowPath, FlowMarkdown.Serialize(flow));
            File.WriteAllText(
                planPath,
                JsonSerializer.Serialize(plan, MauiTestingJsonContext.Default.MauiTestPlan));
            return (flowPath, planPath);
        }

        public FakeAndroidProvider CreateAndroidProvider()
        {
            var sdk = Path.Combine(Root, "android-sdk");
            var platformTools = Path.Combine(sdk, "platform-tools");
            Directory.CreateDirectory(platformTools);
            File.WriteAllText(Path.Combine(platformTools, OperatingSystem.IsWindows() ? "adb.exe" : "adb"), "");
            return new FakeAndroidProvider { SdkPath = sdk };
        }

        public string WriteApk()
        {
            var path = Path.Combine(Root, "app.apk");
            File.WriteAllBytes(path, [1, 2, 3]);
            return path;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(Environment.CurrentDirectory);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "MauiLabs.slnx")))
                    return current.FullName;
                current = current.Parent;
            }
            throw new InvalidOperationException("Repository root not found.");
        }
    }
}
