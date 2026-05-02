using System;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class BasicTestState
	{
		public bool CanEdit { get; set; } = true;
		public string Text { get; set; } = "Bar";
		public int ClickCount { get; set; } = 1;
	}

	public class BasicTestView : Component<BasicTestState>
	{
		public override View Render() =>
			VStack(
				(State.CanEdit
					? (View) TextField(State.Text, "Enter text")
						.OnTextChanged(t => SetState(s => s.Text = t))
					: Text($"{State.Text}: multiText")),
				Text(State.Text),
				HStack(
					Button("Toggle Entry/Label",
						() => SetState(s => s.CanEdit = !s.CanEdit))
						.Background(Colors.Salmon),
					Button("Update Text",
						() => SetState(s => s.Text = $"Click Count: {s.ClickCount++}"))
				)
			);
	}
}
