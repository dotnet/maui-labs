using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class AttachmentTests
{
    [Fact]
    public void Attachments_AddRemoveAndClear_UpdateReadOnlyCollection()
    {
        var control = new CopilotChatView();
        var first = CreateAttachment("first.png");
        var second = CreateAttachment("second.txt", "text/plain");

        control.AddAttachment(first);
        control.AddAttachment(second);
        Assert.Equal([first, second], control.Attachments);

        Assert.True(control.RemoveAttachment(first));
        Assert.Equal([second], control.Attachments);

        control.ClearAttachments();
        Assert.Empty(control.Attachments);
    }

    [Fact]
    public void TakePendingMessage_AttachmentOnly_IncludesDataContentAndClearsAttachment()
    {
        var control = new CopilotChatView();
        var attachment = CreateAttachment("photo.png");
        control.AddAttachment(attachment);

        var request = Assert.IsType<ChatMessage>(control.TakePendingMessage());

        Assert.Equal(ChatRole.User, request.Role);
        var content = Assert.IsType<DataContent>(Assert.Single(request.Contents));
        Assert.Same(attachment.Content, content);
        Assert.Equal("photo.png", content.Name);
        Assert.Empty(control.Attachments);
    }

    [Fact]
    public void TakePendingMessage_TextAndAttachment_PreservesContentOrder()
    {
        var control = new CopilotChatView
        {
            Text = "Describe this",
        };
        var attachment = CreateAttachment("photo.png");
        control.AddAttachment(attachment);

        var request = Assert.IsType<ChatMessage>(control.TakePendingMessage());

        Assert.Equal(2, request.Contents.Count);
        Assert.Equal("Describe this", Assert.IsType<TextContent>(request.Contents[0]).Text);
        Assert.Same(attachment.Content, request.Contents[1]);
        Assert.Equal(string.Empty, control.Text);
    }

    [Fact]
    public void TakePendingMessage_NoTextOrAttachments_ReturnsNull()
    {
        var control = new CopilotChatView { Text = "  " };

        Assert.Null(control.TakePendingMessage());
    }

    [Fact]
    public async Task PickAttachmentsAsync_UsesConfiguredPickerAndLimits()
    {
        var picked = CreateAttachment("picked.png");
        var picker = new TestAttachmentPicker([picked]);
        var control = new CopilotChatView
        {
            AttachmentPicker = picker,
            MaxAttachmentBytes = 1234,
        };

        await control.PickAttachmentsAsync();

        Assert.Equal(1234, picker.MaxBytes);
        Assert.Equal([picked], control.Attachments);
    }

    [Fact]
    public async Task PickAttachmentsFromButtonAsync_PickerFailure_SetsErrorWithoutThrowing()
    {
        var control = new CopilotChatView
        {
            AttachmentPicker = new ThrowingAttachmentPicker(),
        };

        await control.PickAttachmentsFromButtonAsync();

        Assert.Equal("That attachment could not be added.", control.AttachmentError);
        Assert.Empty(control.Attachments);
    }

    [Fact]
    public void ChatAttachment_InvalidName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ChatAttachment("", new DataContent(new byte[] { 1 }, "image/png")));
    }

    private static ChatAttachment CreateAttachment(
        string name,
        string mediaType = "image/png") =>
        new(name, new DataContent(new byte[] { 1, 2, 3 }, mediaType));

    private sealed class TestAttachmentPicker(
        IReadOnlyList<ChatAttachment> attachments) : IChatAttachmentPicker
    {
        internal long MaxBytes { get; private set; }

        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            FilePickerFileType? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default)
        {
            MaxBytes = maxBytesPerFile;
            return Task.FromResult(attachments);
        }
    }

    private sealed class ThrowingAttachmentPicker : IChatAttachmentPicker
    {
        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            FilePickerFileType? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("File is too large.");
    }
}
