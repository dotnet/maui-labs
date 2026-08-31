using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class DevFlowSkillReferenceTests
{
    [Fact]
    public void LocalCiFixSkill_IsMirroredAndUsesOnlyTheDeveloperLane()
    {
        var root = RepositoryRoot();
        var pluginRoot = Path.Combine(
            root,
            "plugins",
            "dotnet-maui",
            "skills",
            "maui-devflow-ci-fix");
        var projectRoot = Path.Combine(root, ".github", "skills", "maui-devflow-ci-fix");
        var pluginFiles = Files(pluginRoot);
        var projectFiles = Files(projectRoot);

        Assert.Equal(pluginFiles.Keys.Order(StringComparer.Ordinal), projectFiles.Keys.Order(StringComparer.Ordinal));
        foreach (var path in pluginFiles.Keys)
            Assert.Equal(pluginFiles[path], projectFiles[path]);

        var skill = File.ReadAllText(Path.Combine(pluginRoot, "SKILL.md"));
        Assert.DoesNotMatch(@"\bmaui_test_[a-z_]+\b", skill);
        Assert.Contains("ordinary worktree edit", skill, StringComparison.Ordinal);
        Assert.Contains("failureCorrespondence", skill, StringComparison.Ordinal);
        Assert.Contains("Never weaken a test", skill, StringComparison.Ordinal);
        Assert.Contains("Never stage, commit, push", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentedDevFlowSkillReferences_ResolveToRealSkills()
    {
        var root = RepositoryRoot();
        var existing = Directory
            .EnumerateDirectories(Path.Combine(root, "plugins", "dotnet-maui", "skills"))
            .Concat(Directory.EnumerateDirectories(Path.Combine(root, ".github", "skills")))
            .Select(Path.GetFileName)
            .Where(static name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var scanRoots = new[]
        {
            Path.Combine(root, "plugins", "dotnet-maui", "skills"),
            Path.Combine(root, ".github", "skills"),
            Path.Combine(root, ".github", "agents"),
            Path.Combine(root, "tests", "dotnet-maui"),
        };

        var references = scanRoots
            .SelectMany(scanRoot => Directory.EnumerateFiles(scanRoot, "*.*", SearchOption.AllDirectories))
            .Where(static path => Path.GetExtension(path) is ".md" or ".yaml" or ".yml")
            .SelectMany(path => Regex.Matches(
                    File.ReadAllText(path),
                    @"\bmaui-devflow-[a-z0-9-]+\b",
                    RegexOptions.CultureInvariant)
                .Select(match => (Path: path, Skill: match.Value)))
            .Where(reference => !existing.Contains(reference.Skill))
            .Distinct()
            .OrderBy(reference => reference.Skill, StringComparer.Ordinal)
            .ThenBy(reference => reference.Path, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            references.Count == 0,
            "Unresolved DevFlow skill references:" + Environment.NewLine +
            string.Join(
                Environment.NewLine,
                references.Select(reference => $"{reference.Skill}: {reference.Path}")));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static Dictionary<string, byte[]> Files(string root)
        => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                File.ReadAllBytes,
                StringComparer.Ordinal);
}
