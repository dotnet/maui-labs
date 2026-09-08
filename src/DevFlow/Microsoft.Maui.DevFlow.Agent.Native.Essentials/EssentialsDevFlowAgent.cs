using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Native.Essentials;

/// <summary>
/// Entry point for hosting the DevFlow agent with .NET MAUI Essentials-backed endpoints in a plain
/// .NET Android, iOS, Mac Catalyst or macOS app.
/// </summary>
/// <remarks>
/// <para>
/// Use this instead of <see cref="DevFlowAgent"/> when you want the preferences, secure storage,
/// device, permission, geolocation and sensor endpoints to answer rather than return
/// <c>501 not_supported</c>. Everything else behaves identically.
/// </para>
/// <para>
/// The substitution is explicit rather than discovered by reflection so it survives trimming and
/// AOT, which plain .NET iOS apps rely on.
/// </para>
/// <code>
/// // Android — MainActivity.OnCreate
/// Microsoft.Maui.ApplicationModel.Platform.Init(this, savedInstanceState);
/// this.StartDevFlowAgentWithEssentials();
///
/// // iOS / Mac Catalyst — AppDelegate.FinishedLaunching
/// // macOS — AppDelegate.DidFinishLaunching
/// EssentialsDevFlowAgent.Start();
/// </code>
/// <para>
/// On Android, Essentials needs <c>Platform.Init</c> to have run before any of its APIs are used,
/// and permission results have to be forwarded from
/// <c>MainActivity.OnRequestPermissionsResult</c> to
/// <c>Platform.OnRequestPermissionsResult</c>. That is Essentials' own requirement, not DevFlow's.
/// </para>
/// </remarks>
public static class EssentialsDevFlowAgent
{
    /// <summary>
    /// Starts the agent with default options and Essentials-backed endpoints.
    /// </summary>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService Start() => Start(new AgentOptions());

    /// <summary>
    /// Starts the agent with the supplied options and Essentials-backed endpoints. Calling this
    /// more than once returns the agent started by the first call.
    /// </summary>
    /// <param name="options">Agent configuration. Port, broker registration and feature switches.</param>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService Start(AgentOptions options)
        => DevFlowAgent.Start(options, static o => new EssentialsNativeDevFlowAgentService(o));
}
