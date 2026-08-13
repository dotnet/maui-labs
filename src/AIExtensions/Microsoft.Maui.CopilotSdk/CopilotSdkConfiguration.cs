using GitHub.Copilot;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// The decision returned by a <see cref="CopilotSdkPermissionHandler"/> when the Copilot runtime
/// asks whether a tool invocation (file write, shell command, custom tool, etc.) may proceed.
/// </summary>
/// <remarks>
/// This is a deliberately small, stable surface that does not expose the GitHub Copilot SDK's
/// experimental low-level permission decision types. The adapter translates it into the
/// appropriate SDK decision internally.
/// </remarks>
public enum CopilotSdkPermissionDecision
{
    /// <summary>Deny the request. The runtime reports the tool call as rejected.</summary>
    Deny = 0,

    /// <summary>Approve the request for this single invocation only.</summary>
    Approve = 1,
}

/// <summary>
/// A callback invoked before the Copilot runtime executes a tool that requires permission.
/// </summary>
/// <param name="request">
/// The permission request describing the operation. Inspect the concrete type
/// (for example <see cref="PermissionRequestShell"/>, <see cref="PermissionRequestWrite"/>,
/// or <see cref="PermissionRequestCustomTool"/>) to make a decision.
/// </param>
/// <param name="invocation">Contextual information about the invocation, including the session id.</param>
/// <returns>The <see cref="CopilotSdkPermissionDecision"/> describing whether to allow the operation.</returns>
public delegate ValueTask<CopilotSdkPermissionDecision> CopilotSdkPermissionHandler(
    PermissionRequest request,
    PermissionInvocation invocation);

/// <summary>
/// Configuration for <see cref="CopilotSdkChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The adapter creates a single shared <see cref="CopilotClient"/> from this configuration and, for
/// each request, a request-scoped <c>CopilotSession</c>. A session stays active only across an
/// external tool call and its M.E.AI result. Durable completed-turn state is preserved by the runtime
/// and addressed through <see cref="Microsoft.Extensions.AI.ChatOptions.ConversationId"/>.
/// </para>
/// <para><b>Safety.</b> The adapter never enables blanket ambient file or shell access. Only the tools
/// explicitly supplied through <see cref="Microsoft.Extensions.AI.ChatOptions.Tools"/> are advertised to
/// the runtime, and built-in tools are excluded. Permission requests are, by default, denied unless they
/// target one of the caller-supplied tools. Override <see cref="PermissionHandler"/> to customize this.
/// </para>
/// </remarks>
public sealed class CopilotSdkConfiguration
{
    /// <summary>
    /// The default model id (for example <c>"gpt-5"</c> or <c>"claude-sonnet-4.5"</c>).
    /// When <see langword="null"/> (the default) the runtime's own default model is used.
    /// Overridden per request by <see cref="Microsoft.Extensions.AI.ChatOptions.ModelId"/>.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// System instructions prepended to every conversation. Combined with, and placed before,
    /// <see cref="Microsoft.Extensions.AI.ChatOptions.Instructions"/> and any system/developer role
    /// messages in the request.
    /// </summary>
    public string? SystemInstructions { get; set; }

    /// <summary>
    /// The default reasoning effort ("low", "medium", "high", "xhigh", "max") for models that support it.
    /// Overridden per request by <see cref="Microsoft.Extensions.AI.ChatOptions.Reasoning"/> or the
    /// <c>"ReasoningEffort"</c> additional property. When <see langword="null"/> the model default applies.
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Whether to authenticate as the logged-in GitHub user (via the Copilot CLI). Default <see langword="true"/>.
    /// Ignored when <see cref="GitHubToken"/> is set.
    /// </summary>
    public bool UseLoggedInUser { get; set; } = true;

    /// <summary>
    /// An explicit GitHub token to authenticate with. When set it takes priority over <see cref="UseLoggedInUser"/>.
    /// </summary>
    public string? GitHubToken { get; set; }

    /// <summary>
    /// Path to the Copilot CLI binary. When set, the client connects over stdio via
    /// <see cref="RuntimeConnection.ForStdio(string, System.Collections.Generic.IList{string})"/> using this path.
    /// When <see langword="null"/> the SDK uses its bundled runtime.
    /// </summary>
    public string? CliPath { get; set; }

    /// <summary>
    /// Additional command-line arguments passed to the Copilot CLI when <see cref="CliPath"/> is set.
    /// </summary>
    public IReadOnlyList<string>? CliArguments { get; set; }

    /// <summary>
    /// Working directory for the runtime session. When <see langword="null"/> the runtime uses its own
    /// process working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Base directory for Copilot data (session state, config). Sets <c>COPILOT_HOME</c> on the runtime.
    /// When <see langword="null"/> the runtime default (<c>~/.copilot</c>) is used.
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// A callback consulted when the runtime requests permission to run a tool. When <see langword="null"/>
    /// (the default) a safe policy is used that approves only the caller-supplied tools and denies every
    /// other operation (file writes, shell commands, network access, memory, etc.).
    /// </summary>
    public CopilotSdkPermissionHandler? PermissionHandler { get; set; }

    /// <summary>
    /// Maximum time to wait between streaming events before a response is considered stalled. When exceeded,
    /// the in-flight session is aborted and a <see cref="TimeoutException"/> is thrown. Default: 5 minutes.
    /// </summary>
    public TimeSpan StreamingInactivityTimeout { get; set; } = TimeSpan.FromMinutes(5);

    internal void Validate()
    {
        if (StreamingInactivityTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StreamingInactivityTimeout),
                StreamingInactivityTimeout,
                "The streaming inactivity timeout must be greater than zero.");
        }
    }
}
