#nullable enable
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

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
