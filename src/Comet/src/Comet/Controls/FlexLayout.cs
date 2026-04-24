using Comet.Layout;
using Microsoft.Maui.Layouts;

// Expose Yoga's richer flex vocabulary directly. Comet no longer defines its own
// FlexDirection/FlexWrap/FlexJustify/FlexAlignItems/FlexAlignContent/FlexAlignSelf enums;
// use Comet.Layout.Yoga.* (FlexDirection, FlexWrap, FlexJustify, FlexAlign, FlexPositionType)
// instead. Breaking change — acceptable in this preview.
using YogaFlexDirection = Comet.Layout.Yoga.FlexDirection;
using YogaFlexWrap = Comet.Layout.Yoga.FlexWrap;
using YogaFlexJustify = Comet.Layout.Yoga.FlexJustify;
using YogaFlexAlign = Comet.Layout.Yoga.FlexAlign;

namespace Comet
{
	public class FlexLayout : AbstractLayout
	{
		public FlexLayout(
			YogaFlexDirection direction = YogaFlexDirection.Row,
			YogaFlexWrap wrap = YogaFlexWrap.NoWrap,
			YogaFlexJustify justifyContent = YogaFlexJustify.FlexStart,
			YogaFlexAlign alignItems = YogaFlexAlign.Stretch,
			YogaFlexAlign alignContent = YogaFlexAlign.Stretch,
			double gap = 0,
			double rowGap = -1,
			double columnGap = -1)
		{
			Direction = direction;
			Wrap = wrap;
			JustifyContent = justifyContent;
			AlignItems = alignItems;
			AlignContent = alignContent;
			Gap = gap;
			RowGap = rowGap;
			ColumnGap = columnGap;
		}

		public YogaFlexDirection Direction { get; }
		public YogaFlexWrap Wrap { get; }
		public YogaFlexJustify JustifyContent { get; }
		public YogaFlexAlign AlignItems { get; }
		public YogaFlexAlign AlignContent { get; }

		/// <summary>Default gap applied on both axes. Negative values are ignored.</summary>
		public double Gap { get; }

		/// <summary>Row gap override (cross-axis spacing between wrapped rows). When &lt; 0, falls back to <see cref="Gap"/>.</summary>
		public double RowGap { get; }

		/// <summary>Column gap override (main-axis spacing in a row layout, or cross-axis in a column layout). When &lt; 0, falls back to <see cref="Gap"/>.</summary>
		public double ColumnGap { get; }

		protected override ILayoutManager CreateLayoutManager() => new YogaFlexLayoutManager(this);
	}
}
