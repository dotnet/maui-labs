#nullable enable
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Backend;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	/// <summary>
	/// Proves the C# Yoga engine drives the backend node protocol: a materialized Comet tree
	/// is laid out by <see cref="CometBackendLayoutEngine"/>, with leaf intrinsic sizes coming
	/// from each node's <c>Measure</c> and the computed frames pushed via <c>Arrange</c>. This
	/// is the host-side proof that Yoga — not the native UI kit — positions the tree.
	/// </summary>
	public class BackendLayoutTests
	{
		static BackendLayoutTests() => ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		static readonly BackendContext Ctx = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, v => new FakeBackendNode(v.GetType().Name), Ctx);

		static FakeBackendNode Node(View v) => (FakeBackendNode)v.Node!;

		[Fact]
		public void RetainedLeafMeasurement_ReusesConstraints_InvalidatesMutations_AndStillArranges()
		{
			var node = new CachingFakeBackendNode("Text");
			node.MeasureFunc = (width, _) => new Size(width, 24);

			CometBackendLayoutEngine.MeasureNode(node, 200, 100);
			CometBackendLayoutEngine.MeasureNode(node, 200, 100);
			Assert.Equal(1, node.MeasureCount);

			node.Arrange(new Rect(0, 0, 200, 24));
			node.Arrange(new Rect(0, 0, 240, 24));
			Assert.Equal(2, node.Log.Count(entry => entry.StartsWith("arrange ")));
			Assert.Equal(240, node.ArrangedFrame!.Value.Width, 3);

			foreach (var (id, value) in new[]
			{
				(PropertyIds.Text_Value, PropertyValue.From("cortado")),
				(PropertyIds.Text_FontSize, PropertyValue.From(20d)),
				(PropertyIds.Padding, PropertyValue.FromObject(new Microsoft.Maui.Thickness(4))),
				(PropertyIds.IsVisible, PropertyValue.From(false)),
			})
			{
				var before = node.MeasureCount;
				node.ApplyProperty(id, in value);
				CometBackendLayoutEngine.MeasureNode(node, 200, 100);
				Assert.True(node.MeasureCount > before);
			}

			var beforeConstraintChange = node.MeasureCount;
			CometBackendLayoutEngine.MeasureNode(node, 240, 100);

			Assert.True(node.MeasureCount > beforeConstraintChange);
		}

		[Fact]
		public void RefreshView_ContentFillsAllocatedGridRow()
		{
			var list = new CollectionView();
			var refresh = new RefreshView();
			refresh.Add(list);
			var root = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { 100, "*" })
			{
				new Text("header").Cell(row: 0),
				refresh.Cell(row: 1),
			};
			Bridge(root);

			CometBackendLayoutEngine.Layout(root, new Size(400, 800));

			Assert.Equal(new Rect(0, 100, 400, 700), Node(refresh).ArrangedFrame);
			Assert.Equal(new Rect(0, 0, 400, 700), Node(list).ArrangedFrame);
		}

		sealed class CachingFakeBackendNode : FakeBackendNode, IBackendMeasureCache
		{
			readonly BackendMeasureCache _cache = new();

			public CachingFakeBackendNode(string kind)
				: base(kind)
			{
			}

			public int MeasureCount { get; private set; }

			public override void ApplyProperty(PropertyId id, in PropertyValue value)
			{
				InvalidateMeasureCache();
				base.ApplyProperty(id, in value);
			}

			public override Size Measure(double widthConstraint, double heightConstraint)
			{
				MeasureCount++;
				return base.Measure(widthConstraint, heightConstraint);
			}

			Size IBackendMeasureCache.MeasureCached(double widthConstraint, double heightConstraint)
				=> _cache.GetOrMeasure(widthConstraint, heightConstraint, Measure);

			public void InvalidateMeasureCache()
			{
				_cache.Clear();
			}
		}

		[Fact]
		public void Diff_AddsAndRemovesTrailingBackendChildren()
		{
			var first = new Text("first");
			var removed = new Text("removed");
			var original = new Grid { first, removed };
			var rootNode = Bridge(original);
			var removedNode = Node(removed);

			var withoutTrailing = new Grid { new Text("first") };
			withoutTrailing.Diff(original, false);

			Assert.Same(rootNode, withoutTrailing.Node);
			Assert.Single(rootNode.Children);
			Assert.True(removedNode.Disposed);

			var added = new Text("added");
			var restored = new Grid { new Text("first"), added };
			restored.Diff(withoutTrailing, false);

			Assert.Same(rootNode, restored.Node);
			Assert.Equal(2, rootNode.Children.Count);
			Assert.Same(Node(added), rootNode.Children[1]);
		}

		[Fact]
		public void VStack_StacksChildrenAlongYWithSpacing()
		{
			var a = new Text("a");
			var b = new Text("b");
			var root = new VStack(spacing: 10f) { a, b };
			Bridge(root);

			Node(a).MeasureResult = new Size(100, 20);
			Node(b).MeasureResult = new Size(100, 30);

			CometBackendLayoutEngine.Layout(root, new Size(200, 400));

			var fa = Node(a).ArrangedFrame!.Value;
			var fb = Node(b).ArrangedFrame!.Value;

			Assert.Equal(0, fa.Y, 3);
			Assert.Equal(20, fa.Height, 3);
			// Second child is offset by the first child's height + the 10pt gap.
			Assert.Equal(30, fb.Y, 3);
			Assert.Equal(30, fb.Height, 3);
		}

		[Fact]
		public void HStack_StacksChildrenAlongX()
		{
			var a = new Text("a");
			var b = new Text("b");
			var root = new HStack(spacing: 0f) { a, b };
			Bridge(root);

			Node(a).MeasureResult = new Size(40, 20);
			Node(b).MeasureResult = new Size(60, 20);

			CometBackendLayoutEngine.Layout(root, new Size(400, 200));

			var fa = Node(a).ArrangedFrame!.Value;
			var fb = Node(b).ArrangedFrame!.Value;

			Assert.Equal(0, fa.X, 3);
			Assert.Equal(40, fa.Width, 3);
			// Second child sits to the right of the first.
			Assert.Equal(40, fb.X, 3);
			Assert.Equal(60, fb.Width, 3);
			Assert.Equal(0, fb.Y, 3);
		}

		[Fact]
		public void NestedStacks_ComposeOffsets()
		{
			var leaf = new Text("x");
			var inner = new HStack(spacing: 0f) { leaf };
			var outer = new VStack(spacing: 0f) { new Text("header"), inner };
			Bridge(outer);

			Node(((IContainerView)outer).GetChildren()[0]).MeasureResult = new Size(100, 50); // header
			Node(leaf).MeasureResult = new Size(30, 30);

			CometBackendLayoutEngine.Layout(outer, new Size(300, 300));

			// The inner HStack is the second row of the outer VStack → offset down by the
			// header's height; the leaf is arranged relative to its HStack parent.
			var innerFrame = Node(inner).ArrangedFrame!.Value;
			Assert.Equal(50, innerFrame.Y, 3);

			var leafFrame = Node(leaf).ArrangedFrame!.Value;
			Assert.Equal(0, leafFrame.X, 3);
			Assert.Equal(30, leafFrame.Width, 3);
		}

		[Fact]
		public void StretchedText_IsMeasuredWithContainerWidth_AndWraps()
		{
			// A "text" leaf whose height grows as its width shrinks (simulating wrapping):
			// natural single line is 600 wide x 20 tall; constrained narrower, it wraps taller.
			var text = new Text("long");
			var root = new VStack(spacing: 0f) { text };
			Bridge(root);

			Node(text).MeasureFunc = (w, h) =>
			{
				const double natural = 600, line = 20;
				if (double.IsInfinity(w) || w >= natural) return new Size(natural, line);
				int lines = (int)System.Math.Ceiling(natural / w);
				return new Size(w, lines * line);
			};

			CometBackendLayoutEngine.Layout(root, new Size(200, 1000));

			// The leaf must be measured with the (stretched) container width, not infinity,
			// so it wraps: 600/200 = 3 lines * 20 = 60 tall.
			Assert.Equal(200, Node(text).LastMeasureWidth, 3);
			Assert.Equal(60, Node(text).ArrangedFrame!.Value.Height, 3);
		}

		[Fact]
		public void PaddedStack_MeasuresChildWithContentWidth_NotFullWidth()
		{
			// Mirrors the probe: a padded VStack; the text child must be measured with the
			// content width (available - padding), so it wraps inside the padding.
			var text = new Text("long");
			var root = new VStack(spacing: 0f) { text }.Padding(24);
			Bridge(root);

			Node(text).MeasureFunc = (w, h) =>
			{
				const double natural = 600, line = 20;
				if (double.IsInfinity(w) || w >= natural) return new Size(natural, line);
				int lines = (int)System.Math.Ceiling(natural / w);
				return new Size(w, lines * line);
			};

			CometBackendLayoutEngine.Layout(root, new Size(400, 1000));

			// 400 wide - 48 padding = 352 content width.
			Assert.Equal(352, Node(text).LastMeasureWidth, 3);
			// 600/352 = 2 lines * 20 = 40 tall; and the child sits at x=24 (left padding).
			Assert.Equal(40, Node(text).ArrangedFrame!.Value.Height, 3);
			Assert.Equal(24, Node(text).ArrangedFrame!.Value.X, 3);
		}

		[Fact]
		public void FlexGrow_ExpandsToFillMainAxis_PushingSiblings()
		{
			// HStack: A (40) | filler (grows) | C (60), in 400 wide → filler = 300, C at x=340.
			var a = new Text("a");
			var filler = new Text("f").FlexGrow(1);
			var c = new Text("c");
			var root = new HStack(spacing: 0f) { a, filler, c };
			Bridge(root);
			Node(a).MeasureResult = new Size(40, 20);
			Node(filler).MeasureResult = new Size(10, 20);
			Node(c).MeasureResult = new Size(60, 20);

			CometBackendLayoutEngine.Layout(root, new Size(400, 100));

			Assert.Equal(300, Node(filler).ArrangedFrame!.Value.Width, 2);
			Assert.Equal(340, Node(c).ArrangedFrame!.Value.X, 2);
		}

		[Fact]
		public void BaselineAlignedRow_LinesUpChildBaselines()
		{
			// HStack with two .AlignBaseline() texts of different heights/baselines: the engine
			// must shift each so their baselines (top + baseline offset) coincide on one line.
			// Bigger text has the larger baseline, so it sets the shared line and the smaller rides up.
			var big = new Text("big").AlignBaseline();
			var small = new Text("small").AlignBaseline();
			var root = new HStack(spacing: 0f) { big, small };
			Bridge(root);

			Node(big).MeasureResult = new Size(40, 30);
			Node(big).BaselineResult = 24;          // baseline 24px from its top
			Node(small).MeasureResult = new Size(40, 20);
			Node(small).BaselineResult = 16;        // baseline 16px from its top

			CometBackendLayoutEngine.Layout(root, new Size(400, 100));

			var fBig = Node(big).ArrangedFrame!.Value;
			var fSmall = Node(small).ArrangedFrame!.Value;

			// Shared baseline = max(24, 16) = 24: big sits at y=0, small drops to y=8.
			Assert.Equal(0, fBig.Y, 2);
			Assert.Equal(8, fSmall.Y, 2);
			// The invariant: top + baseline is equal across the row (one common baseline).
			Assert.Equal(fBig.Y + 24, fSmall.Y + 16, 2);
		}

		[Fact]
		public void CenterAlignedChild_IsCenteredOnCrossAxis_NotStretched()
		{
			// A VStack child with center horizontal alignment keeps its intrinsic width and
			// centers, instead of stretching to fill (the container default).
			var a = new Text("a").HorizontalLayoutAlignment(Microsoft.Maui.Primitives.LayoutAlignment.Center);
			var root = new VStack(spacing: 0f) { a };
			Bridge(root);
			Node(a).MeasureResult = new Size(100, 20);

			CometBackendLayoutEngine.Layout(root, new Size(300, 100));

			var f = Node(a).ArrangedFrame!.Value;
			Assert.Equal(100, f.Width, 2);      // intrinsic, not stretched to 300
			Assert.Equal(100, f.X, 2);          // centered: (300-100)/2
		}

		[Fact]
		public void ExplicitFrameWidth_IsHonored()
		{
			var a = new Text("a").Frame(width: 80);
			var root = new VStack(spacing: 0f) { a };
			Bridge(root);
			Node(a).MeasureResult = new Size(999, 20);

			CometBackendLayoutEngine.Layout(root, new Size(300, 100));

			Assert.Equal(80, Node(a).ArrangedFrame!.Value.Width, 2);
		}

		[Fact]
		public void BottomAlignedGrid_UsesContentHeightAndFillsAvailableWidth()
		{
			var title = new Text("PHOTO");
			var camera = new Text("CAMERA");
			var gallery = new Text("GALLERY");
			var content = new VStack(spacing: 1) { camera, gallery };
			var panel = new Grid(rows: new object[] { 64, "Auto" })
			{
				title.Cell(row: 0),
				content.Cell(row: 1),
			}
			.Bottom()
			.FillHorizontal();
			var root = new Grid
			{
				new Grid(),
				panel,
			};
			Bridge(root);

			Node(title).MeasureResult = new Size(50, 20);
			Node(camera).MeasureResult = new Size(80, 28);
			Node(gallery).MeasureResult = new Size(80, 28);

			CometBackendLayoutEngine.Layout(root, new Size(412, 915));

			var frame = Node(panel).ArrangedFrame!.Value;
			Assert.Equal(412, frame.Width, 2);
			Assert.Equal(121, frame.Height, 2);
			Assert.Equal(794, frame.Y, 2);
			Assert.True(Node(camera).ArrangedFrame!.Value.Width > 0);
			Assert.True(Node(gallery).ArrangedFrame!.Value.Width > 0);
		}

		[Fact]
		public void LayoutContent_PinsWidth_AndWrapsHeightToContent()
		{
			// The list-row / scroll-content model: width is pinned to the host, height grows to fit
			// the content (here two stacked rows) rather than filling a viewport.
			var top = new Text("top");
			var bottom = new Text("bottom");
			var row = new VStack(spacing: 8f) { top, bottom };
			Bridge(row);

			Node(top).MeasureResult = new Size(100, 20);
			Node(bottom).MeasureResult = new Size(100, 30);

			var size = CometBackendLayoutEngine.LayoutContent(row, 250);

			// Width pinned to the host; height = 20 + 8 gap + 30 = 58 (wrapped, not the screen).
			Assert.Equal(250, size.Width, 3);
			Assert.Equal(58, size.Height, 3);
			Assert.Equal(58, Node(row).ArrangedFrame!.Value.Height, 3);
			// Children stack within the pinned width.
			Assert.Equal(0, Node(top).ArrangedFrame!.Value.Y, 3);
			Assert.Equal(28, Node(bottom).ArrangedFrame!.Value.Y, 3);
		}

		[Fact]
		public void LayoutContent_StretchesChildToPinnedWidth_SoTextWraps()
		{
			// A row whose body text wraps: pinned to 200 wide, a 600-wide natural line wraps to
			// 3 lines (60 tall), and the row height reflects it — the virtualized-row height path.
			var text = new Text("long");
			var row = new VStack(spacing: 0f) { text };
			Bridge(row);

			Node(text).MeasureFunc = (w, h) =>
			{
				const double natural = 600, line = 20;
				if (double.IsInfinity(w) || w >= natural) return new Size(natural, line);
				int lines = (int)System.Math.Ceiling(natural / w);
				return new Size(w, lines * line);
			};

			var size = CometBackendLayoutEngine.LayoutContent(row, 200);

			Assert.Equal(200, Node(text).LastMeasureWidth, 3);
			Assert.Equal(60, size.Height, 3);
		}

		[Fact]
		public void LayoutContent_RepeatedValueRangeRows_KeepCompleteTextGeometry()
		{
			var rows = new VStack(spacing: 1f);
			var rowGeometry = new List<(Grid Row, Text Label, Text Range, HStack Trailing, Text Status, Text Chevron)>();

			foreach (var (method, range) in new[]
			{
				("TURKISH", "5 g - 20 g"),
				("ESPRESSO", "5 g - 30 g"),
				("POUR OVER", "10 g - 60 g"),
			})
			{
				var label = new Text(method);
				var value = new Text(range);
				var status = new Text("AUTO");
				var chevron = new Text(">");
				var trailing = new HStack(spacing: 8f)
				{
					status,
					chevron,
				}.Center();
				var row = new Grid(
					columns: new object[] { "*", "Auto" },
					rows: new object[] { "Auto", "Auto" },
					columnSpacing: 8f)
				{
					label.Cell(row: 0, column: 0),
					value.Cell(row: 1, column: 0),
					trailing.Cell(row: 0, column: 1, rowSpan: 2),
				}
				.Padding(new Microsoft.Maui.Thickness(16))
				.MinimumHeight(80);

				rows.Add(row);
				rowGeometry.Add((row, label, value, trailing, status, chevron));
			}

			Bridge(rows);
			foreach (var geometry in rowGeometry)
			{
				Node(geometry.Label).MeasureResult = new Size(82, 12);
				Node(geometry.Range).MeasureResult = new Size(92, 22);
				Node(geometry.Status).MeasureResult = new Size(30, 12);
				Node(geometry.Chevron).MeasureResult = new Size(24, 24);
				Assert.Equal(62, CometBackendLayoutEngine.Measure(geometry.Trailing).Width, 3);
			}

			var size = CometBackendLayoutEngine.LayoutContent(rows, 402);

			Assert.Equal(402, size.Width, 3);
			Assert.Equal(242, size.Height, 3);
			double expectedRowY = 0;
			foreach (var geometry in rowGeometry)
			{
				var rowFrame = Node(geometry.Row).ArrangedFrame!.Value;
				var labelFrame = Node(geometry.Label).ArrangedFrame!.Value;
				var rangeFrame = Node(geometry.Range).ArrangedFrame!.Value;
				var trailingFrame = Node(geometry.Trailing).ArrangedFrame!.Value;
				var statusFrame = Node(geometry.Status).ArrangedFrame!.Value;
				var chevronFrame = Node(geometry.Chevron).ArrangedFrame!.Value;

				Assert.Equal(402, rowFrame.Width, 3);
				Assert.Equal(80, rowFrame.Height, 3);
				Assert.Equal(expectedRowY, rowFrame.Y, 3);
				Assert.True(labelFrame.Width >= 82, $"Label clipped: {labelFrame}");
				Assert.True(rangeFrame.Width >= 92, $"Range clipped: {rangeFrame}");
				Assert.True(rangeFrame.Y >= labelFrame.Bottom, $"Text rows overlap: {labelFrame}, {rangeFrame}");
				Assert.True(
					trailingFrame.X >= 300,
					$"Trailing content did not stay at the row edge: row={rowFrame}, label={labelFrame}, range={rangeFrame}, trailing={trailingFrame}, status={statusFrame}, chevron={chevronFrame}");
				Assert.True(statusFrame.Width >= 30, $"Status clipped: {statusFrame}");
				Assert.True(chevronFrame.X >= statusFrame.Right, $"Trailing children overlap: {statusFrame}, {chevronFrame}");
				expectedRowY = rowFrame.Bottom + 1;
			}
		}

		[Fact]
		public void LeafPadding_GrowsTheArrangedBox()
		{
			// A LEAF's .Padding must reach Yoga: the box = measured content + padding (the
			// node insets its own content at render — ComposeNode.PadsOwnContent). Regression
			// guard for the gap where leaf padding was silently dropped (Reply title flush at
			// x=0 despite Padding(16)).
			var padded = new Text("padded").Padding(new Microsoft.Maui.Thickness(16, 8, 16, 4));
			var plain = new Text("plain");
			var root = new VStack(spacing: 0f) { padded, plain };
			Bridge(root);

			Node(padded).MeasureResult = new Size(100, 20);
			Node(plain).MeasureResult = new Size(100, 20);

			CometBackendLayoutEngine.Layout(root, new Size(400, 400));

			var fp = Node(padded).ArrangedFrame!.Value;
			Assert.Equal(20 + 8 + 4, fp.Height, 3);      // content + vertical padding
			// Stretch gives both full width; the padded one is NOT narrower than the plain one
			// (padding grows inward space, the box still fills).
			Assert.Equal(Node(plain).ArrangedFrame!.Value.Width, fp.Width, 3);

			// The next sibling is pushed down by the padded height.
			Assert.Equal(32, Node(plain).ArrangedFrame!.Value.Y, 3);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
