#nullable enable
using System;
using System.Collections.Generic;
using Comet.Backend;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend;

public class NativeListRowTests
{
	static NativeListRowTests()
		=> ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

	static readonly BackendContext Context = new(new EmptyServiceProvider());

	static CountingNode Node(View view) => (CountingNode)view.Node!;

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	public void GridRoot_BottomMargin_IsIncludedOnceInNativeItemExtent(double bottom)
	{
		var text = new Text("row");
		var content = new Grid { text }.Margin(new Thickness(0, 0, 0, bottom));
		using var host = new RowHarness(content);
		Node(text).MeasureResult = new Size(120, 48);

		var measured = host.Row.MeasureExtent(320);
		var arranged = host.Row.Layout(320);

		Assert.Equal(new Size(320, 48 + bottom), measured);
		Assert.Equal(measured, arranged);
		Assert.Equal(new Rect(0, 0, 320, 48 + bottom), Node(host.Row).ArrangedFrame);
		Assert.Equal(new Rect(0, 0, 320, 48), Node(content).ArrangedFrame);
		Assert.Equal(0, ((IStackLayout)host.Row).Spacing);
	}

	[Fact]
	public void GridRoot_HorizontalAndVerticalMargins_InsetContentWithoutGrowingPinnedWidth()
	{
		var text = new Text("row");
		var content = new Grid { text }.Margin(new Thickness(10, 3, 20, 5));
		using var host = new RowHarness(content);
		Node(text).MeasureResult = new Size(120, 48);

		var size = host.Row.Layout(300);

		Assert.Equal(new Size(300, 56), size);
		Assert.Equal(new Rect(10, 3, 270, 48), Node(content).ArrangedFrame);
		Assert.Equal(new Rect(0, 0, 270, 48), Node(text).ArrangedFrame);
	}

