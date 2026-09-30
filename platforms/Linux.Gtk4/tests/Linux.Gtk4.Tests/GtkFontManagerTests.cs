using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class GtkFontManagerTests
{
	[Theory]
	[InlineData("en-US", 32, "32")]
	[InlineData("fr-FR", 24.5, "24.5")]
	public void BuildFontCss_ManagerAndFallback_UseInvariantPixelSizes(string culture, double size, string expected)
	{
		var previousCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
			var font = Font.OfSize("Open Sans", size);
			var manager = new GtkFontManager(new EmptyFontRegistry(), NullLogger<GtkFontManager>.Instance);

			foreach (var css in new[] { manager.BuildFontCss(font), GtkFontManager.BuildFallbackFontCss(font) })
			{
				Assert.Contains($"font-size: {expected}px;", css);
				Assert.Contains("font-family: \"Open Sans\";", css);
				Assert.DoesNotContain("pt;", css);
			}
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
		}
	}

	[Fact]
	public void BuildFontCss_DefaultFont_ReturnsEmptyFragmentForReset()
	{
		var manager = new GtkFontManager(new EmptyFontRegistry(), NullLogger<GtkFontManager>.Instance);

		Assert.Empty(manager.BuildFontCss(Font.Default));
		Assert.Empty(GtkFontManager.BuildFallbackFontCss(Font.Default));
	}

	sealed class EmptyFontRegistry : IGtkFontRegistry
	{
		public IEnumerable<string> GetAllRegisteredKeys() => [];

		public bool TryGetFontPath(string fontKey, out string fontPath)
		{
			fontPath = string.Empty;
			return false;
		}
	}
}
