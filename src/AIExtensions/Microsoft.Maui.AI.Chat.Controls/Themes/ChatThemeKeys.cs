namespace Microsoft.Maui.AI.Chat.Controls.Themes;

/// <summary>
/// Well-known resource keys used by the built-in chat theme.
/// Host apps can override any of these keys in their own resource dictionaries.
/// </summary>
/// <summary>Resource keys for the control templates defined in <c>ChatTheme.xaml</c> (used by the built-in views).</summary>
public static class ChatThemeKeys
{
    // ControlTemplates
    public const string ChatMessageTemplate = "MauiAIChat.ChatMessageTemplate";
    public const string FunctionInvocationTemplate = "MauiAIChat.FunctionInvocationTemplate";
    public const string DefaultTemplate = "MauiAIChat.DefaultTemplate";
    public const string ToolApprovalTemplate = "MauiAIChat.ToolApprovalTemplate";

    // ToolApproval Styles
    public const string ToolApprovalArgsStackStyle = "MauiAIChat.ToolApproval.ArgsStackStyle";
    public const string ToolApprovalArgsRowStyle = "MauiAIChat.ToolApproval.ArgsRowStyle";
    public const string ToolApprovalEmptyArgsLabelStyle = "MauiAIChat.ToolApproval.EmptyArgsLabelStyle";
    public const string ToolApprovalArgNameLabelStyle = "MauiAIChat.ToolApproval.ArgNameLabelStyle";
    public const string ToolApprovalArgValueLabelStyle = "MauiAIChat.ToolApproval.ArgValueLabelStyle";

    // Colors — Messages
    public const string UserBackground = "MauiAIChat.User.Background";
    public const string UserTextColor = "MauiAIChat.User.TextColor";
    public const string AssistantBackground = "MauiAIChat.Assistant.Background";
    public const string AssistantTextColor = "MauiAIChat.Assistant.TextColor";
    public const string FunctionCallBackground = "MauiAIChat.FunctionCall.Background";
    public const string FunctionCallTextColor = "MauiAIChat.FunctionCall.TextColor";
    public const string FunctionResultBackground = "MauiAIChat.FunctionResult.Background";
    public const string FunctionResultTextColor = "MauiAIChat.FunctionResult.TextColor";
    public const string ErrorBackground = "MauiAIChat.Error.Background";
    public const string ErrorTextColor = "MauiAIChat.Error.TextColor";
    public const string DefaultTextColor = "MauiAIChat.Default.TextColor";

    // Colors — Input area
    public const string InputBackground = "MauiAIChat.Input.Background";
    public const string SendBackground = "MauiAIChat.Send.Background";
    public const string SendTextColor = "MauiAIChat.Send.TextColor";

    // Colors — Suggestions
    public const string SuggestionBackground = "MauiAIChat.Suggestion.Background";
    public const string SuggestionTextColor = "MauiAIChat.Suggestion.TextColor";

    // Timestamp styling
    public const string TimestampTextColor = "MauiAIChat.Timestamp.TextColor";
    public const string TimestampFontSize = "MauiAIChat.Timestamp.FontSize";

    // Bubble sizing
    public const string BubbleMaxWidth = "MauiAIChat.Bubble.MaxWidth";

    // CopilotChatView
    public const string CopilotChatViewTemplate = "MauiAIChat.CopilotChatViewTemplate";
    public const string MessageListViewTemplate = "MauiAIChat.MessageListViewTemplate";
}
