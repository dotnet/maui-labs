using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using YamlDotNet.RepresentationModel;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public sealed class FlowQaScriptTests
{
    static readonly string RepositoryRoot = AppFixtureBase.FindRepoRoot();

    [Theory]
    [InlineData("android", "Category=FlowPilot")]
    [InlineData("windows", "Category=WindowsFlowQa")]
    [InlineData("ios", "Category=AppleTestAgent")]
    [InlineData("maccatalyst", "Category=AppleTestAgent")]
    public void PowerShellDryRun_UsesPlatformFilterAndSafeArtifactRoots(string platform, string expectedFilter)
    {
        var result = RunPowerShell(
            "--platform", platform,
            "--repeat", "2",
            "--results-root", ResultsRoot(platform),
            "--no-build",
            "--dry-run");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("dry-run", root.GetProperty("status").GetString());
        Assert.Equal(platform, root.GetProperty("platform").GetString());
        Assert.Equal(2, root.GetProperty("repeat").GetInt32());
        Assert.Equal(expectedFilter, root.GetProperty("testFilter").GetString());
        Assert.True(root.GetProperty("noBuild").GetBoolean());
        Assert.Equal(
            $"artifacts/TestResults/devflow-flow/{platform}",
            root.GetProperty("artifactPaths").GetProperty("testResults").GetString());

        var arguments = root.GetProperty("command").GetProperty("arguments")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("--no-build", arguments);
        Assert.Contains(expectedFilter, arguments);
    }

    [Fact]
    public void PowerShellDryRun_RejectsUnsafeOutputAndExperimentalGate()
    {
        var unsafeRoot = RunPowerShell(
            "--platform", "android",
            "--results-root", ".",
            "--dry-run");
        Assert.Equal(2, unsafeRoot.ExitCode);
        Assert.Contains("--results-root", unsafeRoot.StandardError, StringComparison.Ordinal);

        var missingExperimental = RunPowerShell(
            "--platform", "macos",
            "--results-root", ResultsRoot("macos"),
            "--dry-run");
        Assert.Equal(2, missingExperimental.ExitCode);
        Assert.Contains("--experimental", missingExperimental.StandardError, StringComparison.Ordinal);

        var experimental = RunPowerShell(
            "--platform", "macos",
            "--experimental",
            "--results-root", ResultsRoot("macos"),
            "--dry-run");
        Assert.Equal(0, experimental.ExitCode);
        using var document = JsonDocument.Parse(experimental.StandardOutput);
        Assert.Equal(
            "samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj",
            document.RootElement.GetProperty("appProject").GetString());
        Assert.Equal("Category=AppKitFlowQa", document.RootElement.GetProperty("testFilter").GetString());
        Assert.Equal("appkit", document.RootElement.GetProperty("backend").GetString());
        Assert.False(document.RootElement.GetProperty("officialCoverage").GetBoolean());
        Assert.False(document.RootElement.GetProperty("macCatalystEquivalent").GetBoolean());

        var qualification = RunPowerShell(
            "--platform", "macos",
            "--experimental",
            "--qualification",
            "--results-root", ResultsRoot("macos"),
            "--dry-run");
        Assert.Equal(2, qualification.ExitCode);
        Assert.Contains("--qualification", qualification.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellDryRun_PhysicalIosRequiresInputsAndRedactsThem()
    {
        var missingInputs = RunPowerShell(
            "--platform", "ios",
            "--physical-device",
            "--results-root", ResultsRoot("ios"),
            "--dry-run");
        Assert.Equal(2, missingInputs.ExitCode);
        Assert.Contains("--device-id", missingInputs.StandardError, StringComparison.Ordinal);

        const string signingIdentity = "FLOW-QA-SIGNING-IDENTITY-SENTINEL";
        const string provisioningProfile = "FLOW-QA-PROFILE-SENTINEL";
        const string keychain = "FLOW-QA-KEYCHAIN-SENTINEL";
        var physical = RunPowerShell(
            "--platform", "ios",
            "--physical-device",
            "--device-id", "FLOW-QA-DEVICE-SENTINEL",
            "--signing-identity", signingIdentity,
            "--provisioning-profile", provisioningProfile,
            "--keychain", keychain,
            "--results-root", ResultsRoot("ios"),
            "--dry-run");

        Assert.Equal(0, physical.ExitCode);
        Assert.DoesNotContain(signingIdentity, physical.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(provisioningProfile, physical.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(keychain, physical.StandardOutput, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(physical.StandardOutput);
        Assert.True(document.RootElement.GetProperty("physicalDevice").GetBoolean());
        Assert.True(document.RootElement.GetProperty("signingInputsConfigured").GetBoolean());
    }

    [Fact]
    public void PowerShellDryRun_ReportsQualificationIntentWithoutClaimingSuccess()
    {
        var result = RunPowerShell(
            "--platform", "android",
            "--qualification",
            "--results-root", ResultsRoot("android"),
            "--dry-run");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("dry-run", document.RootElement.GetProperty("status").GetString());
        Assert.True(document.RootElement.GetProperty("qualificationRequested").GetBoolean());
    }

    [Fact]
    public void PowerShellWindowsDryRun_UsesFixtureOwnedCleanRepetitions()
    {
        var result = RunPowerShell(
            "--platform", "windows",
            "--repeat", "3",
            "--results-root", ResultsRoot("windows"),
            "--dry-run");

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("Category=WindowsFlowQa", document.RootElement.GetProperty("testFilter").GetString());

        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "eng",
            "devflow",
            "Run-DevFlowFlowQa.ps1"));
        Assert.Contains("DEVFLOW_RUN_WINDOWS_FLOW_QA", script, StringComparison.Ordinal);
        Assert.Contains("windows-fixture-per-flow-clean-attempts", script, StringComparison.Ordinal);
        Assert.Contains("cleanRepetitionsPerFlow", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Measure-Object -Character -Sum", script, StringComparison.Ordinal);
        Assert.DoesNotContain("adb kill-server", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb start-server", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PowerShellWindowsSessionPreflight_DisconnectedSessionFailsClosedBeforeTestProcessOrReplay()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var runId = $"flow-qa-windows-session-{Guid.NewGuid():N}";
        var artifactRunRoot = Path.Combine(RepositoryRoot, "artifacts", "devflow", runId, "windows");
        try
        {
            var result = RunPowerShell(
                new Dictionary<string, string?>
                {
                    ["DEVFLOW_FLOW_QA_RUN_ID"] = runId,
                    ["DEVFLOW_WINDOWS_SESSION_PREFLIGHT_TEST_STATE"] = "disconnected",
                },
                "--platform", "windows",
                "--results-root", ResultsRoot("windows"));

            Assert.Equal(3, result.ExitCode);
            Assert.Contains("active, unlocked desktop session", result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(
                RepositoryRoot,
                "artifacts",
                "TestResults",
                "devflow-flow",
                "windows",
                $"devflow-flow-windows-{runId}.trx")));
            Assert.Empty(Directory.GetFiles(
                artifactRunRoot,
                "test-output-*.txt",
                SearchOption.AllDirectories));

            var sessionPath = Path.Combine(
                artifactRunRoot,
                "host-diagnostics",
                "windows-session.json");
            Assert.True(File.Exists(sessionPath));
            using var sessionDocument = JsonDocument.Parse(File.ReadAllText(sessionPath));
            var session = sessionDocument.RootElement;
            Assert.Equal("disconnected", session.GetProperty("wtsConnectionState").GetString());
            Assert.Equal("rejected", session.GetProperty("admissionResult").GetString());
            Assert.Equal("wts-connection-state-disconnected", session.GetProperty("reason").GetString());
            Assert.DoesNotContain("quser", File.ReadAllText(sessionPath), StringComparison.OrdinalIgnoreCase);

            var manifestPath = Path.Combine(artifactRunRoot, "manifest.json");
            Assert.True(File.Exists(manifestPath));
            using var manifestDocument = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var hostQa = manifestDocument.RootElement.GetProperty("hostQa");
            var firstAttempt = hostQa.GetProperty("firstAttempt");
            Assert.Equal("infrastructure-error", firstAttempt.GetProperty("outcome").GetString());
            Assert.False(firstAttempt.GetProperty("mutationDispatched").GetBoolean());
        }
        finally
        {
            var runDirectory = Path.Combine(RepositoryRoot, "artifacts", "devflow", runId);
            if (Directory.Exists(runDirectory))
                Directory.Delete(runDirectory, recursive: true);
        }
    }

    [Fact]
    public void PowerShellPreflight_WritesManifestAndSeparatesPendingFromHostPrerequisites()
    {
        var runId = $"flow-qa-script-test-{Guid.NewGuid():N}";
        var artifactRunRoot = Path.Combine(RepositoryRoot, "artifacts", "devflow", runId);
        try
        {
            var result = RunPowerShell(
                new Dictionary<string, string?> { ["DEVFLOW_FLOW_QA_RUN_ID"] = runId },
                "--platform", "ios",
                "--flow-filter", "FLOW-QA-FILTER-SENTINEL",
                "--results-root", ResultsRoot("ios"));

            var expectedExitCode = OperatingSystem.IsMacOS() ? 5 : 3;
            var expectedStatus = OperatingSystem.IsMacOS() ? "pending-spike" : "failed";
            var expectedClassification = OperatingSystem.IsMacOS() ? "capability-missing" : "prerequisite-missing";
            Assert.Equal(expectedExitCode, result.ExitCode);

            var manifestPath = Path.Combine(artifactRunRoot, "ios", "manifest.json");
            var flowRunPath = Path.Combine(artifactRunRoot, "ios", "flow-run.json");
            Assert.True(File.Exists(manifestPath));
            Assert.True(File.Exists(flowRunPath));
            var manifestText = File.ReadAllText(manifestPath);
            Assert.DoesNotContain("FLOW-QA-FILTER-SENTINEL", manifestText, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(manifestText);
            var hostQa = document.RootElement.GetProperty("hostQa");
            Assert.Equal(expectedStatus, hostQa.GetProperty("status").GetString());
            Assert.Equal(expectedClassification, hostQa.GetProperty("classification").GetString());
            Assert.Equal(JsonValueKind.Null, hostQa.GetProperty("firstAttempt").ValueKind);
            Assert.Contains(
                "diagnostic-rerun",
                document.RootElement.GetProperty("omissions").EnumerateArray()
                    .Select(static item => item.GetProperty("kind").GetString()));
            Assert.Contains(
                "package-digest",
                document.RootElement.GetProperty("omissions").EnumerateArray()
                    .Select(static item => item.GetProperty("kind").GetString()));

            var artifacts = document.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
            // The manifest is written after the digests are taken, so listing it would publish a
            // hash that never matches the file finally on disk.
            Assert.DoesNotContain(
                artifacts,
                artifact => artifact.GetProperty("path").GetString()!
                    .EndsWith("/manifest.json", StringComparison.Ordinal));
            var flowRunEntry = Assert.Single(
                artifacts,
                artifact => artifact.GetProperty("path").GetString()!
                    .EndsWith("/flow-run.json", StringComparison.Ordinal));
            Assert.Equal(
                "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(flowRunPath)))
                    .ToLowerInvariant(),
                flowRunEntry.GetProperty("sha256").GetString());

            var omissionKeys = document.RootElement.GetProperty("omissions").EnumerateArray()
                .Select(static item =>
                    $"{item.GetProperty("kind").GetString()}|{item.GetProperty("reason").GetString()}")
                .ToArray();
            Assert.Equal(omissionKeys.Distinct(StringComparer.Ordinal).Count(), omissionKeys.Length);
        }
        finally
        {
            if (Directory.Exists(artifactRunRoot))
                Directory.Delete(artifactRunRoot, recursive: true);
        }
    }

    [Fact]
    public void ShellScript_HasValidSyntaxAndMatchesPowerShellDryRunContract()
    {
        var shellScript = Path.Combine(RepositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.sh");
        var source = File.ReadAllText(shellScript);
        Assert.Contains("--platform", source, StringComparison.Ordinal);
        Assert.Contains("Category=FlowPilot", source, StringComparison.Ordinal);
        Assert.Contains("Category=AppleTestAgent", source, StringComparison.Ordinal);
        Assert.Contains("Category=AppKitFlowQa", source, StringComparison.Ordinal);
        Assert.Contains("capability-missing", source, StringComparison.Ordinal);
        Assert.Contains("--experimental", source, StringComparison.Ordinal);
        Assert.DoesNotContain("adb kill-server", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("adb start-server", source, StringComparison.OrdinalIgnoreCase);

        if (!TryGetRequiredBash(out var bash))
            return;

        var syntax = RunProcess(bash, "-n", "eng/devflow/Run-DevFlowFlowQa.sh");
        Assert.Equal(0, syntax.ExitCode);

        var shell = RunProcess(
            bash,
            "eng/devflow/Run-DevFlowFlowQa.sh",
            "--platform", "android",
            "--repeat", "2",
            "--results-root", "./artifacts/TestResults/devflow-flow/android",
            "--no-build",
            "--qualification",
            "--dry-run");
        Assert.Equal(0, shell.ExitCode);

        var powerShell = RunPowerShell(
            "--platform", "android",
            "--repeat", "2",
            "--results-root", ResultsRoot("android"),
            "--no-build",
            "--qualification",
            "--dry-run");

        using var shellDocument = JsonDocument.Parse(shell.StandardOutput);
        using var powerShellDocument = JsonDocument.Parse(powerShell.StandardOutput);
        Assert.Equal(
            powerShellDocument.RootElement.GetProperty("testFilter").GetString(),
            shellDocument.RootElement.GetProperty("testFilter").GetString());
        Assert.Equal(
            powerShellDocument.RootElement.GetProperty("artifactPaths").GetProperty("testResults").GetString(),
            shellDocument.RootElement.GetProperty("artifactPaths").GetProperty("testResults").GetString());
        Assert.Equal(
            powerShellDocument.RootElement.GetProperty("noBuild").GetBoolean(),
            shellDocument.RootElement.GetProperty("noBuild").GetBoolean());
        Assert.True(shellDocument.RootElement.GetProperty("qualificationRequested").GetBoolean());
        Assert.Equal(
            powerShellDocument.RootElement.GetProperty("qualificationRequested").GetBoolean(),
            shellDocument.RootElement.GetProperty("qualificationRequested").GetBoolean());
    }

    [Fact]
    public void ShellAppleSpikeDryRun_ConstructsHostProofWithoutExposingSessionSecret()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var shell = RunProcess(
            bash,
            "eng/devflow/Run-DevFlowFlowQa.sh",
            "--platform", "ios",
            "--apple-spike",
            "--target-app", "./artifacts/apple-spike-fake.app",
            "--target-bundle-id", "com.example.devflow.spike",
            "--simulator-id", "SIMULATOR-SENTINEL",
            "--safe-action-id", "AddButton",
            "--results-root", "./artifacts/TestResults/devflow-flow/ios",
            "--dry-run");

        Assert.Equal(0, shell.ExitCode);
        Assert.DoesNotContain("DEVFLOW_APPLE_AGENT_SESSION_SECRET", shell.StandardOutput, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(shell.StandardOutput);
        var root = document.RootElement;
        Assert.True(root.GetProperty("appleSpike").GetBoolean());

        var rejectedQualification = RunProcess(
            bash,
            "eng/devflow/Run-DevFlowFlowQa.sh",
            "--platform", "macos",
            "--experimental",
            "--qualification",
            "--results-root", "./artifacts/TestResults/devflow-flow/macos",
            "--dry-run");
        Assert.Equal(2, rejectedQualification.ExitCode);
        Assert.Contains("--qualification", rejectedQualification.StandardError, StringComparison.Ordinal);
        Assert.Equal(
            ExpectedAppleCapabilityState(root),
            root.GetProperty("capability").GetProperty("state").GetString());
        var arguments = root.GetProperty("command").GetProperty("arguments")
            .EnumerateArray()
            .Select(static item => item.GetString())
            .ToArray();
        Assert.Contains("--target-bundle-id", arguments);
        Assert.Contains("com.example.devflow.spike", arguments);
        Assert.DoesNotContain(arguments, static argument =>
            string.Equals(argument, "--session-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void ShellAppleQaDryRun_AutomaticallyRequiresProofBeforeCategoryExecution()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var shell = RunProcess(
            bash,
            "eng/devflow/Run-DevFlowFlowQa.sh",
            "--platform", "maccatalyst",
            "--results-root", "./artifacts/TestResults/devflow-flow/maccatalyst",
            "--dry-run");

        Assert.Equal(0, shell.ExitCode);
        using var document = JsonDocument.Parse(shell.StandardOutput);
        Assert.True(document.RootElement.GetProperty("appleSpike").GetBoolean());
        Assert.Equal(
            ExpectedAppleCapabilityState(document.RootElement),
            document.RootElement.GetProperty("capability").GetProperty("state").GetString());
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.sh"));
        Assert.Contains("run_apple_flow_qa", script, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_RUN_APPLE_FLOW_QA", script, StringComparison.Ordinal);
        Assert.Contains("apple-flow-qa.json", script, StringComparison.Ordinal);
        Assert.Contains("Apple evidence is adapted read-only", script, StringComparison.Ordinal);
        Assert.Contains("run_qualification || true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellExperimentalAppKitDryRun_UsesDedicatedFixtureAndManifestLabels()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var shell = RunProcess(
            bash,
            "eng/devflow/Run-DevFlowFlowQa.sh",
            "--platform", "macos",
            "--experimental",
            "--repeat", "2",
            "--results-root", "./artifacts/TestResults/devflow-flow/macos",
            "--dry-run");

        Assert.Equal(0, shell.ExitCode);
        using var document = JsonDocument.Parse(shell.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("Category=AppKitFlowQa", root.GetProperty("testFilter").GetString());
        Assert.Equal(
            "samples/DevFlow.Sample.MacOS/DevFlow.Sample.MacOS.csproj",
            root.GetProperty("appProject").GetString());
        Assert.True(root.GetProperty("experimental").GetBoolean());
        Assert.Equal("appkit", root.GetProperty("backend").GetString());
        Assert.False(root.GetProperty("officialCoverage").GetBoolean());
        Assert.False(root.GetProperty("macCatalystEquivalent").GetBoolean());
        Assert.True(root.GetProperty("appleSpike").GetBoolean());
    }

    [Fact]
    public void FlowQaDocumentation_ContainsRequiredPlatformHandoffsAndArtifactInstructions()
    {
        var documentation = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "DevFlow", "flow-qa.md"));
        var appleSpike = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "DevFlow", "apple-xctest-spike.md"));

        Assert.Contains("--platform android", documentation, StringComparison.Ordinal);
        Assert.Contains("--platform windows", documentation, StringComparison.Ordinal);
        Assert.Contains("--platform ios", documentation, StringComparison.Ordinal);
        Assert.Contains("--platform maccatalyst", documentation, StringComparison.Ordinal);
        Assert.Contains("--platform macos", documentation, StringComparison.Ordinal);
        Assert.Contains("--physical-device", documentation, StringComparison.Ordinal);
        Assert.Contains("Compress-Archive", documentation, StringComparison.Ordinal);
        Assert.Contains("zip -r", documentation, StringComparison.Ordinal);
        Assert.Contains("capability-missing", documentation, StringComparison.Ordinal);
        Assert.Contains("AppKit", documentation, StringComparison.Ordinal);
        Assert.Contains("DevFlow.Sample.MacOS", documentation, StringComparison.Ordinal);
        Assert.Contains("macCatalystEquivalent: false", documentation, StringComparison.Ordinal);
        Assert.Contains("python3", documentation, StringComparison.Ordinal);
        Assert.Contains("openssl", documentation, StringComparison.Ordinal);
        Assert.Contains("10.0.301", documentation, StringComparison.Ordinal);
        Assert.Contains("10.0.203", documentation, StringComparison.Ordinal);
        Assert.Contains("verify-apple-qa", documentation, StringComparison.Ordinal);
        Assert.Contains("--import-diagnostics", documentation, StringComparison.Ordinal);
        Assert.Contains("signed-device harness", documentation, StringComparison.Ordinal);
        Assert.Contains("--qualification", documentation, StringComparison.Ordinal);
        Assert.Contains("fixture-initialization.json", documentation, StringComparison.Ordinal);
        Assert.Contains("adb kill-server", documentation, StringComparison.Ordinal);
        Assert.Contains("adb start-server", documentation, StringComparison.Ordinal);
        Assert.Contains("never run", documentation, StringComparison.Ordinal);
        Assert.Contains("verify-apple-qa", appleSpike, StringComparison.Ordinal);
        Assert.Contains("--import-diagnostics", appleSpike, StringComparison.Ordinal);
        Assert.Contains("signed-device", appleSpike, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellRedaction_RemovesSchemePrefixedCredentialsAndHeldSigningInputs()
    {
        const string probe = """
            param([string] $ScriptPath)

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            foreach ($assignment in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                            $node.Left.Extent.Text -eq '$script:SecretValues'
                    }, $true)) {
                . ([scriptblock]::Create($assignment.Extent.Text))
            }
            $wanted = @('Register-SecretValue', 'Protect-DiagnosticText')
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in $wanted
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            Register-SecretValue 'FLOW-QA-SIGNING-IDENTITY-SENTINEL'
            Register-SecretValue 'FLOW-QA-PROFILE-SENTINEL'
            Register-SecretValue '/Users/runner/FLOW-QA-KEYCHAIN-SENTINEL.keychain-db'

            @(
                'Authorization: Bearer FLOW-QA-TOKEN-SENTINEL',
                'authorization=Basic RkxPVy1RQS1UT0tFTi1TRU5USU5FTA==',
                'sending header Bearer FLOW-QA-TOKEN-SENTINEL to the agent',
                'codesign --sign FLOW-QA-SIGNING-IDENTITY-SENTINEL --keychain /Users/runner/FLOW-QA-KEYCHAIN-SENTINEL.keychain-db',
                'profile FLOW-QA-PROFILE-SENTINEL selected',
                'DEVFLOW_IOS_KEYCHAIN=/Users/runner/other.keychain-db'
            ) | ForEach-Object { Protect-DiagnosticText $_ }
            """;

        var result = RunProbe("redaction-probe", probe, "-ScriptPath", FlowQaScriptPath);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("SENTINEL", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("RkxPVy1RQS1UT0tFTi1TRU5USU5FTA", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Bearer [REDACTED]", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("DEVFLOW_IOS_KEYCHAIN=[REDACTED]", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellArtifactRecords_ExcludeTheManifestAndRepeatIdentically()
    {
        const string probe = """
            param(
                [string] $ScriptPath,
                [string] $RepositoryRoot,
                [string] $ArtifactRoot,
                [string] $ResultsRoot,
                [string] $RunId,
                [string] $ExcludePath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $MaxArtifactRecords = 256

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            $wanted = @(
                'Get-ArtifactRecords',
                'Get-CanonicalPath',
                'Get-PathComparison',
                'Get-RepositoryRelativePath',
                'Get-FileDigest',
                'Get-FileDigestAndSize',
                'Get-ArtifactKind')
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in $wanted
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $arguments = @{
                RepositoryRoot = $RepositoryRoot
                Roots = @($ArtifactRoot, $ResultsRoot)
                ResultsRoot = $ResultsRoot
                RunId = $RunId
                ExcludePaths = @($ExcludePath)
            }
            [ordered]@{
                first = Get-ArtifactRecords @arguments
                second = Get-ArtifactRecords @arguments
            } | ConvertTo-Json -Depth 10 -Compress
            """;

        var probeRoot = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(probeRoot, "artifacts-root");
        var resultsRoot = Path.Combine(probeRoot, "results-root");
        const string runId = "probe-run-1";
        try
        {
            WriteProbeFile(Path.Combine(artifactRoot, "manifest.json"), "{\"schema\":1}");
            WriteProbeFile(Path.Combine(artifactRoot, "flow-run.json"), "{\"schema\":1}");
            WriteProbeFile(Path.Combine(artifactRoot, runId, "failure.mauitrace"), "trace");
            WriteProbeFile(Path.Combine(resultsRoot, $"devflow-flow-android-{runId}.trx"), "<trx />");
            WriteProbeFile(Path.Combine(resultsRoot, "unrelated-run.trx"), "<trx />");

            var result = RunProbe(
                "artifact-records-probe",
                probe,
                "-ScriptPath", FlowQaScriptPath,
                "-RepositoryRoot", probeRoot,
                "-ArtifactRoot", artifactRoot,
                "-ResultsRoot", resultsRoot,
                "-RunId", runId,
                "-ExcludePath", Path.Combine(artifactRoot, "manifest.json"));

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StandardOutput);
            var first = document.RootElement.GetProperty("first");
            var second = document.RootElement.GetProperty("second");

            // A second identical pass must produce byte-identical evidence: repeated writes may not
            // accumulate omissions, reorder records, or drift.
            Assert.Equal(first.GetRawText(), second.GetRawText());

            var paths = first.GetProperty("records").EnumerateArray()
                .Select(static record => record.GetProperty("path").GetString()!)
                .ToArray();
            Assert.DoesNotContain(paths, static path => path.EndsWith("manifest.json", StringComparison.Ordinal));
            Assert.Contains("artifacts-root/flow-run.json", paths);
            Assert.Contains($"artifacts-root/{runId}/failure.mauitrace", paths);
            Assert.Contains($"results-root/devflow-flow-android-{runId}.trx", paths);
            Assert.DoesNotContain(paths, static path => path.EndsWith("unrelated-run.trx", StringComparison.Ordinal));
            Assert.All(paths, path =>
            {
                Assert.False(Path.IsPathRooted(path), $"'{path}' is rooted.");
                Assert.DoesNotContain('\\', path);
            });
            Assert.Equal(paths, paths.OrderBy(static path => path, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            if (Directory.Exists(probeRoot))
                Directory.Delete(probeRoot, recursive: true);
        }
    }

    [Fact]
    public void PowerShellScript_FailsClosedOnDiagnosticTruncationAndMissingExitStatus()
    {
        var script = File.ReadAllText(FlowQaScriptPath);

        Assert.StartsWith("#Requires -Version 7.3", script, StringComparison.Ordinal);
        Assert.Contains("$MaxDiagnosticLines = 1000", script, StringComparison.Ordinal);
        Assert.Contains(
            "[truncated: the recorded diagnostic reached its line or character limit]",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$exitCode = if ($null -eq $reportedExitCode) { 1 } else { [int] $reportedExitCode }",
            script,
            StringComparison.Ordinal);
        // Structured run evidence decides a failure classification; free text is only consulted
        // when the host recorded none.
        Assert.Contains("Get-StructuredFailureClassification", script, StringComparison.Ordinal);
        Assert.Contains("classificationSource", script, StringComparison.Ordinal);
        Assert.Contains(
            "if (-not [string]::IsNullOrWhiteSpace($StructuredClassification)) {",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The Android lane finalizes the shared flow-pilot manifest after the test host has already
    /// left a native exit status behind. A finalizer that reads that status calls its own success a
    /// failure and republishes the generic fallback, so the run's evidence changes shape purely
    /// because the flow failed. When finalization really does fail, the unfinalized manifest is the
    /// only account the test process wrote of the attempts it observed: it is preserved under a
    /// fixed bounded name and published, never overwritten by the fallback.
    /// </summary>
    [Fact]
    public void PowerShellAndroidFinalizer_SurvivesAFailingFlowRunExitStatusAndPreservesPilotEvidence()
    {
        const string probe = """
            param(
                [string] $ScriptPath,
                [string] $RepositoryRoot,
                [string] $ArtifactRoot,
                [string] $ResultsRoot,
                [string] $ManifestPath,
                [string] $FallbackManifestPath,
                [string] $FallbackRepositoryRoot)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            foreach ($assignment in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                            $node.Left.Extent.Text -in @(
                                '$MaxArtifactRecords',
                                '$MaxPreservedManifestBytes',
                                '$UnfinalizedManifestName')
                    }, $true)) {
                . ([scriptblock]::Create($assignment.Extent.Text))
            }
            $wanted = @(
                'Finalize-AndroidManifest',
                'Save-UnfinalizedManifest',
                'Resolve-PreservedPilotManifest',
                'New-ArtifactRecord',
                'Get-ArtifactKind',
                'Write-AtomicJson',
                'Write-GenericManifest',
                'Get-FileDigest',
                'Get-FileDigestAndSize',
                'Get-RepositoryRelativePath',
                'Get-CanonicalPath')
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in $wanted
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            $hostQa = [ordered]@{ runId = 'finalizer-probe'; status = 'failed'; host = [ordered]@{ hostOs = 'probe' } }

            # Exactly what a failing `dotnet test` leaves behind before the finalizer is called.
            $global:LASTEXITCODE = 3
            $finalized = Finalize-AndroidManifest `
                -RepositoryRoot $RepositoryRoot `
                -ManifestPath $ManifestPath `
                -ArtifactRoot $ArtifactRoot `
                -ResultsRoot $ResultsRoot `
                -Commit '0123456789abcdef0123456789abcdef01234567' `
                -RunId 'finalizer-probe' `
                -HostQa $hostQa

            # The second manifest is finalized against a root that holds no finalizer at all, which
            # is the failure this fallback exists for. The pilot manifest it starts from is the
            # evidence that must survive.
            $pilotBytes = '{"schema":1,"kind":"devflow-flow-pilot","flows":[{"name":"probe"}],"artifacts":[]}'
            [System.IO.File]::WriteAllText($FallbackManifestPath, $pilotBytes, [System.Text.UTF8Encoding]::new($false))
            $global:LASTEXITCODE = 0
            $fallback = Finalize-AndroidManifest `
                -RepositoryRoot $FallbackRepositoryRoot `
                -ManifestPath $FallbackManifestPath `
                -ArtifactRoot $ArtifactRoot `
                -ResultsRoot $ResultsRoot `
                -Commit '0123456789abcdef0123456789abcdef01234567' `
                -RunId 'finalizer-probe' `
                -HostQa $hostQa

            $preserved = Save-UnfinalizedManifest -RepositoryRoot $FallbackRepositoryRoot -ManifestPath $FallbackManifestPath
            $preservedPath = Join-Path (Split-Path -Parent $FallbackManifestPath) $UnfinalizedManifestName

            # The three branches the fallback can take, decided in one place.
            $resolvedWithRoom = Resolve-PreservedPilotManifest `
                -RepositoryRoot $FallbackRepositoryRoot `
                -ManifestPath $FallbackManifestPath `
                -RecordedArtifacts 0
            $resolvedAtCap = Resolve-PreservedPilotManifest `
                -RepositoryRoot $FallbackRepositoryRoot `
                -ManifestPath $FallbackManifestPath `
                -RecordedArtifacts $MaxArtifactRecords
            $atCapCopyRemoved = -not (Test-Path -LiteralPath $preservedPath -PathType Leaf)
            $missingManifest = Join-Path $FallbackRepositoryRoot 'never-written.json'
            $resolvedMissing = Resolve-PreservedPilotManifest `
                -RepositoryRoot $FallbackRepositoryRoot `
                -ManifestPath $missingManifest `
                -RecordedArtifacts 0

            # Restored for the assertions below, which read the preserved bytes.
            $null = Resolve-PreservedPilotManifest `
                -RepositoryRoot $FallbackRepositoryRoot `
                -ManifestPath $FallbackManifestPath `
                -RecordedArtifacts 0
            Write-GenericManifest `
                -ManifestPath $FallbackManifestPath `
                -RepositoryRoot $FallbackRepositoryRoot `
                -Commit '0123456789abcdef0123456789abcdef01234567' `
                -RunId 'finalizer-probe' `
                -Platform android `
                -AppProject $ScriptPath `
                -FlowDigests @() `
                -HostQa $hostQa `
                -Artifacts @($preserved.record) `
                -ArtifactSummary ([ordered]@{ maxArtifacts = 256; recordedArtifacts = 1; omittedArtifacts = 0; truncated = $false }) `
                -Omissions @([ordered]@{ kind = 'shared-manifest'; reason = 'probe'; preserved = $true; preservedPath = $preserved.record.path })

            $finalizedManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
            $fallbackManifest = Get-Content -LiteralPath $FallbackManifestPath -Raw | ConvertFrom-Json -AsHashtable
            [ordered]@{
                finalizedOk = [bool] $finalized.ok
                finalizedKind = [string] $finalizedManifest['kind']
                finalizedAt = -not [string]::IsNullOrWhiteSpace([string] $finalizedManifest['finalizedAt'])
                finalizedOmissions = @(@($finalizedManifest['omissions']) | ForEach-Object { [string] $_['kind'] })
                fallbackOk = [bool] $fallback.ok
                fallbackKind = [string] $fallbackManifest['kind']
                fallbackOmissions = @(@($fallbackManifest['omissions']) | ForEach-Object { [string] $_['kind'] })
                preservedOk = [bool] $preserved.ok
                preservedBytesMatch = (Test-Path -LiteralPath $preservedPath -PathType Leaf) -and
                    ([System.IO.File]::ReadAllText($preservedPath) -ceq $pilotBytes)
                preservedRecordPath = [string] $preserved.record.path
                withRoomPreserved = [bool] $resolvedWithRoom.omission['preserved']
                withRoomRecorded = $null -ne $resolvedWithRoom.record
                atCapPreserved = [bool] $resolvedAtCap.omission['preserved']
                atCapOverCap = [bool] $resolvedAtCap.overCap
                atCapRecorded = $null -ne $resolvedAtCap.record
                atCapCopyRemoved = [bool] $atCapCopyRemoved
                missingPreserved = [bool] $resolvedMissing.omission['preserved']
                missingFailure = [string] $resolvedMissing.omission['preservedFailure']
            } | ConvertTo-Json -Depth 6 -Compress
            """;

        var probeRoot = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        var artifactRoot = Path.Combine(probeRoot, "artifacts-root");
        var resultsRoot = Path.Combine(probeRoot, "results-root");
        var fallbackRepositoryRoot = Path.Combine(probeRoot, "no-finalizer-here");
        try
        {
            WriteProbeFile(Path.Combine(artifactRoot, "finalizer-probe", "flow-run.json"), "{\"schema\":1}");
            Directory.CreateDirectory(resultsRoot);
            Directory.CreateDirectory(fallbackRepositoryRoot);

            var result = RunProbe(
                "android-finalizer-probe",
                probe,
                "-ScriptPath", FlowQaScriptPath,
                "-RepositoryRoot", RepositoryRoot,
                "-ArtifactRoot", artifactRoot,
                "-ResultsRoot", resultsRoot,
                "-ManifestPath", Path.Combine(artifactRoot, "manifest.json"),
                "-FallbackManifestPath", Path.Combine(fallbackRepositoryRoot, "manifest.json"),
                "-FallbackRepositoryRoot", fallbackRepositoryRoot);

            Assert.Equal(0, result.ExitCode);
            var json = SplitLines(result.StandardOutput)
                .Last(static line => line.StartsWith("{", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            // A stale exit status from the flow run may not demote a finalizer that succeeded.
            Assert.True(root.GetProperty("finalizedOk").GetBoolean());
            Assert.Equal("devflow-flow-pilot", root.GetProperty("finalizedKind").GetString());
            Assert.True(root.GetProperty("finalizedAt").GetBoolean());
            Assert.DoesNotContain(
                "shared-manifest",
                root.GetProperty("finalizedOmissions").EnumerateArray()
                    .Select(static kind => kind.GetString()));

            // A finalizer that really cannot run reports the failure to its caller rather than
            // deciding the fallback itself, and the pilot manifest survives byte for byte.
            Assert.False(root.GetProperty("fallbackOk").GetBoolean());
            Assert.True(root.GetProperty("preservedOk").GetBoolean());
            Assert.True(root.GetProperty("preservedBytesMatch").GetBoolean());
            Assert.EndsWith(
                "manifest.unfinalized.json",
                root.GetProperty("preservedRecordPath").GetString(),
                StringComparison.Ordinal);
            Assert.Equal("devflow-flow-qa", root.GetProperty("fallbackKind").GetString());
            Assert.Contains(
                "shared-manifest",
                root.GetProperty("fallbackOmissions").EnumerateArray()
                    .Select(static kind => kind.GetString()));

            // Room in the inventory: the copy is kept and published.
            Assert.True(root.GetProperty("withRoomPreserved").GetBoolean());
            Assert.True(root.GetProperty("withRoomRecorded").GetBoolean());

            // No room: nothing is published, the copy is removed rather than left unreferenced,
            // and the reference the cap dropped is counted.
            Assert.False(root.GetProperty("atCapPreserved").GetBoolean());
            Assert.False(root.GetProperty("atCapRecorded").GetBoolean());
            Assert.True(root.GetProperty("atCapOverCap").GetBoolean());
            Assert.True(root.GetProperty("atCapCopyRemoved").GetBoolean());

            // Nothing to preserve: the failure is named rather than implied.
            Assert.False(root.GetProperty("missingPreserved").GetBoolean());
            Assert.Equal("no-manifest", root.GetProperty("missingFailure").GetString());
        }
        finally
        {
            if (Directory.Exists(probeRoot))
                Directory.Delete(probeRoot, recursive: true);
        }
    }

    /// <summary>
    /// The two entry points must reach the same redaction decisions. A credential a reader would
    /// recognize is removed by both; a word that only looks like a scheme, and a digest a reader
    /// needs, survive both.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellRedaction_AgreeOnCredentialsAndOnOrdinaryWords()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        string[] mustRedact =
        [
            "authorization: Bearer QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo",
            "token=FLOW-QA-TOKEN-SENTINEL-0123456789",
            "password: hunter2-sentinel-value",
            "ntlm TlRMTVNTUAABAAAAB4IIogAAAAAAAAAA",
            "codesign --sign FLOW-QA-SIGNING-IDENTITY-SENTINEL",
            "DEVFLOW_IOS_KEYCHAIN=/Users/runner/other.keychain-db",
            // Trailing punctuation outside a quote/brace must not make the match fall away.
            "GET /x?auth=1 Bearer QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo&next=2",
            "<header>Bearer QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo</header>",
            "Bearer QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo#fragment",
            // A credential key inside a longer header name is still a credential key.
            "X-Api-Key: FLOW-QA-HEADER-SENTINEL-0123456789",
            "X-Auth-Token: FLOW-QA-HEADER-SENTINEL-0123456789",
        ];
        string[] mustPreserve =
        [
            "digest sha256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            "basic authentication is required by the proxy",
            "negotiate protocol version 3",
            "the digest algorithm is negotiate friendly",
            "restored 42 packages in 3.4s",
            // '_' is a word character on both sides, so neither rule treats these as credentials.
            "session_token=abcdefgh12345",
            "foo_bearer abcdefgh12345",
        ];
        var corpus = mustRedact.Concat(mustPreserve).ToArray();

        var powerShell = RunPowerShellRedaction(corpus);
        var shell = RunShellRedaction(bash, corpus);
        Assert.Equal(corpus.Length, powerShell.Length);
        Assert.Equal(corpus.Length, shell.Length);

        for (var index = 0; index < corpus.Length; index++)
        {
            var expectRedaction = index < mustRedact.Length;
            foreach (var (name, line) in new[] { ("powershell", powerShell[index]), ("shell", shell[index]) })
            {
                Assert.False(
                    line.Contains("SENTINEL", StringComparison.Ordinal),
                    $"{name} leaked a sentinel for '{corpus[index]}': {line}");
                Assert.False(
                    line.Contains("QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVo", StringComparison.Ordinal),
                    $"{name} leaked a credential for '{corpus[index]}': {line}");
                Assert.Equal(expectRedaction, line.Contains("[REDACTED]", StringComparison.Ordinal));
            }

            if (expectRedaction)
                continue;

            // A preserved line has to survive intact in both, not merely avoid the marker.
            Assert.Equal(corpus[index], powerShell[index]);
            Assert.Equal(corpus[index], shell[index]);
        }
    }


    /// <summary>
    /// Both entry points publish the same artifact facts for the same run: one omission list in
    /// both reports, one artifact summary, a sorted repository-relative list that never contains
    /// the manifest, and a flow-run digest that describes the bytes finally on disk.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellPreflight_PublishTheSameArtifactFacts()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var shellRunId = $"flow-qa-parity-sh-{Guid.NewGuid():N}";
        var powerShellRunId = $"flow-qa-parity-ps-{Guid.NewGuid():N}";
        var shellRoot = Path.Combine(RepositoryRoot, "artifacts", "devflow", shellRunId);
        var powerShellRoot = Path.Combine(RepositoryRoot, "artifacts", "devflow", powerShellRunId);
        try
        {
            var shellRun = RunProcess(
                bash,
                new Dictionary<string, string?> { ["DEVFLOW_FLOW_QA_RUN_ID"] = shellRunId },
                "eng/devflow/Run-DevFlowFlowQa.sh",
                "--platform", "ios",
                "--results-root", "./artifacts/TestResults/devflow-flow/ios");
            var powerShellRun = RunPowerShell(
                new Dictionary<string, string?> { ["DEVFLOW_FLOW_QA_RUN_ID"] = powerShellRunId },
                "--platform", "ios",
                "--results-root", ResultsRoot("ios"));
            Assert.Equal(powerShellRun.ExitCode, shellRun.ExitCode);

            var shellFacts = ReadArtifactFacts(Path.Combine(shellRoot, "ios"));
            var powerShellFacts = ReadArtifactFacts(Path.Combine(powerShellRoot, "ios"));

            Assert.Equal(powerShellFacts.ManifestOmissions, shellFacts.ManifestOmissions);
            Assert.Equal(powerShellFacts.FlowRunOmissions, shellFacts.FlowRunOmissions);
            Assert.Equal(powerShellFacts.Summary, shellFacts.Summary);
            Assert.Equal(powerShellFacts.RelativePaths, shellFacts.RelativePaths);

            foreach (var facts in new[] { shellFacts, powerShellFacts })
            {
                // Both files state the same omissions in the same write pass.
                Assert.Equal(facts.ManifestOmissions, facts.FlowRunOmissions);
                Assert.Equal(facts.ManifestSummary, facts.FlowRunSummary);
                Assert.DoesNotContain(facts.Paths, path => path.EndsWith("/manifest.json", StringComparison.Ordinal));
                Assert.Contains(facts.Paths, path => path.EndsWith("/flow-run.json", StringComparison.Ordinal));
                Assert.Equal(facts.Paths, facts.Paths.OrderBy(static path => path, StringComparer.Ordinal).ToArray());
                Assert.All(facts.Paths, path =>
                {
                    Assert.False(Path.IsPathRooted(path), $"'{path}' is rooted.");
                    Assert.DoesNotContain('\\', path);
                    Assert.False(path.StartsWith("../", StringComparison.Ordinal), $"'{path}' escapes the checkout.");
                });
                Assert.Equal(facts.Paths.Length, facts.RecordedArtifacts);
                Assert.Equal(facts.FlowRunDigest, facts.RecordedFlowRunDigest);
            }
        }
        finally
        {
            foreach (var directory in new[] { shellRoot, powerShellRoot })
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }

            // Both entry points also write .trx files into the shared results root.
            var resultsRoot = ResultsRoot("ios");
            if (Directory.Exists(resultsRoot))
            {
                foreach (var file in Directory.EnumerateFiles(resultsRoot, "*", SearchOption.AllDirectories)
                             .Where(path =>
                                 path.Contains(shellRunId, StringComparison.Ordinal) ||
                                 path.Contains(powerShellRunId, StringComparison.Ordinal)))
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <summary>
    /// Structured run evidence decides a failure classification in both entry points, and the
    /// bounded text fallback only answers when the host recorded none.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellClassification_ReadStructuredEvidenceIdentically()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var cases = new (string Name, string FlowRun, string? Manifest, string? Expected)[]
        {
            ("flow-failure",
                """{"schema":1,"outcome":{"status":"failed"},"failure":{"failureId":"x","class":"assertion-failed"}}""",
                null,
                "flow-failure"),
            ("infrastructure",
                """{"schema":1,"outcome":{"status":"infrastructure-error"},"failure":{"failureId":"x","class":"timeout"}}""",
                null,
                "infrastructure-failure"),
            ("capability",
                """{"schema":1,"outcome":{"status":"failed"},"failure":{"failureId":"x","class":"capability-missing"}}""",
                null,
                "capability-missing"),
            ("manifest-attempt",
                """{"schema":1,"outcome":{"status":"passed"}}""",
                """{"schema":1,"kind":"devflow-flow-pilot","flows":[{"name":"f","firstAttempt":{"outcome":"infrastructure-error","failureClass":"infrastructure"}}]}""",
                "infrastructure-failure"),
            ("no-structured-failure",
                """{"schema":1,"outcome":{"status":"passed","verified":true}}""",
                null,
                null),
        };

        foreach (var (name, flowRun, manifest, expected) in cases)
        {
            var artifactRoot = Path.Combine(
                RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe",
                $"classification-{Guid.NewGuid():N}");
            try
            {
                WriteProbeFile(Path.Combine(artifactRoot, "run-1", "flow-run.json"), flowRun);
                if (manifest is not null)
                    WriteProbeFile(Path.Combine(artifactRoot, "manifest.json"), manifest);

                var shellHarness = Path.Combine(artifactRoot, "classification-harness.sh");
                File.WriteAllText(
                    shellHarness,
                    """
                    set -uo pipefail
                    artifact_root="$1"
                    flow_run_path="$artifact_root/script-owned-flow-run.json"
                    extract() {
                      awk -v name="$1" 'index($0, name "() {") == 1 { printing = 1 } printing { print } printing && $0 == "}" { printing = 0 }' eng/devflow/Run-DevFlowFlowQa.sh
                    }
                    eval "$(extract classify_structured_fields)"
                    eval "$(extract structured_failure_classification)"
                    [[ $(type -t structured_failure_classification) == function ]] || exit 90
                    if result=$(structured_failure_classification); then printf '%s\n' "$result"; else printf 'none\n'; fi
                    """.Replace("\r\n", "\n"),
                    new System.Text.UTF8Encoding(false));

                var shell = RunProcess(bash, shellHarness, artifactRoot);
                Assert.Equal(0, shell.ExitCode);

                const string probe = """
                    param([string] $ScriptPath, [string] $ArtifactRoot)

                    Set-StrictMode -Version Latest
                    $ErrorActionPreference = 'Stop'

                    $tokens = $null
                    $errors = $null
                    $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
                    $wanted = @(
                        'Get-StructuredFailureClassification',
                        'Get-ClassificationFromStructuredFields',
                        'Get-CanonicalPath',
                        'Get-PathComparison')
                    foreach ($definition in $ast.FindAll({
                                param($node)
                                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                                    $node.Name -in $wanted
                            }, $true)) {
                        . ([scriptblock]::Create($definition.Extent.Text))
                    }

                    $classification = Get-StructuredFailureClassification `
                        -ArtifactRoot $ArtifactRoot `
                        -ScriptReportPath (Join-Path $ArtifactRoot 'script-owned-flow-run.json')
                    if ($null -eq $classification) { 'none' } else { $classification }
                    """;

                var powerShell = RunProbe(
                    "classification-probe", probe, "-ScriptPath", FlowQaScriptPath, "-ArtifactRoot", artifactRoot);
                Assert.Equal(0, powerShell.ExitCode);

                var expectedText = expected ?? "none";
                Assert.Equal(expectedText, powerShell.StandardOutput.Trim());
                Assert.Equal(expectedText, shell.StandardOutput.Trim());
            }
            finally
            {
                if (Directory.Exists(artifactRoot))
                    Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// The two entry points report the same capability state for the same checkout, so a lane is
    /// never "planned" in one and "pending" in the other.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellCapabilityState_AgreeForEveryLane()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        foreach (var (platform, extra) in new[]
                 {
                     ("android", Array.Empty<string>()),
                     ("ios", Array.Empty<string>()),
                     ("maccatalyst", Array.Empty<string>()),
                     ("macos", new[] { "--experimental" }),
                 })
        {
            var shellArguments = new List<string>
            {
                "eng/devflow/Run-DevFlowFlowQa.sh",
                "--platform", platform,
                "--results-root", $"./artifacts/TestResults/devflow-flow/{platform}",
                "--dry-run",
            };
            shellArguments.InsertRange(3, extra);
            var powerShellArguments = new List<string>
            {
                "--platform", platform,
                "--results-root", ResultsRoot(platform),
                "--dry-run",
            };
            powerShellArguments.InsertRange(2, extra);

            var shell = RunProcess(bash, shellArguments.ToArray());
            var powerShell = RunPowerShell(powerShellArguments.ToArray());
            Assert.Equal(0, shell.ExitCode);
            Assert.Equal(0, powerShell.ExitCode);

            using var shellDocument = JsonDocument.Parse(shell.StandardOutput);
            using var powerShellDocument = JsonDocument.Parse(powerShell.StandardOutput);
            var shellCapability = shellDocument.RootElement.GetProperty("capability");
            var powerShellCapability = powerShellDocument.RootElement.GetProperty("capability");

            Assert.Equal(
                powerShellCapability.GetProperty("required").GetString(),
                shellCapability.GetProperty("required").GetString());
            Assert.Equal(
                powerShellCapability.GetProperty("available").GetBoolean(),
                shellCapability.GetProperty("available").GetBoolean());
            Assert.Equal(
                powerShellCapability.GetProperty("state").GetString(),
                shellCapability.GetProperty("state").GetString());

            var expectedState = platform == "android"
                ? "planned"
                : powerShellCapability.GetProperty("sourceAvailable").GetBoolean()
                    ? "proof-required"
                    : "pending-spike";
            Assert.Equal(expectedState, powerShellCapability.GetProperty("state").GetString());
        }
    }

    /// <summary>
    /// The text fallback is the last resort, and the only rule that reads free text at all. Its
    /// markers used to be bare words, so an ordinary line naming the emulator, or a flow assertion
    /// that timed out, turned a product defect into an "infrastructure failure" nobody had to fix.
    /// Both entry points must reach the same verdict from the same line.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellTextFallback_ClassifyOnlyAnchoredInfrastructureMarkers()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        (string Line, string Expected)[] cases =
        [
            // Product failures. Each one names something a host prints on an ordinary bad run.
            ("Assert.Equal() Failure: the emulator screenshot shows no Add button", "flow-failure"),
            ("Waiting for element AddButton timed out after 5000 ms", "flow-failure"),
            ("maui_tap on device did not settle; the assertion timed out after 30s", "flow-failure"),
            ("The install step timed out while the app was being deployed", "flow-failure"),
            ("The host reported that the WebView did not become ready", "flow-failure"),
            ("The infrastructure of this app is described in docs/architecture.md", "flow-failure"),
            ("simctl is not used by this lane", "flow-failure"),
            ("1 test failed: MauiTodoFlows.AddItem", "flow-failure"),
            // Infrastructure failures, each an anchored phrase a failing host really prints.
            ("adb: device 'emulator-5554' not found", "infrastructure-failure"),
            ("emulator: ERROR: x86_64 emulation currently requires hardware acceleration", "infrastructure-failure"),
            ("Workload 'maui-android' is not installed.", "infrastructure-failure"),
            ("The .NET SDK was not found on this machine", "infrastructure-failure"),
            ("The DevFlow agent did not become ready within 60s", "infrastructure-failure"),
            ("android-fixture-initialization: the emulator never booted", "infrastructure-failure"),
            ("xcrun: error: unable to find utility simctl", "infrastructure-failure"),
            ("emulator boot timed out after 300s", "infrastructure-failure"),
            ("agent readiness timed out", "infrastructure-failure"),
            // The capability marker still wins over everything else.
            ("capability-missing: no usable device capability was declared", "capability-missing"),
        ];

        var shell = RunShellClassification(bash, cases.Select(static item => item.Line).ToArray());
        var powerShell = RunPowerShellClassification(cases.Select(static item => item.Line).ToArray());
        Assert.Equal(cases.Length, shell.Length);
        Assert.Equal(cases.Length, powerShell.Length);

        for (var index = 0; index < cases.Length; index++)
        {
            Assert.Equal(cases[index].Expected, shell[index]);
            Assert.Equal(cases[index].Expected, powerShell[index]);
        }
    }

    static string[] RunShellClassification(string bash, IReadOnlyList<string> corpus)
    {
        // The shipped function and the shipped pattern are lifted out of the shipped file, so the
        // harness cannot drift away from what the entry point actually runs.
        const string harness = """
            set -uo pipefail
            eval "$(grep '^INFRASTRUCTURE_DIAGNOSTIC_PATTERN=' eng/devflow/Run-DevFlowFlowQa.sh)"
            eval "$(awk 'index($0, "classify_execution() {") == 1 { printing = 1 } printing { print } printing && $0 == "}" { printing = 0 }' eng/devflow/Run-DevFlowFlowQa.sh)"
            [[ $(type -t classify_execution) == function ]] || exit 90
            [[ -n "${INFRASTRUCTURE_DIAGNOSTIC_PATTERN:-}" ]] || exit 91
            line_file=$(mktemp)
            trap 'rm -f "$line_file"' EXIT
            while IFS= read -r line; do
              printf '%s\n' "$line" >"$line_file"
              classify_execution 1 "$line_file"
              printf '\n'
            done <"$1"
            """;

        var corpusPath = WriteCorpus(corpus);
        var harnessPath = Path.Combine(Path.GetDirectoryName(corpusPath)!, "classification-harness.sh");
        File.WriteAllText(harnessPath, harness.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
        try
        {
            var result = RunProcess(bash, harnessPath, corpusPath);
            Assert.Equal(0, result.ExitCode);
            return SplitLines(result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(corpusPath)!))
                Directory.Delete(Path.GetDirectoryName(corpusPath)!, recursive: true);
        }
    }

    static string[] RunPowerShellClassification(IReadOnlyList<string> corpus)
    {
        const string probe = """
            param([string] $ScriptPath, [string] $CorpusPath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            foreach ($assignment in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                            $node.Left.Extent.Text -eq '$script:InfrastructureDiagnosticPattern'
                    }, $true)) {
                . ([scriptblock]::Create($assignment.Extent.Text))
            }
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -eq 'Get-ExecutionClassification'
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            foreach ($line in [System.IO.File]::ReadAllLines($CorpusPath)) {
                [Console]::Out.WriteLine((Get-ExecutionClassification `
                            -ExitCode 1 `
                            -Output $line `
                            -StructuredClassification $null `
                            -Markers $null))
            }
            """;

        var corpusPath = WriteCorpus(corpus);
        try
        {
            var result = RunProbe(
                "classification-parity-probe", probe, "-ScriptPath", FlowQaScriptPath, "-CorpusPath", corpusPath);
            Assert.Equal(0, result.ExitCode);
            return SplitLines(result.StandardOutput);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(corpusPath)!))
                Directory.Delete(Path.GetDirectoryName(corpusPath)!, recursive: true);
        }
    }

    /// <summary>
    /// A held value is redacted by exact match, which is the only rule that can see it: a signing
    /// identity or keychain reference appears in tool output with no key or scheme around it. Tools
    /// echo those values back with whatever casing they prefer, so a case-sensitive match publishes
    /// the secret the moment one of them changes it. PowerShell already compares
    /// case-insensitively; the shell has to agree.
    /// </summary>
    [Fact]
    public void ShellAndPowerShellExactValueRedaction_AreCaseInsensitiveInBoth()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        string[] secrets =
        [
            "FLOW-QA-SIGNING-IDENTITY-SENTINEL",
            "Runner-Keychain-SENTINEL-Value",
        ];
        string[] corpus =
        [
            "codesign --sign flow-qa-signing-identity-sentinel /path/to/app",
            "codesign --sign Flow-Qa-Signing-Identity-Sentinel /path/to/app",
            "codesign --sign FLOW-QA-SIGNING-IDENTITY-SENTINEL /path/to/app",
            "security list-keychains -> runner-keychain-sentinel-value",
            "security list-keychains -> RUNNER-KEYCHAIN-SENTINEL-VALUE",
        ];

        var powerShell = RunPowerShellRedaction(corpus, secrets);
        var shell = RunShellRedaction(bash, corpus, secrets);
        Assert.Equal(corpus.Length, powerShell.Length);
        Assert.Equal(corpus.Length, shell.Length);

        for (var index = 0; index < corpus.Length; index++)
        {
            foreach (var (name, line) in new[] { ("powershell", powerShell[index]), ("shell", shell[index]) })
            {
                Assert.False(
                    line.Contains("SENTINEL", StringComparison.OrdinalIgnoreCase),
                    $"{name} leaked a held value for '{corpus[index]}': {line}");
                Assert.Contains("[REDACTED]", line, StringComparison.Ordinal);
            }

            // The surrounding diagnostic still has to survive in both.
            Assert.Equal(powerShell[index], shell[index]);
        }
    }

    /// <summary>
    /// The endpoint the Apple lanes read out of a host readiness file decides who receives the
    /// ephemeral session secret this script minted. A readiness file is an artifact on disk, so a
    /// stale, racing, or tampered one must not be able to point the authenticated XCTest agent at
    /// a host off this machine.
    /// </summary>
    [Fact]
    public void AppleEndpoint_IsAcceptedOnlyWhenItIsLoopback()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        (string Endpoint, bool Accepted)[] cases =
        [
            ("http://127.0.0.1:51234/", true),
            ("http://127.0.0.1:51234", true),
            ("http://127.4.5.6:51234/agent", true),
            ("http://localhost:51234/", true),
            ("http://[::1]:51234/", true),
            ("https://127.0.0.1:51234/", true),
            ("http://10.0.0.5:51234/", false),
            ("http://evil.example.com:51234/", false),
            ("http://127.0.0.1@evil.example.com:51234/", false),
            ("http://127.0.0.1.evil.example.com/", false),
            ("http://[2001:db8::1]:51234/", false),
            ("ws://127.0.0.1:51234/", false),
            ("file:///etc/passwd", false),
            ("", false),
        ];

        var directory = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var harnessPath = Path.Combine(directory, "endpoint-harness.sh");
        try
        {
            File.WriteAllText(
                harnessPath,
                """
                set -uo pipefail
                extract() {
                  awk -v name="$1" 'index($0, name "() {") == 1 { printing = 1 } printing { print } printing && $0 == "}" { printing = 0 }' eng/devflow/Run-DevFlowFlowQa.sh
                }
                eval "$(extract is_loopback_endpoint)"
                [[ $(type -t is_loopback_endpoint) == function ]] || exit 90
                for candidate in "$@"; do
                  if is_loopback_endpoint "$candidate"; then printf 'accepted\n'; else printf 'refused\n'; fi
                done
                """.Replace("\r\n", "\n"),
                new System.Text.UTF8Encoding(false));

            var arguments = new List<string> { harnessPath };
            arguments.AddRange(cases.Select(static item => item.Endpoint));
            var result = RunProcess(bash, arguments.ToArray());
            Assert.Equal(0, result.ExitCode);

            var verdicts = SplitLines(result.StandardOutput);
            Assert.Equal(cases.Length, verdicts.Length);
            for (var index = 0; index < cases.Length; index++)
            {
                Assert.Equal(
                    cases[index].Accepted ? "accepted" : "refused",
                    verdicts[index]);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// When the shared Android flow-pilot manifest cannot be finalized, whatever the finalizer left
    /// behind is not a manifest this run can vouch for - but it is the only account the test
    /// process wrote of the attempts it observed. It is preserved under a fixed bounded name and
    /// published as an artifact before the generic manifest replaces it, and the omission is
    /// stated in both reports: a flow-run that stayed silent would contradict the manifest a
    /// consumer reads beside it.
    /// </summary>
    [Fact]
    public void AndroidFinalizationFailure_PreservesThePilotManifestAndSaysSoInBothReports()
    {
        if (!TryGetRequiredBash(out var bash))
            return;

        var runId = $"flow-qa-android-fallback-{Guid.NewGuid():N}";
        var runRoot = Path.Combine(RepositoryRoot, "artifacts", "devflow", runId);
        var artifactRoot = Path.Combine(runRoot, "android");
        var shimDirectory = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", $"shims-{Guid.NewGuid():N}");
        var dotnetShimMarker = Path.Combine(shimDirectory, "dotnet-invoked");
        var pwshShimMarker = Path.Combine(shimDirectory, "pwsh-invoked");
        Directory.CreateDirectory(shimDirectory);
        const string pilotManifest =
            """{"schema":1,"kind":"devflow-flow-pilot","flows":[{"name":"pilot"}],"artifacts":[]}""";
        try
        {
            // A previous run under the same run id left a copy behind. The artifact scan hashes it
            // before this pass overwrites it, so the stale record has to be dropped rather than
            // published beside the fresh one.
            Directory.CreateDirectory(artifactRoot);
            File.WriteAllText(
                Path.Combine(artifactRoot, "manifest.unfinalized.json"),
                """{"schema":1,"kind":"devflow-flow-pilot","stale":true}""",
                new System.Text.UTF8Encoding(false));

            // The test host writes the pilot manifest the way the real one does and then fails,
            // and the finalizer cannot run at all. That is exactly the condition this covers: a
            // shared manifest that exists but could not be finalized.
            WriteExecutableShim(
                Path.Combine(shimDirectory, "dotnet"),
                "#!/bin/sh\n" +
                ": > \"$FLOW_QA_TEST_DOTNET_SHIM_MARKER\"\n" +
                "mkdir -p \"$FLOW_QA_TEST_ARTIFACT_ROOT\"\n" +
                "printf '%s' '" + pilotManifest + "' > \"$FLOW_QA_TEST_ARTIFACT_ROOT/manifest.json\"\n" +
                "exit 1\n");
            WriteExecutableShim(
                Path.Combine(shimDirectory, "pwsh"),
                "#!/bin/sh\n" +
                ": > \"$FLOW_QA_TEST_PWSH_SHIM_MARKER\"\n" +
                "exit 1\n");

            var result = RunProcess(
                bash,
                new Dictionary<string, string?>
                {
                    ["DEVFLOW_FLOW_QA_RUN_ID"] = runId,
                    ["FLOW_QA_TEST_ARTIFACT_ROOT"] = artifactRoot,
                    ["FLOW_QA_TEST_DOTNET_SHIM_MARKER"] = dotnetShimMarker,
                    ["FLOW_QA_TEST_PWSH_SHIM_MARKER"] = pwshShimMarker,
                    ["PATH"] = shimDirectory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                },
                "eng/devflow/Run-DevFlowFlowQa.sh",
                "--platform", "android",
                "--results-root", "./artifacts/TestResults/devflow-flow/android");
            Assert.NotEqual(0, result.ExitCode);
            Assert.True(
                File.Exists(dotnetShimMarker) && File.Exists(pwshShimMarker),
                "Both PATH shims must execute; otherwise the real toolchain can replace the test host.");

            var manifestPath = Path.Combine(artifactRoot, "manifest.json");
            var flowRunPath = Path.Combine(artifactRoot, "flow-run.json");
            var preservedPath = Path.Combine(artifactRoot, "manifest.unfinalized.json");
            Assert.True(
                File.Exists(manifestPath),
                $"No manifest was written. stdout={result.StandardOutput}; stderr={result.StandardError}");
            Assert.True(File.Exists(flowRunPath));

            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            using var flowRun = JsonDocument.Parse(File.ReadAllText(flowRunPath));

            // The generic manifest replaced whatever the failed finalization left behind, and the
            // pilot evidence survived it byte for byte.
            Assert.Equal("devflow-flow-qa", manifest.RootElement.GetProperty("kind").GetString());
            Assert.True(File.Exists(preservedPath), "The unfinalized pilot manifest was destroyed.");
            Assert.Equal(pilotManifest, File.ReadAllText(preservedPath));

            foreach (var report in new[] { manifest.RootElement, flowRun.RootElement })
            {
                var omission = report.GetProperty("omissions").EnumerateArray()
                    .Single(static item => item.GetProperty("kind").GetString() == "shared-manifest");
                Assert.True(omission.GetProperty("preserved").GetBoolean());
                Assert.EndsWith(
                    "android/manifest.unfinalized.json",
                    omission.GetProperty("preservedPath").GetString(),
                    StringComparison.Ordinal);
            }

            Assert.Equal(
                manifest.RootElement.GetProperty("omissions").ToString(),
                flowRun.RootElement.GetProperty("omissions").ToString());

            // Both reports state the same artifact facts, and the manifest's own list is what its
            // summary counts. An inventory that contradicts the count beside it is refused whole.
            Assert.Equal(
                manifest.RootElement.GetProperty("artifactSummary").ToString(),
                flowRun.RootElement.GetProperty("artifactSummary").ToString());
            var artifacts = manifest.RootElement.GetProperty("artifacts").EnumerateArray().ToArray();
            Assert.Equal(
                artifacts.Length,
                manifest.RootElement.GetProperty("artifactSummary").GetProperty("recordedArtifacts").GetInt32());
            Assert.False(manifest.RootElement.GetProperty("artifactSummary").GetProperty("truncated").GetBoolean());

            // Every digest the manifest publishes describes the bytes on disk, including the two
            // files this pass rewrote after the first list was taken.
            foreach (var artifact in artifacts)
            {
                var path = Path.Combine(
                    RepositoryRoot,
                    artifact.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Assert.True(File.Exists(path), $"'{path}' is published but absent.");
                Assert.Equal(
                    $"sha256:{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}",
                    artifact.GetProperty("sha256").GetString());
                Assert.Equal(new FileInfo(path).Length, artifact.GetProperty("sizeBytes").GetInt64());
            }

            // The preserved manifest is one of them, listed exactly once even though a stale copy
            // of it was already on disk when the artifact scan ran, and the rewritten report is
            // hashed again so the digest published for it is not the one taken before the omission
            // was added.
            Assert.Single(
                artifacts,
                artifact => artifact.GetProperty("path").GetString()!
                    .EndsWith("android/manifest.unfinalized.json", StringComparison.Ordinal));
            Assert.Contains(
                artifacts,
                artifact => artifact.GetProperty("path").GetString()!
                    .EndsWith("/flow-run.json", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(runRoot))
                Directory.Delete(runRoot, recursive: true);
            if (Directory.Exists(shimDirectory))
                Directory.Delete(shimDirectory, recursive: true);
            var resultsRoot = ResultsRoot("android");
            if (Directory.Exists(resultsRoot))
            {
                foreach (var file in Directory.EnumerateFiles(resultsRoot, "*", SearchOption.AllDirectories)
                             .Where(path => path.Contains(runId, StringComparison.Ordinal)))
                {
                    File.Delete(file);
                }
            }
        }
    }

    /// <summary>
    /// The recorded diagnostic is a bounded projection, and a failing host usually says what went
    /// wrong on its last line. Classifying from the projection meant a capability the host named
    /// past the cap was read as an ordinary flow failure, which is a lane reported as a product
    /// defect. The markers are scanned over everything the command produced; nothing unbounded is
    /// ever written.
    /// </summary>
    [Fact]
    public void PowerShellClassification_ReadsMarkersPastTheRecordedDiagnosticCap()
    {
        var directory = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string probe = """
                param([string] $ScriptPath, [string] $WorkingDirectory)

                Set-StrictMode -Version Latest
                $ErrorActionPreference = 'Stop'

                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
                $wantedAssignments = @(
                    '$MaxDiagnosticCharacters',
                    '$MaxDiagnosticLines',
                    '$script:SecretValues',
                    '$script:InfrastructureDiagnosticPattern')
                foreach ($assignment in $ast.FindAll({
                            param($node)
                            $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                                $node.Left.Extent.Text -in $wantedAssignments
                        }, $true)) {
                    . ([scriptblock]::Create($assignment.Extent.Text))
                }
                foreach ($definition in $ast.FindAll({
                            param($node)
                            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                                $node.Name -in @(
                                    'Register-SecretValue',
                                    'Protect-DiagnosticText',
                                    'Invoke-RecordedCommand',
                                    'Get-ExecutionClassification')
                        }, $true)) {
                    . ([scriptblock]::Create($definition.Extent.Text))
                }

                $noisy = Join-Path $WorkingDirectory 'noisy-host.ps1'
                $body = @'
                for ($index = 0; $index -lt 2000; $index++) {
                    Write-Output "line $index of ordinary build chatter"
                }
                Write-Output 'capability-missing: the host declared no usable device capability'
                exit 4
                '@
                [System.IO.File]::WriteAllText($noisy, $body, [System.Text.UTF8Encoding]::new($false))

                $executable = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
                $result = Invoke-RecordedCommand `
                    -FileName $executable `
                    -Arguments @('-NoLogo', '-NoProfile', '-File', $noisy) `
                    -DiagnosticPath (Join-Path $WorkingDirectory 'diagnostic.txt')

                [ordered]@{
                    exitCode = $result.exitCode
                    truncated = [bool] $result.truncated
                    projectionHasMarker = [bool] ($result.output -match '(?i)capability-missing')
                    diagnosticHasMarker = [bool] ((Get-Content -LiteralPath $result.diagnosticPath -Raw) -match '(?i)capability-missing')
                    withMarkers = Get-ExecutionClassification -ExitCode $result.exitCode -Output $result.output -StructuredClassification $null -Markers $result.markers
                    withoutMarkers = Get-ExecutionClassification -ExitCode $result.exitCode -Output $result.output -StructuredClassification $null -Markers $null
                } | ConvertTo-Json -Compress
                """;

            var result = RunProbe(
                "classification-cap-probe", probe, "-ScriptPath", FlowQaScriptPath, "-WorkingDirectory", directory);
            Assert.Equal(0, result.ExitCode);

            var json = SplitLines(result.StandardOutput).Last(static line => line.StartsWith("{", StringComparison.Ordinal));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(4, root.GetProperty("exitCode").GetInt32());
            Assert.True(root.GetProperty("truncated").GetBoolean());
            // The marker really did fall outside the bounded projection that gets written.
            Assert.False(root.GetProperty("projectionHasMarker").GetBoolean());
            Assert.False(root.GetProperty("diagnosticHasMarker").GetBoolean());
            Assert.Equal("capability-missing", root.GetProperty("withMarkers").GetString());
            Assert.NotEqual("capability-missing", root.GetProperty("withoutMarkers").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    static string[] RunPowerShellRedaction(IReadOnlyList<string> corpus)
        => RunPowerShellRedaction(corpus, ["FLOW-QA-SIGNING-IDENTITY-SENTINEL"]);

    static string[] RunPowerShellRedaction(IReadOnlyList<string> corpus, IReadOnlyList<string> secrets)
    {
        const string probe = """
            param([string] $ScriptPath, [string] $CorpusPath, [string] $SecretsPath)

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            $tokens = $null
            $errors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref] $tokens, [ref] $errors)
            foreach ($assignment in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                            $node.Left.Extent.Text -eq '$script:SecretValues'
                    }, $true)) {
                . ([scriptblock]::Create($assignment.Extent.Text))
            }
            foreach ($definition in $ast.FindAll({
                        param($node)
                        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                            $node.Name -in @('Register-SecretValue', 'Protect-DiagnosticText')
                    }, $true)) {
                . ([scriptblock]::Create($definition.Extent.Text))
            }

            foreach ($secret in [System.IO.File]::ReadAllLines($SecretsPath)) {
                Register-SecretValue $secret
            }
            foreach ($line in [System.IO.File]::ReadAllLines($CorpusPath)) {
                [Console]::Out.WriteLine((Protect-DiagnosticText $line))
            }
            """;

        var corpusPath = WriteCorpus(corpus);
        var secretsPath = WriteSecrets(corpusPath, secrets);
        try
        {
            var result = RunProbe(
                "redaction-parity-probe",
                probe,
                "-ScriptPath", FlowQaScriptPath,
                "-CorpusPath", corpusPath,
                "-SecretsPath", secretsPath);
            Assert.Equal(0, result.ExitCode);
            return SplitLines(result.StandardOutput);
        }
        finally
        {
            DeleteCorpus(corpusPath);
        }
    }

    static string[] RunShellRedaction(string bash, IReadOnlyList<string> corpus)
        => RunShellRedaction(bash, corpus, ["FLOW-QA-SIGNING-IDENTITY-SENTINEL"]);

    static string[] RunShellRedaction(string bash, IReadOnlyList<string> corpus, IReadOnlyList<string> secrets)
    {
        // The shipped functions are lifted out of the shipped file, so the harness cannot drift
        // away from what the entry point actually runs.
        const string harness = """
            set -uo pipefail
            MAX_DIAGNOSTIC_LINES=1000
            extract() {
              awk -v name="$1" 'index($0, name "() {") == 1 { printing = 1 } printing { print } printing && $0 == "}" { printing = 0 }' eng/devflow/Run-DevFlowFlowQa.sh
            }
            eval "$(extract register_secret_value)"
            eval "$(extract redact_stream)"
            [[ $(type -t register_secret_value) == function ]] || exit 90
            [[ $(type -t redact_stream) == function ]] || exit 91
            secret_values=()
            while IFS= read -r secret; do
              register_secret_value "$secret"
            done <"$2"
            redact_stream <"$1"
            """;

        var corpusPath = WriteCorpus(corpus);
        var secretsPath = WriteSecrets(corpusPath, secrets);
        var harnessPath = Path.Combine(Path.GetDirectoryName(corpusPath)!, "redaction-harness.sh");
        File.WriteAllText(harnessPath, harness.Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
        try
        {
            var result = RunProcess(bash, harnessPath, corpusPath, secretsPath);
            Assert.Equal(0, result.ExitCode);
            return SplitLines(result.StandardOutput);
        }
        finally
        {
            File.Delete(harnessPath);
            DeleteCorpus(corpusPath);
        }
    }

    static string WriteSecrets(string corpusPath, IReadOnlyList<string> secrets)
    {
        var path = Path.Combine(Path.GetDirectoryName(corpusPath)!, "secrets.txt");
        File.WriteAllText(path, string.Join("\n", secrets) + "\n", new System.Text.UTF8Encoding(false));
        return path;
    }

    static string WriteCorpus(IReadOnlyList<string> corpus)
    {
        var directory = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "corpus.txt");
        File.WriteAllText(path, string.Join("\n", corpus) + "\n", new System.Text.UTF8Encoding(false));
        return path;
    }

    static void DeleteCorpus(string corpusPath)
    {
        var directory = Path.GetDirectoryName(corpusPath);
        if (directory is not null && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    static string[] SplitLines(string value)
        // Kept positional: an empty line has to stay in place, or a redactor that ever emits one
        // would shift the comparison onto the wrong corpus entry.
        => value.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    static ArtifactFacts ReadArtifactFacts(string artifactRoot)
    {
        var manifestPath = Path.Combine(artifactRoot, "manifest.json");
        var flowRunPath = Path.Combine(artifactRoot, "flow-run.json");
        Assert.True(File.Exists(manifestPath), $"No manifest at '{manifestPath}'.");
        Assert.True(File.Exists(flowRunPath), $"No flow run report at '{flowRunPath}'.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var flowRun = JsonDocument.Parse(File.ReadAllText(flowRunPath));
        var paths = manifest.RootElement.GetProperty("artifacts").EnumerateArray()
            .Select(static artifact => artifact.GetProperty("path").GetString()!)
            .ToArray();
        var flowRunEntry = manifest.RootElement.GetProperty("artifacts").EnumerateArray()
            .Single(static artifact => artifact.GetProperty("path").GetString()!
                .EndsWith("/flow-run.json", StringComparison.Ordinal));

        return new ArtifactFacts(
            OmissionKinds(manifest.RootElement),
            OmissionKinds(flowRun.RootElement),
            SummaryFacts(manifest.RootElement),
            SummaryFacts(flowRun.RootElement),
            paths,
            paths.Select(path => path[(path.IndexOf("/ios/", StringComparison.Ordinal) + 1)..]).ToArray(),
            manifest.RootElement.GetProperty("artifactSummary").GetProperty("recordedArtifacts").GetInt32(),
            "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(flowRunPath))).ToLowerInvariant(),
            flowRunEntry.GetProperty("sha256").GetString()!);

        static string[] OmissionKinds(JsonElement root)
            => root.GetProperty("omissions").EnumerateArray()
                .Select(static omission => omission.GetProperty("kind").GetString()!)
                .ToArray();

        // Compared by value, not by formatting: the shell writes compact JSON and PowerShell
        // writes it indented, and neither is a fact about the run.
        static string SummaryFacts(JsonElement root)
        {
            var summary = root.GetProperty("artifactSummary");
            return string.Join(
                ";",
                $"maxArtifacts={summary.GetProperty("maxArtifacts").GetInt32()}",
                $"recordedArtifacts={summary.GetProperty("recordedArtifacts").GetInt32()}",
                $"omittedArtifacts={summary.GetProperty("omittedArtifacts").GetInt32()}",
                $"truncated={summary.GetProperty("truncated").GetBoolean()}");
        }
    }

    sealed record ArtifactFacts(
        string[] ManifestOmissions,
        string[] FlowRunOmissions,
        string ManifestSummary,
        string FlowRunSummary,
        string[] Paths,
        string[] RelativePaths,
        int RecordedArtifacts,
        string FlowRunDigest,
        string RecordedFlowRunDigest)
    {
        public string Summary => ManifestSummary;
    }

    [Fact]
    public void RestoredEngineeringScripts_DeclareTheSameMinimumPowerShellVersion()
    {
        foreach (var name in new[]
                 {
                     "Run-DevFlowFlowQa.ps1",
                     "Finalize-DevFlowFlowPilotManifest.ps1",
                     "Measure-ReplayStability.ps1",
                     "Publish-DevFlowFailureIssue.ps1",
                 })
        {
            var script = File.ReadAllText(Path.Combine(RepositoryRoot, "eng", "devflow", name));
            Assert.StartsWith("#Requires -Version 7.3", script, StringComparison.Ordinal);
        }

        var documentation = File.ReadAllText(Path.Combine(RepositoryRoot, "docs", "DevFlow", "flow-qa.md"));
        Assert.Contains("PowerShell 7.3 or later", documentation, StringComparison.Ordinal);
        Assert.Contains("pwsh ./eng/devflow/Run-DevFlowFlowQa.ps1", documentation, StringComparison.Ordinal);
    }

    /// <summary>
    /// The parity tests skip where bash is absent, which is honest but silent, so a lane has to
    /// turn that skip into a failure - and it has to be a lane every DevFlow pull request selects.
    /// Requiring them only on the label-gated Windows lane meant the shell entry point could stop
    /// being covered for every unlabeled pull request without a single red check. This reads the
    /// selection rules the workflow actually evaluates rather than trusting the text of an env
    /// block: the requiring lane must be gated on the path canary alone, and the plan output it is
    /// gated on must be derived without consulting any label.
    /// </summary>
    [Fact]
    public void ShellEntryPointTests_AreRequiredOnALaneEveryDevFlowPullRequestSelects()
    {
        var workflowPath = Path.Combine(RepositoryRoot, ".github", "workflows", "devflow-integration.yml");
        var yaml = new YamlStream();
        using (var reader = new StringReader(File.ReadAllText(workflowPath)))
            yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)root.Children[new YamlScalarNode("jobs")];
        var plan = (YamlMappingNode)jobs.Children[new YamlScalarNode("plan")];

        var requiring = jobs.Children
            .Select(pair => (Name: ((YamlScalarNode)pair.Key).Value!, Job: (YamlMappingNode)pair.Value))
            .Where(static entry => RequiresShellTests(entry.Job))
            .ToArray();
        Assert.NotEmpty(requiring);

        // The discrimination this test makes has to be real, so the label-gated lane is checked
        // too: if "run-windows" ever stopped being label-gated, the assertion below would pass for
        // the wrong reason.
        Assert.True(
            IsLabelGated(plan, "run-windows"),
            "run-windows is no longer label-gated, so this test can no longer tell the two lanes apart.");
        Assert.False(IsLabelGated(plan, "devflow-paths-changed"));

        var unlabeled = requiring
            .Where(entry =>
            {
                Assert.True(
                    entry.Job.Children.TryGetValue(new YamlScalarNode("if"), out var condition),
                    $"The '{entry.Name}' job requires the shell tests but states no selection condition.");
                var referenced = PlanOutputsReferencedBy(((YamlScalarNode)condition).Value!);
                return referenced.Contains("devflow-paths-changed") &&
                    referenced.All(output => !IsLabelGated(plan, output));
            })
            .ToArray();
        Assert.NotEmpty(unlabeled);

        // That lane has to actually run the tests that cover the shell entry point.
        Assert.Contains(
            unlabeled,
            entry => StepsOf(entry.Job).Any(static step =>
                RunTextOf(step) is { } run &&
                run.Contains("Microsoft.Maui.DevFlow.Agent.IntegrationTests", StringComparison.Ordinal) &&
                run.Contains("FlowQaScriptTests", StringComparison.Ordinal)));

        // devflow-flow-gate grades that lane, so its failure is a red required check rather than
        // an advisory one.
        var gate = (YamlMappingNode)jobs.Children[new YamlScalarNode("devflow-flow-gate")];
        var graded = ((YamlSequenceNode)gate.Children[new YamlScalarNode("needs")])
            .Select(node => ((YamlScalarNode)node).Value!)
            .ToArray();
        Assert.Contains(unlabeled, entry => graded.Contains(entry.Name));
    }

    static bool RequiresShellTests(YamlMappingNode job)
    {
        if (DeclaresShellRequirement(job))
            return true;

        return StepsOf(job).Any(DeclaresShellRequirement);
    }

    static bool DeclaresShellRequirement(YamlMappingNode node)
        => node.Children.TryGetValue(new YamlScalarNode("env"), out var env) &&
            env is YamlMappingNode mapping &&
            mapping.Children.TryGetValue(new YamlScalarNode("DEVFLOW_REQUIRE_SHELL_TESTS"), out var value) &&
            ((YamlScalarNode)value).Value == "1";

    static IEnumerable<YamlMappingNode> StepsOf(YamlMappingNode job)
        => job.Children.TryGetValue(new YamlScalarNode("steps"), out var steps) && steps is YamlSequenceNode sequence
            ? sequence.OfType<YamlMappingNode>()
            : [];

    static string? RunTextOf(YamlMappingNode step)
        => step.Children.TryGetValue(new YamlScalarNode("run"), out var run) ? ((YamlScalarNode)run).Value : null;

    static string[] PlanOutputsReferencedBy(string condition)
        => System.Text.RegularExpressions.Regex
            .Matches(condition, @"needs\.plan\.outputs\.([A-Za-z0-9._-]+)")
            .Select(static match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Resolves a plan output to the shell that produces it and reports whether that shell reads a
    /// label at all. This is the selection semantics the workflow evaluates, not the wording of
    /// the condition.
    /// </summary>
    static bool IsLabelGated(YamlMappingNode plan, string outputName)
    {
        var outputs = (YamlMappingNode)plan.Children[new YamlScalarNode("outputs")];
        var expression = ((YamlScalarNode)outputs.Children[new YamlScalarNode(outputName)]).Value!;
        var reference = System.Text.RegularExpressions.Regex.Match(
            expression, @"steps\.([A-Za-z0-9._-]+)\.outputs\.([A-Za-z0-9._-]+)");
        Assert.True(reference.Success, $"'{outputName}' is not produced by a step of the plan job.");

        var stepId = reference.Groups[1].Value;
        var stepOutput = reference.Groups[2].Value;
        var step = StepsOf(plan).Single(candidate =>
            candidate.Children.TryGetValue(new YamlScalarNode("id"), out var id) &&
            ((YamlScalarNode)id).Value == stepId);
        var run = RunTextOf(step)!;

        // A decide-style step routes each output through a named function; the path canary is
        // computed inline. Either way the question is the same: does the shell that decides this
        // output consult a label?
        var routed = System.Text.RegularExpressions.Regex.Match(
            run, @"echo\s+""" + System.Text.RegularExpressions.Regex.Escape(stepOutput) + @"=\$\(([a-z_]+)");
        if (!routed.Success)
            return run.Contains("LABEL", StringComparison.Ordinal);

        var function = routed.Groups[1].Value;
        var body = System.Text.RegularExpressions.Regex.Match(
            run,
            @"^\s*" + System.Text.RegularExpressions.Regex.Escape(function) + @"\(\) \{.*?^\s*\}",
            System.Text.RegularExpressions.RegexOptions.Singleline |
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(body.Success, $"The '{function}' selection function was not found in the plan job.");
        return body.Value.Contains("LABEL", StringComparison.Ordinal);
    }

    static string FlowQaScriptPath =>
        Path.Combine(RepositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.ps1");

    /// <summary>
    /// A checked-in Apple agent source tree means the runtime proof is required but not yet
    /// established; no source at all means the spike itself is still pending. Asserting one of them
    /// unconditionally makes the test a statement about the checkout rather than about the script.
    /// </summary>
    static string ExpectedAppleCapabilityState(JsonElement dryRun)
        => dryRun.GetProperty("capability").GetProperty("sourceAvailable").GetBoolean()
            ? "proof-required"
            : "pending-spike";

    static void WriteProbeFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
    }

    static ProcessResult RunProbe(string name, string probe, params string[] arguments)
    {
        var directory = Path.Combine(
            RepositoryRoot, "artifacts", "TestResults", "devflow-flow-qa-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, $"{name}.ps1");
        try
        {
            File.WriteAllText(probePath, probe, new System.Text.UTF8Encoding(false));
            var executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
            var invocation = new List<string> { "-NoLogo", "-NoProfile", "-File", probePath };
            invocation.AddRange(arguments);
            return RunProcess(executable, null, invocation.ToArray());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    static string ResultsRoot(string platform)
        => Path.Combine(RepositoryRoot, "artifacts", "TestResults", "devflow-flow", platform);

    static ProcessResult RunPowerShell(params string[] arguments)
    {
        return RunPowerShell(null, arguments);
    }

    static ProcessResult RunPowerShell(
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
    {
        var script = Path.Combine(RepositoryRoot, "eng", "devflow", "Run-DevFlowFlowQa.ps1");
        var executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        var invocation = new List<string> { "-NoProfile", "-File", script };
        invocation.AddRange(arguments);
        return RunProcess(executable, environment, invocation.ToArray());
    }

    static string? FindBash()
    {
        if (!OperatingSystem.IsWindows())
            return "bash";

        var gitBash = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git",
            "bin",
            "bash.exe");
        return File.Exists(gitBash) ? gitBash : null;
    }

    /// <summary>
    /// The Windows integration lane sets <c>DEVFLOW_REQUIRE_SHELL_TESTS=1</c>, so CI fails rather
    /// than silently passing when the shell entry point was not exercised. Other hosts may omit
    /// shell coverage after writing an explicit test-output notice.
    /// </summary>
    static bool TryGetRequiredBash([NotNullWhen(true)] out string? bash)
    {
        bash = FindBash();
        if (bash is not null)
            return true;

        if (string.Equals(
                Environment.GetEnvironmentVariable("DEVFLOW_REQUIRE_SHELL_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Fail(
                "bash was not found, so the shell entry point is untested. " +
                "Install Git for Windows or clear DEVFLOW_REQUIRE_SHELL_TESTS.");
        }

        Console.WriteLine(
            "bash was not found, so the shell entry point could not be exercised on this host.");
        return false;
    }

    /// <summary>Writes a PATH shim and makes it executable on POSIX hosts.</summary>
    static void WriteExecutableShim(string path, string script)
    {
        File.WriteAllText(path, script, new System.Text.UTF8Encoding(false));
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    static ProcessResult RunProcess(string fileName, params string[] arguments)
        => RunProcess(fileName, null, arguments);

    static ProcessResult RunProcess(
        string fileName,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
                process.StartInfo.Environment[key] = value;
        }
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        // Both pipes are drained at once. Reading one to the end first deadlocks as soon as the
        // other fills its buffer, which is exactly what a chatty failure does.
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(120_000), $"Timed out running {fileName}.");
        Assert.True(
            Task.WaitAll([standardOutputTask, standardErrorTask], 30_000),
            $"Timed out draining the output of {fileName}.");
        return new ProcessResult(
            process.ExitCode,
            standardOutputTask.Result.Trim(),
            standardErrorTask.Result.Trim());
    }

    sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
