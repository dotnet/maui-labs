namespace Microsoft.Maui.AI.CopilotSdk;

/// <summary>
/// Configuration options for the Copilot Chat control when using DI registration.
/// </summary>
public sealed class CopilotChatConfiguration
{
    /// <summary>Model identifier (e.g. "gpt-4.1", "claude-sonnet-4.5"). Used by <see cref="CopilotSdkChatClient"/>.</summary>
    public string Model { get; set; } = "gpt-4.1";

    /// <summary>System message prepended to all conversations.</summary>
    public string? SystemMessage { get; set; }

    /// <summary>Whether to use the logged-in GitHub user (gh CLI) for auth. Default true.</summary>
    public bool UseLoggedInUser { get; set; } = true;

    /// <summary>Explicit GitHub token. If set, <see cref="UseLoggedInUser"/> is ignored.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Path to the Copilot CLI binary. If null, the SDK uses its bundled CLI or searches PATH.</summary>
    public string? CliPath { get; set; }
}
