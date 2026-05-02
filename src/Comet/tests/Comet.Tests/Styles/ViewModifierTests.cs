using System;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests for ViewModifier, ViewModifier&lt;T&gt;, ComposedModifier,
	/// and ViewModifierExtensions (§3).
	/// Written TDD-style against the Style/Theme Spec.
	/// </summary>
	public class ViewModifierTests : TestBase
	{
		// ================================================================
		// Test helpers — concrete modifier implementations
		// ================================================================

		sealed class BackgroundModifier : ViewModifier
		{
			readonly Color _color;

			public BackgroundModifier(Color color) => _color = color;

			public override View Apply(View view)
			{
				view.Background(new SolidPaint(_color));
				return view;
			}
		}

		sealed class FontSizeModifier : ViewModifier
		{
			readonly double _size;

			public FontSizeModifier(double size) => _size = size;

			public override View Apply(View view)
			{
				view.FontSize(_size);
				return view;
			}
		}

		sealed class TextColorModifier : ViewModifier<Text>
		{
			readonly Color _color;

			public TextColorModifier(Color color) => _color = color;

			public override Text Apply(Text view)
			{
				view.Color(_color);
				return view;
			}
		}

		sealed class ButtonOpacityModifier : ViewModifier<Button>
		{
			readonly double _opacity;

			public ButtonOpacityModifier(double opacity) => _opacity = opacity;

			public override Button Apply(Button view)
			{
				view.Opacity(_opacity);
				return view;
			}
		}

		// ================================================================
		// ViewModifier.Apply (§3.2)
		// ================================================================

		[Fact]
		public void ViewModifier_Apply_ModifiesViewBackground()
		{
			var modifier = new BackgroundModifier(Colors.Red);
			var view = new Text("Hello");

			modifier.Apply(view);

			var bg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			Assert.NotNull(bg);
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Red, solid.Color);
		}

		[Fact]
		public void ViewModifier_Apply_ModifiesViewFontSize()
		{
			var modifier = new FontSizeModifier(24);
			var view = new Text("Hello");

			modifier.Apply(view);

			var size = view.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(24.0, size);
		}

		[Fact]
		public void ViewModifier_Apply_ReturnsTheView()
		{
			var modifier = new BackgroundModifier(Colors.Blue);
			var view = new Text("Hello");

			var result = modifier.Apply(view);

			Assert.Same(view, result);
		}

		// ================================================================
		// ViewModifier<T>.Apply — type filtering (§3.2)
		// ================================================================

		[Fact]
		public void TypedModifier_Apply_AppliesOnlyToCorrectType()
		{
			var modifier = new TextColorModifier(Colors.Green);
			var text = new Text("Hello");

			modifier.Apply(text);

			var color = text.GetEnvironment<Color>(EnvironmentKeys.Colors.Color);
			Assert.Equal(Colors.Green, color);
		}

		[Fact]
		public void TypedModifier_Apply_PassesThroughWrongType()
		{
			var modifier = new TextColorModifier(Colors.Green);
			var button = new Button("Click");

			// Capture color before applying
			var colorBefore = button.GetEnvironment<Color>(EnvironmentKeys.Colors.Color);

			// Apply through the base class Apply(View) method
			var result = ((ViewModifier)modifier).Apply(button);

			Assert.Same(button, result);
			// Button color should not have been changed to Green by the Text modifier
			var colorAfter = button.GetEnvironment<Color>(EnvironmentKeys.Colors.Color);
			Assert.Equal(colorBefore, colorAfter);
			Assert.NotEqual(Colors.Green, colorAfter);
		}

		[Fact]
		public void TypedModifier_ButtonModifier_DoesNotAffectText()
		{
			var modifier = new ButtonOpacityModifier(0.5);
			var text = new Text("Hello");

			var result = ((ViewModifier)modifier).Apply(text);

			Assert.Same(text, result);
		}

		// ================================================================
		// ViewModifier.Empty (§4.8)
		// ================================================================

		[Fact]
		public void ViewModifier_Empty_DoesNothing()
		{
			var view = new Text("Hello");
			var initialBg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));

			ViewModifier.Empty.Apply(view);

			var afterBg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			Assert.Equal(initialBg, afterBg);
		}

		[Fact]
		public void ViewModifier_Empty_ReturnsView()
		{
			var view = new Text("Hello");
			var result = ViewModifier.Empty.Apply(view);
			Assert.Same(view, result);
		}

		// ================================================================
		// ComposedModifier (§3.7)
		// ================================================================

		[Fact]
		public void ComposedModifier_AppliesBothModifiersInOrder()
		{
			var bgModifier = new BackgroundModifier(Colors.Red);
			var fontModifier = new FontSizeModifier(32);
			var composed = new ComposedModifier(bgModifier, fontModifier);

			var view = new Text("Hello");
			composed.Apply(view);

			var bg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Red, solid.Color);

			var size = view.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(32.0, size);
		}

		[Fact]
		public void ComposedModifier_LastWriterWins()
		{
			// First sets background to Red, second sets background to Blue
			var first = new BackgroundModifier(Colors.Red);
			var second = new BackgroundModifier(Colors.Blue);
			var composed = new ComposedModifier(first, second);

			var view = new Text("Hello");
			composed.Apply(view);

			var bg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Blue, solid.Color);
		}

		// ================================================================
		// .Then() composition (§3.7)
		// ================================================================

		[Fact]
		public void Then_CreatesComposedModifier()
		{
			var first = new BackgroundModifier(Colors.Red);
			var second = new FontSizeModifier(18);

			var composed = first.Then(second);

			Assert.IsType<ComposedModifier>(composed);
		}

		[Fact]
		public void Then_ComposedModifierAppliesBoth()
		{
			var first = new BackgroundModifier(Colors.Green);
			var second = new FontSizeModifier(20);
			var composed = first.Then(second);

			var view = new Text("Hello");
			composed.Apply(view);

			var bg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			Assert.NotNull(bg);

			var size = view.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(20.0, size);
		}

		[Fact]
		public void Then_Chaining_AppliesAllThreeModifiers()
		{
			var a = new BackgroundModifier(Colors.Red);
			var b = new FontSizeModifier(14);
			var c = new BackgroundModifier(Colors.Blue);

			// a.Then(b).Then(c) — should produce nested composition
			var composed = a.Then(b).Then(c);

			var view = new Text("Hello");
			composed.Apply(view);

			// c overwrites a's background
			var bg = view.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Blue, solid.Color);

			// b's font size should remain
			var size = view.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(14.0, size);
		}

		// ================================================================
		// .Modifier() extension (§3.5)
		// ================================================================

		[Fact]
		public void ModifierExtension_AppliesAndReturnsView()
		{
			var modifier = new BackgroundModifier(Colors.Yellow);
			var text = new Text("Hello");

			var result = text.Modifier(modifier);

			Assert.Same(text, result);
			var bg = text.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Yellow, solid.Color);
		}

		[Fact]
		public void ModifierExtension_IsFluent()
		{
			var text = new Text("Hello")
				.Modifier(new BackgroundModifier(Colors.Red))
				.Modifier(new FontSizeModifier(16));

			var bg = text.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			Assert.NotNull(bg);

			var size = text.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(16.0, size);
		}

		[Fact]
		public void ModifierExtension_ParamsAppliesMultipleInOrder()
		{
			var text = new Text("Hello");

			text.Modifier(
				new BackgroundModifier(Colors.Red),
				new FontSizeModifier(22),
				new BackgroundModifier(Colors.Green)
			);

			// Last BackgroundModifier wins
			var bg = text.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var solid = Assert.IsType<SolidPaint>(bg);
			Assert.Equal(Colors.Green, solid.Color);

			var size = text.GetEnvironment<double>(EnvironmentKeys.Fonts.Size);
			Assert.Equal(22.0, size);
		}

		// ================================================================
		// Static modifier instances — singleton pattern (§3.6)
		// ================================================================

		[Fact]
		public void StaticModifier_CanBeReusedAcrossMultipleViews()
		{
			var modifier = new BackgroundModifier(Colors.Purple);

			var text1 = new Text("A");
			var text2 = new Text("B");

			modifier.Apply(text1);
			modifier.Apply(text2);

			var bg1 = text1.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));
			var bg2 = text2.GetEnvironment<Paint>(nameof(Microsoft.Maui.IView.Background));

			var solid1 = Assert.IsType<SolidPaint>(bg1);
			var solid2 = Assert.IsType<SolidPaint>(bg2);
			Assert.Equal(Colors.Purple, solid1.Color);
			Assert.Equal(Colors.Purple, solid2.Color);
		}
	}
}
