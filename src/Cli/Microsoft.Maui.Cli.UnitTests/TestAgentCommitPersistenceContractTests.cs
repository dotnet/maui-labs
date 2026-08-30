using System.Text.Json;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// `maui_test_author commit` advances the broker-owned authoring session only. `WorkflowPlanStore`,
/// which actually writes the Markdown flow and plan sidecar, is reachable from exactly one place —
/// the Inspector's `/api/flows/commit` route — and the test-agent path never calls it. An agent that
/// read "commit ok" as "files written" reported success for files that were never created, so the
/// tool contract, the tool description, and the skill all have to say this out loud.
/// </summary>
public class TestAgentCommitPersistenceContractTests
{
    private static string RepoRoot()
    {
        // Worktrees make `.git` a file, not a directory, so the repository marker is the solution.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(params string[] segments)
        => File.ReadAllText(Path.Combine([RepoRoot(), .. segments]));

    [Fact]
    public void SessionCommit_DoesNotReachTheWorkspaceStore()
    {
        var service = Read("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Broker", "TestAgentSessionService.cs");

        // If this ever becomes false the tool may legitimately claim files were written, and the
        // persistence note below must be revisited rather than silently left lying.
        Assert.DoesNotContain("WorkflowPlanStore", service, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoringTool_ReportsThatCommitWroteNoFiles()
    {
        var tool = Read("src", "Cli", "Microsoft.Maui.Cli", "DevFlow", "Mcp", "Tools", "TestAgentAuthoringTools.cs");

        Assert.Contains("wroteFiles = false", tool, StringComparison.Ordinal);
        Assert.Contains("No Markdown flow or plan sidecar was written to", tool, StringComparison.Ordinal);
        Assert.Contains("Never report a successful commit as files created.", tool, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("plugins", "dotnet-maui")]
    [InlineData(".github", null)]
    public void AuthoringSkill_ForbidsClaimingFilesWereWritten(string root, string? owner)
    {
        string[] segments = owner is null
            ? [root, "skills", "maui-devflow-test", "references", "author.md"]
            : [root, owner, "skills", "maui-devflow-test", "references", "author.md"];
        var author = Read(segments);

        Assert.Contains("commits the authoring session, not the workspace", author, StringComparison.Ordinal);
        Assert.Contains("never claim files exist", author, StringComparison.Ordinal);
    }
}
