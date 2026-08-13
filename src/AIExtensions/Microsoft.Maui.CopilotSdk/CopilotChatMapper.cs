using System.Globalization;
using System.Text;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk;

/// <summary>
/// Pure, deterministic helpers that translate Microsoft.Extensions.AI request shapes into the
/// values needed to drive a Copilot session. Kept separate from <see cref="CopilotSdkChatClient"/>
/// so the mapping rules are individually unit testable.
/// </summary>
internal static class CopilotChatMapper
{
    /// <summary>
    /// Builds the combined system message from (in order): the configured system instructions,
    /// <see cref="ChatOptions.Instructions"/>, any system/developer role messages, and a JSON
    /// formatting instruction derived from <see cref="ChatOptions.ResponseFormat"/>.
    /// Returns <see langword="null"/> when there is nothing to say.
    /// </summary>
    public static string? BuildSystemInstructions(
        CopilotSdkConfiguration configuration,
        ChatOptions? options,
        IReadOnlyList<ChatMessage> messages)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuration.SystemInstructions))
        {
            parts.Add(configuration.SystemInstructions!.Trim());
        }

        if (!string.IsNullOrWhiteSpace(options?.Instructions))
        {
            parts.Add(options!.Instructions!.Trim());
        }

        foreach (var message in messages)
        {
            if (IsInstructionRole(message.Role))
            {
                var text = message.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }
        }

        if (options?.ResponseFormat is ChatResponseFormatJson json)
        {
            if (json.Schema is { } schema)
            {
                parts.Add(
                    "Respond with a single JSON value that conforms to this JSON schema. " +
                    "Do not include markdown code fences or any prose outside the JSON:\n" +
                    schema.GetRawText());
            }
            else
            {
                parts.Add(
                    "Respond with a single valid JSON value. " +
                    "Do not include markdown code fences or any prose outside the JSON.");
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    /// <summary>
    /// Resolves the reasoning effort, preferring (in order) <see cref="ChatOptions.Reasoning"/>,
    /// the <c>"ReasoningEffort"</c> additional property, then <see cref="CopilotSdkConfiguration.ReasoningEffort"/>.
    /// </summary>
    public static string? ResolveReasoningEffort(CopilotSdkConfiguration configuration, ChatOptions? options)
    {
        if (options?.Reasoning?.Effort is { } effort)
        {
            var mapped = MapReasoningEffort(effort);
            if (mapped is not null)
            {
                return mapped;
            }
        }

        if (options?.AdditionalProperties is { } props &&
            props.TryGetValue("ReasoningEffort", out var raw) &&
            raw is string s &&
            !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return string.IsNullOrWhiteSpace(configuration.ReasoningEffort) ? null : configuration.ReasoningEffort;
    }

    private static string? MapReasoningEffort(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.ExtraHigh => "xhigh",
        _ => null,
    };

    /// <summary>
    /// Builds pending proxy functions, the source-qualified <c>AvailableTools</c> allowlist,
    /// the <c>ExcludedTools</c> list, and the set of bare tool names for the safe permission policy.
    /// </summary>
    public static ToolMapping BuildTools(
        ChatOptions? options,
        PendingToolCoordinator coordinator)
    {
        var declarations = new List<AIFunctionDeclaration>();
        var available = new List<string>();
        var allowedNames = new HashSet<string>(StringComparer.Ordinal);

        if (options?.ToolMode is not NoneChatToolMode
            && options?.Tools is { Count: > 0 } tools)
        {
            foreach (var tool in tools)
            {
                AIFunctionDeclaration? declaration = tool switch
                {
                    // Strip the caller's implementation; a PendingToolAIFunction below bridges
                    // execution back to the outer M.E.AI tool loop.
                    AIFunction function => function.AsDeclarationOnly(),
                    AIFunctionDeclaration decl => decl,
                    _ => null,
                };

                if (declaration is null || string.IsNullOrEmpty(declaration.Name))
                {
                    continue;
                }

                declarations.Add(new PendingToolAIFunction(
                    declaration,
                    coordinator));
                available.Add("custom:" + declaration.Name);
                allowedNames.Add(declaration.Name);
            }
        }

        // When caller tools are supplied, the AvailableTools allowlist restricts the model to exactly
        // those tools (built-ins are excluded by omission). When there are none, explicitly exclude all
        // built-in tools so the model can never reach ambient file/shell capabilities.
        var excluded = declarations.Count == 0 ? new List<string> { "builtin:*" } : [];

        return new ToolMapping(declarations, available, excluded, allowedNames);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the final request message carries tool results. Earlier tool
    /// results are completed history, not a continuation of the currently pending SDK turn.
    /// </summary>
    public static bool IsToolContinuation(IReadOnlyList<ChatMessage> messages)
    {
        var finalMessage = GetLastNonInstructionMessage(messages);
        return finalMessage is not null
            && finalMessage.Contents.Any(
                static content => content is FunctionResultContent);
    }

    /// <summary>Enumerates every <see cref="FunctionResultContent"/> in the request, in order.</summary>
    public static IReadOnlyList<FunctionResultContent> GetToolResults(IReadOnlyList<ChatMessage> messages)
    {
        var results = new List<FunctionResultContent>();
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionResultContent result)
                {
                    results.Add(result);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Builds the prompt for a brand new conversation. System/developer messages are excluded (they go to
    /// the system message). When there is prior history, it is preserved as a labelled transcript that
    /// precedes the final user message, so no earlier turn is silently discarded.
    /// </summary>
    public static string BuildInitialPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var conversation = messages.Where(m => !IsInstructionRole(m.Role)).ToList();
        if (conversation.Count == 0)
        {
            return string.Empty;
        }

        var current = conversation[^1];
        var history = conversation.Take(conversation.Count - 1).ToList();

        var currentText = GetMessageText(current);
        if (history.Count == 0)
        {
            return currentText;
        }

        var builder = new StringBuilder();
        builder.Append("Conversation so far:\n");
        foreach (var message in history)
        {
            var text = GetMessageText(message);
            if (text.Length == 0)
            {
                continue;
            }

            builder.Append(RoleLabel(message.Role)).Append(": ").Append(text).Append('\n');
        }

        builder.Append("\nCurrent message:\n").Append(currentText);
        return builder.ToString();
    }

    /// <summary>
    /// Builds the prompt for a follow-up turn on an existing conversation. The runtime already holds the
    /// durable history, so only the latest user message is sent.
    /// </summary>
    public static string BuildFollowUpPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var last = GetLastNonInstructionMessage(messages);
        return last is null ? string.Empty : GetMessageText(last);
    }

    /// <summary>Extracts image attachments (raw bytes and data URIs) from the message carrying the prompt.</summary>
    public static IReadOnlyList<Attachment> BuildAttachments(IReadOnlyList<ChatMessage> messages)
    {
        var message = GetLastNonInstructionMessage(messages);
        if (message is null)
        {
            return [];
        }

        var attachments = new List<Attachment>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case DataContent data when data.HasTopLevelMediaType("image"):
                    attachments.Add(new AttachmentBlob
                    {
                        Data = data.Base64Data.ToString(),
                        MimeType = data.MediaType,
                    });
                    break;

                case UriContent uri when uri.Uri.Scheme == "data":
                    if (TryParseDataUri(uri.Uri.OriginalString, out var mime, out var base64) &&
                        mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        attachments.Add(new AttachmentBlob { Data = base64, MimeType = mime });
                    }

                    break;
            }
        }

        return attachments;
    }

    private static ChatMessage? GetLastNonInstructionMessage(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (!IsInstructionRole(messages[i].Role))
            {
                return messages[i];
            }
        }

        return null;
    }

    private static string GetMessageText(ChatMessage message)
    {
        // Prefer plain text; fall back to concatenating any textual content (including tool results).
        var text = message.Text;
        if (!string.IsNullOrEmpty(text))
        {
            return text.Trim();
        }

        var builder = new StringBuilder();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent tc when !string.IsNullOrEmpty(tc.Text):
                    builder.Append(tc.Text);
                    break;
                case FunctionResultContent frc when frc.Result is not null:
                    builder.Append(frc.Result.ToString());
                    break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string RoleLabel(ChatRole role)
    {
        if (role == ChatRole.User)
        {
            return "User";
        }

        if (role == ChatRole.Assistant)
        {
            return "Assistant";
        }

        if (role == ChatRole.Tool)
        {
            return "Tool";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(role.Value);
    }

    private static bool IsInstructionRole(ChatRole role) =>
        role == ChatRole.System ||
        string.Equals(role.Value, "system", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role.Value, "developer", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseDataUri(string uri, out string mimeType, out string base64)
    {
        mimeType = string.Empty;
        base64 = string.Empty;

        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = uri.IndexOf(',');
        if (comma <= 0)
        {
            return false;
        }

        var header = uri.Substring("data:".Length, comma - "data:".Length);
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        mimeType = header.Replace(";base64", string.Empty, StringComparison.OrdinalIgnoreCase);
        base64 = uri[(comma + 1)..];
        return true;
    }

    internal readonly record struct ToolMapping(
        IReadOnlyList<AIFunctionDeclaration> Declarations,
        IReadOnlyList<string> AvailableTools,
        IReadOnlyList<string> ExcludedTools,
        IReadOnlySet<string> AllowedToolNames);
}
