namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// The mutation-lease identity the CLI presents to agents.
/// </summary>
/// <remarks>
/// Each <c>maui devflow</c> command runs in its own process. A random identity per
/// process made two consecutive commands look like two different drivers, so the
/// second one was refused for the lease duration ("Another session is driving
/// this app") — which breaks every script and AI agent that issues one command per
/// call. All CLI commands for the same user therefore share one stable identity.
/// The Inspector and MCP sessions keep their own identities, so exclusivity between
/// a human and an automated driver is unchanged. Set
/// <see cref="EnvironmentVariable"/> to give a separate automation its own identity.
/// </remarks>
internal static class CliMutationLease
{
    internal const string EnvironmentVariable = "MAUI_DEVFLOW_LEASE_ID";
    internal const string FileName = "cli-lease-id";
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private static readonly Lazy<string> s_id = new(() => Resolve(
        Environment.GetEnvironmentVariable(EnvironmentVariable),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mauidevflow")));

    public static string Id => s_id.Value;

    /// <summary>
    /// Returns the explicit identity when set, otherwise the identity stored in
    /// <paramref name="stateDirectory"/>, creating it on first use. Falls back to a
    /// per-process identity when the directory cannot be written.
    /// </summary>
    internal static string Resolve(string? explicitId, string stateDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
            return explicitId.Trim();

        var path = Path.Combine(stateDirectory, FileName);
        try
        {
            Directory.CreateDirectory(stateDirectory);
            using var stateLock = AcquireLock(path + ".lock");

            if (TryReadValid(path, out var stored))
                return stored;

            var created = Guid.NewGuid().ToString("N");
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, created);
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }

            return created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static FileStream AcquireLock(string path)
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static bool TryReadValid(string path, out string value)
    {
        value = "";
        if (!File.Exists(path))
            return false;

        value = File.ReadAllText(path).Trim();
        return IsValid(value);
    }

    private static bool IsValid(string value) => value.Length is > 0 and <= 128 && !value.Any(char.IsWhiteSpace);
}
