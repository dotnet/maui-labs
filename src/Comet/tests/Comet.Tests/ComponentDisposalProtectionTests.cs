#nullable enable
using System;
using System.Collections.Generic;
using Comet.Backend;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests;

/// <summary>
/// Verifies that diffing detaches retained children from the old parent while terminal
/// parent disposal still releases every child and backend subscription.
/// </summary>
public class ComponentDisposalProtectionTests : TestBase
{
    /// <summary>A minimal Component whose body reads a Signal, creating a body subscription.</summary>
    sealed class LiveComponent : View
    {
        public readonly Signal<int> Counter = new(0);

        public LiveComponent()
        {
            Body = () =>
            {
                // Reading Counter.Value inside a reactive scope creates a body dependency
                var label = new Text($"Count: {Counter.Value}");
                return label;
            };
        }
    }

    /// <summary>A plain View with no body — no subscriptions.</summary>
    sealed class InertView : View { }

    [Fact]
    public void HasActiveBodySubscriptions_TrueAfterBuild()
    {
        var component = new LiveComponent();
        // Force the body to build, which creates the BodyDependencySubscriber
        var built = component.GetView();
        Assert.NotNull(built);
        Assert.True(component.HasActiveBodySubscriptions,
            "Component should have active body subscriptions after GetView()");
    }

    [Fact]
    public void HasActiveBodySubscriptions_FalseBeforeBuild()
    {
        var component = new LiveComponent();
        // Before any build, no subscriptions exist
        Assert.False(component.HasActiveBodySubscriptions);
    }

    [Fact]
    public void HasActiveBodySubscriptions_FalseAfterDispose()
    {
        var component = new LiveComponent();
        _ = component.GetView();
        Assert.True(component.HasActiveBodySubscriptions);

        component.Dispose();

        Assert.False(component.HasActiveBodySubscriptions,
            "After Dispose, body subscriptions should be cleared");
        Assert.True(component.IsDisposed);
    }

    [Fact]
    public void ContainerView_Dispose_DisposesActiveComponent()
    {
        var component = new LiveComponent();
        _ = component.GetView();
        Assert.True(component.HasActiveBodySubscriptions);

        var container = new VStack { component };
        container.Dispose();

        Assert.True(component.IsDisposed);
        Assert.False(component.HasActiveBodySubscriptions);
    }

    [Fact]
    public void ContainerView_Dispose_StillDisposesInertChildren()
    {
        var inert = new InertView();
        var container = new VStack { inert };
        container.Dispose();

        Assert.True(inert.IsDisposed,
            "Inert children (no body subscriptions) must still be disposed");
    }

    [Fact]
    public void ContainerView_Dispose_MixedChildren_DisposesAll()
    {
        var live = new LiveComponent();
        _ = live.GetView();
        var inert = new InertView();

        var container = new VStack { live, inert };
        container.Dispose();

        Assert.True(live.IsDisposed, "Live component must be disposed by its terminal owner");
        Assert.True(inert.IsDisposed, "Inert child must be disposed");
    }

    [Fact]
    public void ContentView_Dispose_DisposesActiveComponent()
    {
        var component = new LiveComponent();
        _ = component.GetView();

        var content = new ContentView { Content = component };
        content.Dispose();

        Assert.True(component.IsDisposed);
    }

    [Fact]
    public void ContentView_Dispose_StillDisposesInertContent()
    {
        var inert = new InertView();
        var content = new ContentView { Content = inert };
        content.Dispose();

        Assert.True(inert.IsDisposed,
            "Inert content must still be disposed");
    }

    [Fact]
    public void AlreadyDisposedComponent_NotSkipped()
    {
        var component = new LiveComponent();
        _ = component.GetView();
        component.Dispose(); // Dispose it first
        Assert.True(component.IsDisposed);
        Assert.False(component.HasActiveBodySubscriptions);

        // A second container disposal touching this already-disposed view should be a no-op
        // (View.OnDispose guards via disposedValue)
        var container = new VStack { component };
        container.Dispose(); // Should not throw
    }

    [Fact]
    public void TerminallyDisposedComponent_DirectDisposeIsIdempotent()
    {
        var component = new LiveComponent();
        _ = component.GetView();

        var container = new VStack { component };
        container.Dispose();
        Assert.True(component.IsDisposed);

        component.Dispose();
        Assert.True(component.IsDisposed);
        Assert.False(component.HasActiveBodySubscriptions);
    }

    [Fact]
    public void TerminallyDisposedComponent_UnsubscribesFromSignal()
    {
        var component = new LiveComponent();
        _ = component.GetView();

        var container = new VStack { component };
        container.Dispose();
        Assert.True(component.IsDisposed);
        Assert.False(component.HasActiveBodySubscriptions);

        var oldValue = component.Counter.Value;
        component.Counter.Value = oldValue + 1;

        Assert.True(component.IsDisposed);
        Assert.False(component.HasActiveBodySubscriptions);
    }

