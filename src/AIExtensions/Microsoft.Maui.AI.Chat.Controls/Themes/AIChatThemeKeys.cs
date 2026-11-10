namespace Microsoft.Maui.AI.Chat.Controls.Themes;

// Resource keys the built-in AI views look up from code. The values are the documented
// "MauiAIChat.*" keys, so host apps override them in their own resource dictionaries.
internal static class AIChatThemeKeys
{
    public const string FunctionInvocationTemplate = "MauiAIChat.FunctionInvocationTemplate";
    public const string DefaultTemplate = "MauiAIChat.DefaultTemplate";
    public const string ToolApprovalTemplate = "MauiAIChat.ToolApprovalTemplate";

    public const string ToolApprovalArgsStackStyle = "MauiAIChat.ToolApproval.ArgsStackStyle";
    public const string ToolApprovalArgsRowStyle = "MauiAIChat.ToolApproval.ArgsRowStyle";
    public const string ToolApprovalEmptyArgsLabelStyle = "MauiAIChat.ToolApproval.EmptyArgsLabelStyle";
    public const string ToolApprovalArgNameLabelStyle = "MauiAIChat.ToolApproval.ArgNameLabelStyle";
    public const string ToolApprovalArgValueLabelStyle = "MauiAIChat.ToolApproval.ArgValueLabelStyle";

    public const string MessageListTemplate = "MauiAIChat.MessageListTemplate";
}
