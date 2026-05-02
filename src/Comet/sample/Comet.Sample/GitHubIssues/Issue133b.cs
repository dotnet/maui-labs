using System;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Issue133b : Component<CreditCard>
	{
		readonly Reactive<bool> remember = false;

		public override View Render() => VStack(20,

			new BorderedEntry(State.Number,"Enter CC Number", "\uf09d")
				.Margin(left:20, right: 20)

		).FillHorizontal().Alignment(Alignment.Top);

		public class BorderedEntry : HStack
		{
			public BorderedEntry(string val, string placeholder, string icon) : base(spacing: 8)
			{
				Add(Text(icon)
					.Frame(width: 24)
					.Margin(left: 8)
					.FontFamily("FontAwesome"));

				Add(TextField(val, placeholder));

				this.Frame(height: 40).RoundedBorder(color: Colors.Grey);

			}
		}
	}
}
