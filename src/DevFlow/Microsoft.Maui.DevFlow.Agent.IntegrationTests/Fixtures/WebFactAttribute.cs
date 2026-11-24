namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// A fact that only runs against the web sample (<c>DEVFLOW_TEST_PLATFORM=web</c>). The web sample is
/// much smaller than the shared DevFlow sample, so its tests are kept separate; run them with
/// <c>--filter Category=Web</c>.
/// </summary>
public sealed class WebFactAttribute : FactAttribute
{
    public WebFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PLATFORM"), "web", StringComparison.OrdinalIgnoreCase))
            Skip = "Runs only with DEVFLOW_TEST_PLATFORM=web.";
    }
}
