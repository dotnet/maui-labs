#if ANDROID
using Android.App;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Android-specific bootstrap overloads.
/// </summary>
public static class DevFlowAgentAndroidExtensions
{
    /// <summary>
    /// Starts the agent and binds it to <paramref name="activity"/>.
    /// </summary>
    /// <param name="activity">The activity whose window the agent walks.</param>
    /// <param name="options">Optional agent configuration.</param>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService StartDevFlowAgent(this Activity activity, AgentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(activity);

        NativeUi.CurrentActivity = activity;
        return options == null ? DevFlowAgent.Start() : DevFlowAgent.Start(options);
    }

    /// <summary>
    /// Points the agent at a different activity, e.g. from <c>OnResume</c> in a multi-activity app.
    /// </summary>
    /// <param name="activity">The activity that is now in the foreground.</param>
    public static void BindDevFlowAgent(this Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        NativeUi.CurrentActivity = activity;
    }
}
#endif
