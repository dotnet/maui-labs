using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>
/// Browser fixtures run normally locally. CI runs them in the dedicated Chromium job,
/// not in the general build/signing jobs that do not provision Playwright browsers.
/// </summary>
public sealed class InspectorBrowserFactAttribute : FactAttribute
{
    public InspectorBrowserFactAttribute()
    {
        var browserJob = Environment.GetEnvironmentVariable("MAUI_INSPECTOR_BROWSER_TESTS") == "1";
        var buildAgent = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true"
            || string.Equals(Environment.GetEnvironmentVariable("TF_BUILD"), "true", StringComparison.OrdinalIgnoreCase);
        if (buildAgent && !browserJob)
            Skip = "Runs in the dedicated Inspector Chromium job (MAUI_INSPECTOR_BROWSER_TESTS=1).";
    }
}
