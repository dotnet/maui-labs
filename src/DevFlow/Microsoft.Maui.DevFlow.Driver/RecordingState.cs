using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Persisted state for an active screen recording session.
/// Stored at ~/.mauidevflow/recording-state.json between CLI invocations.
/// </summary>
public record RecordingState
{
    [JsonPropertyName("recordingPid")]
    public int RecordingPid { get; init; }

    [JsonPropertyName("watchdogPid")]
    public int? WatchdogPid { get; init; }

    [JsonPropertyName("outputFile")]
    public required string OutputFile { get; init; }

    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>
    /// For Android: the on-device file path that needs to be pulled after stopping.
    /// </summary>
    [JsonPropertyName("deviceOutputFile")]
    public string? DeviceOutputFile { get; init; }

    /// <summary>
    /// For Android: optional device serial for adb -s.
    /// </summary>
    [JsonPropertyName("serial")]
    public string? Serial { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; }
}

/// <summary>
/// Manages recording state persistence to ~/.mauidevflow/recording-state.json.
/// </summary>
public static class RecordingStateManager
{
    private static readonly string StateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mauidevflow");

    private static readonly string StateFile = Path.Combine(StateDir, "recording-state.json");

    public static void Save(RecordingState state)
    {
        Directory.CreateDirectory(StateDir);
        var json = DriverJson.SerializeUntyped(state, indented: true);
        File.WriteAllText(StateFile, json);
    }

    public static RecordingState? Load()
    {
        if (!File.Exists(StateFile)) return null;
        try
        {
            var json = File.ReadAllText(StateFile);
            return DriverJson.Deserialize<RecordingState>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(StateFile))
            File.Delete(StateFile);
    }

    public static bool IsRecording()
    {
        var state = Load();
        if (state == null) return false;

        // Check if the recording process is actually still alive
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(state.RecordingPid);
            return !proc.HasExited;
        }
        catch
        {
            // Process no longer exists — clean up stale state
            Delete();
            return false;
        }
    }
}
