using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>Covers <see cref="ChatDraft"/>, <see cref="ChatAttachment"/>, and <see cref="ChatSuggestion"/>.</summary>
public class ChatDraftTests
{
    [Fact]
    public void Draft_TrimsTextAndDefaultsAttachments()
    {
        var draft = new ChatDraft("  hello  ");

        Assert.Equal("hello", draft.Text);
        Assert.True(draft.HasText);
        Assert.Empty(draft.Attachments);
        Assert.False(draft.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Draft_WithoutTextOrAttachments_IsEmpty(string? text)
    {
        var draft = new ChatDraft(text);

        Assert.Equal(string.Empty, draft.Text);
        Assert.False(draft.HasText);
        Assert.True(draft.IsEmpty);
    }

    [Fact]
    public void Draft_WithOnlyAttachments_IsNotEmpty()
    {
        var draft = new ChatDraft(null, [ChatFactory.Attachment()]);

        Assert.True(draft.IsEmpty is false);
        Assert.Single(draft.Attachments);
    }

    [Fact]
    public void Draft_IgnoresNullAttachments()
    {
        var draft = new ChatDraft("hi", [ChatFactory.Attachment(), null!]);

        Assert.Single(draft.Attachments);
    }

    [Fact]
    public void Draft_CopiesAttachmentsAtConstruction()
    {
        var attachments = new List<ChatAttachment> { ChatFactory.Attachment() };
        var draft = new ChatDraft("hi", attachments);

        attachments.Clear();

        Assert.Single(draft.Attachments);
    }

    [Fact]
    public void CreateContents_TextOnly_ProducesOneTextContent()
    {
        var draft = new ChatDraft("hello");

        var contents = draft.CreateContents();

        Assert.Equal("hello", Assert.IsType<TextMessageContent>(Assert.Single(contents)).Text);
    }

    [Fact]
    public void CreateContents_AttachmentOnly_ProducesMediaContent()
    {
        var draft = new ChatDraft(null, [ChatFactory.Attachment("cat.png")]);

        var media = Assert.IsType<MediaMessageContent>(Assert.Single(draft.CreateContents()));

        Assert.Equal("cat.png", media.FileName);
        Assert.True(media.IsImage);
    }

    [Fact]
    public void CreateContents_Mixed_PutsTextFirst()
    {
        var draft = new ChatDraft("look", [ChatFactory.Attachment("cat.png")]);

        var contents = draft.CreateContents();

        Assert.Collection(
            contents,
            content => Assert.IsType<TextMessageContent>(content),
            content => Assert.IsType<MediaMessageContent>(content));
    }

    [Fact]
    public void CreateContents_Empty_ProducesNothing() => Assert.Empty(new ChatDraft(null).CreateContents());

    [Fact]
    public void Attachment_FromBytes_ExposesMetadata()
    {
        var attachment = new ChatAttachment("notes.pdf", "application/pdf", new byte[] { 1, 2 }, "Meeting notes");

        Assert.Equal("notes.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal(2, attachment.ByteCount);
        Assert.Equal("Meeting notes", attachment.AltText);
        Assert.False(attachment.IsImage);
        Assert.Null(attachment.Uri);
    }

    [Fact]
    public void Attachment_FromUri_HasNoBytes()
    {
        var uri = new Uri("https://example.test/cat.png");
        var attachment = new ChatAttachment("cat.png", "image/png", uri);

        Assert.Equal(uri, attachment.Uri);
        Assert.Equal(0, attachment.ByteCount);
        Assert.True(attachment.IsImage);
    }

    [Fact]
    public void Attachment_WithEmptyBuffer_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            new ChatAttachment("a.bin", "application/octet-stream", ReadOnlyMemory<byte>.Empty));

    [Theory]
    [InlineData(null, "image/png")]
    [InlineData("", "image/png")]
    [InlineData("cat.png", null)]
    [InlineData("cat.png", " ")]
    public void Attachment_WithBlankMetadata_Throws(string? fileName, string? mediaType) =>
        Assert.ThrowsAny<ArgumentException>(() => new ChatAttachment(fileName!, mediaType!, new byte[] { 1 }));

    [Fact]
    public void Attachment_ToContent_CarriesSourceAndMetadata()
    {
        var attachment = new ChatAttachment("cat.png", "image/png", new byte[] { 1, 2, 3 }, "A cat");

        var content = attachment.ToContent();

        Assert.Equal("cat.png", content.FileName);
        Assert.Equal("A cat", content.AltText);
        Assert.Equal("image/png", content.MediaType);
        Assert.Equal(3, content.ByteCount);
    }

    [Fact]
    public void Attachment_ToContent_FromUri_KeepsUri()
    {
        var uri = new Uri("https://example.test/cat.png");
        var content = new ChatAttachment("cat.png", "image/png", uri).ToContent();

        Assert.Equal(uri, content.Uri);
        Assert.False(content.HasData);
    }

    [Fact]
    public void Suggestion_DefaultsPromptToLabel()
    {
        var suggestion = new ChatSuggestion("Say hello");

        Assert.Equal("Say hello", suggestion.Label);
        Assert.Equal("Say hello", suggestion.Prompt);
        Assert.Null(suggestion.Icon);
        Assert.False(suggestion.HasIcon);
    }

    [Fact]
    public void Suggestion_WithPromptAndIcon_KeepsBoth()
    {
        var suggestion = new ChatSuggestion("Greet", "Say hello to the team", "👋");

        Assert.Equal("Say hello to the team", suggestion.Prompt);
        Assert.Equal("👋", suggestion.Icon);
        Assert.True(suggestion.HasIcon);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Suggestion_WithBlankLabel_Throws(string? label) =>
        Assert.ThrowsAny<ArgumentException>(() => new ChatSuggestion(label!));
}
