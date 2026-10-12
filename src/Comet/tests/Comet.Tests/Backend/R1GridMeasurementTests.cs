#nullable enable
using System;
using Comet;
using Comet.Backend;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests.Backend
{
	public class R1GridMeasurementTests
	{
		static R1GridMeasurementTests() =>
			ThreadHelper.SetFireOnMainThread(action => action?.Invoke());

		static readonly BackendContext Context = new(new EmptyServiceProvider());

		static FakeBackendNode Bridge(View root)
			=> (FakeBackendNode)CometBackendBridge.Materialize(
				root, view => new FakeBackendNode(view.GetType().Name), Context);

		static FakeBackendNode Node(View view) => (FakeBackendNode)view.Node!;

		[Fact]
		public void AutoRow_NestedPaddedGrid_IncludesPaddingExactlyOnce()
		{
			var glyph = new Text("glyph");
			var padded = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto" })
			{
				glyph,
			}.Padding(new Microsoft.Maui.Thickness(0, 18, 0, 30));
			var root = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*" })
			{
				padded.Cell(row: 0),
				new Spacer().Cell(row: 1),
			};
			Bridge(root);
			Node(glyph).MeasureResult = new Size(32, 38.4);

			CometBackendLayoutEngine.Layout(root, new Size(400, 300));

			// Measured Yoga leaves are text nodes and force-ceil fractional dimensions to
			// the configured 1pt pixel grid. The 38.4pt glyph therefore occupies 39pt, and
			// the nested layout adds its 18+30 padding exactly once.
			var expectedHeight = Math.Ceiling(38.4) + 18 + 30;
			Assert.Equal(expectedHeight, Node(padded).ArrangedFrame!.Value.Height, 3);
		}

		[Fact]
		public void AutoRow_PaddedLeaf_HonorsMinimumHeightAfterNativeMeasure()
		{
			var button = new Button("BACK", () => { })
				.Padding(new Microsoft.Maui.Thickness(0, 18, 0, 30))
				.MinimumHeight(72);
			var root = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*" })
			{
				button.Cell(row: 0),
				new Spacer().Cell(row: 1),
			};
			Bridge(root);
			Node(button).MeasureResult = new Size(80, 20);

			CometBackendLayoutEngine.Layout(root, new Size(400, 300));

			Assert.Equal(72, Node(button).ArrangedFrame!.Value.Height, 3);
		}

		[Fact]
		public void AutoParent_UnboundedStarHeader_UsesIntrinsicTitleHeight()
		{
			var caption = new Text("SETTINGS");
			var title = new Text("Settings");
			var header = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*" })
			{
				caption.Cell(row: 0),
				title.Cell(row: 1),
			}
			.Padding(new Microsoft.Maui.Thickness(16, 76, 16, 14))
			.MinimumHeight(120);
			var root = new Grid(
				columns: new object[] { "*" },
				rows: new object[] { "Auto", "*" })
			{
				header.Cell(row: 0),
				new Spacer().Cell(row: 1),
			};
			Bridge(root);
			Node(caption).MeasureResult = new Size(80, 14);
			Node(title).MeasureResult = new Size(160, 38);

			CometBackendLayoutEngine.Layout(root, new Size(402, 500));

			Assert.Equal(142, Node(header).ArrangedFrame!.Value.Height, 3);
		}

		[Fact]
		public void UnboundedStarRow_WrappingChild_UsesResolvedColumnWidth()
		{
			var text = new Text("Wrapping content");
			var root = new Grid(
				columns: new object[] { 100, "*" },
				rows: new object[] { "*" })
			{
				text.Cell(row: 0, column: 0),
			};
			Bridge(root);
			Node(text).MeasureFunc = (width, _) =>
				new Size(Math.Min(400, width), 20 * Math.Ceiling(400 / width));

			var measured = CometBackendLayoutEngine.LayoutContent(root, 400);

			Assert.Equal(80, measured.Height, 3);
			Assert.Equal(100, text.Frame.Width, 3);
			Assert.Equal(80, text.Frame.Height, 3);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void UnboundedMixedZeroWeightStars_DistributeOnlyRequiredIntrinsicSize(bool vertical)
		{
			var content = new Text("Content");
			var root = new Grid(
				columns: vertical ? new object[] { "Auto" } : new object[] { "0*", "*" },
				rows: vertical ? new object[] { "0*", "*" } : new object[] { "Auto" })
			{
				content.Cell(row: 0, column: 0, rowSpan: vertical ? 2 : 1, colSpan: vertical ? 1 : 2),
			};
			Bridge(root);
			Node(content).MeasureResult = new Size(100, 100);

			var measured = CometBackendLayoutEngine.Measure(root);

			Assert.Equal(100, vertical ? measured.Height : measured.Width, 3);
		}

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
