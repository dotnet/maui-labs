using System;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	public class BoxView : View
	{
		public BoxView()
		{
		}

		public BoxView(Color color)
		{
			Color = color;
		}

		public BoxView(Func<Color> color)
		{
			Color = PropertySubscription<Color>.FromFunc(color);
		}

		private PropertySubscription<Color> _color;
		public PropertySubscription<Color> Color
		{
			get => _color;
			set => this.SetPropertySubscription(ref _color, value);
		}

		private PropertySubscription<CornerRadius> _cornerRadius;
		public PropertySubscription<CornerRadius> CornerRadius
		{
			get => _cornerRadius;
			set => this.SetPropertySubscription(ref _cornerRadius, value);
		}
	}
}
