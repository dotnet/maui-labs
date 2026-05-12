using Xunit;

namespace Microsoft.Maui.CopilotChat.Tests;

public class CopilotChatMessageTests
{
    [Fact]
    public void Constructor_SetsKindAndText()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.User, "Hello");

        Assert.Equal(ChatMessageKind.User, msg.Kind);
        Assert.Equal("Hello", msg.Text);
        Assert.Null(msg.Icon);
        Assert.False(msg.HasIcon);
    }

    [Fact]
    public void Constructor_WithIcon_SetsHasIcon()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test", ChatIcons.Wrench);

        Assert.Equal(ChatIcons.Wrench, msg.Icon);
        Assert.True(msg.HasIcon);
    }

    [Fact]
    public void Text_Set_RaisesPropertyChanged()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Assistant, "initial");
        var changed = new List<string>();
        msg.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        msg.Text = "updated";

        Assert.Equal("updated", msg.Text);
        Assert.Contains("Text", changed);
    }

    [Fact]
    public void Text_SetSameValue_DoesNotRaisePropertyChanged()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Assistant, "same");
        var changed = new List<string>();
        msg.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        msg.Text = "same";

        Assert.Empty(changed);
    }

    [Fact]
    public void HasDetails_FalseByDefault()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");

        Assert.False(msg.HasDetails);
    }

    [Fact]
    public void HasDetails_TrueWhenToolArgsSet()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");
        msg.ToolArgs = "{ \"key\": \"value\" }";

        Assert.True(msg.HasDetails);
    }

    [Fact]
    public void HasDetails_TrueWhenToolResultSet()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");
        msg.ToolResult = "result data";

        Assert.True(msg.HasDetails);
    }

    [Fact]
    public void ToggleExpand_WhenHasDetails_TogglesIsExpanded()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");
        msg.ToolArgs = "args";

        Assert.False(msg.IsExpanded);

        msg.ToggleExpand();
        Assert.True(msg.IsExpanded);

        msg.ToggleExpand();
        Assert.False(msg.IsExpanded);
    }

    [Fact]
    public void ToggleExpand_WhenNoDetails_DoesNothing()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");

        msg.ToggleExpand();

        Assert.False(msg.IsExpanded);
    }

    [Fact]
    public void ExpandIcon_ChangesWithIsExpanded()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");
        msg.ToolArgs = "args";

        Assert.Equal(ChatIcons.ChevronRight, msg.ExpandIcon);

        msg.IsExpanded = true;
        Assert.Equal(ChatIcons.ChevronDown, msg.ExpandIcon);
    }

    [Fact]
    public void ToggleExpandCommand_IsNotNull()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");

        Assert.NotNull(msg.ToggleExpandCommand);
        Assert.True(msg.ToggleExpandCommand.CanExecute(null));
    }

    [Fact]
    public void ToolArgs_Set_RaisesHasDetailsChanged()
    {
        var msg = new CopilotChatMessage(ChatMessageKind.Tool, "test");
        var changed = new List<string>();
        msg.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        msg.ToolArgs = "args";

        Assert.Contains("ToolArgs", changed);
        Assert.Contains("HasDetails", changed);
    }
}
