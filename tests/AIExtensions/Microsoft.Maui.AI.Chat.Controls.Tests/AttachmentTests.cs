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
    public async Task PickAttachmentsAsync_PickerFailure_SetsErrorWithoutThrowing()
    {
        var control = new CopilotChatView
        {
            AttachmentPicker = new ThrowingAttachmentPicker(),
        };

        await control.PickAttachmentsAsync();

        Assert.Equal("That attachment could not be added.", control.AttachmentError);
        Assert.Empty(control.Attachments);
    }

    private static ChatAttachment CreateAttachment(
        string name,
        string mediaType = "image/png") =>
        new(name, mediaType, new byte[] { 1, 2, 3 });

    private sealed class TestAttachmentPicker(
        IReadOnlyList<ChatAttachment> attachments) : IChatAttachmentPicker
    {
        internal long MaxBytes { get; private set; }

        public Task<IReadOnlyList<ChatAttachment>> PickAsync(
            object? fileTypes,
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
            object? fileTypes,
            long maxBytesPerFile,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("File is too large.");
    }
}
