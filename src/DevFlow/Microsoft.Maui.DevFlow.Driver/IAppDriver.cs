namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Interface for MAUI app automation drivers.
/// Combines Agent API calls with platform-specific capabilities.
/// </summary>
public interface IAppDriver : IDisposable
{
    /// <summary>
    /// The platform this driver targets.
    /// </summary>
    string Platform { get; }

    /// <summary>
    /// Connect to the agent running in the MAUI app.
    /// </summary>
    Task ConnectAsync(string host = "localhost", int port = 9223);

    /// <summary>
    /// Check agent connection status.
    /// </summary>
    Task<AgentStatus?> GetStatusAsync();

    /// <summary>
    /// Get the visual tree.
    /// </summary>
    Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0);

    /// <summary>
    /// Query elements matching criteria.
    /// </summary>
    Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null);

    /// <summary>
    /// Tap an element by ID.
    /// </summary>
    Task<bool> TapAsync(string elementId);

    /// <summary>
    /// Fill text into an element.
    /// </summary>
    Task<bool> FillAsync(string elementId, string text);

    /// <summary>
    /// Clear text from an element.
    /// </summary>
    Task<bool> ClearAsync(string elementId);

    /// <summary>
    /// Take a screenshot (returns PNG bytes).
    /// </summary>
    Task<byte[]?> ScreenshotAsync();

    /// <summary>
    /// Press the back button (platform-specific).
    /// </summary>
    Task BackAsync();

    /// <summary>
    /// Send a key press (platform-specific).
    /// </summary>
    Task PressKeyAsync(string key);

    /// <summary>
    /// Start screen recording. Returns immediately; recording runs in background.
    /// </summary>
    /// <param name="outputFile">Output file path for the recording.</param>
    /// <param name="timeoutSeconds">Max recording duration (default 30s). Recording auto-stops after this.</param>
    Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30);

    /// <summary>
    /// Stop an active screen recording and finalize the output file.
    /// </summary>
    /// <returns>The path to the recorded video file.</returns>
    Task<string> StopRecordingAsync();
}
