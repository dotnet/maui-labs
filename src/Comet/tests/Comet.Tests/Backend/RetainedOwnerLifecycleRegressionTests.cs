#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.Backend;

public class RetainedOwnerLifecycleRegressionTests
{
	static RetainedOwnerLifecycleRegressionTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	class CountingNode : FakeBackendNode
	{
		public CountingNode(string kind) : base(kind) { }

		public int DisposeCount { get; private set; }

		public override void Dispose()
		{
			DisposeCount++;
			base.Dispose();
		}
	}

	sealed class OwnContentNode : CountingNode, IBackendManagesOwnContent
	{
		public OwnContentNode(string kind) : base(kind) { }
	}

	sealed class ReconciledScrollNode : CountingNode, IBackendReconcilesOwnContent, ICometBackendNode
	{
		ScrollView _owner;

		public ReconciledScrollNode(ScrollView owner) : base("scroll")
			=> _owner = owner;

		public FakeBackendNode? ContentNode { get; private set; }
		public View? ContentView { get; private set; }
		public int OwnerChangeCount { get; private set; }

		public void MaterializeContent()
		{
			ContentView = _owner.Content;
			ContentNode = ContentView is null
				? null
				: (FakeBackendNode)CometBackendBridge.MaterializeChild(ContentView, _owner);
		}

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			_owner = Assert.IsType<ScrollView>(newView);
			ContentView = _owner.Content;
			ContentNode = ContentView?.Node as FakeBackendNode;
			OwnerChangeCount++;
		}
	}

	sealed class ClosedDialogScreen : View
	{
		public Signal<int> Count { get; } = new(0);

		[Body]
		View Body() => new VStack
		{
			new Text($"Count: {Count.Value}").AutomationId("live_count"),
			new AlertDialog(
				new Signal<bool>(false),
				new Text("Hidden message").AutomationId("hidden_dialog_message"),
				new Button("OK", () => { }).AutomationId("hidden_dialog_confirm"))
				.AutomationId("closed_dialog"),
		}.AutomationId("live_screen");
	}

	sealed class ScrollScreen : View
	{
		public Signal<int> Count { get; } = new(0);

		[Body]
		View Body() => new ScrollView
		{
			new Text($"Value: {Count.Value}").AutomationId("scroll_value"),
		}.AutomationId("live_scroll");
	}

	sealed class CountingPage : View
	{
		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DisposeCount++;
			base.Dispose(disposing);
		}
	}

	sealed class StagedResource : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose()
			=> DisposeCount++;
	}

	sealed class StagedResourcePage : ContentView
	{
		public StagedResource Resource { get; } = new();
		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				DisposeCount++;
				Resource.Dispose();
			}
			base.Dispose(disposing);
		}
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void ClosedDialog_ReactiveScreenUpdate_RetainsGenerationAndHiddenSlotsStayAbsent()
	{
		CometDevRegistry.Reset();
		CometDevRegistry.Enabled = true;
		try
		{
			var created = new List<CountingNode>();
			ICometBackendNode Factory(View view)
			{
				CountingNode node = view is NavigationView or AlertDialog
					? new OwnContentNode(view.GetType().Name)
					: new CountingNode(view.GetType().Name);
				created.Add(node);
				return node;
			}

			var navigation = new NavigationView();
			CometBackendBridge.Materialize(navigation, Factory, Ctx);
			var screen = new ClosedDialogScreen();
			using var generation = new OwnedContentGeneration(navigation, Factory, Ctx);
			generation.Materialize(screen);

			var firstRoot = screen.GetView();
			var firstRootNode = firstRoot.Node;
			var initialNodeCount = created.Count;
			Assert.False(BackendContentReadiness.HasUnmaterializedBridgeContent(screen));
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId?.StartsWith("hidden_dialog_", StringComparison.Ordinal) == true);

			screen.Count.Value = 1;
			ReactiveScheduler.FlushSync();

			var currentRoot = screen.GetView();
			Assert.NotSame(firstRoot, currentRoot);
			Assert.Same(firstRootNode, currentRoot.Node);
			Assert.Equal(initialNodeCount, created.Count);
			Assert.False(BackendContentReadiness.HasUnmaterializedBridgeContent(screen));
			Assert.Single(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "live_screen");
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId?.StartsWith("hidden_dialog_", StringComparison.Ordinal) == true);

			screen.Dispose();
			navigation.Dispose();
		}
		finally
		{
			CometDevRegistry.Enabled = false;
			CometDevRegistry.Reset();
		}
	}

	[Fact]
	public void ScrollContent_ReactiveOwnerTransfer_UpdatesTextParentageAndDisposal()
	{
		CometDevRegistry.Reset();
		CometDevRegistry.Enabled = true;
		try
		{
			ReconciledScrollNode? scrollNode = null;
			var created = new List<CountingNode>();
			ICometBackendNode Factory(View view)
			{
				CountingNode node;
				if (view is ScrollView scroll)
				{
					scrollNode = new ReconciledScrollNode(scroll);
					node = scrollNode;
				}
				else
				{
					node = new CountingNode(view.GetType().Name);
				}
				created.Add(node);
				return node;
			}

			var screen = new ScrollScreen();
			CometBackendBridge.Materialize(screen, Factory, Ctx);
			var originalScroll = Assert.IsType<ScrollView>(screen.GetView());
			var originalContent = Assert.IsType<Text>(originalScroll.Content);
			var retainedScrollNode = Assert.IsType<ReconciledScrollNode>(originalScroll.Node);
			retainedScrollNode.MaterializeContent();
			var retainedTextNode = Assert.IsType<CountingNode>(originalContent.Node);
			Assert.Equal("Value: 0", retainedTextNode.Get(PropertyIds.Text_Value).AsString);

			screen.Count.Value = 1;
			ReactiveScheduler.FlushSync();

			var currentScroll = Assert.IsType<ScrollView>(screen.GetView());
			var currentContent = Assert.IsType<Text>(currentScroll.Content);
			Assert.NotSame(originalScroll, currentScroll);
			Assert.NotSame(originalContent, currentContent);
			Assert.True(originalScroll.IsDisposed);
			Assert.True(originalContent.IsDisposed);
			Assert.Same(retainedScrollNode, currentScroll.Node);
			Assert.Same(retainedTextNode, currentContent.Node);
			Assert.Same(currentContent, retainedScrollNode.ContentView);
			Assert.Same(retainedTextNode, retainedScrollNode.ContentNode);
			Assert.Equal(1, retainedScrollNode.OwnerChangeCount);
			Assert.Equal("Value: 1", retainedTextNode.Get(PropertyIds.Text_Value).AsString);
			Assert.Equal(2, created.Count);

			var snapshot = CometDevRegistry.Snapshot();
			var scrollEntry = Assert.Single(snapshot, node => node.AutomationId == "live_scroll");
			var textEntry = Assert.Single(snapshot, node => node.AutomationId == "scroll_value");
			Assert.Equal(scrollEntry.Id, textEntry.ParentId);

			screen.Dispose();
			Assert.Equal(1, retainedScrollNode.DisposeCount);
			Assert.Equal(1, retainedTextNode.DisposeCount);
		}
		finally
		{
			CometDevRegistry.Enabled = false;
			CometDevRegistry.Reset();
		}
	}

	[Fact]
	public void NavigationPop_DisposesRemovedPageExactlyOnceAndPreservesRoot()
	{
		var root = new CountingPage();
		var detail = new CountingPage();
		var stack = new List<View> { root, detail };
		var released = new List<View>();

		Assert.True(NavigationStackLifecycle.TryPop(
			stack,
			(page, _) => released.Add(page),
			out var popped));

		Assert.Same(detail, popped);
		Assert.Equal(new[] { detail }, released);
		Assert.Equal(1, detail.DisposeCount);
		Assert.Equal(0, root.DisposeCount);
		Assert.Equal(new[] { root }, stack);
		Assert.False(NavigationStackLifecycle.TryPop(stack, (_, _) => { }, out _));
		Assert.Equal(1, detail.DisposeCount);
	}

	[Fact]
	public void NavigationPopToRoot_DisposesEachRemovedPageOnceAndRetainsRoot()
	{
		var root = new CountingPage();
		root.AutomationId("root");
		var first = new CountingPage();
		var second = new CountingPage();
		var stack = new List<View> { root, first, second };
		var released = new List<View>();
		var equivalentRoot = new CountingPage();
		equivalentRoot.AutomationId("root");

		var retained = NavigationStackLifecycle.ResetToRoot(
			stack,
			equivalentRoot,
			(page, _) => released.Add(page));

		Assert.Same(root, retained);
		Assert.Equal(new View[] { second, first }, released);
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, second.DisposeCount);
		Assert.Equal(0, root.DisposeCount);
		Assert.Equal(new View[] { root }, stack);

		NavigationStackLifecycle.ResetToRoot(
			stack,
			root,
			(_, _) => throw new InvalidOperationException("No retained page should be released twice."));
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, second.DisposeCount);
		Assert.Equal(0, root.DisposeCount);
	}

	[Fact]
	public void DetachedNavigationPop_DisposesRemovedPageResourceAndPreservesRootUntilTerminalDispose()
	{
		var root = new StagedResourcePage();
		var detail = new StagedResourcePage();
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, detail });
		navigation.DetachBackendCallbacks();

		navigation.Pop();
		navigation.Pop();

		Assert.Equal(new View[] { root }, navigation.GetBackendNavigationStack());
		Assert.Equal(1, detail.DisposeCount);
		Assert.Equal(1, detail.Resource.DisposeCount);
		Assert.Equal(0, root.DisposeCount);
		Assert.Equal(0, root.Resource.DisposeCount);

		navigation.Dispose();

		Assert.Equal(1, detail.DisposeCount);
		Assert.Equal(1, detail.Resource.DisposeCount);
		Assert.Equal(1, root.DisposeCount);
		Assert.Equal(1, root.Resource.DisposeCount);
	}

	[Fact]
	public void DetachedNavigationPopToRoot_DisposesAllDetailsAndPreservesRootUntilTerminalDispose()
	{
		var root = new StagedResourcePage();
		var first = new StagedResourcePage();
		var second = new StagedResourcePage();
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, first, second });
		navigation.DetachBackendCallbacks();

		navigation.PopToRoot();
		navigation.PopToRoot();

		Assert.Equal(new View[] { root }, navigation.GetBackendNavigationStack());
		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, first.Resource.DisposeCount);
		Assert.Equal(1, second.DisposeCount);
		Assert.Equal(1, second.Resource.DisposeCount);
		Assert.Equal(0, root.DisposeCount);

		navigation.Dispose();

		Assert.Equal(1, root.DisposeCount);
		Assert.Equal(1, root.Resource.DisposeCount);
	}

	[Fact]
	public void NavigationStackReplacement_DisposesOnlyRemovedOwnedPages()
	{
		var root = new StagedResourcePage();
		var first = new StagedResourcePage();
		var second = new StagedResourcePage();
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, first, second });

		navigation.SetBackendNavigationStack(new View[] { root, second });

		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(0, second.DisposeCount);
		Assert.Equal(0, root.DisposeCount);

		navigation.SetBackendNavigationStack(new View[] { root });

		Assert.Equal(1, first.DisposeCount);
		Assert.Equal(1, second.DisposeCount);
		Assert.Equal(0, root.DisposeCount);

		navigation.Dispose();

		Assert.Equal(1, root.DisposeCount);
		Assert.Equal(1, first.Resource.DisposeCount);
		Assert.Equal(1, second.Resource.DisposeCount);
	}

	[Fact]
	public void NavigationTerminalDispose_DisposesRootAndEveryPushedResourceExactlyOnce()
	{
		var root = new StagedResourcePage();
		var first = new StagedResourcePage();
		var second = new StagedResourcePage();
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, first, second });

		navigation.Dispose();
		navigation.Dispose();

		foreach (var page in new[] { root, first, second })
		{
			Assert.Equal(1, page.DisposeCount);
			Assert.Equal(1, page.Resource.DisposeCount);
		}
	}

	[Fact]
	public void NavigationTerminalDispose_DisposesEquivalentReplacementContentAndPriorStack()
	{
		var originalRoot = new StagedResourcePage();
		var detail = new StagedResourcePage();
		var navigation = new NavigationView { Content = originalRoot };
		navigation.SetBackendNavigationStack(new View[] { originalRoot, detail });
		var replacementRoot = new StagedResourcePage();
		navigation.Content = replacementRoot;

		navigation.Dispose();

		foreach (var page in new[] { originalRoot, detail, replacementRoot })
		{
			Assert.Equal(1, page.DisposeCount);
			Assert.Equal(1, page.Resource.DisposeCount);
		}
	}

	[Fact]
	public void RemovingNestedNavigationPage_DisposesItsIndependentStackExactlyOnce()
	{
		var outerRoot = new StagedResourcePage();
		var nestedRoot = new StagedResourcePage();
		var nestedDetail = new StagedResourcePage();
		var nested = new NavigationView { Content = nestedRoot };
		nested.SetBackendNavigationStack(new View[] { nestedRoot, nestedDetail });
		var outer = new NavigationView { Content = outerRoot };
		outer.SetBackendNavigationStack(new View[] { outerRoot, nested });
		outer.DetachBackendCallbacks();

		outer.Pop();

		Assert.True(nested.IsDisposed);
		Assert.Equal(1, nestedRoot.DisposeCount);
		Assert.Equal(1, nestedRoot.Resource.DisposeCount);
		Assert.Equal(1, nestedDetail.DisposeCount);
		Assert.Equal(1, nestedDetail.Resource.DisposeCount);
		Assert.Equal(0, outerRoot.DisposeCount);

		outer.Dispose();

		Assert.Equal(1, outerRoot.DisposeCount);
		Assert.Equal(1, nestedRoot.DisposeCount);
		Assert.Equal(1, nestedDetail.DisposeCount);
	}

	[Fact]
	public void TransferredNavigationOwner_DoesNotDisposePagesUntilReplacementTerminates()
	{
		var root = new StagedResourcePage();
		var detail = new StagedResourcePage();
		var previous = new NavigationView { Content = root };
		previous.SetBackendNavigationStack(new View[] { root, detail });
		var replacement = new NavigationView { Content = root };

		replacement.SetBackendNavigationStack(previous.GetBackendNavigationStack());
		previous.Dispose();

		Assert.Equal(0, root.DisposeCount);
		Assert.Equal(0, detail.DisposeCount);
		Assert.Same(replacement, root.Navigation);
		Assert.Same(replacement, detail.Navigation);

		replacement.Dispose();

		Assert.Equal(1, root.DisposeCount);
		Assert.Equal(1, root.Resource.DisposeCount);
		Assert.Equal(1, detail.DisposeCount);
		Assert.Equal(1, detail.Resource.DisposeCount);
	}

	[Fact]
	public void PlatformNodes_ReferenceSharedLifecycleSeams_CompileGuard()
	{
		var projectRoot = IOPath.GetFullPath(IOPath.Combine(
			AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
		var composeNavigation = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "Compose", "ComposeNavigationNode.cs"));
		var swiftNavigation = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUINavigationNode.cs"));
		var composeScroll = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "Compose", "ComposeScrollNode.cs"));
		var swiftScroll = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "SwiftUI", "SwiftUIScrollNode.cs"));

		foreach (var navigation in new[] { composeNavigation, swiftNavigation })
		{
			Assert.Contains("NavigationStackLifecycle.DetermineOwnerTransfer", navigation);
			Assert.Contains("NavigationStackLifecycle.PrepareCurrentForExposure", navigation);
			Assert.Contains("NavigationStackLifecycle.TryPop", navigation);
			Assert.Contains("NavigationStackLifecycle.ResetToRoot", navigation);
			Assert.DoesNotContain("ForceInitializeOwnContent", navigation);
		}

		Assert.Contains("IBackendReconcilesOwnContent", composeScroll);
		Assert.Contains("IBackendReconcilesOwnContent", swiftScroll);
		Assert.Contains("OnOwnerViewChanged", composeScroll);
		Assert.Contains("OnOwnerViewChanged", swiftScroll);
	}
}
