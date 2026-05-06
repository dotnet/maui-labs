using System;
using Comet.Reactive;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class BindingSampleState
	{
		public bool CanEdit { get; set; } = true;
		public string Text { get; set; } = "Bar";
		public int ClickCount { get; set; } = 1;
	}

	public class BindingSample : Component<BindingSampleState>
	{
		public override View Render() =>
			NavigationView(ScrollView(
				VStack(
					(State.CanEdit
						? (View) TextField(State.Text, "Enter text")
							.OnTextChanged(t => SetState(s => s.Text = t))
						: Text($"{State.Text}: multiText")),
					Text(State.Text),
					HStack(
						Button("Toggle Entry/Label",
							() => SetState(s => s.CanEdit = !s.CanEdit)),
						Button("Update Text",
							() => SetState(s => s.Text = $"Click Count: {s.ClickCount++}")),
						Button("Update FontSize",
							() => {
								var font = View.GetGlobalEnvironment<float?>(EnvironmentKeys.Fonts.Size) ?? 14;
								var size = font + 5;
								View.SetGlobalEnvironment(EnvironmentKeys.Fonts.Size, size);
							})
					),
					Toggle(State.CanEdit)
						.OnToggled(v => SetState(s => s.CanEdit = v))
				)
			));
	}
}
