using System;
using Comet.Tests.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Regression tests for source-generated controls.
	/// Verifies that Button, Text, TextField, Slider, Toggle, and other
	/// CometGenerate-produced controls still construct and configure correctly.
	/// </summary>
	public class GeneratedControlRegressionTests : TestBase
	{
		// ---- Button ----

		[Fact]
		public void Button_CanBeCreatedWithTextAndAction()
		{
			bool clicked = false;
			var button = new Button("Click me", () => clicked = true);
			Assert.NotNull(button);
			Assert.Equal("Click me", button.Text?.CurrentValue);
		}

		[Fact]
		public void Button_TextBinding_ResolvedCorrectly()
		{
			var button = new Button("Initial");
			Assert.Equal("Initial", button.Text?.CurrentValue);
		}

		[Fact]
		public void Button_NullText_DoesNotThrow()
		{
			var button = new Button((string)null);
			Assert.NotNull(button);
			Assert.Null(button.Text?.CurrentValue);
		}

		[Fact]
		public void Button_ClickAction_CanBeNull()
		{
			var button = new Button("OK", null);
			Assert.NotNull(button);
		}

		// ---- Text (Label) ----

		[Fact]
		public void Text_CanBeCreatedWithString()
		{
			var text = new Text("Hello World");
			Assert.NotNull(text);
			Assert.Equal("Hello World", text.Value?.CurrentValue);
		}

		[Fact]
		public void Text_EmptyString_Works()
		{
			var text = new Text("");
			Assert.Equal("", text.Value?.CurrentValue);
		}

		[Fact]
		public void Text_BindingFunc_Resolves()
		{
			var message = "Dynamic";
			var text = new Text(() => message);
			Assert.NotNull(text);
		}

		// ---- TextField (Entry) ----

		[Fact]
		public void TextField_CanBeCreatedWithTextAndPlaceholder()
		{
			var field = new TextField("value", "Enter text");
			Assert.NotNull(field);
			Assert.Equal("value", field.Text?.CurrentValue);
			Assert.Equal("Enter text", field.Placeholder?.CurrentValue);
		}

		[Fact]
		public void TextField_DefaultConstruction_Works()
		{
			var field = new TextField();
			Assert.NotNull(field);
		}

		[Fact]
		public void TextField_WithCompletedAction()
		{
			bool completed = false;
			var field = new TextField("test", "hint", () => completed = true);
			Assert.NotNull(field);
		}

		// ---- SecureField (Entry with IsPassword) ----

		[Fact]
		public void SecureField_CanBeCreated()
		{
			var field = new SecureField("secret", "Password");
			Assert.NotNull(field);
			Assert.Equal("secret", field.Text?.CurrentValue);
			Assert.Equal("Password", field.Placeholder?.CurrentValue);
		}

		// ---- Slider ----

		[Fact]
		public void Slider_CanBeCreatedWithValue()
		{
			var slider = new Slider(0.5);
			Assert.NotNull(slider);
			Assert.Equal(0.5, slider.Value?.CurrentValue);
		}

		[Fact]
		public void Slider_DefaultMinMax()
		{
			var slider = new Slider(0.5, 0d, 1d);
			Assert.Equal(0.0, slider.Minimum?.CurrentValue);
			Assert.Equal(1.0, slider.Maximum?.CurrentValue);
		}

		[Fact]
		public void Slider_CustomRange()
		{
			var slider = new Slider(50d, 0d, 100d);
			Assert.Equal(50.0, slider.Value?.CurrentValue);
			Assert.Equal(0.0, slider.Minimum?.CurrentValue);
			Assert.Equal(100.0, slider.Maximum?.CurrentValue);
		}

		// ---- Toggle (Switch) ----

		[Fact]
		public void Toggle_CanBeCreatedWithBool()
		{
			var toggle = new Toggle(true);
			Assert.NotNull(toggle);
			Assert.True(toggle.Value?.CurrentValue);
		}

		[Fact]
		public void Toggle_DefaultFalse()
		{
			var toggle = new Toggle(false);
			Assert.False(toggle.Value?.CurrentValue);
		}

		// ---- ProgressBar ----

		[Fact]
		public void ProgressBar_CanBeCreated()
		{
			var progress = new ProgressBar(0.75);
			Assert.NotNull(progress);
			Assert.Equal(0.75, progress.Value?.CurrentValue);
		}

		// ---- ActivityIndicator ----

		[Fact]
		public void ActivityIndicator_DefaultIsRunning()
		{
			// Parameterless constructor; the CometGenerate DefaultValue
			// is applied when the constructor parameter is used.
			var indicator = new ActivityIndicator(true);
			Assert.NotNull(indicator);
			Assert.True(indicator.IsRunning?.CurrentValue);
		}

		[Fact]
		public void ActivityIndicator_ExplicitlyNotRunning()
		{
			var indicator = new ActivityIndicator(false);
			Assert.False(indicator.IsRunning?.CurrentValue);
		}

		[Fact]
		public void ActivityIndicator_ParameterlessConstruction()
		{
			var indicator = new ActivityIndicator();
			Assert.NotNull(indicator);
		}

		// ---- CheckBox ----

		[Fact]
		public void CheckBox_CanBeCreated()
		{
			var cb = new CheckBox(true);
			Assert.NotNull(cb);
			Assert.True(cb.IsChecked?.CurrentValue);
		}

		// ---- SearchBar ----

		[Fact]
		public void SearchBar_CanBeCreated()
		{
			var bar = new SearchBar("query");
			Assert.NotNull(bar);
			Assert.Equal("query", bar.Text?.CurrentValue);
		}

		// ---- TextEditor ----

		[Fact]
		public void TextEditor_CanBeCreated()
		{
			var editor = new TextEditor("content");
			Assert.NotNull(editor);
			Assert.Equal("content", editor.Text?.CurrentValue);
		}

		// ---- DatePicker ----

		[Fact]
		public void DatePicker_CanBeCreated()
		{
			var date = DateTime.Today;
			var picker = new DatePicker((DateTime?)date);
			Assert.NotNull(picker);
			Assert.Equal(date, picker.Date?.CurrentValue);
		}

		// ---- TimePicker ----

		[Fact]
		public void TimePicker_CanBeCreated()
		{
			var time = TimeSpan.FromHours(14);
			var picker = new TimePicker((TimeSpan?)time);
			Assert.NotNull(picker);
			Assert.Equal(time, picker.Time?.CurrentValue);
		}

		// ---- Stepper ----

		[Fact]
		public void Stepper_CanBeCreated()
		{
			var stepper = new Stepper(5d, 0d, 10d, 1d);
			Assert.NotNull(stepper);
			Assert.Equal(5.0, stepper.Value?.CurrentValue);
			Assert.Equal(0.0, stepper.Minimum?.CurrentValue);
			Assert.Equal(10.0, stepper.Maximum?.CurrentValue);
			Assert.Equal(1.0, stepper.Interval?.CurrentValue);
		}

		// ---- IndicatorView ----

		[Fact]
		public void IndicatorView_CanBeCreated()
		{
			var indicator = new IndicatorView(5);
			Assert.NotNull(indicator);
			Assert.Equal(5, indicator.Count?.CurrentValue);
		}

		// ---- Generated controls inside View body ----

		class ViewWithGeneratedControls : View
		{
			[Body]
			View body() => new VStack
			{
				new Text("Title"),
				new Button("Click", () => { }),
				new TextField("input", "type here"),
				new Slider(0.5, 0d, 1d),
				new Toggle(true),
			};
		}

		[Fact]
		public void GeneratedControls_WorkInsideViewBody()
		{
			var view = new ViewWithGeneratedControls();
			view.SetViewHandlerToGeneric();

			var built = view.BuiltView;
			Assert.NotNull(built);
			Assert.IsType<VStack>(built);

			var stack = (VStack)built;
			Assert.Equal(5, stack.Count);
			Assert.IsType<Text>(stack[0]);
			Assert.IsType<Button>(stack[1]);
			Assert.IsType<TextField>(stack[2]);
			Assert.IsType<Slider>(stack[3]);
			Assert.IsType<Toggle>(stack[4]);
		}

		// ---- Multiple constructions don't interfere ----

		[Fact]
		public void MultipleInstances_AreIndependent()
		{
			var b1 = new Button("First");
			var b2 = new Button("Second");

			Assert.Equal("First", b1.Text?.CurrentValue);
			Assert.Equal("Second", b2.Text?.CurrentValue);
			Assert.NotSame(b1, b2);
		}

		// ---- Verify generated controls are View subclasses ----

		[Fact]
		public void AllGeneratedControls_InheritFromView()
		{
			Assert.IsAssignableFrom<View>(new Button("x"));
			Assert.IsAssignableFrom<View>(new Text("x"));
			Assert.IsAssignableFrom<View>(new TextField());
			Assert.IsAssignableFrom<View>(new SecureField());
			Assert.IsAssignableFrom<View>(new Slider(0d));
			Assert.IsAssignableFrom<View>(new Toggle(false));
			Assert.IsAssignableFrom<View>(new ProgressBar(0d));
			Assert.IsAssignableFrom<View>(new ActivityIndicator());
			Assert.IsAssignableFrom<View>(new CheckBox(false));
			Assert.IsAssignableFrom<View>(new SearchBar(""));
			Assert.IsAssignableFrom<View>(new TextEditor(""));
			Assert.IsAssignableFrom<View>(new Stepper(0d, 0d, 1d, 1d));
		}
	}
}