	[Fact]
	public void NoMarginStack_PreservesContentSpacingAndNativeExtent()
	{
		var first = new Text("first");
		var second = new Text("second");
		var content = new VStack(spacing: 8) { first, second };
		using var host = new RowHarness(content);
		Node(first).MeasureResult = new Size(100, 20);
		Node(second).MeasureResult = new Size(100, 30);

		var size = host.Row.Layout(250);

		Assert.Equal(new Size(250, 58), size);
		Assert.Equal(new Rect(0, 0, 250, 58), Node(content).ArrangedFrame);
		Assert.Equal(new Rect(0, 28, 250, 30), Node(second).ArrangedFrame);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CompositeRoot_MarginOnDeclarationOrBuiltView_IsIncludedOnce(bool marginOnBody)
	{
		var text = new Text("row");
		var margin = new Thickness(7, 3, 11, 5);
		var content = new View
		{
			Body = () => new Grid { text }.Margin(marginOnBody ? margin : Thickness.Zero),
		};
		if (!marginOnBody)
			content.Margin(margin);
		using var host = new RowHarness(content);
		Node(text).MeasureResult = new Size(120, 48);

		var size = host.Row.Layout(300);

		Assert.Equal(new Size(300, 56), size);
		Assert.Equal(new Rect(7, 3, 282, 48), Node(content.BuiltView).ArrangedFrame);
		Assert.Same(host.Owner, content.Parent);
		Assert.Same(content, content.BuiltView.Parent);
		Assert.Same(content, Assert.Single(((IContainerView)host.Row).GetChildren()));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void WrappingUnboundedStarRow_MeasuresInsideMarginsAndResolvedStarColumn(bool composite)
	{
		var text = new Text("wrapping").Cell(column: 1);
		// Unbounded star rows use the resolved column width for intrinsic height.
		// Auto rows currently measure against the whole grid width instead.
		var grid = new Grid(columns: new object[] { 80, "*" }, rows: new object[] { "*" })
		{
			text,
		}.Margin(new Thickness(10, 4, 20, 6));
		View content = composite ? new View { Body = () => grid } : grid;
		using var host = new RowHarness(content);
		Node(text).MeasureFunc = (width, _) =>
			new Size(Math.Min(360, width), 20 * Math.Ceiling(360 / width));

		var measured = host.Row.MeasureExtent(230);
		var size = host.Row.Layout(230);

		Assert.Equal(new Size(230, 70), measured);
		Assert.Equal(measured, size);
		Assert.Equal(120, Node(text).LastMeasureWidth, 3);
		Assert.Equal(new Rect(10, 4, 200, 60), Node(grid).ArrangedFrame);
		Assert.Equal(new Rect(80, 0, 120, 60), Node(text).ArrangedFrame);

		Assert.Equal(new Size(350, 50), host.Row.Layout(350));
		Assert.Equal(240, Node(text).LastMeasureWidth, 3);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void HorizontalItem_IntrinsicWidthIncludesMarginsAndPreservesCardWidth(bool margins)
	{
		var content = new Grid().Frame(width: 120, height: 48)
			.Margin(margins ? new Thickness(7, 3, 11, 5) : Thickness.Zero);
		using var host = new RowHarness(content);

		var intrinsic = host.Row.MeasureIntrinsicExtent();
		var size = host.Row.Layout(intrinsic.Width);

		Assert.Equal(new Size(margins ? 138 : 120, margins ? 56 : 48), intrinsic);
		Assert.Equal(intrinsic, size);
		Assert.Equal(new Rect(margins ? 7 : 0, margins ? 3 : 0, 120, 48), Node(content).ArrangedFrame);

		// An explicit carousel width is the native item's width, not an extra margin allowance.
		Assert.Equal(180, host.Row.Layout(180).Width, 3);
		Assert.Equal(120, Node(content).ArrangedFrame!.Value.Width, 3);
	}

	[Fact]
	public void CenterTarget_UsesBoundedNativeExtentIncludingMargins()
	{
		var text = new Text("wrapping");
		var content = new Grid { text }.Margin(new Thickness(10, 4, 10, 6));
		using var host = new RowHarness(content);
		Node(text).MeasureFunc = (width, _) =>
			new Size(Math.Min(360, width), 20 * Math.Ceiling(360 / width));

		var targetExtent = host.Row.MeasureExtent(140).Height;
		Assert.Equal(70, targetExtent, 3);
		Assert.Equal(targetExtent, host.Row.Layout(140).Height, 3);

		using var scroll = new ListInitialScrollState();
		Assert.True(scroll.TrySchedule(300, 5, 2, ListScrollPosition.Center, targetExtent, out var plan));
		Assert.Equal(150, plan.EdgeSpacing);
		Assert.Equal(35, plan.TargetOffset);
	}

	[Fact]
	public void MeasureOnly_ReusesWrapperWithoutReparentingAllocatingNodesOrArranging()
	{
		var content = new ParentTrackingView
		{
			Body = () => new Grid().Frame(height: 48).Margin(new Thickness(10, 3, 20, 5)),
		};
		using var host = new RowHarness(content);
		host.Row.Layout(300);
		var wrapperNode = Node(host.Row);
		var contentNode = Node(content.BuiltView);
		var frame = contentNode.ArrangedFrame;
		var wrapperFrame = wrapperNode.ArrangedFrame;
		var nodeCount = host.Nodes.Count;
		var parentChanges = content.ParentChanges;

		for (var i = 0; i < 10; i++)
		{
			Assert.Equal(new Size(230, 56), host.Row.MeasureExtent(230));
			Assert.Equal(56, host.Row.MeasureIntrinsicExtent().Height, 3);
		}

		Assert.Equal(parentChanges, content.ParentChanges);
		Assert.Same(host.Owner, content.Parent);
		Assert.Same(wrapperNode, host.Row.Node);
		Assert.Same(contentNode, content.BuiltView.Node);
		Assert.Equal(frame, contentNode.ArrangedFrame);
		Assert.Equal(wrapperFrame, wrapperNode.ArrangedFrame);
		Assert.Equal(nodeCount, host.Nodes.Count);
		Assert.All(host.Nodes, node => Assert.Equal(0, node.DisposeCount));
	}

	[Fact]
	public void CompositeBodyRebuild_UsesCurrentBuiltViewAndRetainedNode()
	{
		var height = new Signal<int>(40);
		var content = new View
		{
			Body = () => new Grid().Frame(height: height.Value).Margin(new Thickness(0, 0, 0, 1)),
		};
		using var host = new RowHarness(content);
		var originalBody = content.BuiltView;
		var originalNode = Node(originalBody);
		Assert.Equal(41, host.Row.Layout(300).Height, 3);

		height.Value = 64;
		ReactiveScheduler.FlushSync();

		Assert.NotSame(originalBody, content.BuiltView);
		Assert.Same(originalNode, content.BuiltView.Node);
		Assert.Same(originalNode, Assert.Single(Node(host.Row).Children));
		Assert.Equal(65, host.Row.MeasureExtent(300).Height, 3);
		Assert.Equal(65, host.Row.Layout(300).Height, 3);
		Assert.Equal(new Rect(0, 0, 300, 64), originalNode.ArrangedFrame);
	}

	[Fact]
	public void ReleasingGeneration_PreservesLogicalTemplateForWidthRematerialization()
	{
		var text = new Text("row");
		var content = new Grid { text }.Margin(new Thickness(0, 0, 0, 1));
		using var host = new RowHarness(content);
		Node(text).MeasureResult = new Size(120, 48);
		host.Row.Layout(300);

		host.Generation.Dispose();
		host.Row.Dispose();

		Assert.False(content.IsDisposed);
		Assert.False(text.IsDisposed);
		Assert.Same(host.Owner, content.Parent);
		Assert.Null(content.Node);
		Assert.Null(text.Node);
		Assert.All(host.Nodes, node => Assert.Equal(1, node.DisposeCount));

		using var nextGeneration = new OwnedContentGeneration(host.Owner, Factory, Context);
		using var nextRow = NativeListRow.Materialize(content, nextGeneration);
		Node(text).MeasureResult = new Size(120, 48);
		Assert.Equal(new Size(400, 49), nextRow.Layout(400));

		var nextNode = Node(content);
		host.Generation.Dispose();
		host.Row.Dispose();
		Assert.Same(nextNode, content.Node);
		Assert.Equal(0, nextNode.DisposeCount);
		nextGeneration.Dispose();
		Assert.Equal(1, nextNode.DisposeCount);
	}

	[Fact]
	public void ListReload_StillDisposesCachedCompositeAndItsBodySubscriptions()
	{
		var signal = new Signal<int>(40);
		var builds = 0;
		using var list = new ListView<int>(() => new[] { 1 })
		{
			ViewFor = _ => new View
			{
				Body = () =>
				{
					builds++;
					return new Grid().Frame(height: signal.Value).Margin(new Thickness(0, 0, 0, 1));
				},
			},
		};
		var content = ((IListView)list).ViewFor(0, 0);
		using var generation = new OwnedContentGeneration(list, Factory, Context);
		using var row = NativeListRow.Materialize(content, generation);
		Assert.Same(list, content.Parent);
		Assert.True(content.HasActiveBodySubscriptions);

		list.ReloadData();

		Assert.True(content.IsDisposed);
		Assert.False(content.HasActiveBodySubscriptions);
		var buildsAfterReload = builds;
		signal.Value = 64;
		ReactiveScheduler.FlushSync();
		Assert.Equal(buildsAfterReload, builds);

		generation.Dispose();
		row.Dispose();
		var replacement = ((IListView)list).ViewFor(0, 0);
		Assert.NotSame(content, replacement);
		using var nextGeneration = new OwnedContentGeneration(list, Factory, Context);
		using var nextRow = NativeListRow.Materialize(replacement, nextGeneration);
		Assert.Equal(new Size(300, 65), nextRow.Layout(300));
		nextGeneration.Dispose();
	}

	static ICometBackendNode Factory(View view) => new CountingNode(view.GetType().Name);

	sealed class RowHarness : IDisposable
	{
		public ListView Owner { get; } = new();
		public List<CountingNode> Nodes { get; } = new();
		public OwnedContentGeneration Generation { get; }
		public NativeListRow Row { get; }

		public RowHarness(View content)
		{
			Owner.Add(content);
			content.Parent = Owner;
			Generation = new OwnedContentGeneration(Owner, view =>
			{
				var node = new CountingNode(view.GetType().Name);
				Nodes.Add(node);
				return node;
			}, Context);
			Row = NativeListRow.Materialize(content, Generation);
		}

		public void Dispose()
		{
			Generation.Dispose();
			Row.Dispose();
			Owner.Dispose();
		}
	}

	sealed class ParentTrackingView : View
	{
		public int ParentChanges { get; private set; }

		protected override void OnParentChange(View parent)
		{
			ParentChanges++;
			base.OnParentChange(parent);
		}
	}

	sealed class CountingNode(string kind) : FakeBackendNode(kind)
	{
		public int DisposeCount { get; private set; }

		public override void Dispose()
		{
			DisposeCount++;
			base.Dispose();
		}
	}

	sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}
}
