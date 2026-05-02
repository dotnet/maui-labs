using Xunit;
using static Comet.CometControls;

namespace Comet.Tests
{
	public class FactoryMethodTests : TestBase
	{
		[Fact]
		public void ButtonFactoryCreatesButton()
		{
			var button = Button("Click me", () => { });
			Assert.NotNull(button);
			Assert.IsType<Comet.Button>(button);
			Assert.Equal("Click me", button.Text?.CurrentValue);
		}

		[Fact]
		public void ButtonFactoryWithFuncCreatesButton()
		{
			var label = "Dynamic";
			var button = Button(() => label);
			Assert.NotNull(button);
			Assert.Equal("Dynamic", button.Text?.CurrentValue);
		}

		[Fact]
		public void ButtonParameterlessFactory()
		{
			var button = Button();
			Assert.NotNull(button);
			Assert.IsType<Comet.Button>(button);
		}

		[Fact]
		public void TextFactoryCreatesText()
		{
			var text = Text("Hello");
			Assert.NotNull(text);
			Assert.IsType<Comet.Text>(text);
			Assert.Equal("Hello", text.Value?.CurrentValue);
		}

		[Fact]
		public void TextFactoryWithFuncCreatesText()
		{
			var msg = "World";
			var text = Text(() => msg);
			Assert.NotNull(text);
			Assert.Equal("World", text.Value?.CurrentValue);
		}

		[Fact]
		public void ToggleFactoryCreatesToggle()
		{
			var toggle = Toggle(true);
			Assert.NotNull(toggle);
			Assert.IsType<Comet.Toggle>(toggle);
			Assert.True(toggle.Value?.CurrentValue);
		}

		[Fact]
		public void SliderFactoryCreatesSlider()
		{
			var slider = Slider(0.5);
			Assert.NotNull(slider);
			Assert.IsType<Comet.Slider>(slider);
			Assert.Equal(0.5, slider.Value?.CurrentValue);
		}

		[Fact]
		public void TextFieldFactoryCreatesTextField()
		{
			var field = TextField("initial", "Enter text");
			Assert.NotNull(field);
			Assert.IsType<Comet.TextField>(field);
			Assert.Equal("initial", field.Text?.CurrentValue);
		}

		[Fact]
		public void FactoryMethodsChainsWithExtensions()
		{
			var button = Button("Styled")
				.Background(Microsoft.Maui.Graphics.Colors.Red);
			Assert.NotNull(button);
			Assert.IsType<Comet.Button>(button);
		}

		[Fact]
		public void OnPrefixedExtensionMethodExists()
		{
			var button = Button("Test")
				.OnPressed(() => { })
				.OnReleased(() => { });
			Assert.NotNull(button);
		}

		[Fact]
		public void DatePickerFactoryCreates()
		{
			var picker = DatePicker((System.DateTime?)System.DateTime.Today);
			Assert.NotNull(picker);
			Assert.IsType<Comet.DatePicker>(picker);
		}

		[Fact]
		public void StepperFactoryCreates()
		{
			var stepper = Stepper(5.0, 0.0, 10.0, 1.0);
			Assert.NotNull(stepper);
			Assert.IsType<Comet.Stepper>(stepper);
		}

		[Fact]
		public void CheckBoxFactoryCreates()
		{
			var cb = CheckBox(true);
			Assert.NotNull(cb);
			Assert.IsType<Comet.CheckBox>(cb);
		}

		[Fact]
		public void FlyoutViewParameterlessFactory()
		{
			var fv = new FlyoutView();
			Assert.NotNull(fv);
			Assert.IsType<Comet.FlyoutView>(fv);
		}

		[Fact]
		public void VStackFactoryCreatesVStack()
		{
			var child1 = Text("Child1");
			var child2 = Text("Child2");
			var stack = VStack(child1, child2);
			Assert.NotNull(stack);
			Assert.IsType<Comet.VStack>(stack);
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void HStackFactoryCreatesHStack()
		{
			var child1 = Button("Button1");
			var child2 = Button("Button2");
			var stack = HStack(child1, child2);
			Assert.NotNull(stack);
			Assert.IsType<Comet.HStack>(stack);
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void ZStackFactoryCreatesZStack()
		{
			var child1 = Text("Bottom");
			var child2 = Text("Top");
			var stack = ZStack(child1, child2);
			Assert.NotNull(stack);
			Assert.IsType<Comet.ZStack>(stack);
			Assert.Equal(2, stack.Count);
		}

		[Fact]
		public void GridFactoryCreatesGrid()
		{
			var child1 = Text("Cell1");
			var child2 = Text("Cell2");
			var grid = Grid(child1, child2);
			Assert.NotNull(grid);
			Assert.IsType<Comet.Grid>(grid);
			Assert.Equal(2, grid.Count);
		}

		[Fact]
		public void VStackFactoryWithNoChildrenWorks()
		{
			var stack = VStack();
			Assert.NotNull(stack);
			Assert.IsType<Comet.VStack>(stack);
			Assert.Equal(0, stack.Count);
		}
	}
}
