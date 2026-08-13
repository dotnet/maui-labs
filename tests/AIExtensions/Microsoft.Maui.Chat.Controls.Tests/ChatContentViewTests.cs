using Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.Chat.Controls.Tests;

/// <summary>
/// Covers <see cref="ChatContentView"/> and the built-in views: subscription lifetime, in-place updates,
/// per-cell state reset, and the bubble chrome driven by grouping flags and appearance.
/// </summary>
public class ChatContentViewTests
{
    [Fact]
    public void AssigningAnItem_RefreshesOnce()
    {
        var view = new RecordingView();

        view.Item = ChatFactory.Item(ChatFactory.Remote());

        Assert.Equal(1, view.RefreshCount);
        Assert.Equal(1, view.ItemChangedCount);
    }

    [Fact]
    public void AssigningAnItem_ReportsTheOldAndNewRow()
    {
        var view = new RecordingView();
        var first = ChatFactory.Item(ChatFactory.Remote());
        var second = ChatFactory.Item(ChatFactory.Remote());

        view.Item = first;
        view.Item = second;

        Assert.Same(first, view.LastOldItem);
        Assert.Same(second, view.LastNewItem);
    }

    [Fact]
    public void ContentChange_UpdatesInPlaceWithoutARebuild()
    {
        var content = new TextMessageContent("He");
        var view = new RecordingView { Item = ChatFactory.Item(ChatFactory.Remote(), content) };
        var refreshesBefore = view.RefreshCount;

        content.Append("llo");

        Assert.Equal(1, view.ContentUpdatedCount);
        Assert.Equal(refreshesBefore, view.RefreshCount);
    }

    [Fact]
    public void ContentChange_AfterReassignment_NoLongerReachesTheOldRow()
    {
        var content = new TextMessageContent("He");
        var view = new RecordingView { Item = ChatFactory.Item(ChatFactory.Remote(), content) };

        view.Item = ChatFactory.Item(ChatFactory.Remote());
        content.Append("llo");

        Assert.Equal(0, view.ContentUpdatedCount);
    }

    [Fact]
    public void ClearingTheItem_Unsubscribes()
    {
        var content = new TextMessageContent();
        var view = new RecordingView { Item = ChatFactory.Item(ChatFactory.Remote(), content) };

        view.Item = null;
        content.Append("more");

        Assert.Equal(0, view.ContentUpdatedCount);
    }

    [Fact]
    public void FlagChange_RefreshesTheView()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        var view = new RecordingView { Item = item };
        var before = view.RefreshCount;

        item.UpdateFlags(true, true, true, false, false);

