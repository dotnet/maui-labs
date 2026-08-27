using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class DevFlowHostPlatform
{
    public static bool IsAndroid(string? platform, string? tfm = null)
        => DevFlowPlatform.Normalize(platform) == DevFlowPlatform.Android
            || DevFlowPlatform.Normalize(tfm) == DevFlowPlatform.Android;

    public static bool IsIosSimulator(string? platform)
        => DevFlowPlatform.Normalize(platform) == DevFlowPlatform.iOS;
}
