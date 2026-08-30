using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The restricted profile's tool list is a security claim, not a convenience: the skill tells an
/// agent that these tools are the whole surface, and the docs tell a reviewer the same. Drift in
/// either direction is a real defect — an undocumented tool is an unreviewed capability, and a
/// documented-but-absent tool sends an agent to call something that will never answer.
/// </summary>
public sealed class RestrictedMcpToolInventoryTests
{
    private static readonly string[] Expected =
    [
        "maui_test_action",
        "maui_test_agents",
        "maui_test_assertion",
        "maui_test_author",
        "maui_test_capabilities",
        "maui_test_explore",
        "maui_test_failure",
        "maui_test_improvements",
        "maui_test_layout_diagnostics",
        "maui_test_patch",
        "maui_test_run",
        "maui_test_status",
        "maui_test_trace",
        "maui_test_validate",
    ];

    [Fact]
    public void TestAgentProfile_ExposesExactlyFourteenTools()
    {
        var inventory = McpServerHost.GetToolInventory(
            McpServerProfile.TestAgent,
            PreviewFlags());

        Assert.Equal(14, inventory.Count);
        Assert.Equal(Expected, inventory);
    }

    [Fact]
    public void TestAgentProfile_ExposesNothingWhenAgentAuthoringPreviewIsOff()
    {
        var inventory = McpServerHost.GetToolInventory(
            McpServerProfile.TestAgent,
            MauiPreviewFeatureFlags.CreateDefault());

        Assert.Empty(inventory);
    }

    /// <summary>
    /// The published inventory is a hand-maintained string list, so on its own it proves only that
    /// the list did not change — not that it describes the code. This walks the authoritative
    /// registered types and asserts that the <c>[McpServerTool]</c> names actually defined on them
    /// are exactly that list, so adding a tool method to an already-registered type, renaming one,
    /// or deleting one fails here instead of shipping an undeclared or phantom tool.
    /// </summary>
    [Fact]
    public void RegisteredToolTypes_DefineExactlyTheDeclaredToolNames()
    {
        var declared = McpServerHost.TestAgentToolTypes
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(static method => method.GetCustomAttribute<McpServerToolAttribute>())
            .Where(static attribute => attribute is not null)
            .Select(static attribute => attribute!.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Expected, declared);
        Assert.Equal(
            Expected,
            McpServerHost.GetToolInventory(McpServerProfile.TestAgent, PreviewFlags()));
    }

    /// <summary>
    /// Reflection alone cannot see the registration itself: a type could define its tools and never
    /// be handed to the builder, or be handed to the builder and be absent from the authoritative
    /// list. The builder takes each type through a generic <c>WithTools&lt;T&gt;()</c> call, so the
    /// registrations are only readable from source. This ties the two together.
    /// </summary>
    [Fact]
    public void RegisteredToolTypes_MatchTheWithToolsCallsInTheTestAgentBranch()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "McpServerHost.cs"));

        var start = source.IndexOf("if (profile == McpServerProfile.TestAgent)", StringComparison.Ordinal);
        Assert.True(start >= 0, "The test-agent registration branch is missing.");
        var end = source.IndexOf("else if (profile == McpServerProfile.Full)", start, StringComparison.Ordinal);
        Assert.True(end > start, "The full-profile registration branch is missing.");

        var registered = Regex.Matches(source[start..end], @"\.WithTools<(?<type>[A-Za-z0-9_]+)>\(\)")
            .Select(match => match.Groups["type"].Value)
            .ToArray();

        Assert.Equal(
            McpServerHost.TestAgentToolTypes.Select(static type => type.Name).ToArray(),
            registered);
    }

    [Fact]
    public void SkillInventory_MatchesTheProfileExactly()
    {
        foreach (var skill in SkillRoots())
        {
            var text = File.ReadAllText(Path.Combine(skill, "SKILL.md"));
            Assert.Contains(
                "exposes exactly these 14 tools",
                text,
                StringComparison.Ordinal);
            Assert.Equal(Expected, ToolNames(text).Order(StringComparer.Ordinal).ToArray());
        }
    }

    [Fact]
    public void DocumentedInventory_MatchesTheProfileExactly()
    {
        var testAgentDoc = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "DevFlow", "test-agent.md"));
        var inventorySection = Section(testAgentDoc, "## Tool inventory");

        Assert.Equal(Expected, ToolNames(inventorySection).Order(StringComparer.Ordinal).ToArray());

        var mcpDoc = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "docs", "DevFlow", "testing-mcp-server.md"));
        Assert.Contains("exactly these 14 tools", mcpDoc, StringComparison.Ordinal);
    }

    /// <summary>
    /// The published inventory is what an agent trusts. Nothing under the skills, docs, evaluations,
    /// or agent definitions may name a <c>maui_test_*</c> tool this profile does not serve, because
    /// an agent that reads the name will call it and stall on a tool the broker never answers.
    /// </summary>
    [Fact]
    public void NothingAdvertisesToolsThisLayerDoesNotServe()
    {
        var roots = new List<string>(SkillRoots())
        {
            Path.Combine(RepositoryRoot(), "docs", "DevFlow"),
            Path.Combine(RepositoryRoot(), "tests", "dotnet-maui", "maui-devflow-test"),
            Path.Combine(RepositoryRoot(), ".github", "agents"),
        };

        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".md" or ".yaml" or ".yml"))
                    continue;
                foreach (var name in ToolNames(File.ReadAllText(file)).Distinct(StringComparer.Ordinal))
                {
                    Assert.True(
                        Expected.Contains(name, StringComparer.Ordinal),
                        $"{file} advertises '{name}', which the test-agent profile does not expose.");
                }
            }
        }
    }

    private static IEnumerable<string> ToolNames(string text)
        => Regex.Matches(text, @"maui_test_[a-z_]+")
            .Select(match => match.Value)
            .Where(name => !name.EndsWith('_'))
            .Distinct(StringComparer.Ordinal);

    private static string Section(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{heading}' is missing.");
        var end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    private static MauiPreviewFeatureFlags PreviewFlags()
        => MauiPreviewFeatureFlagConfiguration.FromEnvironment(name => name switch
        {
            "DEVFLOW_PREVIEW_AGENT_AUTHORING" => "true",
            _ => null,
        });

    private static string[] SkillRoots() =>
    [
        Path.Combine(RepositoryRoot(), ".github", "skills", "maui-devflow-test"),
        Path.Combine(RepositoryRoot(), "plugins", "dotnet-maui", "skills", "maui-devflow-test"),
    ];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
