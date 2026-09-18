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

	sealed class CountingNode : FakeBackendNode
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
