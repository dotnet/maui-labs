using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>
/// Factory for creating ContentContext instances wrapping different block types.
/// Mirrors the test helpers used in Blazor tests for rendering different block types.
/// </summary>
internal static class BlockFactory
{
    private static AgentContext CreateSession()
    {
        var client = new TestChatClient("test");
        var agent = new UIAgent(client);
        return new AgentContext(agent);
    }

    public static ContentContext MakeText(string role, string text)
    {
        var block = new RichContentBlock();
        block.AppendText(text);
        block.Role = role == "User" ? ChatRole.User : ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }

    public static ContentContext MakeToolCall(string toolName)
    {
        var block = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", toolName, null),
            Result = null,
        };
        block.Role = ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }

    public static ContentContext MakeToolResult(string toolName, string result)
    {
        var block = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", toolName, null),
            Result = new FunctionResultContent("c1", result),
        };
        block.Role = ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }

    public static ContentContext MakeApproval(string toolName)
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("c1", toolName, null),
            Result = null,
        };
        innerBlock.Role = ChatRole.Assistant;
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        var block = new FunctionApprovalBlock(innerBlock, request);
        block.Role = ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }

    public static ContentContext MakeReasoning(string text)
    {
        var block = new ReasoningContentBlock();
        block.AppendText(text);
        block.Role = ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }

    public static ContentContext MakeMedia()
    {
        var block = new MediaContentBlock();
        block.Role = ChatRole.Assistant;
        return new ContentContext(CreateSession(), block);
    }
}
