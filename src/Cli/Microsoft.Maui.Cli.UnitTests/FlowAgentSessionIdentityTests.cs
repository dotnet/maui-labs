using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// The opaque agent session identity is compiled into the app binary by
/// <c>Microsoft.Maui.DevFlow.Agent.targets</c>. These tests pin that it is derived from the build
/// inputs rather than regenerated per invocation, so two <c>flow run</c>s of the same flow against
/// the same commit build the same app assembly.
/// </summary>
public sealed class FlowAgentSessionIdentityTests
{
    private static FlowExecutionRequest Request(
        string projectPath,
        string? targetFramework = "net10.0-android",
        string configuration = "Debug",
        string platform = "android")
        => new()
        {
            FlowPath = "flow.md",
            ProjectPath = projectPath,
            TargetFramework = targetFramework,
            Configuration = configuration,
            Platform = platform,
        };

    [Fact]
    public void CreateBuildScopedAgentSessionId_SameBuildInputs_IsStableAcrossInvocations()
    {
        var request = Request(Path.Combine("C:", "repo", "app", "App.csproj"));

        var first = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(request);
        var second = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(request);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_SatisfiesAgentSessionIdentityValidation()
    {
        var sessionId = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "repo", "app", "App.csproj")));

        Assert.StartsWith("flow", sessionId, StringComparison.Ordinal);
        Assert.Equal(36, sessionId.Length);
        Assert.All(sessionId, character => Assert.True(char.IsAsciiLetterOrDigit(character)));
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_DifferentProjectPath_Differs()
    {
        var first = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "worktree-a", "App.csproj")));
        var second = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "worktree-b", "App.csproj")));

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("net10.0-ios", "Debug", "android")]
    [InlineData("net10.0-android", "Release", "android")]
    [InlineData("net10.0-android", "Debug", "ios")]
    [InlineData("net10.0-ANDROID", "debug", "Android")]
    [InlineData(null, "Debug", "android")]
    [InlineData("net10.0-android", "Debug", "ios-simulator")]
    public void CreateBuildScopedAgentSessionId_DifferentArtifactSelector_StillMatches(
        string? targetFramework,
        string configuration,
        string platform)
    {
        var baseline = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "repo", "App.csproj")));
        var variant = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "repo", "App.csproj"), targetFramework, configuration, platform));

        Assert.Equal(baseline, variant);
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_SurvivesAgentTargetsSanitisationUnchanged()
    {
        var sessionId = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "repo", "App.csproj")));

        var sanitised = Regex.Replace(sessionId.ToLowerInvariant(), "[^a-z0-9]+", "");

        Assert.Equal(sessionId, sanitised);
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_MatchesGoldenConstant()
    {
        // An external oracle: SHA-256 of the lowercased full path, first 32 hex characters. A
        // component that varied per process or per machine could not satisfy this.
        var (projectPath, expected) = OperatingSystem.IsWindows()
            ? (@"C:\repo\app\App.csproj", "flow624136b09085ab89ca18375cc9631d24")
            : ("/repo/app/App.csproj", "flow8184d5ed3b101ec58e1bc8408d2b9e57");

        Assert.Equal(expected, FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(Request(projectPath)));
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_IsDistinguishableFromTheAgentTargetsDefault()
    {
        // Microsoft.Maui.DevFlow.Agent.targets defaults to "dw" + the sanitised path tail, so a
        // flow-run agent must never be mistakable for a plain `maui devflow run` agent.
        var sessionId = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
            Request(Path.Combine("C:", "repo", "App.csproj")));

        Assert.StartsWith("flow", sessionId, StringComparison.Ordinal);
        Assert.False(sessionId.StartsWith("dw", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_UnresolvableProjectPath_StillReturnsAValidIdentity()
    {
        // Path.GetFullPath("") throws; the fallback must not escape as an unstructured exception.
        var sessionId = FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(Request(""));

        Assert.Equal(36, sessionId.Length);
        Assert.All(sessionId, character => Assert.True(char.IsAsciiLetterOrDigit(character)));
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_EquivalentProjectPathSpelling_Matches()
    {
        var directory = Path.Combine(Path.GetTempPath(), "devflow-session-id");
        var direct = Path.Combine(directory, "App.csproj");
        var indirect = Path.Combine(directory, "nested", "..", "App.csproj");

        Assert.Equal(
            FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(Request(direct)),
            FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(Request(indirect)));
    }

    [Fact]
    public void CreateBuildScopedAgentSessionId_CaseOnlyPathDifference_IsDeliberatelyFolded()
    {
        // Case folding is unconditional, so on a case-sensitive filesystem two genuinely distinct
        // files share an identity. That is a strict subset of the collisions the agent targets'
        // own 24-character path-tail default already has, and it keeps one repository path from
        // producing two identities depending on the host filesystem.
        var directory = Path.Combine(Path.GetTempPath(), "devflow-session-id");

        Assert.Equal(
            FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
                Request(Path.Combine(directory, "App.csproj"))),
            FlowExecutionCoordinator.CreateBuildScopedAgentSessionId(
                Request(Path.Combine(directory.ToUpperInvariant(), "APP.CSPROJ"))));
    }
}
