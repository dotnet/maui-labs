namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>Helpers for configuring a live <see cref="CopilotSdkChatClient"/> against the installed Copilot CLI.</summary>
internal static class LiveTestSupport
{
    public static CopilotSdkConfiguration CreateConfiguration()
    {
        return new CopilotSdkConfiguration
        {
            Model = Environment.GetEnvironmentVariable("COPILOT_SDK_MODEL"),
            UseLoggedInUser = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN")),
            GitHubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
            CliPath = ResolveCliPath(),
            StreamingInactivityTimeout = TimeSpan.FromMinutes(2),
        };
    }

    // Prefers an explicit COPILOT_CLI_PATH, then searches PATH for the installed CLI. Returns null to let
    // the SDK use its own bundled runtime resolution.
    private static string? ResolveCliPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("COPILOT_CLI_PATH");
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var names = OperatingSystem.IsWindows() ? new[] { "copilot.exe", "copilot.cmd" } : new[] { "copilot" };
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
