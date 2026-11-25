namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Uses simctl to capture and record an Apple TV simulator.
/// </summary>
public class TvOSSimulatorAppDriver : iOSSimulatorAppDriver
{
    public override string Platform => "tvOSSimulator";

    protected override string RecordingPlatform => "tvos";

    protected override void EnsureNativeInteractionSupported()
        => throw new NotSupportedException("Native tvOS interaction requires remote focus input, which is not supported. Use agent UI actions for app controls.");

    public override Task<ThemeResult> SetThemeAsync(DevFlowTheme theme, ThemeSetScope scope = ThemeSetScope.Auto)
        => scope == ThemeSetScope.System
            ? Task.FromResult(new ThemeResult
            {
                Theme = theme,
                RequestedTheme = theme,
                Source = "system",
                Success = false,
                Message = "tvOS Simulator does not support simctl appearance changes. Use app scope.",
            })
            : base.SetThemeAsync(theme, ThemeSetScope.App);
}
