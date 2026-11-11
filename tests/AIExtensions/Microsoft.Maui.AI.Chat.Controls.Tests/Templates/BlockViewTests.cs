using System.Runtime.CompilerServices;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public sealed class BlockViewTests : IDisposable
{
    public void Dispose() => ImmediateDispatcher.Install();

    [Fact]
    public void Block_IsTheBindingContext()
    {
        var view = new ChangeCountingView();
        var block = Blocks.Text("hi");

        view.BindingContext = block;
        Assert.Same(block, view.Block);

        view.BindingContext = "not a block";
        Assert.Null(view.Block);
    }

    [Fact]
    public void OnBlockChanged_RunsWhenTheBlockIsAttached()
    {
        var view = new ChangeCountingView();
        var block = Blocks.Text("hi");

        view.BindingContext = block;

        Assert.Equal(1, view.ChangeCount);
        Assert.Same(block, view.LastBlock);
    }

    [Fact]
    public void OnBlockChanged_RunsForEachBlockAndWhenTheBlockGoes()
    {
        var view = new ChangeCountingView();
        var first = Blocks.Text("one");
        var second = Blocks.Text("two");

        view.BindingContext = first;
        view.BindingContext = second;
        Assert.Equal(2, view.ChangeCount);
        Assert.Same(second, view.LastBlock);

        view.BindingContext = null;
        Assert.Equal(3, view.ChangeCount);
        Assert.Null(view.LastBlock);
    }

    [Fact]
    public void OnBlockChanged_FollowsAnInheritedBindingContext()
    {
        var view = new ChangeCountingView();
        var parent = new ContentView { Content = view };
        var block = Blocks.Text("hi");

        parent.BindingContext = block;

        Assert.Same(block, view.Block);
        Assert.Equal(1, view.ChangeCount);
    }

    [Fact]
    public void BlockChanges_InOneDispatcherPass_RunOnBlockChangedOnce()
    {
        using var dispatcher = TestDispatcher.Install();
        var view = new ChangeCountingView();
        var block = Blocks.Text("a");
        view.BindingContext = block;

        block.AppendText("b");
        block.AppendText("c");
        block.Role = ChatRole.User;

        Assert.Equal(1, view.ChangeCount);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunPending();
        Assert.Equal(2, view.ChangeCount);

        block.AppendText("d");
        dispatcher.RunPending();
        Assert.Equal(3, view.ChangeCount);
    }

    [Fact]
    public void AChangePendingForThePreviousBlock_DoesNotRunForIt()
    {
        using var dispatcher = TestDispatcher.Install();
        var view = new ChangeCountingView();
        var first = Blocks.Text("a");
        var second = Blocks.Text("b");
        view.BindingContext = first;

        first.AppendText("!");
        view.BindingContext = second;
        dispatcher.RunPending();
        Assert.Equal(2, view.ChangeCount);

        first.AppendText("?");
        Assert.Equal(0, dispatcher.PendingCount);

        second.AppendText("!");
        dispatcher.RunPending();
        Assert.Equal(3, view.ChangeCount);
        Assert.Same(second, view.LastBlock);
    }

    [Fact]
    public async Task AChangeRaisedOnAnotherThread_RunsOnTheViewsDispatcher()
    {
        using var dispatcher = TestDispatcher.Install();
        var view = new ChangeCountingView();
        var block = Blocks.Text("a");
        view.BindingContext = block;

        await Task.Run(() => block.AppendText("b"));

        Assert.Equal(1, view.ChangeCount);
        dispatcher.RunPending();
        Assert.Equal(2, view.ChangeCount);
        Assert.Equal(Environment.CurrentManagedThreadId, view.LastThreadId);
    }

    [Fact]
    public void WithoutADispatcher_AChangeRunsOnBlockChangedAtOnce()
    {
        DispatcherProvider.SetCurrent(new NoDispatcher());
        var view = new ChangeCountingView();
        var block = Blocks.Text("a");
        view.BindingContext = block;

        block.Role = ChatRole.User;

        Assert.Equal(2, view.ChangeCount);
    }

    [Fact]
    public void OnBlockPropertyChanged_SeesEachChangeAndCanHandleItAlone()
    {
        using var dispatcher = TestDispatcher.Install();
        var view = new PropertyWatchingView();
        var block = Blocks.Text("a");
        view.BindingContext = block;

        block.AppendText("b");

        Assert.Contains(nameof(RichContentBlock.RawText), view.Names);
        Assert.Equal(0, dispatcher.PendingCount);
        Assert.Equal(1, view.ChangeCount);
    }

    [Fact]
    public void ABlockThatOutlivesItsView_DoesNotKeepTheViewAlive()
    {
        var block = Blocks.Text("a");
        var view = AttachAndForget(block);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(view.TryGetTarget(out _));
        block.AppendText("b");
    }

    [Fact]
    public void TypedBlockView_ExposesItsBlockType()
    {
        var view = new ToolNameView();

        view.BindingContext = Blocks.ToolCall("search");
        Assert.Equal("search", view.Block?.ToolName);
        Assert.Equal("search", view.Label.Text);

        view.BindingContext = Blocks.Text("hi");
        Assert.Null(view.Block);
        Assert.Null(view.Label.Text);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ChangeCountingView> AttachAndForget(ContentBlock block) =>
        new(new ChangeCountingView { BindingContext = block });

    private sealed class ChangeCountingView : BlockView
    {
        public int ChangeCount { get; private set; }

        public ContentBlock? LastBlock { get; private set; }

        public int LastThreadId { get; private set; }

        protected override void OnBlockChanged()
        {
            ChangeCount++;
            LastBlock = Block;
            LastThreadId = Environment.CurrentManagedThreadId;
        }
    }

    private sealed class PropertyWatchingView : BlockView
    {
        public List<string?> Names { get; } = [];

        public int ChangeCount { get; private set; }

        protected override void OnBlockChanged() => ChangeCount++;

        protected override void OnBlockPropertyChanged(string? propertyName) => Names.Add(propertyName);
    }

    private sealed class NoDispatcher : IDispatcherProvider
    {
        public IDispatcher? GetForCurrentThread() => null;
    }
}
