#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Comet.Backend;
using Comet.DevTools;
using Xunit;

namespace Comet.Tests.Backend;

public class RetainedGenerationOwnershipTests
{
	static RetainedGenerationOwnershipTests()
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

	sealed class CountingOwnContentNode : CountingNode, IBackendManagesOwnContent
	{
		public CountingOwnContentNode(string kind) : base(kind) { }
	}

	static readonly BackendContext Ctx = new(new EmptyServiceProvider());

	[Fact]
	public void RematerializingPersistentView_ReleasesOnlyPreviousGeneration()
	{
		CometDevRegistry.Enabled = true;
		try
		{
			CometDevRegistry.Reset();
			var created = new List<CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				created.Add(node);
				return node;
			}

			var view = new Text("rating").AutomationId("bean_rating_summary");
			var firstGeneration = new OwnedContentGeneration(new ContentView(), Factory, Ctx);
			var secondGeneration = new OwnedContentGeneration(new ContentView(), Factory, Ctx);

			var first = firstGeneration.Materialize(view);
			var second = secondGeneration.Materialize(view);

			Assert.NotSame(first, second);
			Assert.Equal(1, created[0].DisposeCount);
			Assert.Equal(0, created[1].DisposeCount);
			Assert.Same(second, view.Node);
			Assert.Single(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "bean_rating_summary");

			firstGeneration.Dispose();
			Assert.Equal(1, created[0].DisposeCount);
			Assert.Equal(0, created[1].DisposeCount);
			Assert.Same(second, view.Node);
			Assert.Single(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "bean_rating_summary");

			secondGeneration.Dispose();
			Assert.Equal(1, created[1].DisposeCount);
			Assert.Null(view.Node);
			Assert.DoesNotContain(
				CometDevRegistry.Snapshot(),
				node => node.AutomationId == "bean_rating_summary");
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	[Fact]
	public void GenerationDispose_IsIdempotentAndOwnsRegistryAndNodeCleanup()
	{
		CometDevRegistry.Enabled = true;
		try
		{
			CometDevRegistry.Reset();
			var created = new List<CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetType().Name);
				created.Add(node);
				return node;
			}

			var child = new Text("child").AutomationId("owned_child");
			var root = new VStack { child }.AutomationId("owned_root");
			var generation = new OwnedContentGeneration(new ContentView(), Factory, Ctx);
			generation.Materialize(root);

			Assert.Equal(2, CometDevRegistry.Snapshot().Count);

			generation.Dispose();
			generation.Dispose();

			Assert.Empty(CometDevRegistry.Snapshot());
			Assert.Null(root.Node);
			Assert.Null(child.Node);
			Assert.All(created, node => Assert.Equal(1, node.DisposeCount));
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	[Fact]
	public void PreMaterializedBodyReplacement_ReleasesObsoleteGenerationAndKeepsUniqueAutomationIds()
	{
		CometDevRegistry.Enabled = true;
		try
		{
			CometDevRegistry.Reset();
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = view is ContentView
					? new CountingOwnContentNode(view.GetType().Name)
					: new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var host = new ContentView().AutomationId("detail_host");
			CometBackendBridge.Materialize(host, Factory, Ctx);

			var oldDetail = new DetailComponent();
			var replacementDetail = new DetailComponent();
			var retainedGeneration = new OwnedContentGeneration(host, Factory, Ctx);
			var obsoleteGeneration = new OwnedContentGeneration(host, Factory, Ctx);
			retainedGeneration.Materialize(oldDetail);
			obsoleteGeneration.Materialize(replacementDetail);
			var oldBody = oldDetail.GetView();
			var replacementBody = replacementDetail.GetView();

			var obsoleteNodes = EnumerateViews(replacementBody)
				.Select(view => nodes[view])
				.ToArray();
			var retainedNodes = EnumerateViews(oldBody)
				.Select(view => nodes[view])
				.ToArray();

			replacementDetail.Diff(oldDetail, false);
			oldDetail.Dispose();

			var snapshot = CometDevRegistry.Snapshot();
			foreach (var automationId in new[]
			{
				"detail_body",
				"bean_rating_summary",
				"bag_roast_date",
				"ProfileAvatar",
			})
			{
				Assert.Single(snapshot, node => node.AutomationId == automationId);
			}

			Assert.All(obsoleteNodes, node => Assert.Equal(1, node.DisposeCount));
			Assert.All(retainedNodes, node => Assert.Equal(0, node.DisposeCount));

			obsoleteGeneration.Dispose();
			Assert.All(obsoleteNodes, node => Assert.Equal(1, node.DisposeCount));
			Assert.All(retainedNodes, node => Assert.Equal(0, node.DisposeCount));
			Assert.All(
				EnumerateViews(replacementBody),
				view => Assert.NotNull(view.Node));

			retainedGeneration.Dispose();
			Assert.All(retainedNodes, node => Assert.Equal(1, node.DisposeCount));
			Assert.All(
				EnumerateViews(replacementBody),
				view => Assert.Null(view.Node));

			replacementDetail.Dispose();

			Assert.All(obsoleteNodes, node => Assert.Equal(1, node.DisposeCount));
			Assert.All(retainedNodes, node => Assert.Equal(1, node.DisposeCount));
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	[Fact]
	public void ReplacedListOwner_DataReductionLeavesNoDetachedOrDuplicateRows()
	{
		CometDevRegistry.Enabled = true;
		try
		{
			CometDevRegistry.Reset();
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = view is IListView
					? new CountingOwnContentNode(view.GetType().Name)
					: new CountingNode(view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var oldList = BeanList(1, 2, 3);
			var oldRoot = new Grid { oldList }.AutomationId("bean_management_page");
			CometBackendBridge.Materialize(oldRoot, Factory, Ctx);

			var oldRows = MaterializeRows(oldList);
			var oldRowNodes = oldRows.Select(row => nodes[row]).ToArray();
			var retainedListNode = oldList.Node;

			var currentList = BeanList(1, 2);
			var currentRoot = new Grid { currentList }.AutomationId("bean_management_page");
			currentRoot.Diff(oldRoot, false);
			oldRoot.Dispose();

			Assert.True(oldList.IsDisposed);
			Assert.All(oldRows, row => Assert.True(row.IsDisposed));
			Assert.All(oldRowNodes, node => Assert.Equal(1, node.DisposeCount));
			Assert.Same(retainedListNode, currentList.Node);
			Assert.False(((CountingNode)retainedListNode!).Disposed);

			var currentRows = MaterializeRows(currentList);
			var snapshot = CometDevRegistry.Snapshot();

			Assert.Single(snapshot, node => node.AutomationId == "bean_row_1");
			Assert.Single(snapshot, node => node.AutomationId == "bean_row_2");
			Assert.DoesNotContain(snapshot, node => node.AutomationId == "bean_row_3");
			Assert.DoesNotContain(
				snapshot,
				node => node.AutomationId?.StartsWith("bean_row_", StringComparison.Ordinal) == true
					&& node.ParentId == -1);

			currentRoot.Dispose();
			Assert.All(currentRows, row => Assert.True(row.IsDisposed));
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	[Fact]
	public void KeyedReduction_PreservesRetainedIdentityAndNativeMoveBeforeDisposal()
	{
		CometDevRegistry.Enabled = true;
		try
		{
			CometDevRegistry.Reset();
			var oldA = new Text("old-a").AutomationId("keyed_a").Key("a");
			var oldB = new Text("old-b").AutomationId("keyed_b").Key("b");
			var oldC = new Text("old-c").AutomationId("keyed_c").Key("c");
			var oldRoot = new Grid { oldA, oldB, oldC };
			var nodes = new Dictionary<View, CountingNode>();
			ICometBackendNode Factory(View view)
			{
				var node = new CountingNode(view.GetKey() ?? view.GetType().Name);
				nodes[view] = node;
				return node;
			}

			var rootNode = (CountingNode)CometBackendBridge.Materialize(oldRoot, Factory, Ctx);
			var removedNode = nodes[oldB];
			var retainedANode = nodes[oldA];
			var retainedCNode = nodes[oldC];
			var newC = new Text("new-c").AutomationId("keyed_c").Key("c");
			var newA = new Text("new-a").AutomationId("keyed_a").Key("a");
			var currentRoot = new Grid
			{
				newC,
				newA,
			};

			currentRoot.Diff(oldRoot, false);
			oldRoot.Dispose();

			Assert.Same(newC, currentRoot[0]);
			Assert.Same(newA, currentRoot[1]);
			Assert.Same(retainedCNode, newC.Node);
			Assert.Same(retainedANode, newA.Node);
			Assert.Same(retainedCNode, rootNode.Children[0]);
			Assert.Same(retainedANode, rootNode.Children[1]);
			Assert.True(oldA.IsDisposed);
			Assert.True(oldC.IsDisposed);
			Assert.Equal(1, removedNode.DisposeCount);
			Assert.Contains(rootNode.Log, entry => entry.StartsWith("move ", StringComparison.Ordinal));
			Assert.True(
				rootNode.Log.FindIndex(entry => entry.StartsWith("move ", StringComparison.Ordinal))
				< rootNode.Log.FindIndex(entry => entry.StartsWith("remove@", StringComparison.Ordinal)));

			var snapshot = CometDevRegistry.Snapshot();
			Assert.Single(snapshot, node => node.AutomationId == "keyed_a");
			Assert.Single(snapshot, node => node.AutomationId == "keyed_c");
			Assert.DoesNotContain(snapshot, node => node.AutomationId == "keyed_b");

			currentRoot.Dispose();
			Assert.Equal(1, retainedANode.DisposeCount);
			Assert.Equal(1, retainedCNode.DisposeCount);
		}
		finally
		{
			CometDevRegistry.Reset();
			CometDevRegistry.Enabled = false;
		}
	}

	sealed class DetailComponent : View
	{
		[Body]
		View body() => DetailBody();
	}

	static Grid DetailBody()
	{
		var body = new Grid
		{
			new Text("No ratings yet").AutomationId("bean_rating_summary"),
			new VStack().AutomationId("bag_roast_date"),
			new Grid().AutomationId("ProfileAvatar"),
		};
		body.AutomationId("detail_body");
		return body;
	}

	static ListView<int> BeanList(params int[] ids) => new(() => ids)
	{
		ViewFor = id => new Grid().AutomationId($"bean_row_{id}"),
	};

	static List<View> MaterializeRows(ListView<int> list)
	{
		var rows = new List<View>();
		var source = (IListView)list;
		for (var index = 0; index < source.Rows(0); index++)
		{
			var row = source.ViewFor(0, index);
			CometBackendBridge.MaterializeChild(row, list);
			rows.Add(row);
		}
		return rows;
	}

	static IEnumerable<View> EnumerateViews(View root)
	{
		yield return root;
		if (root is not IContainerView container)
			yield break;

		foreach (var child in container.GetChildren())
		{
			if (child is null)
				continue;
			foreach (var descendant in EnumerateViews(child))
				yield return descendant;
		}
	}
}
