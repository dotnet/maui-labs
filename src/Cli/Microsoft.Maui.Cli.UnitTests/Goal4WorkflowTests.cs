using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class Goal4WorkflowTests
{
    [Theory]
    [InlineData("push")]
    [InlineData("pull_request")]
    public void CliCi_TracksAllToolingSkillsAndPreservesOtherPaths(string trigger)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var lines = File.ReadAllLines(Path.Combine(directory.FullName, ".github", "workflows", "ci-cli.yml"));
        var start = Array.IndexOf(lines, $"  {trigger}:");
        Assert.True(start >= 0, $"The {trigger} trigger is missing.");
        var paths = lines.Skip(start + 1)
            .TakeWhile(line => string.IsNullOrWhiteSpace(line) || line.StartsWith("    ", StringComparison.Ordinal))
            .SkipWhile(line => line != "    paths:")
            .Skip(1)
            .TakeWhile(line => line.StartsWith("      - ", StringComparison.Ordinal))
            .Select(line => line["      - ".Length..].Trim('\'', '"'));

        Assert.Equal(new[]
        {
            "plugins/dotnet-maui-tooling/skills/**",
            "src/Cli/**",
            "src/DevFlow/Microsoft.Maui.DevFlow.Client/**",
            "src/DevFlow/Microsoft.Maui.DevFlow.Driver/**",
            "src/Go/**",
            "eng/**",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
            "NuGet.config",
        }, paths);
    }
}
