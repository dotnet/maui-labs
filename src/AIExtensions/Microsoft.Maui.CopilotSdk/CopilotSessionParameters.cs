using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// The mapped, backend-agnostic parameters used to create or resume a Copilot session.
/// Produced by <see cref="CopilotSdkChatClient"/> from the configuration and per-request
/// <see cref="ChatOptions"/>, and consumed by an <see cref="ICopilotBackend"/>.
/// </summary>
internal sealed class CopilotSessionParameters
{
    /// <summary>The resolved model id, or <see langword="null"/> to use the runtime default.</summary>
    public string? Model { get; init; }

    /// <summary>The resolved reasoning effort, or <see langword="null"/> to use the model default.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>The combined system message text, or <see langword="null"/> when none is supplied.</summary>
    public string? SystemInstructions { get; init; }

    /// <summary>The pending proxy functions advertised to the runtime.</summary>
    public IReadOnlyList<AIFunctionDeclaration> ToolDeclarations { get; init; } = [];

    /// <summary>
    /// The source-qualified allowlist of tool names (for example <c>custom:get_weather</c>).
    /// When non-empty, only these tools are available to the model.
    /// </summary>
    public IReadOnlyList<string> AvailableTools { get; init; } = [];

    /// <summary>
    /// The source-qualified list of excluded tool patterns (for example <c>builtin:*</c>).
    /// Used to guarantee built-in tools are never available when no caller tools are supplied.
    /// </summary>
    public IReadOnlyList<string> ExcludedTools { get; init; } = [];

    /// <summary>The working directory for the session, or <see langword="null"/> for the runtime default.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// The effective permission handler. Never <see langword="null"/>; the chat client always resolves
    /// either the caller-supplied handler or the safe default policy.
    /// </summary>
    public required CopilotSdkPermissionHandler PermissionHandler { get; init; }
}
