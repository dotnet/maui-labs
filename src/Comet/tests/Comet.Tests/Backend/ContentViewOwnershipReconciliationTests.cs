#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Xunit;

namespace Comet.Tests.Backend;

public class ContentViewOwnershipReconciliationTests
{
	static ContentViewOwnershipReconciliationTests()
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

	sealed class RetainedOwnerNode : CountingNode, IBackendRetainsLogicalContentOnOwnerTransfer, ICometBackendNode
	{
		readonly Action<View>? _ownerChanged;

		public RetainedOwnerNode(Action<View>? ownerChanged = null) : base("retained-owner")
			=> _ownerChanged = ownerChanged;

		public View? Owner { get; private set; }

		void ICometBackendNode.OnOwnerViewChanged(View newView, bool isHotReload)
		{
			Owner = newView;
			_ownerChanged?.Invoke(newView);
			base.OnOwnerViewChanged(newView, isHotReload);
		}
	}

	sealed class TrackingText : Text
	{
		public TrackingText(string value) : base(value) { }

		public int DisposeCount { get; private set; }

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				DisposeCount++;
			base.Dispose(disposing);
		}
	}

	sealed class NavigationRootComponent : View
	{
		public string RootText { get; set; } = "old";

		[Body]
		View Body()
		{
			var navigation = new NavigationView
			{
				Content = new TrackingText(RootText)
					.AutomationId("component_navigation_root")
					.Key("root"),
			};
			navigation.Key("navigation");
			return navigation;
		}
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void ContentView_TextToButton_ReplacesNativeChildAndDisposesOutgoing()
	{
		WithRegistry(() =>
		{
			var oldChild = new TrackingText("old").AutomationId("old_content");
			var oldHost = new ContentView { Content = oldChild }.AutomationId("content_host");
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var hostNode = (CountingNode)CometBackendBridge.Materialize(oldHost, Factory, Ctx);
			var oldChildNode = nodes[oldChild];
			var newChild = new Button("new", () => { }).AutomationId("new_content");
			var replacement = new ContentView { Content = newChild }.AutomationId("content_host");

			replacement.Diff(oldHost, false);
			oldHost.Dispose();

			Assert.Same(hostNode, replacement.Node);
			Assert.Same(replacement, newChild.Parent);
			Assert.Single(hostNode.Children);
			Assert.Same(nodes[newChild], hostNode.Children[0]);
			Assert.Equal(1, oldChild.DisposeCount);
			Assert.Equal(1, oldChildNode.DisposeCount);
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				entry => entry.AutomationId == "old_content");
			Assert.Single(
				CometDevRegistry.Snapshot(),
				entry => entry.AutomationId == "new_content");

			replacement.Dispose();
			Assert.True(newChild.IsDisposed);
			Assert.Equal(1, nodes[newChild].DisposeCount);
		});
	}

	[Fact]
	public void ContentView_NullToView_InsertsNativeChildAndRegistersUnderReplacement()
	{
		WithRegistry(() =>
		{
			var oldHost = new ContentView().AutomationId("content_host");
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var hostNode = (CountingNode)CometBackendBridge.Materialize(oldHost, Factory, Ctx);
			var child = new TrackingText("new").AutomationId("inserted_content");
			var replacement = new ContentView { Content = child }.AutomationId("content_host");

			replacement.Diff(oldHost, false);
			oldHost.Dispose();

			Assert.Single(hostNode.Children);
			Assert.Same(nodes[child], hostNode.Children[0]);
			Assert.Same(replacement, child.Parent);
			var snapshot = CometDevRegistry.Snapshot();
			var hostEntry = Assert.Single(snapshot, entry => entry.AutomationId == "content_host");
			var childEntry = Assert.Single(snapshot, entry => entry.AutomationId == "inserted_content");
			Assert.Equal(hostEntry.Id, childEntry.ParentId);

			replacement.Dispose();
			Assert.Equal(1, child.DisposeCount);
		});
	}

	[Fact]
	public void ContentView_ViewToNull_RemovesNativeChildAndDisposesOutgoing()
	{
		WithRegistry(() =>
		{
			var child = new TrackingText("old").AutomationId("removed_content");
			var oldHost = new ContentView { Content = child }.AutomationId("content_host");
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var hostNode = (CountingNode)CometBackendBridge.Materialize(oldHost, Factory, Ctx);
			var childNode = nodes[child];
			var replacement = new ContentView().AutomationId("content_host");

			replacement.Diff(oldHost, false);
			oldHost.Dispose();

			Assert.Empty(hostNode.Children);
			Assert.Null(replacement.Content);
			Assert.Equal(1, child.DisposeCount);
			Assert.Equal(1, childNode.DisposeCount);
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				entry => entry.AutomationId == "removed_content");

			replacement.Dispose();
		});
	}

	[Fact]
	public void ContentView_KeyedSameTypeChild_TransfersNodeAndRegistryIdentity()
	{
		WithRegistry(() =>
		{
			var oldChild = new TrackingText("old")
				.AutomationId("keyed_content")
				.Key("content");
			var oldHost = new ContentView { Content = oldChild }.AutomationId("content_host");
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var hostNode = (CountingNode)CometBackendBridge.Materialize(oldHost, Factory, Ctx);
			var childNode = nodes[oldChild];
			var replacementChild = new TrackingText("new")
				.AutomationId("keyed_content")
				.Key("content");
			var replacement = new ContentView { Content = replacementChild }
				.AutomationId("content_host");

			replacement.Diff(oldHost, false);
			oldHost.Dispose();

			Assert.Same(childNode, replacementChild.Node);
			Assert.Same(childNode, hostNode.Children[0]);
			Assert.Same(replacement, replacementChild.Parent);
			Assert.Equal("new", childNode.Get(PropertyIds.Text_Value).AsString);
			Assert.Equal(1, oldChild.DisposeCount);
			Assert.Equal(0, childNode.DisposeCount);
			Assert.Single(
				CometDevRegistry.Snapshot(),
				entry => entry.AutomationId == "keyed_content");

			replacement.Dispose();
			Assert.Equal(1, replacementChild.DisposeCount);
			Assert.Equal(1, childNode.DisposeCount);
		});
	}

	[Fact]
	public void ContentView_DifferentChildKey_ReplacesNativeIdentity()
	{
		var oldChild = new TrackingText("old").Key("old");
		var oldHost = new ContentView { Content = oldChild };
		var nodes = new Dictionary<View, CountingNode>();
		ICometBackendNode Factory(View view)
		{
			var node = new CountingNode(view.GetType().Name);
			nodes[view] = node;
			return node;
		}

		var hostNode = (CountingNode)CometBackendBridge.Materialize(oldHost, Factory, Ctx);
		var oldChildNode = nodes[oldChild];
		var replacementChild = new TrackingText("new").Key("new");
		var replacement = new ContentView { Content = replacementChild };

		replacement.Diff(oldHost, false);
		oldHost.Dispose();

		Assert.NotSame(oldChildNode, replacementChild.Node);
		Assert.Single(hostNode.Children);
		Assert.Same(nodes[replacementChild], hostNode.Children[0]);
		Assert.Equal(1, oldChild.DisposeCount);
		Assert.Equal(1, oldChildNode.DisposeCount);

		replacement.Dispose();
		Assert.Equal(1, replacementChild.DisposeCount);
	}

	[Fact]
	public void ContentView_KeyedDrawer_DisposesOutgoingOwnerButPreservesReplacementAndSharedSignal()
	{
		var sharedOpen = new Signal<bool>(false);
		var oldSide = new TrackingText("old-side");
		var oldBody = new TrackingText("old-body");
		var oldDrawer = new Drawer(sharedOpen, oldSide, oldBody).Key("drawer");
		var oldHost = new ContentView { Content = oldDrawer };
		var retainedNode = new RetainedOwnerNode();
		CometBackendBridge.Materialize(
			oldHost,
			view => view is Drawer
				? retainedNode
				: new CountingNode(view.GetType().Name),
			Ctx);
		Assert.Equal(1, PropertyChangedSubscriberCount(sharedOpen));

		var newSide = new TrackingText("new-side");
		var newBody = new TrackingText("new-body");
		var replacementDrawer = new Drawer(sharedOpen, newSide, newBody).Key("drawer");
		var replacement = new ContentView { Content = replacementDrawer };

		replacement.Diff(oldHost, false);
		oldHost.Dispose();

		Assert.True(oldDrawer.IsDisposed);
		Assert.Equal(1, oldSide.DisposeCount);
		Assert.Equal(1, oldBody.DisposeCount);
		Assert.False(replacementDrawer.IsDisposed);
		Assert.False(newSide.IsDisposed);
		Assert.False(newBody.IsDisposed);
		Assert.Same(replacementDrawer, retainedNode.Owner);
		Assert.Same(replacement, replacementDrawer.Parent);
		Assert.Equal(1, PropertyChangedSubscriberCount(sharedOpen));

		replacement.Dispose();
		Assert.Equal(1, newSide.DisposeCount);
		Assert.Equal(1, newBody.DisposeCount);
		Assert.Equal(0, PropertyChangedSubscriberCount(sharedOpen));
	}

	[Fact]
	public void ContentView_KeyedFab_DisposesOutgoingOwnedSlots()
	{
		var oldIcon = new TrackingText("old-icon");
		var oldLabel = new TrackingText("old-label");
		var oldFab = new Fab(oldIcon, oldLabel, () => { }, 56).Key("fab");
		var oldHost = new ContentView { Content = oldFab };
		var retainedNode = new RetainedOwnerNode();
		CometBackendBridge.Materialize(
			oldHost,
			view => view is Fab
				? retainedNode
				: new CountingNode(view.GetType().Name),
			Ctx);

		var newIcon = new TrackingText("new-icon");
		var newLabel = new TrackingText("new-label");
		var replacementFab = new Fab(newIcon, newLabel, () => { }, 56).Key("fab");
		var replacement = new ContentView { Content = replacementFab };

		replacement.Diff(oldHost, false);
		oldHost.Dispose();

		Assert.True(oldFab.IsDisposed);
		Assert.Equal(1, oldIcon.DisposeCount);
		Assert.Equal(1, oldLabel.DisposeCount);
		Assert.False(replacementFab.IsDisposed);
		Assert.Same(replacementFab, retainedNode.Owner);

		replacement.Dispose();
		Assert.Equal(1, newIcon.DisposeCount);
		Assert.Equal(1, newLabel.DisposeCount);
	}

	[Fact]
	public void ContentView_UnkeyedRetainedOwner_RemainsDetachedForRetainedSlots()
	{
		var oldFab = new Fab(
			new TrackingText("old-icon"),
			new TrackingText("old-label"),
			() => { },
			56);
		var oldHost = new ContentView { Content = oldFab };
		CometBackendBridge.Materialize(
			oldHost,
			view => view is Fab
				? new RetainedOwnerNode()
				: new CountingNode(view.GetType().Name),
			Ctx);

		var replacementFab = new Fab(
			new TrackingText("new-icon"),
			new TrackingText("new-label"),
			() => { },
			56);
		var replacement = new ContentView { Content = replacementFab };

		replacement.Diff(oldHost, false);
		oldHost.Dispose();

		Assert.False(oldFab.IsDisposed);
		Assert.Null(oldFab.Parent);
		Assert.False(replacementFab.IsDisposed);

		replacement.Dispose();
		oldFab.Dispose();
	}

	[Fact]
	public void KeyedNavigation_ComponentRoot_ReconcilesEquivalentRootBeforeOwnerTransfer()
	{
		var component = new NavigationRootComponent();
		NavigationView? observedOwner = null;
		var navigationNode = new RetainedOwnerNode(owner =>
		{
			var navigation = Assert.IsType<NavigationView>(owner);
			var root = Assert.IsType<TrackingText>(navigation.Content);
			Assert.Equal("new", root.Value.CurrentValue);
			Assert.NotNull(root.Node);
			observedOwner = navigation;
		});
		var nodes = new Dictionary<View, CountingNode>();
		ICometBackendNode Factory(View view)
		{
			var node = view is NavigationView
				? navigationNode
				: new CountingNode(view.GetType().Name);
			nodes[view] = (CountingNode)node;
			return node;
		}

		CometBackendBridge.Materialize(component, Factory, Ctx);
		var oldNavigation = Assert.IsType<NavigationView>(component.GetView());
		var oldRoot = Assert.IsType<TrackingText>(oldNavigation.Content);
		CometBackendBridge.MaterializeChild(oldRoot, oldNavigation);
		var rootNode = nodes[oldRoot];

		component.RootText = "new";
		component.Reload();

		var currentNavigation = Assert.IsType<NavigationView>(component.GetView());
		var currentRoot = Assert.IsType<TrackingText>(currentNavigation.Content);
		Assert.Same(currentNavigation, observedOwner);
		Assert.Same(navigationNode, currentNavigation.Node);
		Assert.Same(rootNode, currentRoot.Node);
		Assert.True(oldNavigation.IsDisposed);
		Assert.Equal(1, oldRoot.DisposeCount);
		Assert.False(currentRoot.IsDisposed);

		component.Dispose();
		Assert.Equal(1, currentRoot.DisposeCount);
	}

	[Fact]
	public void KeyedNavigation_InContentView_ReconcilesEquivalentRootBeforeOwnerTransfer()
	{
		var oldRoot = new TrackingText("old").Key("root");
		var oldNavigation = new NavigationView { Content = oldRoot }.Key("navigation");
		var oldHost = new ContentView { Content = oldNavigation };
		var navigationNode = new RetainedOwnerNode(owner =>
		{
			var navigation = Assert.IsType<NavigationView>(owner);
			Assert.Equal("new", Assert.IsType<TrackingText>(navigation.Content).Value.CurrentValue);
			Assert.NotNull(navigation.Content.Node);
		});
		var nodes = new Dictionary<View, CountingNode>();
		ICometBackendNode Factory(View view)
		{
			var node = view is NavigationView
				? navigationNode
				: new CountingNode(view.GetType().Name);
			nodes[view] = (CountingNode)node;
			return node;
		}

		CometBackendBridge.Materialize(oldHost, Factory, Ctx);
		CometBackendBridge.MaterializeChild(oldRoot, oldNavigation);
		var rootNode = nodes[oldRoot];
		var replacementRoot = new TrackingText("new").Key("root");
		var replacementNavigation = new NavigationView { Content = replacementRoot }.Key("navigation");
		var replacementHost = new ContentView { Content = replacementNavigation };

		replacementHost.Diff(oldHost, false);
		oldHost.Dispose();

		Assert.Same(rootNode, replacementRoot.Node);
		Assert.Same(navigationNode, replacementNavigation.Node);
		Assert.Equal(1, oldRoot.DisposeCount);
		Assert.False(replacementRoot.IsDisposed);

		replacementHost.Dispose();
		Assert.Equal(1, replacementRoot.DisposeCount);
	}

	[Fact]
	public void NavigationOwnerReset_HandlesEquivalentChangedAndMissingRoots()
	{
		var oldRoot = new Text("old").Key("root");
		var stack = new List<View> { oldRoot };
		var current = new NavigationView { Content = oldRoot }.Key("navigation");

		var equivalent = new NavigationView
		{
			Content = new Text("new").Key("root"),
		}.Key("navigation");
		Assert.False(NavigationStackLifecycle.RequiresOwnerReset(stack, equivalent, false));
		Assert.Equal(
			NavigationOwnerTransferAction.PreserveStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				current,
				stack,
				equivalent,
				false));

		var changed = new NavigationView
		{
			Content = new Button("new", () => { }).Key("root"),
		}.Key("navigation");
		Assert.True(NavigationStackLifecycle.RequiresOwnerReset(stack, changed, false));
		Assert.Equal(
			NavigationOwnerTransferAction.ResetStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				current,
				stack,
				changed,
				false));

		var removed = new NavigationView().Key("navigation");
		Assert.True(NavigationStackLifecycle.RequiresOwnerReset(stack, removed, false));

		var added = new NavigationView
		{
			Content = new Text("new").Key("root"),
		}.Key("navigation");
		Assert.True(NavigationStackLifecycle.RequiresOwnerReset(Array.Empty<View>(), added, false));
		Assert.False(
			NavigationStackLifecycle.RequiresOwnerReset(
				Array.Empty<View>(),
				new NavigationView().Key("navigation"),
				false));
		Assert.True(NavigationStackLifecycle.RequiresOwnerReset(stack, equivalent, true));

		var unkeyedRoot = new Text("first");
		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				new NavigationView { Content = unkeyedRoot },
				new View[] { unkeyedRoot },
				new NavigationView { Content = new Text("updated") },
				false));
		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				new NavigationView { Content = new Text("activity") }.Key("activity"),
				new View[] { new Text("activity") },
				new NavigationView { Content = new Text("settings") }.Key("settings"),
				false));
		Assert.Equal(
			NavigationOwnerTransferAction.SwitchStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				new NavigationView { Content = new Text("activity") },
				new View[] { new Text("activity") },
				new NavigationView { Content = new Button("settings", () => { }) },
				false));
		Assert.Equal(
			NavigationOwnerTransferAction.ResetStack,
			NavigationStackLifecycle.DetermineOwnerTransfer(
				current,
				stack,
				removed,
				false));

		current.Dispose();
		oldRoot.Dispose();
		equivalent.Dispose();
		changed.Dispose();
		removed.Dispose();
		added.Dispose();
	}

	[Fact]
	public void KeyedNavigation_RootRemovalRequestsResetAndDisposesOutgoingRoot()
	{
		var oldRoot = new TrackingText("old").Key("root");
		var oldNavigation = new NavigationView { Content = oldRoot }.Key("navigation");
		var stack = new List<View> { oldRoot };
		var resetRequired = false;
		var navigationNode = new RetainedOwnerNode(owner =>
		{
			var replacement = Assert.IsType<NavigationView>(owner);
			resetRequired = NavigationStackLifecycle.RequiresOwnerReset(
				stack,
				replacement,
				false);
		});
		var nodes = new Dictionary<View, CountingNode>();
		ICometBackendNode Factory(View view)
		{
			var node = view is NavigationView
				? navigationNode
				: new CountingNode(view.GetType().Name);
			nodes[view] = (CountingNode)node;
			return node;
		}

		CometBackendBridge.Materialize(oldNavigation, Factory, Ctx);
		CometBackendBridge.MaterializeChild(oldRoot, oldNavigation);
		var oldRootNode = nodes[oldRoot];
		var replacementNavigation = new NavigationView().Key("navigation");

		replacementNavigation.Diff(oldNavigation, false);
		oldNavigation.Dispose();

		Assert.True(resetRequired);
		Assert.Null(replacementNavigation.Content);
		Assert.True(oldRoot.IsDisposed);
		Assert.Equal(1, oldRoot.DisposeCount);
		Assert.Equal(1, oldRootNode.DisposeCount);
		Assert.Same(navigationNode, replacementNavigation.Node);

		replacementNavigation.Dispose();
	}

	[Fact]
	public void NavigationView_DetachedBackendCallbacks_UpdateInactiveStackOnly()
	{
		var root = new TrackingText("root");
		var detail = new TrackingText("detail");
		var navigation = new NavigationView { Content = root };
		navigation.SetBackendNavigationStack(new View[] { root, detail });
		var resetCalls = 0;
		var navigateCalls = 0;
		var popCalls = 0;
		navigation.SetPerformContentReset(_ => resetCalls++);
		navigation.SetPerformNavigate(_ => navigateCalls++);
		navigation.SetPerformPop(() => popCalls++);

		navigation.DetachBackendCallbacks();
		navigation.PopToRoot();
		var next = new TrackingText("next");
		navigation.Navigate(next);

		Assert.Equal(0, resetCalls);
		Assert.Equal(0, navigateCalls);
		Assert.Equal(0, popCalls);
		Assert.Equal(new View[] { root, next }, navigation.GetBackendNavigationStack());

		navigation.Dispose();
		detail.Dispose();
		next.Dispose();
	}

	static int PropertyChangedSubscriberCount<T>(Signal<T> signal)
	{
		var field = typeof(Signal<T>).GetField(
			nameof(Signal<T>.PropertyChanged),
			BindingFlags.Instance | BindingFlags.NonPublic);
		var handlers = field?.GetValue(signal) as Delegate;
		return handlers?.GetInvocationList().Length ?? 0;
	}

	static void WithRegistry(Action test)
	{
		CometDevRegistry.Reset();
		CometDevRegistry.Enabled = true;
		try
		{
			test();
		}
		finally
		{
			CometDevRegistry.Enabled = false;
			CometDevRegistry.Reset();
		}
	}
}