        Assert.True(view.RefreshCount > before);
    }

    [Fact]
    public void ItemContentNotification_IsTreatedAsAnInPlaceUpdate()
    {
        var item = ChatFactory.Item(ChatFactory.Remote());
        var view = new RecordingView { Item = item };
        var refreshes = view.RefreshCount;

        item.NotifyContentUpdated();

        Assert.Equal(1, view.ContentUpdatedCount);
        Assert.Equal(refreshes, view.RefreshCount);
    }

    [Fact]
    public void MessageNotification_RefreshesTheChrome()
    {
        var item = ChatFactory.Item(ChatFactory.Local());
        var view = new RecordingView { Item = item };
        var refreshes = view.RefreshCount;

        item.NotifyMessageUpdated();

        Assert.Equal(refreshes + 1, view.RefreshCount);
    }

    [Fact]
    public void Appearance_FallsBackToTheSharedDefault()
    {
        var view = new RecordingView();

        Assert.Same(ChatAppearance.Default, view.VisibleAppearance);
    }

    [Fact]
    public void TextView_RendersTheText()
    {
        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("hello")) };

        Assert.Contains(VisualTree.All<Label>(view), label => label.Text == "hello");
    }

    [Fact]
    public void TextView_UpdatesInPlaceWhileStreaming()
    {
        var content = new TextMessageContent("He");
        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote(), content) };

        content.Append("llo");

        Assert.Contains(VisualTree.All<Label>(view), label => label.Text == "Hello");
    }

    [Fact]
    public void TextView_ShowsTheParticipantNameOnlyOnTheFirstRowOfAGroup()
    {
        var item = ChatFactory.Item(ChatFactory.Remote(id: "them", name: "Ada Lovelace"));
        var view = new ChatTextContentView { Item = item };

        Assert.Contains(VisualTree.All<Label>(view), label => label.IsVisible && label.Text == "Ada Lovelace");

        item.UpdateFlags(false, true, true, isFirstFromParticipant: false, isLastFromParticipant: true);

        Assert.DoesNotContain(VisualTree.All<Label>(view), label => label.IsVisible && label.Text == "Ada Lovelace");
    }

    [Fact]
    public void TextView_ShowsTheAvatarInitialsWhenNoAvatarImageIsSet()
    {
        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote(name: "Ada Lovelace")) };

        Assert.Contains(VisualTree.All<Label>(view), label => label.IsVisible && label.Text == "AL");
        Assert.DoesNotContain(VisualTree.All<Image>(view), image => image.IsVisible);
    }

    [Fact]
    public void TextView_WithAvatarImage_UsesItInsteadOfInitials()
    {
        var participant = ChatFactory.Remote(name: "Ada Lovelace");
        participant.Avatar = ImageSource.FromFile("avatar.png");

        var view = new ChatTextContentView { Item = ChatFactory.Item(participant) };

        Assert.Contains(VisualTree.All<Image>(view), image => image.IsVisible && image.Source is not null);
        Assert.DoesNotContain(VisualTree.All<Label>(view), label => label.IsVisible && label.Text == "AL");
    }

    [Fact]
    public void TextView_HidesTheAvatarWhenTheAppearanceSaysSo()
    {
        var appearance = new ChatAppearance { ShowAvatars = false };
        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote(name: "Ada Lovelace"), appearance: appearance) };

        Assert.DoesNotContain(VisualTree.All<Label>(view), label => label.IsVisible && label.Text == "AL");
    }

    [Fact]
    public void TextView_AppliesTheAppearanceToTheBubble()
    {
        var appearance = new ChatAppearance
        {
            BubbleCornerRadius = 4,
            MaxBubbleWidth = 120,
            BubbleStrokeThickness = 2,
            IncomingBubbleColor = Colors.Goldenrod,
            IncomingTextColor = Colors.Navy,
        };

        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote(), appearance: appearance) };
        var bubble = VisualTree.All<Border>(view).First(border => border.Content is ContentView);

        Assert.Equal(120, bubble.MaximumWidthRequest);
        Assert.Equal(2, bubble.StrokeThickness);
        Assert.Equal(Colors.Goldenrod, bubble.BackgroundColor);
        Assert.Contains(VisualTree.All<Label>(view), label => label.TextColor == Colors.Navy);
    }

    [Fact]
    public void TextView_AlignsOutgoingRowsToTheEnd()
    {
        var item = ChatFactory.Item(ChatFactory.Local());
        var view = new ChatTextContentView { Item = item };
        var bubble = VisualTree.All<Border>(view).First(border => border.Content is ContentView);

        Assert.Equal(LayoutOptions.End, bubble.HorizontalOptions);

        item.UpdateFlags(false, true, true, true, true);
        Assert.Equal(LayoutOptions.Start, bubble.HorizontalOptions);
    }

    [Fact]
    public void TextView_ShowsTimestampAndStatusOnlyOnTheLastRowOfAGroup()
    {
        var appearance = new ChatAppearance { TimestampFormat = "HH:mm" };
        var message = new ConversationMessage(
            ChatFactory.Local(),
            "hello",
            "m-1",
            new DateTimeOffset(2024, 1, 1, 9, 30, 0, TimeSpan.Zero))
        {
            Status = ConversationMessageStatus.Read,
        };

        var item = new ChatContentItem(message, message.Contents[0], null, appearance);
        var view = new ChatTextContentView { Item = item };

        var metadata = VisualTree.All<Label>(view).First(label => label.Text?.Contains("09:30", StringComparison.Ordinal) == true);
        Assert.True(metadata.IsVisible);
        Assert.Contains("✓✓", metadata.Text);

        item.UpdateFlags(true, true, true, true, isLastFromParticipant: false);
        Assert.False(metadata.IsVisible);
    }

    [Fact]
    public void TextView_HidesTheStatusForIncomingRows()
    {
        var appearance = new ChatAppearance { TimestampFormat = "HH:mm" };
        var message = new ConversationMessage(
            ChatFactory.Remote(),
            "hello",
            "m-1",
            new DateTimeOffset(2024, 1, 1, 9, 30, 0, TimeSpan.Zero))
        {
            Status = ConversationMessageStatus.Read,
        };

        var view = new ChatTextContentView
        {
            Item = new ChatContentItem(message, message.Contents[0], null, appearance),
        };

        Assert.DoesNotContain(VisualTree.All<Label>(view), label => label.Text?.Contains('✓') == true);
    }

    [Fact]
    public void TextView_SetsASemanticDescription()
    {
        var view = new ChatTextContentView
        {
            Item = ChatFactory.Item(ChatFactory.Remote(name: "Ada"), new TextMessageContent("hello")),
        };

        var description = SemanticProperties.GetDescription(view);

        Assert.Contains("Ada", description);
        Assert.Contains("hello", description);
    }

    [Fact]
    public void TextView_WithoutARow_HidesItself()
    {
        var view = new ChatTextContentView { Item = ChatFactory.Item(ChatFactory.Remote()) };

        view.Item = null;

        var root = Assert.IsType<Grid>(view.Content);
        Assert.False(root.IsVisible);
    }

    [Fact]
    public void MediaView_BuildsAnImageSourceFromBytes()
    {
        var media = ChatFactory.Image();
        var view = new ChatMediaContentView { Item = ChatFactory.Item(ChatFactory.Remote(), media) };

        var image = VisualTree.All<Image>(view).First(i => i.Source is StreamImageSource);
        Assert.NotNull(image.Source);
    }

    [Fact]
    public void MediaView_BuildsAnImageSourceFromAnAbsoluteUri()
    {
        var media = new MediaMessageContent(new Uri("https://example.test/cat.png"), "image/png");
        var view = new ChatMediaContentView { Item = ChatFactory.Item(ChatFactory.Remote(), media) };

        Assert.Contains(VisualTree.All<Image>(view), image => image.Source is UriImageSource);
    }

    [Fact]
    public void MediaView_WithARelativeUri_FallsBackToAFileSource()
    {
        var media = new MediaMessageContent(new Uri("cat.png", UriKind.Relative), "image/png");

        Assert.IsType<FileImageSource>(ChatMediaContentView.CreateImageSource(media));
    }

    [Fact]
    public void MediaView_CreateImageSource_WithNull_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ChatMediaContentView.CreateImageSource(null!));

    [Fact]
    public async Task MediaView_ByteSource_IsReReadable()
    {
        var media = ChatFactory.Image();
        var source = Assert.IsType<StreamImageSource>(ChatMediaContentView.CreateImageSource(media));

        using var first = await source.Stream(CancellationToken.None);
        using var second = await source.Stream(CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(4, first!.Length);
        Assert.Equal(4, second!.Length);
    }

    [Fact]
    public void MediaView_ReusedCell_DropsThePreviousImage()
    {
        var view = new ChatMediaContentView { Item = ChatFactory.Item(ChatFactory.Remote(), ChatFactory.Image()) };
        var image = VisualTree.All<Image>(view).First(i => i.Source is StreamImageSource);

        view.Item = ChatFactory.Item(ChatFactory.Remote(), new TextMessageContent("not media"));

        Assert.Null(image.Source);
    }

    [Fact]
    public void MediaView_UsesTheAltTextAsItsDescription()
    {
        var media = ChatFactory.Image();
        media.AltText = "A ginger cat";

        var view = new ChatMediaContentView { Item = ChatFactory.Item(ChatFactory.Remote(name: "Ada"), media) };

        Assert.Contains("A ginger cat", SemanticProperties.GetDescription(view));
    }

    [Fact]
    public void FileView_ShowsTheFileNameAndSize()
    {
        var view = new ChatFileContentView { Item = ChatFactory.Item(ChatFactory.Remote(), ChatFactory.File("report.pdf")) };
        var labels = VisualTree.All<Label>(view);

        Assert.Contains(labels, label => label.Text == "report.pdf");
        Assert.Contains(labels, label => label.Text?.Contains("application/pdf", StringComparison.Ordinal) == true);
        Assert.Contains(labels, label => label.Text?.Contains("5 B", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3 * 1024 * 1024, "3 MB")]
    public void FileView_FormatsTheSize(int byteCount, string expected)
    {
        var media = new MediaMessageContent(new byte[byteCount], "application/octet-stream") { FileName = "blob.bin" };
        var view = new ChatFileContentView { Item = ChatFactory.Item(ChatFactory.Remote(), media) };

        Assert.Contains(
            VisualTree.All<Label>(view),
            label => label.Text?.Contains(expected, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void FileView_ForUriMedia_ShowsOnlyTheMediaType()
    {
        var media = new MediaMessageContent(new Uri("https://example.test/a.pdf"), "application/pdf")
        {
            FileName = "a.pdf",
        };

        var view = new ChatFileContentView { Item = ChatFactory.Item(ChatFactory.Remote(), media) };

        Assert.Contains(VisualTree.All<Label>(view), label => label.Text == "application/pdf");
    }

    private sealed class RecordingView : ChatBubbleView
    {
        public int RefreshCount { get; private set; }

        public int ContentUpdatedCount { get; private set; }

        public int ItemChangedCount { get; private set; }

        public ChatContentItem? LastOldItem { get; private set; }

        public ChatContentItem? LastNewItem { get; private set; }

        public ChatAppearance VisibleAppearance => Appearance;

        protected override void OnItemChanged(ChatContentItem? oldItem, ChatContentItem? newItem)
        {
            base.OnItemChanged(oldItem, newItem);

            ItemChangedCount++;
            LastOldItem = oldItem;
            LastNewItem = newItem;
        }

        protected override void RefreshContent()
        {
            base.RefreshContent();
            RefreshCount++;
        }

        protected override void OnContentUpdated() => ContentUpdatedCount++;

        protected override string GetContentDescription() => "recording";
    }
}