    [Fact]
    public void DiffRetainedChild_SurvivesOldContainerDisposal()
    {
        var retained = new LiveComponent();
        _ = retained.GetView();
        var oldContainer = new VStack { retained };
        var newContainer = new VStack { retained };

        newContainer.Diff(oldContainer, false);
        oldContainer.Dispose();

        Assert.False(retained.IsDisposed);
        Assert.True(retained.HasActiveBodySubscriptions);

        newContainer.Dispose();
        Assert.True(retained.IsDisposed);
    }

    [Fact]
    public void DiffRetainedContent_SurvivesOldContentViewDisposal()
    {
        var retained = new LiveComponent();
        _ = retained.GetView();
        var oldContent = new ContentView { Content = retained };
        var newContent = new ContentView { Content = retained };

        newContent.Diff(oldContent, false);
        oldContent.Dispose();

        Assert.False(retained.IsDisposed);
        Assert.True(retained.HasActiveBodySubscriptions);

        newContent.Dispose();
        Assert.True(retained.IsDisposed);
    }

    // ── Own-content node tests ──────────────────────────────────────

    /// <summary>Fake backend node that tracks AfterFlush subscription and Dispose calls.</summary>
    sealed class FakeOwnContentNode : ICometBackendNode, IBackendManagesOwnContent
    {
        public bool Disposed { get; private set; }
        public bool AfterFlushSubscribed { get; private set; }

        public FakeOwnContentNode()
        {
            ReactiveScheduler.AfterFlush += OnAfterFlush;
            AfterFlushSubscribed = true;
        }

        void OnAfterFlush()
        {
            // Simulates NavigationNode.ReflowTopScreen — subscribes to static event
        }

        public void Dispose()
        {
            ReactiveScheduler.AfterFlush -= OnAfterFlush;
            AfterFlushSubscribed = false;
            Disposed = true;
        }

        // ICometBackendNode stubs
        public void ApplyProperty(PropertyId id, in PropertyValue value) { }
        public void InsertChild(int index, ICometBackendNode child) { }
        public void RemoveChildAt(int index) { }
        public void MoveChild(int from, int to) { }
        public Microsoft.Maui.Graphics.Size Measure(double w, double h) => Microsoft.Maui.Graphics.Size.Zero;
        public void Arrange(Microsoft.Maui.Graphics.Rect frame) { }
        public void SetEventSink(ICometEventSink? sink) { }
    }

    [Fact]
    public void OwnContentNode_DisposedDuringContainerDispose()
    {
        var child = new InertView();
        var node = new FakeOwnContentNode();
        child.Node = node;
        Assert.True(node.AfterFlushSubscribed);

        var container = new VStack { child };
        container.Dispose();

        // The own-content node MUST be disposed to unsubscribe AfterFlush.
        // Skipping it would leak the static event subscription.
        Assert.True(child.IsDisposed, "View with own-content node must be disposed");
        Assert.True(node.Disposed, "Own-content node must be disposed to unhook AfterFlush");
        Assert.False(node.AfterFlushSubscribed, "AfterFlush must be unsubscribed");
    }

    [Fact]
    public void OwnContentNode_DisposedDuringContentViewDispose()
    {
        var child = new InertView();
        var node = new FakeOwnContentNode();
        child.Node = node;

        var content = new ContentView { Content = child };
        content.Dispose();

        Assert.True(child.IsDisposed);
        Assert.True(node.Disposed);
        Assert.False(node.AfterFlushSubscribed);
    }

    [Fact]
    public void OwnContentNode_WithActiveSubscriptions_DisposedByTerminalOwner()
    {
        var component = new LiveComponent();
        _ = component.GetView();
        var node = new FakeOwnContentNode();
        component.Node = node;

        var container = new VStack { component };
        container.Dispose();

        Assert.True(component.IsDisposed);
        Assert.True(node.Disposed);
        Assert.False(node.AfterFlushSubscribed);
    }

    [Fact]
    public void InertViewWithOwnContentNode_NotLeaked()
    {
        // An inert view (no body subscriptions) with an own-content node
        // MUST be disposed — the IBackendManagesOwnContent check alone must NOT save it
        var view = new InertView();
        var node = new FakeOwnContentNode();
        view.Node = node;
        Assert.True(node.AfterFlushSubscribed);

        var container = new VStack { view };
        container.Dispose();

        Assert.True(view.IsDisposed,
            "Inert view with own-content node must not be skipped — would leak AfterFlush");
        Assert.True(node.Disposed);
    }

    [Fact]
    public void DiffTransferredOwnContentNode_SurvivesOldParentDisposal_ThenDisposesWithNewParent()
    {
        var oldChild = new InertView();
        var node = new FakeOwnContentNode();
        oldChild.Node = node;
        var oldContainer = new VStack { oldChild };

        var newChild = new InertView();
        var newContainer = new VStack { newChild };
        newContainer.Diff(oldContainer, false);

        Assert.Same(node, newChild.Node);
        Assert.Null(oldChild.Node);

        oldContainer.Dispose();

        Assert.False(newChild.IsDisposed);
        Assert.False(node.Disposed);
        Assert.True(node.AfterFlushSubscribed);

        newContainer.Dispose();

        Assert.True(newChild.IsDisposed);
        Assert.True(node.Disposed);
        Assert.False(node.AfterFlushSubscribed);
    }
}
