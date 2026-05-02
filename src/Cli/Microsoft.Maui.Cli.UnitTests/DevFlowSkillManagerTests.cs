using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Skills;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class DevFlowSkillManagerTests
{
    [Fact]
    public async Task InstallRecommended_ProjectScope_WritesBundledSkillsAndUserLevelState()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.Equal("install", result["action"]?.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-connect", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(workspace.Path, ".maui", "devflow-skills.lock.json")));

        var statePath = GetStatePathFromResults(result);
        Assert.StartsWith(Path.Combine(workspace.StateRoot, "workspaces"), statePath, StringComparison.Ordinal);
        Assert.True(File.Exists(statePath));

        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        Assert.Equal(2, skillState["version"]?.GetValue<int>());
        Assert.Equal("project", skillState["scope"]?.GetValue<string>());
        Assert.Equal("claude", skillState["target"]?.GetValue<string>());
        Assert.Equal(workspace.Path, skillState["workspaceRoot"]?.GetValue<string>());
        Assert.Equal(workspace.Path, skillState["targetRoot"]?.GetValue<string>());
        Assert.Equal(".claude/skills", skillState["relativeSkillDirectory"]?.GetValue<string>());
        Assert.True(skillState.ContainsKey("lastCheckedUtc"));
        Assert.False(skillState.ContainsKey("updatedAtUtc"));
        var stateEntries = Assert.IsType<JsonArray>(skillState["entries"]);
        Assert.All(stateEntries.OfType<JsonObject>(), entry =>
        {
            Assert.False(entry.ContainsKey("installedByCommandPath"));
            Assert.False(entry.ContainsKey("installedAtUtc"));
        });

        var statuses = await DevFlowSkillManager.CheckAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);
        var skills = Assert.IsType<JsonArray>(statuses["skills"]);
        Assert.All(skills.OfType<JsonObject>(), skill => Assert.Equal("up-to-date", skill["status"]?.GetValue<string>()));
        Assert.DoesNotContain(skills.OfType<JsonObject>(), skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-connect");
    }

    [Fact]
    public async Task Check_ProjectScope_DetectsDirtySkillFile()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        await File.AppendAllTextAsync(skillPath, "\nmanual edit\n");

        var result = await DevFlowSkillManager.CheckAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);
        var skills = Assert.IsType<JsonArray>(result["skills"]);
        var onboard = skills.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("dirty", onboard["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Check_ProjectScope_DetectsUntrackedSkillFileAsDirty()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var notesPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "notes.md");
        await File.WriteAllTextAsync(notesPath, "local notes");

        var result = await DevFlowSkillManager.CheckAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);

        var skills = Assert.IsType<JsonArray>(result["skills"]);
        var onboard = skills.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("dirty", onboard["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_UpToDateSkillDoesNotRewriteFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        var originalWriteTime = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(skillPath, originalWriteTime);

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.Equal(originalWriteTime, File.GetLastWriteTimeUtc(skillPath));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("unchanged", onboard["action"]?.GetValue<string>());
        Assert.False(onboard.ContainsKey("message"));
    }

    [Fact]
    public async Task Update_ProjectScope_OverwritesDirtyCurrentSkillByDefault()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        await File.AppendAllTextAsync(skillPath, "\nmanual edit\n");

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.DoesNotContain("manual edit", await File.ReadAllTextAsync(skillPath));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("written", onboard["action"]?.GetValue<string>());
        Assert.Equal("up-to-date", onboard["status"]?.GetValue<string>());
        Assert.Equal("Overwrote dirty current skill files with the current CLI bundle.", onboard["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_InteractiveDeclinePreservesDirtyCurrentSkill()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        await File.AppendAllTextAsync(skillPath, "\nmanual edit\n");

        var result = await DevFlowSkillManager.UpdateAsync(
            "project",
            "claude",
            customPath: null,
            force: false,
            allowDowngrade: false,
            confirm: _ => false,
            CancellationToken.None);

        Assert.Contains("manual edit", await File.ReadAllTextAsync(skillPath));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("skipped", onboard["action"]?.GetValue<string>());
        Assert.Equal("dirty", onboard["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_UpdatesOlderManagedSkillWithMessage()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var skillPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md");
        const string olderSkillContent = "older managed skill content";
        await File.WriteAllTextAsync(skillPath, olderSkillContent);

        var statePath = GetStatePathFromResults(installResult);
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var entries = Assert.IsType<JsonArray>(skillState["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        onboardEntry["contentHash"] = HashContent("older bundle");
        onboardEntry["installedByCliVersion"] = "0.0.1";
        var files = Assert.IsType<JsonArray>(onboardEntry["files"]);
        var skillFile = files.OfType<JsonObject>().Single(file => file["path"]?.GetValue<string>() == "SKILL.md");
        skillFile["hash"] = HashContent(olderSkillContent);
        await File.WriteAllTextAsync(statePath, CliJson.SerializeUntyped(skillState));

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("written", onboard["action"]?.GetValue<string>());
        Assert.Equal("up-to-date", onboard["status"]?.GetValue<string>());
        Assert.Equal("Updated skill files from the current CLI bundle.", onboard["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_ReplacesUnmanagedCurrentSkillWithMessage()
    {
        using var workspace = TemporaryWorkspace.Create();
        var skillDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllTextAsync(Path.Combine(skillDirectory, "SKILL.md"), "unmanaged content");

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("written", onboard["action"]?.GetValue<string>());
        Assert.Equal("up-to-date", onboard["status"]?.GetValue<string>());
        Assert.Equal("Replaced unmanaged existing skill folder with the current CLI bundle.", onboard["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task Remove_ManagedSkillWithExtraFileWithoutForce_SkipsAndPreservesFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var notesPath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "notes.md");
        await File.WriteAllTextAsync(notesPath, "local notes");

        var result = await DevFlowSkillManager.RemoveAsync("maui-devflow-onboard", "project", "claude", force: false, cancellationToken: CancellationToken.None);

        var results = Assert.IsType<JsonArray>(result["results"]);
        var status = Assert.IsType<JsonObject>(Assert.Single(results));
        Assert.Equal("dirty", status["status"]?.GetValue<string>());
        Assert.Equal("skipped", status["action"]?.GetValue<string>());
        Assert.True(File.Exists(notesPath));
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesObsoleteManagedSkillFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var obsoletePath = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "references", "old-file.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoletePath)!);
        await File.WriteAllTextAsync(obsoletePath, "old content");

        var statePath = GetStatePathFromResults(installResult);
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var entries = Assert.IsType<JsonArray>(skillState["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        var files = Assert.IsType<JsonArray>(onboardEntry["files"]);
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = "references/old-file.md",
            ["hash"] = HashContent("old content")
        });
        await File.WriteAllTextAsync(statePath, CliJson.SerializeUntyped(skillState));

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(File.Exists(obsoletePath));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("written", onboard["action"]?.GetValue<string>());
        Assert.Equal("Removed obsolete files no longer present in the current CLI bundle.", onboard["message"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_SkipsObsoleteStatePathsOutsideSkillDirectory()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var traversalPath = Path.Combine(workspace.Path, "traversal-sentinel.txt");
        var absolutePath = Path.Combine(workspace.Path, "absolute-sentinel.txt");
        await File.WriteAllTextAsync(traversalPath, "traversal content");
        await File.WriteAllTextAsync(absolutePath, "absolute content");

        var statePath = GetStatePathFromResults(installResult);
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var entries = Assert.IsType<JsonArray>(skillState["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        var files = Assert.IsType<JsonArray>(onboardEntry["files"]);
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = Path.Combine("..", "..", "..", "traversal-sentinel.txt"),
            ["hash"] = HashContent("traversal content")
        });
        files.Add((JsonNode)new JsonObject
        {
            ["path"] = absolutePath,
            ["hash"] = HashContent("absolute content")
        });
        await File.WriteAllTextAsync(statePath, CliJson.SerializeUntyped(skillState));

        await DevFlowSkillManager.UpdateAsync("project", "claude", force: true, allowDowngrade: false, CancellationToken.None);

        Assert.True(File.Exists(traversalPath));
        Assert.True(File.Exists(absolutePath));
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesCleanObsoleteConnectSkill()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var connectDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-connect");
        Directory.CreateDirectory(connectDirectory);
        var connectContent = "obsolete connect skill";
        await File.WriteAllTextAsync(Path.Combine(connectDirectory, "SKILL.md"), connectContent);

        var statePath = GetStatePathFromResults(installResult);
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var entries = Assert.IsType<JsonArray>(skillState["entries"]);
        entries.Add((JsonNode)new JsonObject
        {
            ["skillId"] = "maui-devflow-connect",
            ["skillVersion"] = "0.1.0-preview.1",
            ["scope"] = "project",
            ["target"] = "claude",
            ["targetPath"] = ".claude/skills/maui-devflow-connect",
            ["contentHash"] = HashContent(connectContent),
            ["installedByCliVersion"] = "0.1.0-preview.1",
            ["installedByBundleHash"] = HashContent(connectContent),
            ["files"] = new JsonArray
            {
                (JsonNode)new JsonObject
                {
                    ["path"] = "SKILL.md",
                    ["hash"] = HashContent(connectContent)
                }
            }
        });
        await File.WriteAllTextAsync(statePath, CliJson.SerializeUntyped(skillState));

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(Directory.Exists(connectDirectory));
        var updatedState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var updatedEntries = Assert.IsType<JsonArray>(updatedState["entries"]);
        Assert.DoesNotContain(updatedEntries.OfType<JsonObject>(), entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-connect");
        var results = Assert.IsType<JsonArray>(result["results"]);
        var connectResult = results.OfType<JsonObject>().Single(item => item["skillId"]?.GetValue<string>() == "maui-devflow-connect");
        Assert.Equal("removed", connectResult["action"]?.GetValue<string>());
        Assert.Equal("legacy-removed", connectResult["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesDirtyLegacyConnectSkillWithoutForce()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var connectDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-connect");
        Directory.CreateDirectory(connectDirectory);
        await File.WriteAllTextAsync(Path.Combine(connectDirectory, "SKILL.md"), "manually edited obsolete connect skill");

        var statePath = GetStatePathFromResults(installResult);
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var entries = Assert.IsType<JsonArray>(skillState["entries"]);
        entries.Add((JsonNode)new JsonObject
        {
            ["skillId"] = "maui-devflow-connect",
            ["skillVersion"] = "0.1.0-preview.1",
            ["scope"] = "project",
            ["target"] = "claude",
            ["targetPath"] = ".claude/skills/maui-devflow-connect",
            ["contentHash"] = HashContent("original obsolete connect skill"),
            ["installedByCliVersion"] = "0.1.0-preview.1",
            ["installedByBundleHash"] = HashContent("original obsolete connect skill"),
            ["files"] = new JsonArray
            {
                (JsonNode)new JsonObject
                {
                    ["path"] = "SKILL.md",
                    ["hash"] = HashContent("original obsolete connect skill")
                }
            }
        });
        await File.WriteAllTextAsync(statePath, CliJson.SerializeUntyped(skillState));

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(Directory.Exists(connectDirectory));
        var updatedState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        var updatedEntries = Assert.IsType<JsonArray>(updatedState["entries"]);
        Assert.DoesNotContain(updatedEntries.OfType<JsonObject>(), entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-connect");
        var results = Assert.IsType<JsonArray>(result["results"]);
        var connectResult = results.OfType<JsonObject>().Single(item => item["skillId"]?.GetValue<string>() == "maui-devflow-connect");
        Assert.Equal("removed", connectResult["action"]?.GetValue<string>());
        Assert.Equal("legacy-removed", connectResult["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesUnmanagedLegacyAiDebuggingSkill()
    {
        using var workspace = TemporaryWorkspace.Create();
        var legacyDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-ai-debugging");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, "SKILL.md"), "legacy skill");
        await File.WriteAllTextAsync(Path.Combine(legacyDirectory, ".skill-version"), "legacy version");

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(Directory.Exists(legacyDirectory));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-debug", "SKILL.md")));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var legacyResult = results.OfType<JsonObject>().Single(item => item["skillId"]?.GetValue<string>() == "maui-ai-debugging");
        Assert.Equal("removed", legacyResult["action"]?.GetValue<string>());
        Assert.Equal("legacy-removed", legacyResult["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_InstallsMissingBundledSkills()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.Equal("update", result["action"]?.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-debug", "SKILL.md")));
        var results = Assert.IsType<JsonArray>(result["results"]);
        Assert.Contains(results.OfType<JsonObject>(), item =>
            item["skillId"]?.GetValue<string>() == "maui-devflow-onboard" &&
            item["action"]?.GetValue<string>() == "written" &&
            item["message"]?.GetValue<string>() == "Installed missing skill files from the current CLI bundle.");
        Assert.Contains(results.OfType<JsonObject>(), item =>
            item["skillId"]?.GetValue<string>() == "maui-devflow-debug" &&
            item["action"]?.GetValue<string>() == "written" &&
            item["message"]?.GetValue<string>() == "Installed missing skill files from the current CLI bundle.");
    }

    [Fact]
    public async Task Init_ProjectScope_WithCustomPath_WritesBundledSkillsAndPathState()
    {
        using var workspace = TemporaryWorkspace.Create();

        var result = await DevFlowSkillManager.InstallRecommendedAsync(
            "project",
            "claude",
            customPath: Path.Combine(".agent", "skills"),
            force: false,
            allowDowngrade: false,
            confirm: null,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agent", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agent", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".claude")));
        var statePath = GetStatePathFromResults(result);
        Assert.StartsWith(Path.Combine(workspace.StateRoot, "workspaces"), statePath, StringComparison.Ordinal);
        Assert.False(statePath.EndsWith($"{Path.DirectorySeparatorChar}claude.json", StringComparison.Ordinal));

        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        Assert.Equal(".agent/skills", skillState["relativeSkillDirectory"]?.GetValue<string>());
        Assert.StartsWith("path-", skillState["target"]?.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_ProjectScope_WithAutoTarget_UsesExistingKnownSkillFolder()
    {
        using var workspace = TemporaryWorkspace.Create();
        var existingSkillDirectory = Path.Combine(workspace.Path, ".agents", "skills", "some-other-skill");
        Directory.CreateDirectory(existingSkillDirectory);
        await File.WriteAllTextAsync(Path.Combine(existingSkillDirectory, "SKILL.md"), "unrelated skill");

        var result = await DevFlowSkillManager.UpdateAsync("project", "auto", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agents", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agents", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".claude")));

        var results = Assert.IsType<JsonArray>(result["results"]);
        var onboard = results.OfType<JsonObject>().Single(skill => skill["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        Assert.Equal("agents", onboard["target"]?.GetValue<string>());
    }

    [Fact]
    public async Task Update_ProjectScope_WithAgentTarget_WritesSingularAgentSkills()
    {
        using var workspace = TemporaryWorkspace.Create();

        await DevFlowSkillManager.UpdateAsync("project", "agent", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agent", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".agent", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".claude")));
    }

    [Fact]
    public async Task Update_ProjectScope_WithGithubTarget_WritesGithubSkills()
    {
        using var workspace = TemporaryWorkspace.Create();

        await DevFlowSkillManager.UpdateAsync("project", "github", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.Path, ".github", "skills", "maui-devflow-onboard", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Path, ".github", "skills", "maui-devflow-debug", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Path, ".claude")));
    }

    [Fact]
    public async Task Update_ProjectScope_RemovesUserLegacySkillFromKnownTargets()
    {
        using var workspace = TemporaryWorkspace.Create();
        var userLegacyDirectory = Path.Combine(workspace.UserRoot, ".agents", "skills", "maui-ai-debugging");
        Directory.CreateDirectory(userLegacyDirectory);
        await File.WriteAllTextAsync(Path.Combine(userLegacyDirectory, "SKILL.md"), "legacy user skill");

        var result = await DevFlowSkillManager.UpdateAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        Assert.False(Directory.Exists(userLegacyDirectory));
        var results = Assert.IsType<JsonArray>(result["results"]);
        var legacyResult = results.OfType<JsonObject>().Single(item =>
            item["skillId"]?.GetValue<string>() == "maui-ai-debugging" &&
            item["scope"]?.GetValue<string>() == "user" &&
            item["target"]?.GetValue<string>() == "agents");
        Assert.Equal("removed", legacyResult["action"]?.GetValue<string>());
        Assert.Equal("legacy-removed", legacyResult["status"]?.GetValue<string>());
    }

    [Fact]
    public async Task Doctor_WithDifferentLocalToolManifestVersion_ReportsWarning()
    {
        using var workspace = TemporaryWorkspace.Create();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".config"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace.Path, ".config", "dotnet-tools.json"),
            """
            {
              "version": 1,
              "isRoot": true,
              "tools": {
                "Microsoft.Maui.Cli": {
                  "version": "0.0.1",
                  "commands": [ "maui" ]
                }
              }
            }
            """);

        var result = await DevFlowSkillManager.DoctorAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);

        var warnings = Assert.IsType<JsonArray>(result["warnings"]);
        Assert.Contains(warnings.Select(w => w?.GetValue<string>()), warning => warning?.Contains("local Microsoft.Maui.Cli version 0.0.1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Doctor_WithProjectAndUserScopeHashConflict_ReportsWarning()
    {
        using var workspace = TemporaryWorkspace.Create();
        await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var userResult = await DevFlowSkillManager.InstallRecommendedAsync("user", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var userStatePath = GetStatePathFromResults(userResult);
        var userState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(userStatePath)));
        var entries = Assert.IsType<JsonArray>(userState["entries"]);
        var onboardEntry = entries.OfType<JsonObject>().Single(entry => entry["skillId"]?.GetValue<string>() == "maui-devflow-onboard");
        onboardEntry["contentHash"] = "sha256-conflict";
        await File.WriteAllTextAsync(userStatePath, CliJson.SerializeUntyped(userState));

        var result = await DevFlowSkillManager.DoctorAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);

        var warnings = Assert.IsType<JsonArray>(result["warnings"]);
        Assert.Contains(warnings.Select(w => w?.GetValue<string>()), warning => warning?.Contains("installed in both project and user scope with different content", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Check_ProjectScope_WithMalformedStateFile_RecoversAsUnmanaged()
    {
        using var workspace = TemporaryWorkspace.Create();
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var statePath = GetStatePathFromResults(installResult);
        await File.WriteAllTextAsync(statePath, "{not json");

        var doctor = await DevFlowSkillManager.DoctorAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);
        var warnings = Assert.IsType<JsonArray>(doctor["warnings"]);
        Assert.Contains(warnings.Select(w => w?.GetValue<string>()), warning => warning?.Contains("is not valid JSON and will be rebuilt", StringComparison.Ordinal) == true);

        await File.WriteAllTextAsync(statePath, "{not json");
        var result = await DevFlowSkillManager.CheckAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);

        var skills = Assert.IsType<JsonArray>(result["skills"]);
        Assert.All(skills.OfType<JsonObject>(), skill => Assert.Equal("unknown-or-unmanaged", skill["status"]?.GetValue<string>()));
    }

    [Fact]
    public void VersionsEquivalent_WithDifferentPatchPrefix_ReturnsFalse()
    {
        Assert.False(InvokeVersionsEquivalent("0.0.1", "0.0.10"));
    }

    [Theory]
    [InlineData("0.1.0-preview.5", "0.1.0-preview.4", 1)]
    [InlineData("0.1.0-preview.4", "0.1.0-preview.5", -1)]
    [InlineData("0.1.0-preview.10", "0.1.0-preview.2", 1)]
    [InlineData("0.1.0", "0.1.0-preview.5", 1)]
    [InlineData("0.1.0-preview.5+build1", "0.1.0-preview.5+build2", 0)]
    public void CompareVersionLike_WithSemanticPrerelease_ReturnsExpectedOrder(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(InvokeCompareVersionLike(left, right)));
    }

    [Fact]
    public async Task Remove_WithPathTraversalSkillName_ThrowsAndDoesNotDeleteOutsideSkillRoot()
    {
        using var workspace = TemporaryWorkspace.Create();
        var outsideDirectory = Path.Combine(workspace.Path, "outside");
        Directory.CreateDirectory(outsideDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevFlowSkillManager.RemoveAsync(Path.Combine("..", "..", "outside"), "project", "claude", force: true, cancellationToken: CancellationToken.None));

        Assert.True(Directory.Exists(outsideDirectory));
    }

    [Fact]
    public async Task Remove_UnmanagedSkillDirectoryWithoutForce_SkipsAndPreservesFiles()
    {
        using var workspace = TemporaryWorkspace.Create();
        var skillDirectory = Path.Combine(workspace.Path, ".claude", "skills", "maui-devflow-onboard");
        Directory.CreateDirectory(skillDirectory);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        await File.WriteAllTextAsync(skillFile, "unmanaged content");

        var result = await DevFlowSkillManager.RemoveAsync("maui-devflow-onboard", "project", "claude", force: false, cancellationToken: CancellationToken.None);

        var results = Assert.IsType<JsonArray>(result["results"]);
        var status = Assert.IsType<JsonObject>(Assert.Single(results));
        Assert.Equal("unknown-or-unmanaged", status["status"]?.GetValue<string>());
        Assert.Equal("skipped", status["action"]?.GetValue<string>());
        Assert.Contains("not managed by this CLI", status["message"]?.GetValue<string>());
        Assert.True(File.Exists(skillFile));
    }

    [Fact]
    public async Task Install_ProjectAndUserScopes_UseDifferentUserLevelStateFiles()
    {
        using var workspace = TemporaryWorkspace.Create();

        var projectResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var userResult = await DevFlowSkillManager.InstallRecommendedAsync("user", "claude", force: false, allowDowngrade: false, CancellationToken.None);

        var projectStatePath = GetStatePathFromResults(projectResult);
        var userStatePath = GetStatePathFromResults(userResult);
        Assert.NotEqual(projectStatePath, userStatePath);
        Assert.StartsWith(Path.Combine(workspace.StateRoot, "workspaces"), projectStatePath, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(workspace.StateRoot, "user", "skills", "claude.json"), userStatePath);
        Assert.True(File.Exists(Path.Combine(workspace.UserRoot, ".claude", "skills", "maui-devflow-onboard", "SKILL.md")));
    }

    [Fact]
    public async Task GetFreshnessHintAsync_JsonOutput_DoesNotCreateState()
    {
        using var workspace = TemporaryWorkspace.Create();

        var hint = await DevFlowSkillManager.GetFreshnessHintAsync(machineReadableOutput: true, "claude", CancellationToken.None);

        Assert.Null(hint);
        Assert.False(Directory.Exists(workspace.StateRoot));
    }

    [Fact]
    public async Task GetFreshnessHintAsync_StaleState_ReturnsHintAndRateLimits()
    {
        using var workspace = TemporaryWorkspace.Create();
        DevFlowSkillManager.UtcNowOverrideForTests = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var firstHint = await DevFlowSkillManager.GetFreshnessHintAsync(machineReadableOutput: false, "claude", CancellationToken.None);
        var secondHint = await DevFlowSkillManager.GetFreshnessHintAsync(machineReadableOutput: false, "claude", CancellationToken.None);

        Assert.NotNull(firstHint);
        Assert.Contains("maui devflow skills check", firstHint);
        Assert.Null(secondHint);

        var statePath = Assert.Single(Directory.EnumerateFiles(workspace.StateRoot, "claude.json", SearchOption.AllDirectories));
        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        Assert.True(skillState.ContainsKey("lastPromptedUtc"));
        Assert.False(skillState.ContainsKey("lastCheckedUtc"));
    }

    [Fact]
    public async Task Check_ProjectScope_UpdatesFreshnessMarker()
    {
        using var workspace = TemporaryWorkspace.Create();
        DevFlowSkillManager.UtcNowOverrideForTests = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var installResult = await DevFlowSkillManager.InstallRecommendedAsync("project", "claude", force: false, allowDowngrade: false, CancellationToken.None);
        var statePath = GetStatePathFromResults(installResult);

        DevFlowSkillManager.UtcNowOverrideForTests = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero);
        await DevFlowSkillManager.CheckAsync("project", "claude", online: false, cancellationToken: CancellationToken.None);

        var skillState = Assert.IsType<JsonObject>(CliJson.ParseNode(await File.ReadAllTextAsync(statePath)));
        Assert.StartsWith("2025-01-02T00:00:00", skillState["lastCheckedUtc"]?.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(skillState.ContainsKey("lastCheckedCliVersion"));
        Assert.True(skillState.ContainsKey("lastCheckedBundleHash"));
    }

    sealed class TemporaryWorkspace : IDisposable
    {
        // These tests rely on Directory.GetCurrentDirectory(), so the containing test class must stay in the serialized CLI collection.
        readonly string _originalDirectory;
        readonly string? _originalStateRootOverride;
        readonly string? _originalUserRootOverride;
        readonly DateTimeOffset? _originalUtcNowOverride;

        TemporaryWorkspace(string path, string stateRoot, string userRoot)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            _originalStateRootOverride = DevFlowSkillManager.StateRootOverrideForTests;
            _originalUserRootOverride = DevFlowSkillManager.UserRootOverrideForTests;
            _originalUtcNowOverride = DevFlowSkillManager.UtcNowOverrideForTests;
            Directory.SetCurrentDirectory(path);
            Path = Directory.GetCurrentDirectory();
            StateRoot = System.IO.Path.GetFullPath(stateRoot);
            UserRoot = System.IO.Path.GetFullPath(userRoot);
            DevFlowSkillManager.StateRootOverrideForTests = StateRoot;
            DevFlowSkillManager.UserRootOverrideForTests = UserRoot;
            DevFlowSkillManager.UtcNowOverrideForTests = null;
        }

        public string Path { get; }
        public string StateRoot { get; }
        public string UserRoot { get; }

        public static TemporaryWorkspace Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui-devflow-skills-{Guid.NewGuid():N}");
            var stateRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui-devflow-skills-state-{Guid.NewGuid():N}");
            var userRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui-devflow-skills-user-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            Directory.CreateDirectory(userRoot);
            File.WriteAllText(System.IO.Path.Combine(path, ".git"), string.Empty);
            return new TemporaryWorkspace(path, stateRoot, userRoot);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
            DevFlowSkillManager.StateRootOverrideForTests = _originalStateRootOverride;
            DevFlowSkillManager.UserRootOverrideForTests = _originalUserRootOverride;
            DevFlowSkillManager.UtcNowOverrideForTests = _originalUtcNowOverride;
            try
            {
                Directory.Delete(Path, recursive: true);
                if (Directory.Exists(StateRoot))
                    Directory.Delete(StateRoot, recursive: true);
                if (Directory.Exists(UserRoot))
                    Directory.Delete(UserRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup for test temp folders.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup for test temp folders.
            }
        }
    }

    static string GetStatePathFromResults(JsonObject result)
    {
        var results = Assert.IsType<JsonArray>(result["results"]);
        var firstResult = Assert.IsType<JsonObject>(Assert.Single(results.OfType<JsonObject>(), item => item["skillId"]?.GetValue<string>() == "maui-devflow-onboard"));
        return firstResult["statePath"]?.GetValue<string>() ?? throw new InvalidOperationException("Result did not include statePath.");
    }

    static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n")));
        return "sha256-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static bool InvokeVersionsEquivalent(string left, string right)
    {
        var method = typeof(DevFlowSkillManager).GetMethod("VersionsEquivalent", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, [left, right]));
    }

    static int InvokeCompareVersionLike(string left, string right)
    {
        var method = typeof(DevFlowSkillManager).GetMethod("CompareVersionLike", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<int>(method.Invoke(null, [left, right]));
    }
}
