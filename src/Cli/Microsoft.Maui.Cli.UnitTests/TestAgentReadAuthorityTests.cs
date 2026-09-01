using System.Reflection;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Pins the read-authority tier of every restricted test-agent tool, so the split between
/// "needs an exact target" and "needs the authoring session's read capability" is a decision the
/// code states rather than an accident of parameter lists.
///
/// <c>maui_test_layout_diagnostics</c> is deliberately in the pre-capability discovery tier with
/// <c>maui_test_agents</c>, <c>maui_test_status</c>, and <c>maui_test_capabilities</c>: it reads
/// only the live app's structure. <c>maui_test_improvements</c> requires an envelope because it
/// reads the broker-owned draft plan and flow. Requiring an envelope for the layout scan would make
/// a pure read create authoring session state as a side effect. What bounds the layout scan is its
/// value-free projection, which the tests below assert directly.
/// </summary>
public sealed class TestAgentReadAuthorityTests
{
    /// <summary>Tools that resolve an exact target and read no broker-owned session state.</summary>
    private static readonly string[] PreCapabilityTools =
    [
        "maui_test_agents",
        "maui_test_capabilities",
        "maui_test_layout_diagnostics",
        "maui_test_status",
    ];

    [Fact]
    public void PreCapabilityTools_TakeNoSessionReadCapability()
    {
        foreach (var method in ToolMethods().Where(entry => PreCapabilityTools.Contains(entry.Name, StringComparer.Ordinal)))
        {
            var parameterTypes = method.Method.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.DoesNotContain(typeof(MauiTestAgentRequestEnvelope), parameterTypes);
            // maui_test_status may accept an OPTIONAL session access request; nothing in this tier
            // may require one, and the layout scan takes none at all.
            if (method.Name is "maui_test_layout_diagnostics")
                Assert.DoesNotContain(typeof(MauiTestAgentSessionAccessRequest), parameterTypes);
        }
    }

    [Fact]
    public void LayoutDiagnostics_RequiresAnExactTargetAndNothingElseAuthoritative()
    {
        var method = Assert.Single(
            ToolMethods(),
            entry => entry.Name == "maui_test_layout_diagnostics").Method;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(McpAgentSession), parameters[0].ParameterType);
        Assert.Equal(typeof(MauiTestAgentTarget), parameters[1].ParameterType);
        Assert.Equal(
            new[] { "session", "target", "elementId", "maxElements" },
            parameters.Select(parameter => parameter.Name).ToArray());
        Assert.Equal(typeof(TestAgentDiscoveryTools), method.DeclaringType);
    }

    [Fact]
    public void Improvements_StillRequiresTheSessionEnvelopeBecauseItReadsTheDraft()
    {
        var method = Assert.Single(
            ToolMethods(),
            entry => entry.Name == "maui_test_improvements").Method;

        Assert.Contains(
            typeof(MauiTestAgentRequestEnvelope),
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void LayoutDiagnostics_DeclaresItsTierInTheToolDescription()
    {
        var method = Assert.Single(
            ToolMethods(),
            entry => entry.Name == "maui_test_layout_diagnostics").Method;
        var description = method
            .GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!
            .Description;

        Assert.Contains("pre-capability", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no session read capability", description, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The tier is only defensible while the projection stays value-free, so the request the tool
    /// issues and the shape it returns are asserted from source rather than trusted.
    /// </summary>
    [Fact]
    public void LayoutDiagnostics_ScansWithEvidenceOffSuppressionOffAndNoTextCapture()
    {
        var source = ToolSource();

        Assert.Contains("IncludeEvidence = false", source, StringComparison.Ordinal);
        Assert.Contains("SuppressionMode = LayoutSuppressionModes.Off", source, StringComparison.Ordinal);
        Assert.Contains("Privacy = new LayoutPrivacyOptions { Text = \"none\" }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutDiagnostics_ProjectionNamesTheOmissionsItActuallyMakes()
    {
        var source = ToolSource();

        foreach (var omission in new[]
                 {
                     "source-paths", "control-text", "control-values", "raw-evidence",
                     "policy-reasons", "screenshots", "logs", "network", "system-evidence",
                     "authoring-session-state", "mutation-authority",
                 })
        {
            Assert.Contains($"\"{omission}\"", source, StringComparison.Ordinal);
        }

        // The projection never reaches for a source location, evidence block, suppression key,
        // policy reason, or the system evidence this layer does not populate.
        var projection = Between(source, "var findings = report.Findings", "return TestAgentToolSupport.Success");
        foreach (var forbidden in new[]
                 {
                     "SourceFile", "SourceLine", "Evidence", "SuppressionKey",
                     "SuppressionReason", "SystemEvidence", "Text", "Value",
                 })
        {
            Assert.DoesNotContain(forbidden, projection, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void LayoutDiagnostics_ReportsItsReadAuthorityToTheCaller()
    {
        var source = ToolSource();

        Assert.Contains("readAuthority", source, StringComparison.Ordinal);
        Assert.Contains("tier = \"pre-capability-discovery\"", source, StringComparison.Ordinal);
        Assert.Contains("requiresExplicitTarget = true", source, StringComparison.Ordinal);
        Assert.Contains("requiresSessionReadCapability = false", source, StringComparison.Ordinal);
        Assert.Contains("readsAuthoringSession = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LayoutDiagnostics_DecisionIsDocumentedForReviewers()
    {
        foreach (var skill in new[]
                 {
                     Path.Combine(RepositoryRoot(), ".github", "skills", "maui-devflow-layout-diagnostics", "SKILL.md"),
                     Path.Combine(RepositoryRoot(), "plugins", "dotnet-maui", "skills", "maui-devflow-layout-diagnostics", "SKILL.md"),
                 })
        {
            var text = File.ReadAllText(skill);
            Assert.Contains("pre-capability discovery tier", text, StringComparison.Ordinal);
            Assert.Contains("maui_test_improvements", text, StringComparison.Ordinal);
        }

        var doc = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "DevFlow", "test-agent.md"));
        Assert.Contains("pre-capability", doc, StringComparison.OrdinalIgnoreCase);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' is missing from the tool source.");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"'{end}' is missing from the tool source.");
        return text[from..to];
    }

    private static string ToolSource()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "Tools", "TestAgentDiscoveryTools.cs"));
        var start = source.IndexOf(
            "[McpServerTool(Name = \"maui_test_layout_diagnostics\")",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "The layout diagnostics tool is missing.");
        var end = source.IndexOf("public sealed class TestAgentCapabilitiesTool", start, StringComparison.Ordinal);
        Assert.True(end > start, "The capabilities tool type is missing.");
        return source[start..end];
    }

    private static IEnumerable<(string Name, MethodInfo Method)> ToolMethods()
        => McpServerHost.TestAgentToolTypes
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(static method => (
                Attribute: method.GetCustomAttribute<McpServerToolAttribute>(),
                Method: method))
            .Where(static entry => entry.Attribute is not null)
            .Select(static entry => (entry.Attribute!.Name!, entry.Method));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
