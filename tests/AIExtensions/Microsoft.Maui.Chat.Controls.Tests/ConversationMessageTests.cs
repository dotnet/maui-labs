using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="ConversationMessage"/>: identity, content ordering, and status.</summary>
public class ConversationMessageTests
{
    [Fact]
    public void Constructor_GeneratesIdAndTimestamp()
    {
        var before = DateTimeOffset.Now.AddSeconds(-1);
        var message = new ConversationMessage(ChatFactory.Remote());

        Assert.False(string.IsNullOrWhiteSpace(message.Id));
        Assert.True(message.CreatedAt >= before);
        Assert.Empty(message.Contents);
        Assert.Equal(ConversationMessageStatus.Draft, message.Status);
        Assert.Null(message.ErrorText);
    }

    [Fact]
    public void Constructor_WithExplicitIdentity_IsKept()
    {
        var createdAt = new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var message = new ConversationMessage(ChatFactory.Remote(), "m-1", createdAt);

        Assert.Equal("m-1", message.Id);
        Assert.Equal(createdAt, message.CreatedAt);
    }

    [Fact]
    public void Constructor_WithText_AddsOneTextContent()
    {
        var message = new ConversationMessage(ChatFactory.Remote(), "hello");

        var content = Assert.IsType<TextMessageContent>(Assert.Single(message.Contents));
        Assert.Equal("hello", content.Text);
    }

    [Fact]
    public void Constructor_WithTextAndIdentity_KeepsBoth()
    {
        var createdAt = new DateTimeOffset(2021, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var message = new ConversationMessage(ChatFactory.Remote(), "hi", "m-2", createdAt);

        Assert.Equal("m-2", message.Id);
        Assert.Equal(createdAt, message.CreatedAt);
        Assert.Equal("hi", Assert.IsType<TextMessageContent>(Assert.Single(message.Contents)).Text);
    }

    [Fact]
    public void Constructor_WithNullParticipant_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ConversationMessage(null!));

    [Fact]
    public void Contents_PreserveInsertionOrder()
    {
        var message = new ConversationMessage(ChatFactory.Remote());
        var first = new TextMessageContent("one");
        var second = ChatFactory.Image();

        message.AddContent(first);
        message.AddContent(second);
        message.Contents.Insert(1, new TextMessageContent("middle"));

        Assert.Collection(
            message.Contents,
            content => Assert.Same(first, content),
            content => Assert.Equal("middle", Assert.IsType<TextMessageContent>(content).Text),
            content => Assert.Same(second, content));
    }

    [Fact]
    public void AddContent_ReturnsTheSameInstance()
    {
        var message = new ConversationMessage(ChatFactory.Remote());
        var content = new TextMessageContent("one");

        Assert.Same(content, message.AddContent(content));
    }

    [Fact]
    public void AddContent_WithNull_Throws()
    {
        var message = new ConversationMessage(ChatFactory.Remote());

        Assert.Throws<ArgumentNullException>(() => message.AddContent<MessageContent>(null!));
    }

    [Fact]
    public void Status_IsBindableAndNotifies()
    {
        var message = new ConversationMessage(ChatFactory.Local());
        using var recorder = new PropertyRecorder(message);

        message.Status = ConversationMessageStatus.Delivered;

        Assert.Equal(ConversationMessageStatus.Delivered, message.Status);
        Assert.Contains(nameof(ConversationMessage.Status), recorder.Names);
    }

    [Fact]
    public void ErrorText_IsBindableAndNotifies()
    {
        var message = new ConversationMessage(ChatFactory.Local());
        using var recorder = new PropertyRecorder(message);

        message.Status = ConversationMessageStatus.Failed;
        message.ErrorText = "Could not be delivered.";

        Assert.Equal("Could not be delivered.", message.ErrorText);
        Assert.Contains(nameof(ConversationMessage.ErrorText), recorder.Names);
    }
}
