using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.AI.Chat.Tests;

[Collection("Chat diagnostics")]
public sealed class DiagnosticsViewModelTests
{
    [Fact]
    public void Commands_KeepSidebarStateIndependentAndRefreshSharedBufferAfterClear()
    {
        using var receiver = new ChatDiagnostics();
        var chat = new DiagnosticsViewModel(receiver);
        var embeddings = new DiagnosticsViewModel(receiver);
        var notifications = new List<string?>();
        chat.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        Assert.True(chat.IsOpen);
        Assert.True(embeddings.IsOpen);
        Assert.Equal("Hide AI diagnostics", chat.ToggleDescription);
        chat.CloseCommand.Execute(null);
        Assert.False(chat.IsOpen);
        Assert.Equal("Show AI diagnostics", chat.ToggleDescription);
        Assert.True(embeddings.IsOpen);
        chat.ToggleCommand.Execute(null);
        Assert.True(chat.IsOpen);
        Assert.Equal(["IsOpen", "ToggleDescription", "IsOpen", "ToggleDescription"], notifications);

        receiver.CreateLogger("Microsoft.Extensions.AI.Test").LogDebug("Existing diagnostic");
        chat.Refresh();
        embeddings.Refresh();
        var group = Assert.Single(chat.Groups);
        Assert.Equal("Uncorrelated", group.Title);
        chat.Refresh();
        Assert.Same(group, Assert.Single(chat.Groups));
        embeddings.ClearCommand.Execute(null);
        Assert.Empty(embeddings.Groups);
        Assert.Empty(receiver.Snapshot().Entries);
        chat.Refresh();
        Assert.Empty(chat.Groups);
    }

    [Fact]
    public void ClosedSidebar_StillPresentsCapturedEntriesAfterReopening()
    {
        using var receiver = new ChatDiagnostics();
        var viewModel = new DiagnosticsViewModel(receiver);
        viewModel.CloseCommand.Execute(null);
        receiver.CreateLogger("Microsoft.Extensions.AI.Test").LogError("Existing failure");
        viewModel.ToggleCommand.Execute(null);
        viewModel.Refresh();
        Assert.True(viewModel.IsOpen);
        Assert.Equal("Existing failure", Assert.Single(Assert.Single(viewModel.Groups)).Message);
    }
}
