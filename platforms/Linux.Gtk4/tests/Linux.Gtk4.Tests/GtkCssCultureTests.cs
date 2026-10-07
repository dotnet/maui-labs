using System.Globalization;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

[CollectionDefinition("GTK CSS culture", DisableParallelization = true)]
public sealed class GtkCssCultureCollection;

[Collection("GTK CSS culture")]
public class GtkCssCultureTests
{
	[Theory]
	[InlineData("en-ZA")]
	[InlineData("fr-FR")]
	public void HandlerCss_CommaDecimalCulture_UsesInvariantNumbers(string culture)
	{
		var previousCulture = CultureInfo.CurrentCulture;
		var previousUiCulture = CultureInfo.CurrentUICulture;
		try
		{
			var commaCulture = (CultureInfo)CultureInfo.GetCultureInfo(culture).Clone();
			commaCulture.NumberFormat.NumberDecimalSeparator = ",";
			CultureInfo.CurrentCulture = commaCulture;
			CultureInfo.CurrentUICulture = commaCulture;
			Assert.Equal(",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

			var color = new Color(1f, 0.5f, 0f, 0.25f);
			Assert.Equal("rgba(255,127,0,0.25)", CssTestHandler.FormatColor(color));
			Assert.Equal("letter-spacing: 1.25px;",
				GtkViewHandler<IView, Gtk.Widget>.BuildCharacterSpacingCss(1.25));
			Assert.Equal("letter-spacing: -0.5px;",
				GtkViewHandler<IView, Gtk.Widget>.BuildCharacterSpacingCss(-0.5));
			Assert.Equal("letter-spacing: 0px;",
				GtkViewHandler<IView, Gtk.Widget>.BuildCharacterSpacingCss(0));
			Assert.Equal("border-width: 2.75px; border-style: solid;",
				GtkViewHandler<IView, Gtk.Widget>.BuildStrokeThicknessCss(2.75));
			Assert.Equal("border-width: 0px; border-style: solid;",
				GtkViewHandler<IView, Gtk.Widget>.BuildStrokeThicknessCss(0));
			Assert.Equal("border: 2.75px solid rgba(255,127,0,0.25); ",
				GtkViewHandler<IView, Gtk.Widget>.BuildStrokeCss(2.75, color));
		}
		finally
		{
			CultureInfo.CurrentCulture = previousCulture;
			CultureInfo.CurrentUICulture = previousUiCulture;
		}
	}

	sealed class CssTestHandler() : GtkViewHandler<IView, Gtk.Widget>(ViewMapper)
	{
		public static string FormatColor(Color color) => ToGtkColor(color);
		protected override Gtk.Widget CreatePlatformView() => throw new NotSupportedException();
	}
}
