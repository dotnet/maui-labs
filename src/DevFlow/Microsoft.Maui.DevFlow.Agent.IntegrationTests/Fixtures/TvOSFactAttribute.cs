namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// A fact that only runs against the tvOS sample (<c>DEVFLOW_TEST_PLATFORM=tvos</c>). The tvOS
/// sample is much smaller than the shared DevFlow sample, so its tests are kept separate; run them
/// with <c>--filter Category=TvOS</c>.
/// </summary>
public sealed class TvOSFactAttribute : FactAttribute
{
    public TvOSFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PLATFORM"), "tvos", StringComparison.OrdinalIgnoreCase))
            Skip = "Runs only with DEVFLOW_TEST_PLATFORM=tvos.";
    }
}
