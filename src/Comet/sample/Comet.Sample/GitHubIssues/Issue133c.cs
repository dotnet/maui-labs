using System;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Issue133c : Component<CreditCard>
	{
		public override View Render() => VStack(20,

			new BorderedEntry(State.Number,"Enter CC Number", "\uf09d")
				.Margin(left:20, right: 20)

		).FillHorizontal().Alignment(Alignment.Top);

		private class BorderedEntry : Component
		{
			private string _val;
			private string _placeholder;
			private string _icon;

			public BorderedEntry(string val, string placeholder, string icon)
			{
				_val = val;
				_placeholder = placeholder;
				_icon = icon;
			}

						public override View Render() => HStack(8,
					Text(_icon)
						.Frame(width: 24)
						.Margin(left: 8)
						.FontFamily("FontAwesome"),

					TextField(_val, _placeholder)
				)
				.Frame(height: 40)
				.RoundedBorder(color: Colors.Grey);
		}
	}
}
