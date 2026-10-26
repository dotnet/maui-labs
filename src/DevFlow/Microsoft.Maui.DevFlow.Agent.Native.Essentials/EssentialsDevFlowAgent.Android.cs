#if ANDROID
using Android.App;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Native.Essentials;

/// <summary>
/// Android-specific bootstrap overloads for the Essentials-backed agent.
/// </summary>
public static class EssentialsDevFlowAgentAndroidExtensions
{
    /// <summary>
    /// Starts the agent with Essentials-backed endpoints and binds it to <paramref name="activity"/>.
    /// </summary>
    /// <remarks>
    /// The activity has to be bound or the visual tree has no root to walk, which is why this
    /// exists alongside <see cref="EssentialsDevFlowAgent.Start()"/>. Call
    /// <c>Platform.Init(this, savedInstanceState)</c> first — Essentials needs it before any of its
    /// APIs are used.
    /// </remarks>
    /// <param name="activity">The activity whose window the agent walks.</param>
    /// <param name="options">Optional agent configuration.</param>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService StartDevFlowAgentWithEssentials(
        this Activity activity,
        AgentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(activity);

        activity.BindDevFlowAgent();
        return EssentialsDevFlowAgent.Start(options ?? new AgentOptions());
    }
}
#endif
