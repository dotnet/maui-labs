using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/ConversationTurnRendererTests.cs
/// Tests ContentContext — the MAUI bridge between Core ContentBlocks and the view layer.
/// Verifies all derived properties correctly expose the underlying block state.
/// </summary>
public class ContentContextTests
{
    // ── Role properties ──

    [Fact]
    public void UserBlock_IsUser_True_IsAssistant_False()
    {
        var ctx = BlockFactory.MakeText("User", "Hello");

        Assert.True(ctx.IsUser);
        Assert.False(ctx.IsAssistant);
        Assert.Equal(ChatRole.User, ctx.Role);
    }

    [Fact]
    public void AssistantBlock_IsAssistant_True_IsUser_False()
    {
        var ctx = BlockFactory.MakeText("Assistant", "Hi there");

        Assert.True(ctx.IsAssistant);
        Assert.False(ctx.IsUser);
        Assert.Equal(ChatRole.Assistant, ctx.Role);
    }

    // ── Text content ──

    [Fact]
    public void TextContent_ReturnsRawText_ForRichContentBlock()
    {
        var ctx = BlockFactory.MakeText("User", "Hello World");
        Assert.Equal("Hello World", ctx.TextContent);
    }

    [Fact]
    public void TextContent_ReturnsNull_ForNonTextBlock()
    {
        var ctx = BlockFactory.MakeToolCall("get_weather");
        Assert.Null(ctx.TextContent);
    }

    // ── Tool name ──

    [Fact]
    public void ToolName_FromFunctionInvocation()
    {
        var ctx = BlockFactory.MakeToolCall("get_weather");
        Assert.Equal("get_weather", ctx.ToolName);
    }

    [Fact]
    public void ToolName_FromFunctionResult()
    {
        var ctx = BlockFactory.MakeToolResult("calculate", "42");
        Assert.Equal("calculate", ctx.ToolName);
    }

    [Fact]
    public void ToolName_FromApproval()
    {
        var ctx = BlockFactory.MakeApproval("delete_file");
        Assert.Equal("delete_file", ctx.ToolName);
    }

    [Fact]
    public void ToolName_Null_ForTextBlock()
    {
        var ctx = BlockFactory.MakeText("Assistant", "no tool here");
        Assert.Null(ctx.ToolName);
    }

    // ── Interactive blocks ──

    [Fact]
    public void IsInteractive_True_ForApprovalBlock()
    {
        var ctx = BlockFactory.MakeApproval("send_email");
        Assert.True(ctx.IsInteractive);
    }

    [Fact]
    public void IsInteractive_False_ForTextBlock()
    {
        var ctx = BlockFactory.MakeText("User", "Hello");
        Assert.False(ctx.IsInteractive);
    }

    [Fact]
    public void IsInteractive_False_ForToolCall()
    {
        var ctx = BlockFactory.MakeToolCall("get_weather");
        Assert.False(ctx.IsInteractive);
    }

    // ── Approval state ──

    [Fact]
    public void ApprovalState_Pending_Initially()
    {
        var ctx = BlockFactory.MakeApproval("delete_file");
        Assert.Equal(ApprovalStatus.Pending, ctx.ApprovalState);
        Assert.False(ctx.ApprovalResolved);
    }

    [Fact]
    public void ApprovalState_Approved_AfterApproval()
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("call-1", "delete_file", null)
        };
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        var block = new FunctionApprovalBlock(innerBlock, request);
        block.Role = ChatRole.Assistant;
        block.Approve();

        var session = SessionFactory.Create("ok");
        var ctx = new ContentContext(session, block);

        Assert.Equal(ApprovalStatus.Approved, ctx.ApprovalState);
        Assert.True(ctx.ApprovalResolved);
        Assert.Contains("Approved", ctx.ApprovalResolutionText);
    }

    [Fact]
    public void ApprovalState_Rejected_AfterRejection()
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("call-1", "delete_file", null)
        };
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        var block = new FunctionApprovalBlock(innerBlock, request);
        block.Role = ChatRole.Assistant;
        block.Reject();

        var session = SessionFactory.Create("ok");
        var ctx = new ContentContext(session, block);

        Assert.Equal(ApprovalStatus.Rejected, ctx.ApprovalState);
        Assert.True(ctx.ApprovalResolved);
        Assert.Contains("Rejected", ctx.ApprovalResolutionText);
    }

    [Fact]
    public void ApprovalState_Null_ForNonApprovalBlock()
    {
        var ctx = BlockFactory.MakeText("User", "Hello");
        Assert.Null(ctx.ApprovalState);
        Assert.False(ctx.ApprovalResolved);
        Assert.Null(ctx.ApprovalResolutionText);
    }

    // ── Lifecycle state ──

    [Fact]
    public void LifecycleState_IsExposed()
    {
        var ctx = BlockFactory.MakeText("Assistant", "Streaming...");
        // Default lifecycle state should be accessible
        Assert.IsType<BlockLifecycleState>(ctx.LifecycleState);
    }

    // ── Null guards ──

    [Fact]
    public void Constructor_ThrowsOnNullAgentContext()
    {
        var block = new RichContentBlock();
        Assert.Throws<ArgumentNullException>(() => new ContentContext(null!, block));
    }

    [Fact]
    public void Constructor_ThrowsOnNullBlock()
    {
        var session = SessionFactory.Create("ok");
        Assert.Throws<ArgumentNullException>(() => new ContentContext(session, null!));
    }
}
