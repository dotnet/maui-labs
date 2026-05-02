using System;
using Microsoft.Maui;

// ReSharper disable once CheckNamespace
namespace Comet
{
	public static class FontExtensions
	{
		public static Font GetFont(this View view, Font? defaultFont)
		{
			Font font = Font.Default;
			var size = view.GetEnvironment<double?>(EnvironmentKeys.Fonts.Size) ?? defaultFont?.Size ?? font.Size;
			var name = view.GetEnvironment<string>(EnvironmentKeys.Fonts.Family) ?? defaultFont?.Family ?? font.Family;
			var weight = view.GetEnvironment<FontWeight?>(EnvironmentKeys.Fonts.Weight) ?? defaultFont?.Weight ?? Microsoft.Maui.FontWeight.Regular;
			var slant = view.GetEnvironment<FontSlant?>(EnvironmentKeys.Fonts.Slant) ?? Microsoft.Maui.FontSlant.Default;
			if (!string.IsNullOrWhiteSpace(name))
				return Font.OfSize(name, size, weight, slant);
			return Font.SystemFontOfSize(size, weight, slant);

		}

		public static T FontSize<T>(this T view, double value) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Fonts.Size, (object)value, true);
		public static T FontSize<T>(this T view, Func<double> value) where T : View
			=> view.FontSize(value());

		public static T FontWeight<T>(this T view, FontWeight value) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Fonts.Weight, (object)value, true);
		public static T FontWeight<T>(this T view, Func<FontWeight> value) where T : View
			=> view.FontWeight(value());

		public static T FontFamily<T>(this T view, string value) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Fonts.Family, (object)value, true, ControlState.Default);
		public static T FontFamily<T>(this T view, Func<string> value) where T : View
			=> view.FontFamily(value());

		public static T FontSlant<T>(this T view, FontSlant value) where T : View
			=> view.SetEnvironment(EnvironmentKeys.Fonts.Slant, (object)value, true, ControlState.Default);
		public static T FontSlant<T>(this T view, Func<FontSlant> value) where T : View
			 => view.FontSlant(value());
	}
}
