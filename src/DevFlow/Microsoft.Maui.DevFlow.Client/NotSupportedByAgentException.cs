namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Thrown when the connected agent does not implement the requested capability.
/// </summary>
/// <remarks>
/// The DevFlow agent runs on top of several UI frameworks. A .NET MAUI app can answer every
/// endpoint, while a plain .NET Android / iOS / Mac Catalyst / macOS app only answers the ones its
/// backend implements — theme, storage, sensors and background jobs need the optional Essentials
/// add-on, for example. Rather than failing opaquely, the agent replies <c>501 Not Implemented</c>
/// with <c>{ "error": "not_supported", "capability": ..., "reason": ... }</c> and the driver
/// surfaces it as this exception.
/// <para>
/// Call <see cref="AgentClient.GetCapabilitiesAsync"/> up front to discover what an agent supports
/// instead of probing endpoints and catching this.
/// </para>
/// </remarks>
public sealed class NotSupportedByAgentException : InvalidOperationException
{
    /// <summary>
    /// Creates the exception for a capability the agent declined to serve.
    /// </summary>
    /// <param name="capability">Dotted capability id, e.g. <c>ui.tree</c> or <c>storage.preferences</c>.</param>
    /// <param name="reason">Human-readable explanation reported by the agent.</param>
    public NotSupportedByAgentException(string capability, string? reason)
        : base(BuildMessage(capability, reason))
    {
        Capability = capability;
        Reason = reason;
    }

    /// <summary>
    /// The dotted capability id the agent reported as unsupported, e.g. <c>ui.gesture</c>.
    /// </summary>
    public string Capability { get; }

    /// <summary>
    /// The agent's explanation, e.g. "the native backend has no Essentials add-on referenced".
    /// </summary>
    public string? Reason { get; }

    private static string BuildMessage(string capability, string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? $"The connected agent does not support '{capability}'."
            : $"The connected agent does not support '{capability}': {reason}";
}
