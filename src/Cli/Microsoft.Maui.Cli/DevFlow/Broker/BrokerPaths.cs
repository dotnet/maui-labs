namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Well-known paths for broker state and logs.
/// </summary>
public static class BrokerPaths
{
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mauidevflow");

    public static string StateFile => Path.Combine(ConfigDir, "broker.json");
    public static string LogFile => Path.Combine(ConfigDir, "broker.log");

    internal static void RestrictConfigDirectoryPermissions(string directoryPath)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            directoryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal static void RestrictStateFilePermissions(string stateFilePath)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            stateFilePath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
