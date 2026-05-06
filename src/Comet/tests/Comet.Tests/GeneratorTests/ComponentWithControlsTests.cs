using System;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests that generated controls work correctly inside Component.Render().
	/// Verifies that the Component lifecycle (Render, SetState, re-render)
	/// composes cleanly with source-generated controls.
	/// </summary>
	public class ComponentWithControlsTests : TestBase
	{
		// ---- Simple Component returning generated controls ----

		class TextComponent : Component
		{
			public override View Render()
			{
				return new Text("Component Text")
					.FontSize(20)
					.Color(Colors.Navy);
			}
		}

		[Fact]
		public void Component_CanRenderText()
		{
			var component = new TextComponent();
			component.SetViewHandlerToGeneric();

			var built = component.BuiltView;
			Assert.NotNull(built);
			Assert.IsType<Text>(built);

			var text = (Text)built;
			Assert.Equal("Component Text", text.Value?.CurrentValue);
		}

		[Fact]
		public void Component_RenderedText_HasStyling()
		{
			var component = new TextComponent();
			component.SetViewHandlerToGeneric();

			var text = (Text)component.BuiltView;
			var font = text.GetFont(null);
			Assert.Equal(20, font.Size);

			var color = text.GetColor();
			Assert.Equal(Colors.Navy, color);
		}

		// ---- Component returning a layout with multiple generated controls ----

		class FormComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Login")
						.FontSize(24)
						.FontWeight(Microsoft.Maui.FontWeight.Bold),
					new TextField("", "Username"),
					new SecureField("", "Password"),
					new Button("Sign In", () => { })
						.Background(Colors.Blue)
						.Color(Colors.White),
				};
			}
		}

		[Fact]
		public void Component_CanRenderFormLayout()
		{
			var component = new FormComponent();
			component.SetViewHandlerToGeneric();

			var built = component.BuiltView;
			Assert.IsType<VStack>(built);

			var stack = (VStack)built;
			Assert.Equal(4, stack.Count);
			Assert.IsType<Text>(stack[0]);
			Assert.IsType<TextField>(stack[1]);
			Assert.IsType<SecureField>(stack[2]);
			Assert.IsType<Button>(stack[3]);
		}

		[Fact]
		public void Component_FormControls_HaveCorrectValues()
		{
			var component = new FormComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			var title = (Text)stack[0];
			Assert.Equal("Login", title.Value?.CurrentValue);

			var username = (TextField)stack[1];
			Assert.Equal("Username", username.Placeholder?.CurrentValue);

			var password = (SecureField)stack[2];
			Assert.Equal("Password", password.Placeholder?.CurrentValue);

			var button = (Button)stack[3];
			Assert.Equal("Sign In", button.Text?.CurrentValue);
		}

		// ---- Stateful Component with generated controls ----

		class CounterState
		{
			public int Count { get; set; }
		}

		class CounterComponent : Component<CounterState>
		{
			public override View Render()
			{
				return new VStack
				{
					new Text($"Count: {State.Count}")
						.FontSize(32),
					new Button("Increment", () => SetState(s => s.Count++)),
					new Slider((double)State.Count, 0d, 100d),
				};
			}
		}

		[Fact]
		public void StatefulComponent_CanRenderGeneratedControls()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			var built = component.BuiltView;
			Assert.IsType<VStack>(built);

			var stack = (VStack)built;
			Assert.Equal(3, stack.Count);
			Assert.IsType<Text>(stack[0]);
			Assert.IsType<Button>(stack[1]);
			Assert.IsType<Slider>(stack[2]);
		}

		[Fact]
		public void StatefulComponent_InitialValues_Correct()
		{
			var component = new CounterComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			var text = (Text)stack[0];
			Assert.Equal("Count: 0", text.Value?.CurrentValue);

			var slider = (Slider)stack[2];
			Assert.Equal(0.0, slider.Value?.CurrentValue);
		}

		// ---- Component with Slider and Toggle ----

		class SettingsState
		{
			public double Volume { get; set; } = 0.7;
			public bool Muted { get; set; }
		}

		class SettingsComponent : Component<SettingsState>
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Settings")
						.FontSize(20)
						.FontWeight(Microsoft.Maui.FontWeight.Bold),
					new Text($"Volume: {State.Volume:P0}"),
					new Slider(State.Volume, 0, 1)
						.MinimumTrackColor(Colors.Blue),
					new Toggle(State.Muted),
				};
			}
		}

		[Fact]
		public void SettingsComponent_RendersAllControls()
		{
			var component = new SettingsComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			Assert.Equal(4, stack.Count);
			Assert.IsType<Text>(stack[0]);
			Assert.IsType<Text>(stack[1]);
			Assert.IsType<Slider>(stack[2]);
			Assert.IsType<Toggle>(stack[3]);
		}

		[Fact]
		public void SettingsComponent_InitialState_ReflectedInControls()
		{
			var component = new SettingsComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;

			var slider = (Slider)stack[2];
			Assert.Equal(0.7, slider.Value?.CurrentValue);

			var toggle = (Toggle)stack[3];
			Assert.False(toggle.Value?.CurrentValue);
		}

		// ---- Component with Props and generated controls ----

		class GreetingProps
		{
			public string Name { get; set; } = "World";
		}

		class GreetingState
		{
			public int WaveCount { get; set; }
		}

		class GreetingComponent : Component<GreetingState, GreetingProps>
		{
			public override View Render()
			{
				return new VStack
				{
					new Text($"Hello, {Props.Name}!")
						.FontSize(18)
						.Color(Colors.DarkGreen),
					new Text($"Waved {State.WaveCount} times"),
					new Button("Wave", () => SetState(s => s.WaveCount++)),
				};
			}
		}

		[Fact]
		public void ComponentWithProps_RendersGeneratedControls()
		{
			var component = new GreetingComponent();
			component.Props = new GreetingProps { Name = "Bobbie" };
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			Assert.Equal(3, stack.Count);

			var greeting = (Text)stack[0];
			Assert.Equal("Hello, Bobbie!", greeting.Value?.CurrentValue);
		}

		[Fact]
		public void ComponentWithProps_DefaultProps_Work()
		{
			var component = new GreetingComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			var greeting = (Text)stack[0];
			Assert.Equal("Hello, World!", greeting.Value?.CurrentValue);
		}

		// ---- Nested Components with generated controls ----

		class InnerComponent : Component
		{
			public override View Render()
			{
				return new Text("Inner")
					.FontSize(12)
					.Color(Colors.Gray);
			}
		}

		class OuterComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Outer").FontSize(24),
					new InnerComponent(),
					new Button("Action", () => { }),
				};
			}
		}

		[Fact]
		public void NestedComponents_WithGeneratedControls_Build()
		{
			var outer = new OuterComponent();
			outer.SetViewHandlerToGeneric();

			var built = outer.BuiltView;
			Assert.IsType<VStack>(built);

			var stack = (VStack)built;
			Assert.Equal(3, stack.Count);
			Assert.IsType<Text>(stack[0]);
			Assert.IsType<InnerComponent>(stack[1]);
			Assert.IsType<Button>(stack[2]);
		}

		// ---- Component with ProgressBar and ActivityIndicator ----

		class LoadingComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new ActivityIndicator(true),
					new ProgressBar(0.4)
						.ProgressColor(Colors.Teal),
					new Text("Loading...")
						.FontSize(14)
						.Color(Colors.Gray),
				};
			}
		}

		[Fact]
		public void LoadingComponent_RendersIndicatorAndProgress()
		{
			var component = new LoadingComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			Assert.Equal(3, stack.Count);
			Assert.IsType<ActivityIndicator>(stack[0]);
			Assert.IsType<ProgressBar>(stack[1]);
			Assert.IsType<Text>(stack[2]);

			var indicator = (ActivityIndicator)stack[0];
			Assert.True(indicator.IsRunning?.CurrentValue);

			var progress = (ProgressBar)stack[1];
			Assert.Equal(0.4, progress.Value?.CurrentValue);
		}

		// ---- Component with CheckBox and Stepper ----

		class QuantityComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new CheckBox(false),
					new Stepper(1d, 0d, 10d, 1d),
					new Text("Quantity controls"),
				};
			}
		}

		[Fact]
		public void QuantityComponent_RendersCheckBoxAndStepper()
		{
			var component = new QuantityComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			Assert.Equal(3, stack.Count);
			Assert.IsType<CheckBox>(stack[0]);
			Assert.IsType<Stepper>(stack[1]);
			Assert.IsType<Text>(stack[2]);
		}

		// ---- Fluent chaining on controls inside Component ----

		class HeavilyStyledComponent : Component
		{
			public override View Render()
			{
				return new VStack
				{
					new Text("Styled Title")
						.FontSize(28)
						.FontWeight(Microsoft.Maui.FontWeight.Bold)
						.Color(Colors.DarkSlateBlue)
						.Background(Colors.LightYellow)
						.Margin(10)
						.Frame(width: 300, height: 50),
					new Button("Styled Button")
						.Background(Colors.Indigo)
						.Color(Colors.White)
						.Frame(width: 200, height: 44)
						.Margin(8),
				};
			}
		}

		[Fact]
		public void Component_HeavilyStyling_AllChainsApply()
		{
			var component = new HeavilyStyledComponent();
			component.SetViewHandlerToGeneric();

			var stack = (VStack)component.BuiltView;
			var title = (Text)stack[0];

			var font = title.GetFont(null);
			Assert.Equal(28, font.Size);
			Assert.Equal(Microsoft.Maui.FontWeight.Bold, font.Weight);

			var color = title.GetColor();
			Assert.Equal(Colors.DarkSlateBlue, color);

			var bg = title.GetBackground();
			Assert.NotNull(bg);
		}
	}
}
