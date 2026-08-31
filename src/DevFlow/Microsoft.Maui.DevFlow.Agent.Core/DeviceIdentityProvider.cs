namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Resolves the identity of the virtual device an app is running on, so the broker can pair the
/// app agent with the device around it.
/// <para>
/// Without this, a user selects an app and a device separately and nothing connects the two: the
/// device layer would be a second tool rather than part of DevFlow. An app can only observe a few
/// weak signals about its host, so several are reported and matched in priority order.
/// </para>
/// <para>
/// The value is intentionally best-effort. A desktop app, a physical device, or a platform we do
/// not recognise all resolve to <c>null</c>, which simply means no pairing happens.
/// </para>
/// </summary>
public static class DeviceIdentityProvider
{
    /// <summary>
    /// Overrides resolution. Set by the platform-specific agent package, which can read platform
    /// APIs that this platform-agnostic assembly cannot reference.
    /// </summary>
    public static Func<string?>? Resolver { get; set; }

    /// <summary>
    /// Returns the device identity in its compact wire form, or <c>null</c> when the app is not
    /// running on a recognisable virtual device.
    /// </summary>
    public static string? Resolve()
    {
        try
        {
            var platformResolved = Resolver?.Invoke();
            if (!string.IsNullOrWhiteSpace(platformResolved))
                return platformResolved;

            return ResolveFromEnvironment();
        }
        catch
        {
            // Identity is an optimisation, never a requirement. An app must still register and be
            // inspectable if we cannot work out what it is running on.
            return null;
        }
    }

    /// <summary>
    /// The part of resolution that needs no platform API.
    /// <para>
    /// Apple's simulator runtime injects <c>SIMULATOR_UDID</c> into the process environment, and
    /// that value is exactly the UDID <c>simctl</c> uses, so it is both free to read and an exact
    /// join key.
    /// </para>
    /// </summary>
    internal static string? ResolveFromEnvironment()
    {
        var simulatorUdid = Environment.GetEnvironmentVariable("SIMULATOR_UDID");
        if (!string.IsNullOrWhiteSpace(simulatorUdid))
            return $"platform=ios;udid={simulatorUdid.Trim()}";

        return null;
    }
}
