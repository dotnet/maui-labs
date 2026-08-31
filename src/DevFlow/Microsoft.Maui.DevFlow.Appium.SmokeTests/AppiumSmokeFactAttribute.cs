using Xunit;

namespace Microsoft.Maui.DevFlow.Appium.SmokeTests;

public sealed class AppiumSmokeFactAttribute : FactAttribute
{
    public AppiumSmokeFactAttribute()
    {
        var readiness = AppiumSmokeEnvironment.Evaluate();
        if (!readiness.IsEnabled)
        {
            Skip = readiness.Reason;
        }
    }
}
