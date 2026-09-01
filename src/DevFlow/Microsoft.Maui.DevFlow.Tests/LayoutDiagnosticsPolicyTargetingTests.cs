using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Diagnostics;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Layout suppression policy is a project-scoped review artifact, so it must be resolved from the
/// app under inspection.
///
/// The broker and the MCP server are started by an editor and routinely run in a different
/// repository from the running app, so probing their working directory would silently apply one
/// project's approved suppressions to another project's findings — while still reporting those
/// findings as suppressed. These tests pin resolution to the agent's registered project and prove
/// that "no project root known" degrades to the disclosed user-wide policy rather than to whatever
/// directory this process happens to be in.
/// </summary>
public sealed class LayoutDiagnosticsPolicyTargetingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"devflow-layout-targeting-{Guid.NewGuid():N}");

    public LayoutDiagnosticsPolicyTargetingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "app"));
        Directory.CreateDirectory(Path.Combine(_root, "unrelated"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_ReadsThePolicyOfThePinnedProjectAndNotAnotherOne()
    {
        var app = Path.Combine(_root, "app");
        var unrelated = Path.Combine(_root, "unrelated");
        WritePolicy(app, "app-fingerprint");
        WritePolicy(unrelated, "unrelated-fingerprint");
        // Both files are discoverable through the loader, so the choice is the pin, not availability.
        Assert.Equal(
            "unrelated-fingerprint",
            Assert.Single(LayoutDiagnosticsPolicyLoader.LoadProjectPolicy(unrelated).Suppressions).Fingerprint);

        var request = LayoutDiagnosticsCoordinator.CreateRequest(profile: "ci", policyStartPath: app);

        Assert.Equal("app-fingerprint", Assert.Single(request.Suppressions).Fingerprint);
    }

    [Fact]
    public void Scan_WithoutAKnownProjectRoot_LoadsNoProjectPolicyAtAll()
    {
        WritePolicy(Path.Combine(_root, "unrelated"), "unrelated-fingerprint");

        // "ci" also excludes the user-wide policy, so an unpinned scan must produce nothing rather
        // than adopting whichever `.mauidevflow` the ambient directory tree happens to expose.
        var request = LayoutDiagnosticsCoordinator.CreateRequest(profile: "ci", policyStartPath: null);

        Assert.Empty(request.Suppressions);
    }

    /// <summary>
    /// An empty or whitespace root is a caller mistake, not "no root". Left unguarded it reaches
    /// the loader, which reads a blank start path as "probe my working directory" — the exact
    /// wrong-project failure the pin exists to prevent — so the coordinator refuses it at the
    /// boundary instead of producing a report built from an unrelated project's policy.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Scan_WithABlankProjectRoot_IsRejectedRatherThanProbingTheWorkingDirectory(string blank)
    {
        var rejected = Assert.Throws<ArgumentException>(() =>
            LayoutDiagnosticsCoordinator.CreateRequest(profile: "ci", policyStartPath: blank));

        Assert.Equal("policyStartPath", rejected.ParamName);
        Assert.Contains("working directory", rejected.Message, StringComparison.Ordinal);
        // A genuinely absent root is a different, supported case and stays supported.
        Assert.Empty(
            LayoutDiagnosticsCoordinator.CreateRequest(profile: "ci", policyStartPath: null).Suppressions);
    }

    /// <summary>
    /// The guard above is load-bearing precisely because the loader itself treats a blank start
    /// path as the working directory. Asserting that here keeps the guard from looking redundant
    /// and being deleted.
    /// </summary>
    [Fact]
    public void PolicyLoader_ReadsABlankStartPathAsTheWorkingDirectory()
    {
        var blank = LayoutDiagnosticsPolicyLoader.ResolveProjectConfigPath("");
        var pinned = LayoutDiagnosticsPolicyLoader.ResolveProjectConfigPath(Path.Combine(_root, "app"));

        Assert.NotEqual(
            Path.GetFullPath(blank),
            Path.GetFullPath(pinned));
        Assert.Equal(
            Path.GetFullPath(LayoutDiagnosticsPolicyLoader.ResolveProjectConfigPath(null)),
            Path.GetFullPath(blank));
    }

    [Fact]
    public void Scan_WithSuppressionsOff_LoadsNoPolicyAtAll()    {
        WritePolicy(Path.Combine(_root, "app"), "app-fingerprint");

        var request = LayoutDiagnosticsCoordinator.CreateRequest(
            suppressionMode: LayoutSuppressionModes.Off,
            policyStartPath: Path.Combine(_root, "app"));

        Assert.Empty(request.Suppressions);
        Assert.Equal(LayoutSuppressionModes.Off, request.SuppressionMode);
    }

    [Fact]
    public void Scan_KeepsTheDisclosedUserPolicyWhereverTheProjectRootPointsOrIsAbsent()
    {
        // The user-wide policy is machine-scoped and disclosed, so pinning the project root must
        // not change whether it participates. Only the "ci" profile drops it.
        var userPolicyCount = LayoutDiagnosticsPolicyLoader.LoadUserPolicy().Suppressions.Count;

        var pinned = LayoutDiagnosticsCoordinator.CreateRequest(
            profile: "agent",
            policyStartPath: Path.Combine(_root, "app"));
        var unpinned = LayoutDiagnosticsCoordinator.CreateRequest(profile: "agent", policyStartPath: null);

        Assert.Equal(userPolicyCount, pinned.Suppressions.Count);
        Assert.Equal(userPolicyCount, unpinned.Suppressions.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/App.csproj")]
    public void RegisteredProjectRoot_IsNullWhenTheAgentReportsNothingUsable(string project)
        => Assert.Null(McpAgentSession.ResolveRegisteredProjectRoot(
            new AgentRegistration { Project = project }));

    [Fact]
    public void RegisteredProjectRoot_IsNullWhenThereIsNoAgent()
        => Assert.Null(McpAgentSession.ResolveRegisteredProjectRoot(null));

    /// <summary>
    /// Resolving the root is an optional refinement, so it must fail soft on every failure — not on
    /// a hand-picked exception list. The lookup can launch a broker, open a socket, wait on a
    /// timeout, and touch the filesystem, and a filtered catch turns any unlisted failure of an
    /// optional step into a failed tool call.
    ///
    /// A catch that broad also hides real defects, so each one must leave a trace — and that trace
    /// must name only the exception type. A broker, agent, or path message can carry a filesystem
    /// path or app text, and a fail-soft diagnostic is not a licence to surface either.
    /// </summary>
    [Fact]
    public void AgentProjectRootLookup_CatchesEveryFailureNotAChosenFew()
    {
        foreach (var (relative, signature, terminator) in new[]
                 {
                     (Path.Combine("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "McpAgentSession.cs"),
                      "public async Task<string?> TryGetAgentProjectRootAsync",
                      "internal static string? ResolveRegisteredProjectRoot"),
                     (Path.Combine("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Broker", "BrokerClient.cs"),
                      "public static async Task<string?> TryResolveAgentProjectRootAsync",
                      "public static async Task<AgentRegistration?> ResolveAgentForProjectAsync"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), relative));
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{relative} no longer declares {signature}.");
            var end = source.IndexOf(terminator, start, StringComparison.Ordinal);
            Assert.True(end > start, $"{relative} no longer declares {terminator}.");
            var body = source[start..end];

            Assert.Contains("catch (Exception ex)", body, StringComparison.Ordinal);
            Assert.DoesNotContain("catch (Exception ex) when", body, StringComparison.Ordinal);
            Assert.DoesNotContain("catch (Exception) when", body, StringComparison.Ordinal);

            // The unexpected failure is traced, and the trace names the type and nothing else.
            Assert.Contains("Trace.WriteLine", body, StringComparison.Ordinal);
            Assert.Contains("ex.GetType().Name", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ex.Message", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ex.ToString()", body, StringComparison.Ordinal);
            Assert.DoesNotContain("{ex}", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BrokerProjectRootLookup_ReturnsNullWhenNoAgentAnswers()
    {
        // Port 1 can never carry a DevFlow agent, so this exercises the unreachable path without
        // depending on whether a broker happens to be running on this machine.
        Assert.Null(await BrokerClient.TryResolveAgentProjectRootAsync(1));
    }

    [Fact]
    public void RegisteredProjectRoot_ResolvesADirectoryToItself()
    {
        var directory = Path.Combine(_root, "app");

        Assert.Equal(
            Path.GetFullPath(directory),
            McpAgentSession.ResolveRegisteredProjectRoot(new AgentRegistration { Project = directory }));
    }

    [Fact]
    public void RegisteredProjectRoot_ResolvesAProjectFileToItsDirectory()
    {
        var projectFile = Path.Combine(_root, "app", "App.csproj");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_root, "app")),
            McpAgentSession.ResolveRegisteredProjectRoot(new AgentRegistration { Project = projectFile }));
    }

    [Fact]
    public void RegisteredProjectRoot_ResolvesTheRootThePolicyIsThenReadFrom()
    {
        WritePolicy(Path.Combine(_root, "app"), "app-fingerprint");
        var resolved = McpAgentSession.ResolveRegisteredProjectRoot(new AgentRegistration
        {
            Project = Path.Combine(_root, "app", "App.csproj"),
        });

        var request = LayoutDiagnosticsCoordinator.CreateRequest(profile: "ci", policyStartPath: resolved);

        Assert.Equal("app-fingerprint", Assert.Single(request.Suppressions).Fingerprint);
    }

    /// <summary>
    /// A committed flow file and the app it drives are independent inputs and routinely live in
    /// different directories, so failure-evidence capture during a flow run must pin policy to the
    /// bound app's registration rather than to the flow's own folder. The pin is a required
    /// constructor argument, so a call site cannot silently fall back; end-to-end delivery of the
    /// pinned policy is covered by <c>LayoutDiagnosticsAgentTests</c>.
    /// </summary>
    [Fact]
    public void FlowReplayEvidence_RequiresAnExplicitLayoutPolicyRoot()
    {
        var constructor = Assert.Single(typeof(FlowReplayEvidenceCapture).GetConstructors());
        var parameters = constructor.GetParameters();
        var policy = Assert.Single(
            parameters,
            parameter => parameter.Name == "layoutPolicyStartPath");

        Assert.False(policy.IsOptional);
        // It must not be positionally confusable with the flow-file hint, which is a different
        // concept: that one only rewrites source paths.
        Assert.Equal("projectHint", parameters[policy.Position - 1].Name);
    }

    /// <summary>
    /// Exercises the adapter's real request builder — the same one <c>CaptureAsync</c> uses — so an
    /// argument swap between the flow-file hint and the policy root, or a dropped assignment, fails
    /// here rather than shipping.
    /// </summary>
    [Fact]
    public void FlowReplayEvidence_AsksForTheAppsPolicyRootNotTheFlowsFolder()
    {
        var app = Path.Combine(_root, "app");
        var flowFolder = Path.Combine(_root, "unrelated");

        var capture = new FlowReplayEvidenceCapture(
            client: null!,
            outputPath: Path.Combine(flowFolder, "failure.mauitrace"),
            projectHint: flowFolder,
            layoutPolicyStartPath: new AgentRegistration
            {
                Project = Path.Combine(app, "App.csproj"),
            }.ResolveProjectRoot(),
            source: "flow-run");

        var request = capture.CreateRequest(new MauiFlow { Name = "Sample" }, null);

        Assert.Equal(Path.GetFullPath(app), request.LayoutPolicyStartPath);
        // The app's source paths follow the app too. The flow-file directory is not a project root
        // at all, so leaving this unset would drop every app path to a bare file name.
        Assert.Equal(Path.GetFullPath(app), request.SourcePathRoot);
        Assert.Equal(flowFolder, request.ProjectHint);
        Assert.Equal("flow-run", request.Source);
        Assert.NotEqual(request.ProjectHint, request.LayoutPolicyStartPath);
        Assert.NotEqual(request.ProjectHint, request.SourcePathRoot);
    }

    [Fact]
    public void FlowReplayEvidence_RequestedPolicyRootIsWhatTheScanThenReads()
    {
        var app = Path.Combine(_root, "app");
        var flowFolder = Path.Combine(_root, "unrelated");
        WritePolicy(app, "app-fingerprint");
        WritePolicy(flowFolder, "flow-folder-fingerprint");

        var request = new FlowReplayEvidenceCapture(
            client: null!,
            outputPath: null,
            projectHint: flowFolder,
            layoutPolicyStartPath: app,
            source: "flow-run").CreateRequest(new MauiFlow { Name = "Sample" }, null);

        var scan = LayoutDiagnosticsCoordinator.CreateRequest(
            profile: "ci",
            policyStartPath: request.LayoutPolicyStartPath);

        Assert.Equal("app-fingerprint", Assert.Single(scan.Suppressions).Fingerprint);
    }

    [Fact]
    public void FlowReplayEvidence_ProductionCallSitesNeverPassTheFlowFolderAsThePolicyRoot()
    {
        foreach (var relative in new[]
                 {
                     Path.Combine("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Execution", "FlowExecutionCoordinator.cs"),
                     Path.Combine("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Flows", "FlowTools.cs"),
                     Path.Combine("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "DevFlowCommands.cs"),
                 })
        {
            var source = File.ReadAllText(Path.Combine(RepositoryRoot(), relative));
            var arguments = BalancedCall(source, "FlowReplayEvidenceCapture(");
            // The policy root is the fourth argument, immediately before the source label.
            var policyRoot = ArgumentAt(arguments, 3);

            Assert.DoesNotContain("FlowPath", policyRoot, StringComparison.Ordinal);
            Assert.DoesNotContain("read.Path", policyRoot, StringComparison.Ordinal);
            Assert.DoesNotContain("GetFullPath(file)", policyRoot, StringComparison.Ordinal);
            Assert.True(
                policyRoot.Contains("ResolveProjectRoot()", StringComparison.Ordinal) ||
                policyRoot.Contains("appProjectRoot", StringComparison.Ordinal) ||
                policyRoot.Contains("replayPolicyRoot", StringComparison.Ordinal),
                $"{relative} does not pin the layout policy root to the app project. Saw: {policyRoot}");
        }
    }

    /// <summary>
    /// Splits a balanced call's argument list at top-level commas so an assertion can address a
    /// specific positional argument rather than the whole expression.
    /// </summary>
    private static string ArgumentAt(string call, int index)
    {
        var inner = call[(call.IndexOf('(') + 1)..call.LastIndexOf(')')];
        var arguments = new List<string>();
        var depth = 0;
        var start = 0;
        for (var position = 0; position < inner.Length; position++)
        {
            var character = inner[position];
            if (character is '(' or '[') depth++;
            else if (character is ')' or ']') depth--;
            else if (character == ',' && depth == 0)
            {
                arguments.Add(inner[start..position]);
                start = position + 1;
            }
        }
        arguments.Add(inner[start..]);
        Assert.True(arguments.Count > index, $"Expected more than {index} arguments in: {call}");
        return arguments[index].Trim();
    }

    /// <summary>
    /// Evidence capture reaches the layout scan through the shared coordinator, so the pinned root
    /// must arrive there as <c>policyStartPath</c> and not be quietly dropped.
    /// </summary>
    [Fact]
    public void EvidenceDataSource_ForwardsThePinnedRootAsThePolicyStartPath()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Evidence", "IEvidenceDataSource.cs"));
        var call = BalancedCall(source, "LayoutDiagnosticsCoordinator.ScanAsync(");

        Assert.Contains("policyStartPath: layoutPolicyStartPath", call, StringComparison.Ordinal);
    }

    private static string BalancedCall(string source, string opening)
    {
        var start = source.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{opening}' is missing.");
        var index = start + opening.Length;
        var depth = 1;
        while (index < source.Length && depth > 0)
        {
            if (source[index] == '(') depth++;
            else if (source[index] == ')') depth--;
            index++;
        }
        Assert.Equal(0, depth);
        // Comments carry commas and parentheses of their own, so they are removed before the
        // argument list is split.
        return string.Join(
            "\n",
            source[start..index]
                .Split('\n')
                .Select(line =>
                {
                    var comment = line.IndexOf("//", StringComparison.Ordinal);
                    return comment < 0 ? line : line[..comment];
                }));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static void WritePolicy(string directory, string fingerprint)
        => File.WriteAllText(
            Path.Combine(directory, ".mauidevflow"),
            $$"""
            {
              "layoutDiagnostics": {
                "suppressions": [
                  { "fingerprint": "{{fingerprint}}", "reason": "Reviewed" }
                ]
              }
            }
            """);
}
