using Comet.Layout;

using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;

// ReSharper disable once CheckNamespace
namespace Comet
{
	public static class LayoutExtensions
	{
		/// <summary>
		/// Set the padding to the default thickness.
		/// </summary>
		/// <param name="view"></param>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static T Margin<T>(this T view) where T : View
		{
			var defaultThickness = new Thickness(10);
			view.Margin(defaultThickness);
			return view;
		}

		public static T Margin<T>(this T view, float? left = null, float? top = null, float? right = null, float? bottom = null) where T : View
		{
			view.Margin(new Thickness(
				left ?? 0,
				top ?? 0,
				right ?? 0,
				bottom ?? 0));
			return view;
		}

		public static T Margin<T>(this T view, float value) where T : View
		{
			view.Margin(new Thickness(value));
			return view;
		}

		public static T Frame<T>(this T view, float? width = null, float? height = null) where T : View
		{
			view.FrameConstraints(new FrameConstraints(width, height));
			return view;
		}
		public static T Alignment<T>(this T view, Alignment alignment ) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.VerticalLayoutAlignment, alignment?.Vertical, false);
			view.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, alignment?.Horizontal, false);
			return view;
		}

		public static T Center<T>(this T view) where T : View => view.Alignment(Comet.Alignment.Center);
		public static T Top<T>(this T view) where T : View => view.Alignment(Comet.Alignment.Top);
		public static T Bottom<T>(this T view) where T : View => view.Alignment(Comet.Alignment.Bottom);
		public static T Leading<T>(this T view) where T : View => view.Alignment(Comet.Alignment.Leading);
		public static T Trailing<T>(this T view) where T : View => view.Alignment(Comet.Alignment.Trailing);

		public static T Cell<T>(
			this T view,
			int row = 0,
			int column = 0,
			int rowSpan = 1,
			int colSpan = 1,
			float weightX = 1,
			float weightY = 1,
			float positionX = 0,
			float positionY = 0) where T : View
		{
			view.LayoutConstraints(new GridConstraints(row, column, rowSpan, colSpan, weightX, weightY, positionX, positionY));
			return view;
		}

		// Grid convenience extensions (MAUI-familiar naming)
		public static T GridRow<T>(this T view, int row) where T : View
		{
			var existing = view.GetLayoutConstraints() as GridConstraints;
			if (existing is not null)
				existing.Row = row;
			else
				view.LayoutConstraints(new GridConstraints(row, 0));
			return view;
		}

		public static T GridColumn<T>(this T view, int column) where T : View
		{
			var existing = view.GetLayoutConstraints() as GridConstraints;
			if (existing is not null)
				existing.Column = column;
			else
				view.LayoutConstraints(new GridConstraints(0, column));
			return view;
		}

		public static T GridRowSpan<T>(this T view, int rowSpan) where T : View
		{
			var existing = view.GetLayoutConstraints() as GridConstraints;
			if (existing is not null)
				existing.RowSpan = rowSpan;
			else
				view.LayoutConstraints(new GridConstraints(0, 0, rowSpan, 1));
			return view;
		}

		public static T GridColumnSpan<T>(this T view, int colSpan) where T : View
		{
			var existing = view.GetLayoutConstraints() as GridConstraints;
			if (existing is not null)
				existing.ColumnSpan = colSpan;
			else
				view.LayoutConstraints(new GridConstraints(0, 0, 1, colSpan));
			return view;
		}

		public static T NextRow<T>(this T view, int count = 1) where T : View
		{
			view.SetEnvironment(nameof(NextRow), count, false);
			return view;
		}
		public static T NextColumn<T>(this T view, int count = 1) where T : View
		{
			view.SetEnvironment(nameof(NextColumn), count, false);
			return view;
		}
		public static int GetIsNextColumn(this View view)
			=>view.GetEnvironment<int>(nameof(NextColumn), false);

		public static int GetIsNextRow(this View view)
			=> view.GetEnvironment<int>(nameof(NextRow), false);

		public static void SetFrameFromPlatformView(
			this View view,
			Rect frame, LayoutAlignment defaultHorizontalAlignment = LayoutAlignment.Fill, LayoutAlignment defaultVerticalAlignment = LayoutAlignment.Fill)
		{
			if (view is null)
				return;
			var margin = view.GetMargin();
			if (!margin.IsEmpty)
			{
				frame.X += margin.Left;
				frame.Y += margin.Top;
				frame.Width -= margin.HorizontalThickness;
				frame.Height -= margin.VerticalThickness;
			}
			if (!view.MeasurementValid)
			{
				var sizeThatFits = view.Measure(frame.Size.Width, frame.Size.Height);
				view.MeasuredSize = sizeThatFits;
				view.MeasurementValid = true;
			}


			var width = view.MeasuredSize.Width - margin.HorizontalThickness;
			var height = view.MeasuredSize.Height - margin.VerticalThickness;

			var frameConstraints = view.GetFrameConstraints();

			
			var horizontalSizing = view.GetHorizontalLayoutAlignment(view.Parent as ContainerView,  defaultHorizontalAlignment);
			var verticalSizing = view.GetVerticalLayoutAlignment(view.Parent as ContainerView, defaultVerticalAlignment);


			if (frameConstraints?.Width is not null)
			{
				width = (float)frameConstraints.Width;
			}
			else
			{
				if (horizontalSizing == LayoutAlignment.Fill && !double.IsInfinity(frame.Width))
					width = frame.Width;
			}

			if (frameConstraints?.Height is not null)
			{
				height = (float)frameConstraints.Height;
			}
			else
			{
				if (verticalSizing == LayoutAlignment.Fill && !double.IsInfinity(frame.Height))
					height = frame.Height;
			}

			var xFactor = .5f;
			switch (horizontalSizing)
			{
				case LayoutAlignment.Start:
				case LayoutAlignment.Fill:
					xFactor = 0;
					break;
				case LayoutAlignment.End:
					xFactor = 1;
					break;
			}

			var yFactor = .5f;
			switch (verticalSizing)
			{
				case LayoutAlignment.End:
					yFactor = 1;
					break;
				case LayoutAlignment.Start:
				case LayoutAlignment.Fill:
					yFactor = 0;
					break;
			}

			var x = frame.X + ((frame.Width - width) * xFactor);
			var y = frame.Y + ((frame.Height - height) * yFactor);
			view.Frame = new Rect(x, y, width, height);
		}

		public static T FillHorizontal<T>(this T view) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, LayoutAlignment.Fill, false);
			return view;
		}

		public static T FillVertical<T>(this T view) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.VerticalLayoutAlignment, LayoutAlignment.Fill, false);
			return view;
		}

		public static T FitHorizontal<T>(this T view) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, LayoutAlignment.Start, false);
			return view;
		}

		public static T FitVertical<T>(this T view) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.VerticalLayoutAlignment, LayoutAlignment.Start, false);
			return view;
		}

		public static LayoutAlignment GetHorizontalLayoutAlignment(this View view, ContainerView container, LayoutAlignment defaultSizing = LayoutAlignment.Start)
		{
			var sizing = view.GetEnvironment<LayoutAlignment?>(view, EnvironmentKeys.Layout.HorizontalLayoutAlignment,false);
			if (sizing is not null) return (LayoutAlignment)sizing;

			if (container is not null)
				sizing = view.GetEnvironment<LayoutAlignment?>(view, $"{container.GetType().Name}.{EnvironmentKeys.Layout.HorizontalLayoutAlignment}");
			return sizing ?? defaultSizing;
		}

		public static LayoutAlignment GetVerticalLayoutAlignment(this View view, ContainerView container, LayoutAlignment defaultSizing = LayoutAlignment.Start)
		{
			var sizing = view.GetEnvironment<LayoutAlignment?>(view, EnvironmentKeys.Layout.VerticalLayoutAlignment, false);
			if (sizing is not null) return (LayoutAlignment)sizing;

			if (container is not null)
				sizing = view.GetEnvironment<LayoutAlignment?>(view, $"{container.GetType().Name}.{EnvironmentKeys.Layout.VerticalLayoutAlignment}");
			return sizing ?? defaultSizing;
		}

		public static T VerticalLayoutAlignment<T>(this T view, LayoutAlignment alignment) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Layout.VerticalLayoutAlignment, alignment,false);

		public static T HorizontalLayoutAlignment<T>(this T view, LayoutAlignment alignment) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Layout.HorizontalLayoutAlignment, alignment, false);


		public static T FrameConstraints<T>(this T view, FrameConstraints constraints) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.FrameConstraints, constraints, false);
			return view;
		}

		public static FrameConstraints GetFrameConstraints(this View view, FrameConstraints defaultContraints = null)
		{
			var constraints = view.GetEnvironment<FrameConstraints>(view, EnvironmentKeys.Layout.FrameConstraints,false);
			return constraints ?? defaultContraints;
		}

		// AbsoluteLayout extensions
		public static T LayoutBounds<T>(this T view, Rect bounds) where T : View
		{
			view.SetEnvironment("AbsoluteLayout.LayoutBounds", bounds, false);
			return view;
		}

		public static T LayoutFlags<T>(this T view, AbsoluteLayoutFlags flags) where T : View
		{
			view.SetEnvironment("AbsoluteLayout.LayoutFlags", flags, false);
			return view;
		}

		public static Rect GetLayoutBounds(this View view)
		{
			var bounds = view.GetEnvironment<Rect?>("AbsoluteLayout.LayoutBounds");
			return bounds ?? new Rect(0, 0, -1, -1); // -1 means AutoSize
		}

		public static AbsoluteLayoutFlags GetLayoutFlags(this View view)
		{
			var flags = view.GetEnvironment<AbsoluteLayoutFlags?>("AbsoluteLayout.LayoutFlags");
			return flags ?? AbsoluteLayoutFlags.None;
		}

		// FlexLayout extensions (Yoga vocabulary — see Comet.Layout.Yoga.FlexAlign / FlexPositionType).
		public static T FlexBasis<T>(this T view, double basis) where T : View
		{
			view.SetEnvironment("FlexLayout.Basis", basis, false);
			return view;
		}

		public static T FlexGrow<T>(this T view, double grow) where T : View
		{
			view.SetEnvironment("FlexLayout.Grow", grow, false);
			return view;
		}

		public static T FlexShrink<T>(this T view, double shrink) where T : View
		{
			view.SetEnvironment("FlexLayout.Shrink", shrink, false);
			return view;
		}

		public static T FlexAlignSelf<T>(this T view, Comet.Layout.Yoga.FlexAlign alignSelf) where T : View
		{
			view.SetEnvironment("FlexLayout.AlignSelf", alignSelf, false);
			return view;
		}

		public static T FlexOrder<T>(this T view, int order) where T : View
		{
			view.SetEnvironment("FlexLayout.Order", order, false);
			return view;
		}

		/// <summary>Per-child aspect ratio (width / height). Honoured by the Yoga-backed managers.</summary>
		public static T AspectRatio<T>(this T view, double ratio) where T : View
		{
			view.SetEnvironment("FlexLayout.AspectRatio", ratio, false);
			return view;
		}

		/// <summary>Yoga position type for this child (Static / Relative / Absolute).</summary>
		public static T PositionType<T>(this T view, Comet.Layout.Yoga.FlexPositionType positionType) where T : View
		{
			view.SetEnvironment("FlexLayout.PositionType", positionType, false);
			return view;
		}

		/// <summary>Uniform spacing between children on both axes. Consumed by Yoga-backed layouts.</summary>
		public static T Gap<T>(this T view, double gap) where T : View
		{
			view.SetEnvironment("FlexLayout.Gap", gap, false);
			return view;
		}

		/// <summary>Cross-axis spacing between wrapped rows. Overrides <see cref="Gap"/>.</summary>
		public static T RowGap<T>(this T view, double gap) where T : View
		{
			view.SetEnvironment("FlexLayout.RowGap", gap, false);
			return view;
		}

		/// <summary>Spacing between columns. Overrides <see cref="Gap"/>.</summary>
		public static T ColumnGap<T>(this T view, double gap) where T : View
		{
			view.SetEnvironment("FlexLayout.ColumnGap", gap, false);
			return view;
		}

		public static double GetFlexBasis(this View view)
		{
			var basis = view.GetEnvironment<double?>("FlexLayout.Basis");
			return basis ?? -1;
		}

		public static double GetFlexGrow(this View view)
		{
			var grow = view.GetEnvironment<double?>("FlexLayout.Grow");
			return grow ?? 0;
		}

		public static double GetFlexShrink(this View view)
		{
			var shrink = view.GetEnvironment<double?>("FlexLayout.Shrink");
			return shrink ?? 1;
		}

		public static Comet.Layout.Yoga.FlexAlign GetFlexAlignSelf(this View view)
		{
			var align = view.GetEnvironment<Comet.Layout.Yoga.FlexAlign?>("FlexLayout.AlignSelf");
			return align ?? Comet.Layout.Yoga.FlexAlign.Auto;
		}

		public static int GetFlexOrder(this View view)
		{
			var order = view.GetEnvironment<int?>("FlexLayout.Order");
			return order ?? 0;
		}

		public static double GetAspectRatio(this View view)
		{
			var ratio = view.GetEnvironment<double?>("FlexLayout.AspectRatio");
			return ratio ?? -1;
		}

		public static Comet.Layout.Yoga.FlexPositionType GetPositionType(this View view)
		{
			var pos = view.GetEnvironment<Comet.Layout.Yoga.FlexPositionType?>("FlexLayout.PositionType");
			return pos ?? Comet.Layout.Yoga.FlexPositionType.Relative;
		}

		public static double GetGap(this View view)
		{
			var gap = view.GetEnvironment<double?>("FlexLayout.Gap");
			return gap ?? -1;
		}

		public static double GetRowGap(this View view)
		{
			var gap = view.GetEnvironment<double?>("FlexLayout.RowGap");
			return gap ?? -1;
		}

		public static double GetColumnGap(this View view)
		{
			var gap = view.GetEnvironment<double?>("FlexLayout.ColumnGap");
			return gap ?? -1;
		}

		public static T Margin<T>(this T view, Thickness margin, bool cascades = false) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.Margin, margin, cascades);
			return view;
		}
		public static Thickness GetMargin(this View view, Thickness? defaultValue = null)
		{
			var margin = view.GetEnvironment<Thickness?>(view, EnvironmentKeys.Layout.Margin);
			return margin ?? defaultValue ?? Thickness.Zero;
		}

		public static T Padding<T>(this T view, Thickness padding, bool cascades = false) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.Padding, (object)padding, cascades);
			return view;
		}

		public static T Padding<T>(this T view, Func<Thickness> padding, bool cascades = false) where T : View => view.Padding(padding(), cascades);

		public static Thickness GetPadding(this View view, Thickness? defaultValue = null)
		{
			var margin = view.GetEnvironment<Thickness?>(view, EnvironmentKeys.Layout.Padding);
			return margin ?? defaultValue ?? Thickness.Zero;
		}


		public static T LayoutConstraints<T>(this T view, object contraints, bool cascades = false) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.Constraints, contraints, cascades);
			return view;
		}

		public static object GetLayoutConstraints(this View view, object defaultValue = null)
		{
			var constraints = view.GetEnvironment<object>(view, EnvironmentKeys.Layout.Constraints);
			return constraints ?? defaultValue;
		}

		public static Size Measure(this View view, Size availableSize, bool includeMargin)
		{
			if (availableSize.Width <= 0 || availableSize.Height <= 0)
				return availableSize;
			
			if (includeMargin)
			{
				var margin = view.GetMargin();
				availableSize.Width -= margin.HorizontalThickness;
				availableSize.Height -= margin.VerticalThickness;

				var measuredSize = view.Measure(availableSize.Width,availableSize.Height);
				measuredSize.Width += margin.HorizontalThickness;
				measuredSize.Height += margin.VerticalThickness;
				return measuredSize;
			}

			return view.Measure(availableSize.Width, availableSize.Height);
		}

		public static T IgnoreSafeArea<T>(this T view) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Layout.IgnoreSafeArea, true, false);
			return view;
		}

		public static bool GetIgnoreSafeArea(this View view, bool defaultValue) => (bool?)view.GetEnvironment(view, EnvironmentKeys.Layout.IgnoreSafeArea, false) ?? defaultValue;
	}
}
