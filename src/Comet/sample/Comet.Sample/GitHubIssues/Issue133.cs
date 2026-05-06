using System;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class Issue133 : Component<CreditCard>
	{
		readonly Reactive<bool> remember = false;

		public override View Render() => VStack(20,

			new BorderedEntry(State.Number,"Enter CC Number", "\uf09d")
				.Margin(left:20, right: 20),

			HStack(20,
				new BorderedEntry(State.Expiration, "MM/YYYY", "\uf783")
					.Frame(height: 40, width: 200)
					.Margin(left:20),

				Spacer(),

				new BorderedEntry(State.CVV, "CVV", "\uf023")
					.Frame( height: 40, width: 100)
					.Margin(right:20)
			)


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
