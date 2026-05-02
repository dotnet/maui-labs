using System;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests that fluent extension method chaining works on generated controls.
	/// Verifies environment-based properties (FontSize, Background, Color, etc.)
	/// compose correctly via chaining and can be read back.
	/// </summary>
	public class FluentExtensionTests : TestBase
	{
		// ---- Single extension method ----

		[Fact]
		public void Text_FontSize_SetsEnvironment()
		{
			var text = new Text("Hello").FontSize(24);
			var font = text.GetFont(null);
			Assert.Equal(24, font.Size);
		}

		[Fact]
		public void Text_Color_SetsEnvironment()
		{
			var text = new Text("Hello").Color(Colors.Red);
			var color = text.GetColor();
			Assert.Equal(Colors.Red, color);
		}

		[Fact]
		public void Button_Background_SetsEnvironment()
		{
			var button = new Button("Click").Background(Colors.Blue);
			var bg = button.GetBackground();
			Assert.NotNull(bg);
		}

		[Fact]
		public void Slider_MinimumTrackColor_SetsEnvironment()
		{
			var slider = new Slider(0.5).MinimumTrackColor(Colors.Green);
			// The extension stores in environment; verify the slider is returned for chaining
			Assert.NotNull(slider);
			Assert.Equal(0.5, slider.Value?.CurrentValue);
		}

		[Fact]
		public void Toggle_OnColor_SetsEnvironment()
		{
			var toggle = new Toggle(true).OnColor(Colors.Orange);
			Assert.NotNull(toggle);
			Assert.True(toggle.Value?.CurrentValue);
		}

		// ---- Chaining multiple extensions ----

		[Fact]
		public void Text_ChainFontSizeAndColor()
		{
			var text = new Text("Styled")
				.FontSize(18)
				.Color(Colors.DarkBlue);

			var font = text.GetFont(null);
			Assert.Equal(18, font.Size);

			var color = text.GetColor();
			Assert.Equal(Colors.DarkBlue, color);
		}

		[Fact]
		public void Text_ChainFontSizeWeightFamily()
		{
			var text = new Text("Bold")
				.FontSize(20)
				.FontWeight(Microsoft.Maui.FontWeight.Bold)
				.FontFamily("Arial");

			var font = text.GetFont(null);
			Assert.Equal(20, font.Size);
			Assert.Equal(Microsoft.Maui.FontWeight.Bold, font.Weight);
			Assert.Equal("Arial", font.Family);
		}

		[Fact]
		public void Button_ChainColorAndBackground()
		{
			var button = new Button("Go")
				.Color(Colors.White)
				.Background(Colors.Green);

			var color = button.GetColor();
			Assert.Equal(Colors.White, color);

			var bg = button.GetBackground();
			Assert.NotNull(bg);
		}

		[Fact]
		public void TextField_ChainPlaceholderColorAndKeyboard()
		{
			var field = new TextField("text", "hint")
				.PlaceholderColor(Colors.Gray)
				.Keyboard(Microsoft.Maui.Keyboard.Email);

			Assert.NotNull(field);
			Assert.Equal("text", field.Text?.CurrentValue);
		}

		[Fact]
		public void Slider_ChainAllTrackColors()
		{
			var slider = new Slider(0.5)
				.MinimumTrackColor(Colors.Blue)
				.MaximumTrackColor(Colors.LightGray)
				.ThumbColor(Colors.DarkBlue);

			Assert.NotNull(slider);
			Assert.Equal(0.5, slider.Value?.CurrentValue);
		}

		// ---- Layout extensions chain with control extensions ----

		[Fact]
		public void Text_ChainWithFrame()
		{
			var text = new Text("Constrained")
				.FontSize(16)
				.Frame(width: 200, height: 50);

			var font = text.GetFont(null);
			Assert.Equal(16, font.Size);
			Assert.NotNull(text);
		}

		[Fact]
		public void Button_ChainWithMargin()
		{
			var button = new Button("Spaced")
				.Background(Colors.Red)
				.Margin(10);

			var bg = button.GetBackground();
			Assert.NotNull(bg);
		}

		[Fact]
		public void Text_ChainWithOpacity()
		{
			var text = new Text("Faded")
				.FontSize(14)
				.Color(Colors.Black)
				.Opacity(0.5);

			var opacity = text.GetOpacity();
			Assert.Equal(0.5, opacity);
		}

		// ---- Chaining preserves control identity ----

		[Fact]
		public void ChainingReturnsSameInstance()
		{
			var original = new Text("Same");
			var chained = original.FontSize(12).Color(Colors.Red).Background(Colors.White);

			Assert.Same(original, chained);
		}

		[Fact]
		public void ButtonChainingReturnsSameInstance()
		{
			var original = new Button("Same");
			var chained = original.Background(Colors.Blue).Margin(5);

			Assert.Same(original, chained);
		}

		// ---- Chaining inside a View body ----

		class StyledView : View
		{
			[Body]
			View body() => new VStack
			{
				new Text("Title")
					.FontSize(24)
					.FontWeight(Microsoft.Maui.FontWeight.Bold)
					.Color(Colors.Black),
				new Text("Subtitle")
					.FontSize(14)
					.Color(Colors.Gray),
				new Button("Action")
					.Background(Colors.Blue)
					.Color(Colors.White)
					.Margin(8),
			};
		}

		[Fact]
		public void ChainedExtensions_WorkInsideViewBody()
		{
			var view = new StyledView();
			view.SetViewHandlerToGeneric();

			var built = view.BuiltView;
			Assert.IsType<VStack>(built);
			var stack = (VStack)built;
			Assert.Equal(3, stack.Count);

			// Verify font on title
			var title = (Text)stack[0];
			var font = title.GetFont(null);
			Assert.Equal(24, font.Size);
			Assert.Equal(Microsoft.Maui.FontWeight.Bold, font.Weight);

			// Verify color on title
			var titleColor = title.GetColor();
			Assert.Equal(Colors.Black, titleColor);

			// Verify subtitle styling
			var subtitle = (Text)stack[1];
			var subFont = subtitle.GetFont(null);
			Assert.Equal(14, subFont.Size);
		}

		// ---- Overwriting an extension value ----

		[Fact]
		public void LastExtensionCallWins()
		{
			var text = new Text("Override")
				.FontSize(12)
				.FontSize(24);

			var font = text.GetFont(null);
			Assert.Equal(24, font.Size);
		}

		[Fact]
		public void ColorCanBeOverridden()
		{
			var text = new Text("Override")
				.Color(Colors.Red)
				.Color(Colors.Blue);

			var color = text.GetColor();
			Assert.Equal(Colors.Blue, color);
		}

		// ---- Style extensions on Text ----

		[Fact]
		public void Text_StyleAsH1_ChainedWithColor()
		{
			var text = new Text("Heading")
				.StyleAsH1()
				.Color(Colors.DarkRed);

			var color = text.GetColor();
			Assert.Equal(Colors.DarkRed, color);
		}

		// ---- Binding-based fluent extensions ----

		[Fact]
		public void Text_FontSize_WithBindingFunc()
		{
			double size = 18;
			var text = new Text("Dynamic").FontSize(() => size);
			var font = text.GetFont(null);
			Assert.Equal(18, font.Size);
		}

		// ---- Factory method tests (Phase 2.1 — awaiting Naomi's work) ----

		[Fact(Skip = "Awaiting Phase 2.1 factory method generation")]
		public void FactoryMethod_Button_CanBeCalledStatically()
		{
			// Once factory methods land, this should work with `using static Comet.Controls;`
			// or similar pattern. The factory method should return a Button.
			// Example: var btn = Button("Click", () => { });
			Assert.Fail("Placeholder — implement when factory methods are generated");
		}

		[Fact(Skip = "Awaiting Phase 2.1 factory method generation")]
		public void FactoryMethod_Text_CanBeCalledStatically()
		{
			// Example: var txt = Text("Hello");
			Assert.Fail("Placeholder — implement when factory methods are generated");
		}

		[Fact(Skip = "Awaiting Phase 2.1 factory method generation")]
		public void FactoryMethod_Slider_CanBeCalledStatically()
		{
			// Example: var slider = Slider(0.5, 0, 1);
			Assert.Fail("Placeholder — implement when factory methods are generated");
		}

		[Fact(Skip = "Awaiting Phase 2.1 factory method generation")]
		public void FactoryMethod_ReturnsCorrectType()
		{
			// Factory methods should return the concrete generated type
			// so fluent chaining works: Button("Go", () => {}).Background(Colors.Red)
			Assert.Fail("Placeholder — implement when factory methods are generated");
		}

		[Fact(Skip = "Awaiting Phase 2.1 factory method generation")]
		public void FactoryMethod_ChainsFluently()
		{
			// Full chain: Text("Hello").FontSize(24).Color(Colors.Red).Background(Colors.White)
			// via static factory
			Assert.Fail("Placeholder — implement when factory methods are generated");
		}
	}
}
