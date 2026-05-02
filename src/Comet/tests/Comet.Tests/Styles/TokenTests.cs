using System;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Comet.Tests
{
	/// <summary>
	/// Tests for Token&lt;T&gt; (§8), ColorTokens, TypographyTokens, SpacingTokens, ShapeTokens.
	/// Written TDD-style against the Style/Theme Spec.
	/// </summary>
	public class TokenTests : TestBase
	{
		// ================================================================
		// Token<T> creation and properties (§8.2)
		// ================================================================

		[Fact]
		public void Token_Creation_StoresKeyAndName()
		{
			var token = new Token<Color>("theme.color.primary", "Primary");

			Assert.Equal("theme.color.primary", token.Key);
			Assert.Equal("Primary", token.Name);
		}

		[Fact]
		public void Token_Creation_DefaultNameMatchesKey()
		{
			var token = new Token<double>("theme.spacing.sm");

			Assert.Equal("theme.spacing.sm", token.Key);
			Assert.Equal("theme.spacing.sm", token.Name);
		}

		[Fact]
		public void Token_Creation_StoresDefaultValue()
		{
			var token = new Token<double>("theme.spacing.md", "Medium", 16);

			Assert.Equal(16.0, token.DefaultValue);
		}

		[Fact]
		public void Token_DefaultValue_IsDefaultWhenNotSpecified()
		{
			var token = new Token<Color>("theme.color.primary", "Primary");

			Assert.Equal(default(Color), token.DefaultValue);
		}

		// ================================================================
		// Token<T>.Resolve (§8.4)
		// ================================================================

		[Fact]
		public void Token_Resolve_ReturnsCorrectColorFromTheme()
		{
			var expectedColor = Color.FromArgb("#512BD4");
			var theme = new Theme
			{
				Name = "TestLight",
				Colors = new ColorTokenSet { Primary = expectedColor },
				Typography = new TypographyTokenSet(),
				Spacing = new SpacingTokenSet(),
				Shapes = new ShapeTokenSet(),
			};

			var token = new Token<Color>("theme.color.primary", "Primary")
			{
				Resolver = t => t.Colors.Primary
			};

			var result = token.Resolve(theme);
			Assert.Equal(expectedColor, result);
		}

		[Fact]
		public void Token_Resolve_ReturnsDefaultWhenNoResolver()
		{
			var defaultColor = Colors.Red;
			var token = new Token<Color>("test.key", "Test", defaultColor);

			var theme = new Theme
			{
				Name = "TestTheme",
				Colors = new ColorTokenSet(),
				Typography = new TypographyTokenSet(),
				Spacing = new SpacingTokenSet(),
				Shapes = new ShapeTokenSet(),
			};

			var result = token.Resolve(theme);
			Assert.Equal(defaultColor, result);
		}

		[Fact]
		public void Token_Resolve_ReturnsSpacingFromTheme()
		{
			var theme = new Theme
			{
				Name = "TestTheme",
				Colors = new ColorTokenSet(),
				Typography = new TypographyTokenSet(),
				Spacing = new SpacingTokenSet { Medium = 16 },
				Shapes = new ShapeTokenSet(),
			};

			var token = new Token<double>("theme.spacing.md", "Medium", 0)
			{
				Resolver = t => t.Spacing.Medium
			};

			Assert.Equal(16.0, token.Resolve(theme));
		}

		[Fact]
		public void Token_Resolve_ReturnsShapeFromTheme()
		{
			var theme = new Theme
			{
				Name = "TestTheme",
				Colors = new ColorTokenSet(),
				Typography = new TypographyTokenSet(),
				Spacing = new SpacingTokenSet(),
				Shapes = new ShapeTokenSet { Full = 9999 },
			};

			var token = new Token<double>("theme.shape.full", "Full", 0)
			{
				Resolver = t => t.Shapes.Full
			};

			Assert.Equal(9999.0, token.Resolve(theme));
		}

		// ================================================================
		// Token<T> implicit conversion to Binding<T> (§8.2)
		// ================================================================

		// ================================================================
		// Token<T>.Map projection (§8.2)
		// ================================================================

		[Fact]
		public void Token_Map_ProjectsValueCorrectly()
		{
			var fontSpec = new FontSpec(24, FontWeight.Bold, "Helvetica", 1.5, 0.5);
			var theme = new Theme
			{
				Name = "TestTheme",
				Colors = new ColorTokenSet(),
				Typography = new TypographyTokenSet { DisplayLarge = fontSpec },
				Spacing = new SpacingTokenSet(),
				Shapes = new ShapeTokenSet(),
			};

			ThemeManager.SetTheme(theme);

			var token = new Token<FontSpec>("theme.type.displayLarge", "Display Large")
			{
				Resolver = t => t.Typography.DisplayLarge
			};

			var sizeBinding = token.Map(f => f.Size);
			Assert.NotNull(sizeBinding);
		}

		// ================================================================
		// ColorTokens static fields (§8.3)
		// ================================================================

		[Fact]
		public void ColorTokens_Primary_IsNotNull()
		{
			Assert.NotNull(ColorTokens.Primary);
			Assert.Equal("theme.color.primary", ColorTokens.Primary.Key);
		}

		[Fact]
		public void ColorTokens_OnPrimary_IsNotNull()
		{
			Assert.NotNull(ColorTokens.OnPrimary);
			Assert.Equal("theme.color.onPrimary", ColorTokens.OnPrimary.Key);
		}

		[Fact]
		public void ColorTokens_Surface_IsNotNull()
		{
			Assert.NotNull(ColorTokens.Surface);
			Assert.Equal("theme.color.surface", ColorTokens.Surface.Key);
		}

		[Fact]
		public void ColorTokens_Error_IsNotNull()
		{
			Assert.NotNull(ColorTokens.Error);
			Assert.Equal("theme.color.error", ColorTokens.Error.Key);
		}

		[Fact]
		public void ColorTokens_Background_IsNotNull()
		{
			Assert.NotNull(ColorTokens.Background);
			Assert.Equal("theme.color.background", ColorTokens.Background.Key);
		}

		[Fact]
		public void ColorTokens_Outline_IsNotNull()
		{
			Assert.NotNull(ColorTokens.Outline);
			Assert.Equal("theme.color.outline", ColorTokens.Outline.Key);
		}

		[Fact]
		public void ColorTokens_AllTokensHaveUniqueKeys()
		{
			var tokens = new[]
			{
				ColorTokens.Primary,
				ColorTokens.OnPrimary,
				ColorTokens.PrimaryContainer,
				ColorTokens.OnPrimaryContainer,
				ColorTokens.Secondary,
				ColorTokens.OnSecondary,
				ColorTokens.SecondaryContainer,
				ColorTokens.OnSecondaryContainer,
				ColorTokens.Surface,
				ColorTokens.OnSurface,
				ColorTokens.SurfaceVariant,
				ColorTokens.OnSurfaceVariant,
				ColorTokens.SurfaceContainer,
				ColorTokens.Background,
				ColorTokens.OnBackground,
				ColorTokens.Error,
				ColorTokens.OnError,
				ColorTokens.ErrorContainer,
				ColorTokens.OnErrorContainer,
				ColorTokens.Outline,
				ColorTokens.OutlineVariant,
				ColorTokens.InverseSurface,
				ColorTokens.InverseOnSurface,
				ColorTokens.InversePrimary,
			};

			var keys = new System.Collections.Generic.HashSet<string>();
			foreach (var token in tokens)
			{
				Assert.True(keys.Add(token.Key),
					$"Duplicate token key: {token.Key}");
			}
		}

		// ================================================================
		// TypographyTokens static fields (§8.3)
		// ================================================================

		[Fact]
		public void TypographyTokens_DisplayLarge_IsNotNull()
		{
			Assert.NotNull(TypographyTokens.DisplayLarge);
			Assert.Equal("theme.type.displayLarge", TypographyTokens.DisplayLarge.Key);
		}

		[Fact]
		public void TypographyTokens_BodyMedium_IsNotNull()
		{
			Assert.NotNull(TypographyTokens.BodyMedium);
			Assert.Equal("theme.type.bodyMedium", TypographyTokens.BodyMedium.Key);
		}

		[Fact]
		public void TypographyTokens_LabelSmall_IsNotNull()
		{
			Assert.NotNull(TypographyTokens.LabelSmall);
			Assert.Equal("theme.type.labelSmall", TypographyTokens.LabelSmall.Key);
		}

		[Fact]
		public void TypographyTokens_AllTokensHaveUniqueKeys()
		{
			var tokens = new[]
			{
				TypographyTokens.DisplayLarge,
				TypographyTokens.DisplayMedium,
				TypographyTokens.DisplaySmall,
				TypographyTokens.HeadlineLarge,
				TypographyTokens.HeadlineMedium,
				TypographyTokens.HeadlineSmall,
				TypographyTokens.TitleLarge,
				TypographyTokens.TitleMedium,
				TypographyTokens.TitleSmall,
				TypographyTokens.BodyLarge,
				TypographyTokens.BodyMedium,
				TypographyTokens.BodySmall,
				TypographyTokens.LabelLarge,
				TypographyTokens.LabelMedium,
				TypographyTokens.LabelSmall,
			};

			var keys = new System.Collections.Generic.HashSet<string>();
			foreach (var token in tokens)
			{
				Assert.True(keys.Add(token.Key),
					$"Duplicate token key: {token.Key}");
			}
		}

		// ================================================================
		// SpacingTokens static fields (§8.3)
		// ================================================================

		[Fact]
		public void SpacingTokens_None_HasDefaultValueZero()
		{
			Assert.NotNull(SpacingTokens.None);
			Assert.Equal(0.0, SpacingTokens.None.DefaultValue);
		}

		[Fact]
		public void SpacingTokens_Medium_HasDefaultValue16()
		{
			Assert.NotNull(SpacingTokens.Medium);
			Assert.Equal(16.0, SpacingTokens.Medium.DefaultValue);
		}

		[Fact]
		public void SpacingTokens_ExtraLarge_HasDefaultValue32()
		{
			Assert.NotNull(SpacingTokens.ExtraLarge);
			Assert.Equal(32.0, SpacingTokens.ExtraLarge.DefaultValue);
		}

		[Fact]
		public void SpacingTokens_AllTokensHaveUniqueKeys()
		{
			var tokens = new[]
			{
				SpacingTokens.None,
				SpacingTokens.ExtraSmall,
				SpacingTokens.Small,
				SpacingTokens.Medium,
				SpacingTokens.Large,
				SpacingTokens.ExtraLarge,
			};

			var keys = new System.Collections.Generic.HashSet<string>();
			foreach (var token in tokens)
			{
				Assert.True(keys.Add(token.Key),
					$"Duplicate token key: {token.Key}");
			}
		}

		// ================================================================
		// ShapeTokens static fields (§8.3)
		// ================================================================

		[Fact]
		public void ShapeTokens_Full_HasDefaultValue9999()
		{
			Assert.NotNull(ShapeTokens.Full);
			Assert.Equal(9999.0, ShapeTokens.Full.DefaultValue);
		}

		[Fact]
		public void ShapeTokens_None_HasDefaultValueZero()
		{
			Assert.NotNull(ShapeTokens.None);
			Assert.Equal(0.0, ShapeTokens.None.DefaultValue);
		}

		[Fact]
		public void ShapeTokens_AllTokensHaveUniqueKeys()
		{
			var tokens = new[]
			{
				ShapeTokens.None,
				ShapeTokens.ExtraSmall,
				ShapeTokens.Small,
				ShapeTokens.Medium,
				ShapeTokens.Large,
				ShapeTokens.ExtraLarge,
				ShapeTokens.Full,
			};

			var keys = new System.Collections.Generic.HashSet<string>();
			foreach (var token in tokens)
			{
				Assert.True(keys.Add(token.Key),
					$"Duplicate token key: {token.Key}");
			}
		}

		// ================================================================
		// FontSpec record struct (§5.3)
		// ================================================================

		[Fact]
		public void FontSpec_CreationWithAllProperties()
		{
			var spec = new FontSpec(16, FontWeight.Regular, "Roboto", 1.5, 0.25);

			Assert.Equal(16.0, spec.Size);
			Assert.Equal(FontWeight.Regular, spec.Weight);
			Assert.Equal("Roboto", spec.Family);
			Assert.Equal(1.5, spec.LineHeight);
			Assert.Equal(0.25, spec.LetterSpacing);
		}

		[Fact]
		public void FontSpec_DefaultFamilyIsNull()
		{
			var spec = new FontSpec(14, FontWeight.Bold);

			Assert.Null(spec.Family);
			Assert.Equal(0.0, spec.LineHeight);
			Assert.Equal(0.0, spec.LetterSpacing);
		}

		[Fact]
		public void FontSpec_ValueEquality()
		{
			var a = new FontSpec(16, FontWeight.Regular, "Roboto", 1.5, 0.25);
			var b = new FontSpec(16, FontWeight.Regular, "Roboto", 1.5, 0.25);

			Assert.Equal(a, b);
		}

		[Fact]
		public void FontSpec_ValueInequality()
		{
			var a = new FontSpec(16, FontWeight.Regular);
			var b = new FontSpec(24, FontWeight.Bold);

			Assert.NotEqual(a, b);
		}
	}
}
