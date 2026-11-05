namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public sealed class InspectorBrowserFixtureFactAttribute : FactAttribute
{
    public InspectorBrowserFixtureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DEVFLOW_INSPECTOR_BROWSER_FIXTURES") != "1")
            Skip = "Requires DEVFLOW_INSPECTOR_BROWSER_FIXTURES=1 and installed Chromium; exercised by the dedicated Inspector browser CI job.";
    }
}
