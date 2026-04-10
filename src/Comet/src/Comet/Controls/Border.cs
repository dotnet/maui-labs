using System;
using System.Collections;
using System.Collections.Generic;
using Comet.Layout;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace Comet
{
	public class Border : AbstractLayout, IBorderStroke
	{
		public View Content
		{
			get => Count > 0 ? this[0] : null;
			set
			{
				Clear();
				if (value is not null)
					Add(value);
			}
		}

		protected override ILayoutManager CreateLayoutManager() =>
			new BorderLayoutManager(this);

		protected override Thickness GetDefaultPadding() => Thickness.Zero;

		IShape IBorderStroke.Shape =>
			this.GetEnvironment<IShape>(EnvironmentKeys.View.ClipShape)
			?? new RoundedRectangle(0);

		Paint IStroke.Stroke =>
			this.GetEnvironment<Paint>(EnvironmentKeys.Shape.StrokeColor);

		double IStroke.StrokeThickness =>
			this.GetEnvironment<double?>(EnvironmentKeys.Shape.LineWidth) ?? 0;

		LineCap IStroke.StrokeLineCap => LineCap.Butt;

		LineJoin IStroke.StrokeLineJoin => LineJoin.Miter;

		float[] IStroke.StrokeDashPattern => null;

		float IStroke.StrokeDashOffset => 0;

		float IStroke.StrokeMiterLimit => 10;
	}
}

