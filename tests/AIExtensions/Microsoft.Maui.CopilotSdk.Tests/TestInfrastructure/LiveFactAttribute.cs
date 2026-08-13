namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that skips the test (visibly, with a reason) unless the
/// <c>COPILOT_SDK_LIVE_TESTS</c> environment variable is set to <c>1</c>. Live tests exercise a real
/// GitHub Copilot runtime and are opt-in.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("COPILOT_SDK_LIVE_TESTS") != "1")
        {
            Skip = "Live test skipped. Set COPILOT_SDK_LIVE_TESTS=1 (and ensure the Copilot CLI is available) to run it.";
        }
    }
}
