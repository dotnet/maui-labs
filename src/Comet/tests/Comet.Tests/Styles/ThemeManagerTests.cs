using System;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests for ThemeManager: global/scoped theme setting, 
	/// parent chain resolution, token bindings (§6).
	/// Written TDD-style against the Style/Theme Spec.
	/// </summary>
	public class ThemeManagerTests : TestBase
	{
		// ================================================================
		// Helpers
		// ================================================================

		static Theme CreateLightTheme()
		{
			return new Theme
			{
				Name = "Light",
				Colors = new ColorTokenSet
				{
					Primary = Color.FromArgb("#512BD4"),
					OnPrimary = Colors.White,
					Surface = Colors.White,
					OnSurface = Colors.Black,
					Background = Color.FromArgb("#FFFBFE"),
					OnBackground = Color.FromArgb("#1C1B1F"),
				},
				Typography = new TypographyTokenSet
				{
					BodyMedium = new FontSpec(14, FontWeight.Regular),
				},
				Spacing = new SpacingTokenSet { Medium = 16 },
				Shapes = new ShapeTokenSet { Medium = 12 },
			};
		}

		static Theme CreateDarkTheme()
		{
			return new Theme
			{
				Name = "Dark",
				Colors = new ColorTokenSet
				{
					Primary = Color.FromArgb("#D0BCFF"),
					OnPrimary = Color.FromArgb("#381E72"),
					Surface = Color.FromArgb("#1C1B1F"),
					OnSurface = Color.FromArgb("#E6E1E5"),
					Background = Color.FromArgb("#1C1B1F"),
					OnBackground = Color.FromArgb("#E6E1E5"),
				},
				Typography = new TypographyTokenSet
				{
					BodyMedium = new FontSpec(14, FontWeight.Regular),
				},
				Spacing = new SpacingTokenSet { Medium = 16 },
				Shapes = new ShapeTokenSet { Medium = 12 },
			};
		}

		// ================================================================
		// ThemeManager.SetTheme / Current() — global (§6.2)
		// ================================================================

		[Fact]
		public void ThemeManager_SetTheme_ChangesGlobalTheme()
		{
			var light = CreateLightTheme();
			ThemeManager.SetTheme(light);

			var current = ThemeManager.Current();
			Assert.Equal(light, current);
		}

		[Fact]
		public void ThemeManager_SetTheme_SwitchBetweenThemes()
		{
			var light = CreateLightTheme();
			var dark = CreateDarkTheme();

			ThemeManager.SetTheme(light);
			Assert.Equal("Light", ThemeManager.Current().Name);

			ThemeManager.SetTheme(dark);
			Assert.Equal("Dark", ThemeManager.Current().Name);
		}

		[Fact]
		public void ThemeManager_Current_ReturnsFallbackWhenNoThemeSet()
		{
			// After reset, should return a default (Defaults.Light)
			ResetComet();
			var current = ThemeManager.Current();
			Assert.NotNull(current);
		}

		// ================================================================
		// ThemeManager.Current(view) — scoped resolution (§6.2)
		// ================================================================

		[Fact]
		public void ThemeManager_CurrentView_ReturnsGlobalTheme()
		{
			var light = CreateLightTheme();
			ThemeManager.SetTheme(light);

			var view = new Text("Hello");
			var theme = ThemeManager.Current(view);
			Assert.Equal(light, theme);
		}

		[Fact]
		public void ThemeManager_ScopedTheme_FoundByChildView()
		{
			ResetComet();
			var globalTheme = CreateLightTheme();
			var scopedTheme = CreateDarkTheme();
			ThemeManager.SetTheme(globalTheme);

			Text child = null;
			var parent = new View
			{
				Body = () => new VStack
				{
					(child = new Text("Hello"))
				}.UseTheme(scopedTheme)
			};

			var handler = parent.SetViewHandlerToGeneric();

			var resolved = ThemeManager.Current(child);
			Assert.Equal("Dark", resolved.Name);
		}

		[Fact]
		public void ThemeManager_ScopedTheme_DoesNotAffectSiblings()
		{
			ResetComet();
			var globalTheme = CreateLightTheme();
			var scopedTheme = CreateDarkTheme();
			ThemeManager.SetTheme(globalTheme);

			Text scopedChild = null;
			Text unscopedSibling = null;

			var parent = new View
			{
				Body = () => new VStack
				{
					new VStack
					{
						(scopedChild = new Text("Scoped"))
					}.UseTheme(scopedTheme),
					(unscopedSibling = new Text("Global"))
				}
			};

			var handler = parent.SetViewHandlerToGeneric();

			Assert.Equal("Dark", ThemeManager.Current(scopedChild).Name);
			Assert.Equal("Light", ThemeManager.Current(unscopedSibling).Name);
		}

		[Fact]
		public void ThemeManager_ScopedTheme_DoesNotAffectParent()
		{
			ResetComet();
			var globalTheme = CreateLightTheme();
			var scopedTheme = CreateDarkTheme();
			ThemeManager.SetTheme(globalTheme);

			VStack parentContainer = null;

			var root = new View
			{
				Body = () => (parentContainer = new VStack
				{
					new VStack
					{
						new Text("Hello")
					}.UseTheme(scopedTheme)
				})
			};

			var handler = root.SetViewHandlerToGeneric();

			Assert.Equal("Light", ThemeManager.Current(parentContainer).Name);
		}

		// ================================================================
		// .Theme() extension — scoped override (§6.2)
		// ================================================================

		[Fact]
		public void ThemeExtension_SetsEnvironmentOnView()
		{
			var theme = CreateDarkTheme();
			var view = new VStack { new Text("Hello") };

			view.UseTheme(theme);

			// The theme should be stored in the view's environment
			var resolved = ThemeManager.Current(view);
			Assert.Equal("Dark", resolved.Name);
		}

		[Fact]
		public void ThemeExtension_IsFluent()
		{
			var theme = CreateDarkTheme();
			var stack = new VStack { new Text("Hello") }.UseTheme(theme);

			Assert.IsType<VStack>(stack);
		}

		// ================================================================
		// Token resolution through binding updates (§6.3)
		// ================================================================

		[Fact]
		public void Token_ResolvesFromActiveGlobalTheme()
		{
			var light = CreateLightTheme();
			ThemeManager.SetTheme(light);

			var token = new Token<Color>("theme.color.primary", "Primary")
			{
				Resolver = t => t.Colors.Primary
			};

			var resolved = token.Resolve(ThemeManager.Current());
			Assert.Equal(Color.FromArgb("#512BD4"), resolved);
		}

		[Fact]
		public void Token_ResolvesAfterThemeSwitch()
		{
			var light = CreateLightTheme();
			var dark = CreateDarkTheme();

			ThemeManager.SetTheme(light);
			var token = new Token<Color>("theme.color.primary", "Primary")
			{
				Resolver = t => t.Colors.Primary
			};

			Assert.Equal(Color.FromArgb("#512BD4"), token.Resolve(ThemeManager.Current()));

			ThemeManager.SetTheme(dark);
			Assert.Equal(Color.FromArgb("#D0BCFF"), token.Resolve(ThemeManager.Current()));
		}
	}
}
