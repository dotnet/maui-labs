using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using Microsoft.Maui.Platforms.Linux.Gtk4.Handlers;
using System.Reflection;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

public class GtkCssStylesTests
{
	[Fact]
	public void LegacyCssHelpers_KeepOriginalBinarySignatures()
	{
		var handlerType = typeof(GtkViewHandler<IView, Gtk.Widget>);
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

		Assert.NotNull(handlerType.GetMethod("ApplyCss", flags, [typeof(Gtk.Widget), typeof(string)]));
		Assert.NotNull(handlerType.GetMethod("ApplyCssWithSelector", flags, [typeof(Gtk.Widget), typeof(string), typeof(string)]));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Update_LabelAndButtonProperties_PreservesEveryFragment(bool reverse)
	{
		var styles = new GtkCssStyles();
		(string Property, string Css)[] fragments =
		[
			("MapBackground", "background-color: white; background-image: none;"),
			("MapTextColor", "color: red;"),
			("MapFont", "font-size: 32px; font-family: \"Open Sans\";"),
			("MapPadding", "padding: 4px 8px;"),
			("MapTextDecorations", "text-decoration: underline;"),
			("MapCharacterSpacing", "letter-spacing: 0px;"),
			("MapLineHeight", "line-height: 1.5;"),
			("MapStrokeThickness", "border-width: 2px; border-style: solid;"),
			("MapShadow", "box-shadow: 2px 2px 4px black;")
		];

		var css = string.Empty;
		foreach (var fragment in reverse ? fragments.Reverse() : fragments)
			css = styles.Update(1, "*", fragment.Property, fragment.Css);

		foreach (var fragment in fragments)
			Assert.Contains($"* {{ {fragment.Css} }}", css);
		Assert.Equal(fragments.Length, css.Split('\n').Length);
	}

	[Fact]
	public void Update_FontChange_ReplacesEntireFragmentWithoutLeavingOldAttributes()
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "MapFont", "font-size: 32px; font-family: \"Open Sans\"; font-weight: 700; font-style: italic;");
		styles.Update(1, "*", "MapTextColor", "color: red;");

		var css = styles.Update(1, "*", "MapFont", "font-size: 24px;");

		Assert.Contains("* { font-size: 24px; }", css);
		Assert.Contains("* { color: red; }", css);
		Assert.DoesNotContain("32px", css);
		Assert.DoesNotContain("font-family", css);
		Assert.DoesNotContain("font-weight", css);
		Assert.DoesNotContain("font-style", css);
		Assert.Equal(2, css.Split('\n').Length);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	public void Update_Reset_RemovesOnlyThatProperty(string? reset)
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "MapTextColor", "color: red;");
		styles.Update(1, "*", "MapFont", "font-size: 32px;");

		Assert.Equal("* { font-size: 32px; }", styles.Update(1, "*", "MapTextColor", reset));
		Assert.Equal(string.Empty, styles.Update(1, "*", "MapFont", reset));
		Assert.Equal("* { color: blue; }", styles.Update(1, "*", "MapTextColor", "color: blue;"));
	}

	[Fact]
	public void Update_Selectors_KeepIndependentRulesAndCanResetOne()
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "MapColor", "color: red;");
		styles.Update(1, "* > text > placeholder", "MapColor", "color: gray;");
		var css = styles.Update(1, "* > image:last-child", "MapColor", "color: blue;");

		Assert.Contains("* { color: red; }", css);
		Assert.Contains("* > text > placeholder { color: gray; }", css);
		Assert.Contains("* > image:last-child { color: blue; }", css);

		css = styles.Update(1, "* > text > placeholder", "MapColor", null);
		Assert.DoesNotContain("placeholder", css);
		Assert.Contains("* { color: red; }", css);
		Assert.Contains("* > image:last-child { color: blue; }", css);
	}

	[Fact]
	public void Update_ChildWidgets_DoNotShareStylesEvenForTheSameMapper()
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "UpdateNavBarStyle", "background-color: white;");
		styles.Update(2, "*", "UpdateNavBarStyle", "color: red;");
		styles.Update(3, "*", "UpdateNavBarStyle", "color: blue;");

		Assert.Equal("* { color: green; }", styles.Update(2, "*", "UpdateNavBarStyle", "color: green;"));
		Assert.Equal(string.Empty, styles.Update(3, "*", "UpdateNavBarStyle", null));
		Assert.Equal("* { background-color: white; }", styles.Update(1, "*", "Unused", null));
	}

	[Fact]
	public void Update_CssValuesWithSeparators_AreNotParsedAsDeclarations()
	{
		var styles = new GtkCssStyles();
		const string font = "font-family: \"Family; with: punctuation\"; font-size: 24px;";
		styles.Update(1, "*", "MapFont", font);

		Assert.Contains(font, styles.Update(1, "*", "MapTextColor", "color: red;"));
		Assert.DoesNotContain("font-family", styles.Update(1, "*", "MapFont", "font-size: 32px;"));
	}

	[Fact]
	public void Update_OverlappingDeclarations_LatestMapperWinsWithoutLosingOtherRules()
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "MapCornerRadius", "border-radius: 10px;");
		styles.Update(1, "*", "MapClip", "border-radius: 50%;");
		styles.Update(1, "*", "MapTextColor", "color: red;");

		var css = styles.Update(1, "*", "MapCornerRadius", "border-radius: 20px;");

		Assert.EndsWith("* { border-radius: 20px; }", css);
		Assert.Contains("* { border-radius: 50%; }", css);
		Assert.Contains("* { color: red; }", css);
		Assert.DoesNotContain("10px", css);

		css = styles.Update(1, "*", "MapClip", "border-radius: 25%;");
		Assert.EndsWith("* { border-radius: 25%; }", css);
		Assert.DoesNotContain("50%", css);
	}

	[Fact]
	public void Clear_DisconnectAndReconnect_DoesNotRetainAnyWidgetStyles()
	{
		var styles = new GtkCssStyles();
		styles.Update(1, "*", "MapFont", "font-size: 32px;");
		styles.Update(2, "*", "MapTextColor", "color: red;");

		styles.Clear();

		Assert.Equal("* { letter-spacing: 0px; }", styles.Update(1, "*", "MapCharacterSpacing", "letter-spacing: 0px;"));
		Assert.Equal(string.Empty, styles.Update(2, "*", "Unused", null));
	}
}
