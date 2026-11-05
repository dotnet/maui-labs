#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Comet.Backend;
using Comet.DevTools;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using Xunit;
using IOPath = System.IO.Path;

namespace Comet.Tests.Backend;

public class RetainedLifecycleContractTests
{
	static RetainedLifecycleContractTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}

	sealed class OwnContentNode : FakeBackendNode, IBackendManagesOwnContent
	{
		public OwnContentNode(string kind) : base(kind)
			=> MeasureResult = new Size(240, 120);
	}

	class CountingNode : FakeBackendNode
	{
		public CountingNode(string kind) : base(kind)
			=> MeasureResult = new Size(240, 24);

		public int DisposeCount { get; private set; }

		public override void Dispose()
		{
			DisposeCount++;
			base.Dispose();
		}
	}

	sealed class ActivatableNode : CountingNode, IBackendContentActivation
	{
		public ActivatableNode(string kind) : base(kind) { }

		public List<bool> Activations { get; } = new();

		public void SetContentActive(bool active) => Activations.Add(active);
	}

	sealed class ManagementRoot : View
	{
		public Signal<bool> Loading { get; } = new(true);

		[Body]
		View Body()
		{
			if (Loading.Value)
			{
				return new VStack
				{
					new ActivityIndicator(true).AutomationId("management_loading"),
				}.AutomationId("management_root");
			}

			return new VStack
			{
				new ListView<int>(() => new[] { 1, 2 })
				{
					ViewFor = value => new Text($"Item {value}"),
				}
				.AutomationId("management_list"),
				new Text("Saved").AutomationId("management_toast"),
			}.AutomationId("management_root");
		}
	}

	sealed class ReplacementRoute : View
	{
		readonly string _caption;
		readonly Action _action;

		public ReplacementRoute(string caption, Action action)
		{
			_caption = caption;
			_action = action;
		}

		[Body]
		View Body() => new VStack
		{
			new Text(_caption).AutomationId("replacement_route_caption"),
			new Button("RUN", _action).AutomationId("replacement_route_action"),
		}.AutomationId("replacement_route_root");
	}

	sealed class RootChangingRoute : View
	{
		readonly bool _composite;
		readonly string _caption;

		public RootChangingRoute(bool composite, string caption)
		{
			_composite = composite;
			_caption = caption;
		}

		[Body]
		View Body() => _composite
			? new VStack
			{
				new Text(_caption).AutomationId("root_changing_caption"),
			}.AutomationId("root_changing_stack")
			: new Text(_caption).AutomationId("root_changing_caption");
	}

	sealed class SparseVStack : VStack
	{
		public void AddSparse(View? view)
		{
			if (view is null)
				Views.Add(null!);
			else
				Add(view);
		}
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void Pop_ExposesRematerializedBuriedRootWithCurrentFrames()
	{
		var root = new ManagementRoot();
		var detail = new Text("Detail");
		var stack = new List<View> { root, detail };
		var active = new OwnedContentSlot<FakeBackendNode>(
			new ContentView(),
			Factory,
			Ctx);
		var cached = active.Materialize(root);
		active.Clear();

		root.Loading.Value = false;
		ReactiveScheduler.FlushSync();

		var currentBody = root.GetView();
		Assert.Null(FindByAutomationId(currentBody, "management_list").Node);
		Assert.Null(FindByAutomationId(currentBody, "management_toast").Node);
		Assert.True(BackendContentReadiness.HasUnmaterializedBridgeContent(root));

		Assert.True(NavigationStackLifecycle.TryPop(
			stack,
			(_, _) => { },
			out var popped));
		Assert.Same(detail, popped);

		var exposed = NavigationStackLifecycle.PrepareCurrentForExposure(
			stack,
			view => active.TryGet(out var node, out var activeView) &&
				ReferenceEquals(activeView, view)
					? node
					: null,
			view => cached = active.Materialize(view),
			view => CometBackendLayoutEngine.Layout(view, new Size(320, 640)),
			out var rematerialized);

		Assert.True(rematerialized);
		Assert.Same(cached, exposed);
		Assert.False(BackendContentReadiness.HasUnmaterializedBridgeContent(root));
		Assert.NotNull(NodeByAutomationId(root, "management_list").ArrangedFrame);
		Assert.NotNull(NodeByAutomationId(root, "management_toast").ArrangedFrame);

		active.Dispose();
		root.Dispose();
	}

	[Fact]
	public void RetainedContentCache_ReturningToRoute_ReusesMaterializedGeneration()
	{
		var owner = new ContentView();
		var routes = new View[]
		{
			new Text("first").Key("first"),
			new Text("second").Key("second"),
		};
		var created = new List<ActivatableNode>();
		ICometBackendNode Create(View view)
		{
			var node = new ActivatableNode(view.GetType().Name);
			created.Add(node);
			return node;
		}

		using var cache = new RetainedContentCache<ActivatableNode>(owner, Create, Ctx);
		var first = cache.GetOrMaterialize(0, routes[0]).Node;
		cache.SetActive(0, true);
		cache.SetActive(0, false);
		var second = cache.GetOrMaterialize(1, routes[1]).Node;
		cache.SetActive(1, true);
		cache.SetActive(1, false);
		var returned = cache.GetOrMaterialize(0, routes[0]).Node;
		cache.SetActive(0, true);

		Assert.Same(first, returned);
		Assert.NotSame(first, second);
		Assert.Equal(2, created.Count);
		Assert.Equal(new[] { true, false, true }, first.Activations);
		Assert.Equal(new[] { true, false }, second.Activations);
	}

	[Fact]
	public void RetainedContentCache_OwnerTransferWithSameKey_ReconcilesReplacementDeclaration()
	{
		var owner = new ContentView();
		var replacementOwner = new ContentView();
		var originalCalls = 0;
		var replacementCalls = 0;
		var original = new ReplacementRoute("original", () => originalCalls++).Key("route");
		var replacement = new ReplacementRoute("replacement", () => replacementCalls++).Key("route");
		var created = new List<ActivatableNode>();
		ICometBackendNode Create(View view)
		{
			var node = new ActivatableNode(view.GetType().Name);
			created.Add(node);
			return node;
		}

		using var cache = new RetainedContentCache<ActivatableNode>(owner, Create, Ctx);
		var first = cache.GetOrMaterialize(0, original);

		cache.TransferOwner(replacementOwner, new[] { replacement }, reset: false);
		var retained = cache.GetOrMaterialize(0, replacement);

		Assert.Same(first.Node, retained.Node);
		Assert.Same(replacement, retained.View);
		Assert.Same(
			first.Node,
			NodeByAutomationId(replacement, "replacement_route_root"));
		Assert.Equal(
			"replacement",
			NodeByAutomationId(replacement, "replacement_route_caption")
				.Get(PropertyIds.Text_Value).AsString);

		NodeByAutomationId(replacement, "replacement_route_action")
			.Sink!.OnEvent(EventIds.Clicked);

		Assert.Equal(0, originalCalls);
		Assert.Equal(1, replacementCalls);
		Assert.Equal(3, created.Count);
	}

	[Fact]
	public void RetainedContentCache_SameIndexRootChange_ReplacesActiveGeneration()
	{
		var owner = new ContentView();
		var replacementOwner = new ContentView();
		var original = new RootChangingRoute(true, "stack").Key("route");
		var replacement = new RootChangingRoute(false, "text").Key("route");
		var created = new List<ActivatableNode>();
		ICometBackendNode Create(View view)
		{
			var node = new ActivatableNode(view.GetType().Name);
			created.Add(node);
			return node;
		}

		using var cache = new RetainedContentCache<ActivatableNode>(owner, Create, Ctx);
		var first = cache.GetOrMaterialize(0, original);
		cache.SetActive(0, true);
		var originalGeneration = created.ToArray();

		cache.TransferOwner(replacementOwner, new[] { replacement }, reset: false);
		var current = cache.GetOrMaterialize(0, replacement);
		cache.SetActive(0, true);

		Assert.NotSame(first.Node, current.Node);
		Assert.Same(replacement, current.View);
		Assert.Same(
			current.Node,
			NodeByAutomationId(replacement, "root_changing_caption"));
		Assert.Equal(
			"text",
			current.Node.Get(PropertyIds.Text_Value).AsString);
		Assert.All(originalGeneration, node => Assert.Equal(1, node.DisposeCount));
		Assert.Equal(new[] { true, false }, first.Node.Activations);
		Assert.Equal(new[] { true }, current.Node.Activations);
		Assert.Equal(0, current.Node.DisposeCount);
		Assert.True(cache.TryGet(0, out var retainedNode, out var retainedView));
		Assert.Same(current.Node, retainedNode);
		Assert.Same(replacement, retainedView);
	}

	[Fact]
	public void RetainedContentCache_InactiveCompositeRoute_IsHiddenFromDevRegistry()
	{
		CometDevRegistry.Enabled = true;
		CometDevRegistry.Reset();
		try
		{
			var owner = new ContentView();
			var route = new ReplacementRoute("route", () => { });
			ICometBackendNode Create(View view) => new ActivatableNode(view.GetType().Name);
			using var cache = new RetainedContentCache<ActivatableNode>(owner, Create, Ctx);
			cache.GetOrMaterialize(0, route);
			cache.SetActive(0, true);
			var active = Assert.Single(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "replacement_route_root");

			cache.SetActive(0, false);
			var lateChild = new Text("late").AutomationId("retained_late_child");
			var lateChildNode = new ActivatableNode(nameof(Text));
			CometDevRegistry.Register(
				lateChild,
				lateChildNode,
				FindByAutomationId(route, "replacement_route_root"));

			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "replacement_route_root");
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "retained_late_child");
			Assert.Null(CometDevRegistry.Find(active.Id));

			cache.SetActive(0, true);

			Assert.Contains(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "replacement_route_root");
			Assert.Contains(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "retained_late_child");
			Assert.Same(
				FindByAutomationId(route, "replacement_route_root"),
				CometDevRegistry.Find(active.Id));
			lateChildNode.Dispose();
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	[Fact]
	public void PopToRoot_ExposesRematerializedBuriedRootWithCurrentFrames()
	{
		var root = new ManagementRoot();
		var stack = new List<View>
		{
			root,
			new Text("First detail"),
			new Text("Second detail"),
		};
		var active = new OwnedContentSlot<FakeBackendNode>(
			new ContentView(),
			Factory,
			Ctx);
		var cached = active.Materialize(root);
		active.Clear();

		root.Loading.Value = false;
		ReactiveScheduler.FlushSync();

		var retained = NavigationStackLifecycle.ResetToRoot(
			stack,
			root,
			(_, _) => { });
		Assert.Same(root, retained);

		NavigationStackLifecycle.PrepareCurrentForExposure(
			stack,
			view => active.TryGet(out var node, out var activeView) &&
				ReferenceEquals(activeView, view)
					? node
					: null,
			view => cached = active.Materialize(view),
			view => CometBackendLayoutEngine.Layout(view, new Size(360, 720)),
			out var rematerialized);

		Assert.True(rematerialized);
		Assert.Single(stack);
		Assert.NotNull(NodeByAutomationId(root, "management_list").ArrangedFrame);
		Assert.NotNull(NodeByAutomationId(root, "management_toast").ArrangedFrame);

		active.Dispose();
		root.Dispose();
	}

	[Fact]
	public void OwnContentBoundary_DoesNotRecurseIntoUnmaterializedListRows()
	{
		var list = new ListView<int>(() => new[] { 1 })
		{
			ViewFor = value => new Text($"Row {value}"),
		};
		CometBackendBridge.Materialize(list, Factory, Ctx);

		Assert.IsAssignableFrom<IBackendManagesOwnContent>(list.Node);
		Assert.False(BackendContentReadiness.HasUnmaterializedBridgeContent(list));

		list.Dispose();
	}

	[Fact]
	public void ComposeListNode_DeclaresOwnContentCompileGuard()
	{
		var projectRoot = IOPath.GetFullPath(IOPath.Combine(
			AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
		var source = File.ReadAllText(IOPath.Combine(
			projectRoot, "src", "Comet", "Platform", "Compose", "ComposeListNode.cs"));

		Assert.Contains(
			"ComposeListNode : ComposeNode, IBackendManagesOwnContent",
			source);
	}

	[Fact]
	public void KeyedReconciliation_SkipsSparseNullChildrenWithoutIndexDrift()
	{
		var oldA = new Text("Old A").Key("a");
		var oldB = new Text("Old B").Key("b");
		var original = new SparseVStack();
		original.Add(oldA);
		original.AddSparse(null);
		original.Add(oldB);
		var rootNode = (FakeBackendNode)CometBackendBridge.Materialize(original, Factory, Ctx);
		var oldANode = oldA.Node;
		var oldBNode = oldB.Node;

		var replacement = new SparseVStack();
		replacement.AddSparse(null);
		var newB = new Text("New B").Key("b");
		var newA = new Text("New A").Key("a");
		replacement.Add(newB);
		replacement.Add(newA);
		replacement.AddSparse(null);

		replacement.Diff(original, false);

		Assert.Equal(2, replacement.Count);
		Assert.Same(newB, replacement[0]);
		Assert.Same(newA, replacement[1]);
		Assert.Same(oldBNode, newB.Node);
		Assert.Same(oldANode, newA.Node);
		Assert.Same(oldBNode, rootNode.Children[0]);
		Assert.Same(oldANode, rootNode.Children[1]);
		Assert.Contains(rootNode.Log, entry => entry.StartsWith("move 1->0", StringComparison.Ordinal));

		original.Dispose();
		replacement.Dispose();
	}

	static ICometBackendNode Factory(View view)
		=> view is IListView
			? new OwnContentNode(view.GetType().Name)
			: new CountingNode(view.GetType().Name);

	static View FindByAutomationId(View root, string automationId)
	{
		var rendered = root.GetView() ?? root;
		if (rendered.AutomationId == automationId)
			return rendered;
		if (rendered is IContainerView container)
		{
			foreach (var child in container.GetChildren())
			{
				if (child is null)
					continue;
				var found = FindByAutomationIdOrNull(child, automationId);
				if (found is not null)
					return found;
			}
		}

		throw new InvalidOperationException($"Could not find '{automationId}'.");
	}

	static View? FindByAutomationIdOrNull(View root, string automationId)
	{
		var rendered = root.GetView() ?? root;
		if (rendered.AutomationId == automationId)
			return rendered;
		if (rendered is not IContainerView container)
			return null;

		foreach (var child in container.GetChildren())
		{
			if (child is null)
				continue;
			var found = FindByAutomationIdOrNull(child, automationId);
			if (found is not null)
				return found;
		}
		return null;
	}

	static FakeBackendNode NodeByAutomationId(View root, string automationId)
		=> Assert.IsAssignableFrom<FakeBackendNode>(
			FindByAutomationId(root, automationId).Node);
}
