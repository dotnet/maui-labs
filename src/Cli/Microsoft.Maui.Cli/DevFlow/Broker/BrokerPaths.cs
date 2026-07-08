namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Well-known paths for broker state and logs.
/// </summary>
public static class BrokerPaths
{
    /// <summary>
    /// Test-only override for <see cref="ConfigDir"/>. When set, broker state and logs are
    /// redirected here instead of the user profile, so tests never clobber a live broker.
    /// </summary>
    internal static string? ConfigDirOverride { get; set; }

    public static string ConfigDir =>
        ConfigDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mauidevflow");

    public static string StateFile => Path.Combine(ConfigDir, "broker.json");
    public static string LogFile => Path.Combine(ConfigDir, "broker.log");
}
