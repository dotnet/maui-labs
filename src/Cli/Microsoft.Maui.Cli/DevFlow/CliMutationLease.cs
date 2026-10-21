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
            if (File.Exists(path))
            {
                var stored = File.ReadAllText(path).Trim();
                if (IsValid(stored))
                    return stored;

                File.Delete(path);
            }

            Directory.CreateDirectory(stateDirectory);
            var created = Guid.NewGuid().ToString("N");
            var temp = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllText(temp, created);
            try
            {
                // Two first runs can race; keep whichever identity landed first.
                File.Move(temp, path, overwrite: false);
            }
            catch (IOException)
            {
                File.Delete(temp);
            }

            var winner = File.ReadAllText(path).Trim();
            return IsValid(winner) ? winner : created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static bool IsValid(string value) => value.Length is > 0 and <= 128 && !value.Any(char.IsWhiteSpace);
}
