namespace Microsoft.Maui.AI.Chat.Controls.Themes;

/// <summary>
/// Well-known resource keys used by the built-in chat theme.
/// Host apps can override any of these keys in their own resource dictionaries.
/// </summary>
public static class ChatThemeKeys
{
    // ControlTemplates
    public const string ChatMessageTemplate = "ExtensionsAI.ChatMessageTemplate";
    public const string FunctionCallTemplate = "ExtensionsAI.FunctionCallTemplate";
    public const string FunctionResultTemplate = "ExtensionsAI.FunctionResultTemplate";
    public const string ErrorTemplate = "ExtensionsAI.ErrorTemplate";
    public const string DefaultTemplate = "ExtensionsAI.DefaultTemplate";
    public const string ToolApprovalTemplate = "ExtensionsAI.ToolApprovalTemplate";

    // ToolApproval Styles
    public const string ToolApprovalArgsStackStyle = "ExtensionsAI.ToolApproval.ArgsStackStyle";
    public const string ToolApprovalArgsRowStyle = "ExtensionsAI.ToolApproval.ArgsRowStyle";
    public const string ToolApprovalEmptyArgsLabelStyle = "ExtensionsAI.ToolApproval.EmptyArgsLabelStyle";
    public const string ToolApprovalArgNameLabelStyle = "ExtensionsAI.ToolApproval.ArgNameLabelStyle";
    public const string ToolApprovalArgValueLabelStyle = "ExtensionsAI.ToolApproval.ArgValueLabelStyle";

    // Colors — Messages
    public const string UserBackground = "ExtensionsAI.User.Background";
    public const string UserTextColor = "ExtensionsAI.User.TextColor";
    public const string AssistantBackground = "ExtensionsAI.Assistant.Background";
    public const string AssistantTextColor = "ExtensionsAI.Assistant.TextColor";
    public const string FunctionCallBackground = "ExtensionsAI.FunctionCall.Background";
    public const string FunctionCallTextColor = "ExtensionsAI.FunctionCall.TextColor";
    public const string FunctionResultBackground = "ExtensionsAI.FunctionResult.Background";
    public const string FunctionResultTextColor = "ExtensionsAI.FunctionResult.TextColor";
    public const string ErrorBackground = "ExtensionsAI.Error.Background";
    public const string ErrorTextColor = "ExtensionsAI.Error.TextColor";
    public const string DefaultTextColor = "ExtensionsAI.Default.TextColor";

    // Colors — Input area
    public const string InputBackground = "ExtensionsAI.Input.Background";
    public const string SendBackground = "ExtensionsAI.Send.Background";
    public const string SendTextColor = "ExtensionsAI.Send.TextColor";

    // Colors — Suggestions
    public const string SuggestionBackground = "ExtensionsAI.Suggestion.Background";
    public const string SuggestionTextColor = "ExtensionsAI.Suggestion.TextColor";

    // Timestamp styling
    public const string TimestampTextColor = "ExtensionsAI.Timestamp.TextColor";
    public const string TimestampFontSize = "ExtensionsAI.Timestamp.FontSize";

    // Bubble sizing
    public const string BubbleMaxWidth = "ExtensionsAI.Bubble.MaxWidth";

    // CopilotChatView
    public const string CopilotChatViewTemplate = "ExtensionsAI.CopilotChatViewTemplate";
}
