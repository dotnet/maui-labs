using System;
using Microsoft.Maui;

// ReSharper disable once CheckNamespace
namespace Comet
{
	public static partial class TextExtensions
	{
		public static T HorizontalTextAlignment<T>(this T view, TextAlignment? alignment, bool cascades = true) where T : View =>
		view.SetEnvironment(EnvironmentKeys.Text.HorizontalAlignment, (object)alignment, cascades);
		public static T HorizontalTextAlignment<T>(this T view, Func<TextAlignment?> alignment, bool cascades = true) where T : View =>
			view.HorizontalTextAlignment(alignment(), cascades);
		public static T VerticalTextAlignment<T>(this T view, TextAlignment? alignment, bool cascades = true) where T : View =>
		view.SetEnvironment(EnvironmentKeys.Text.VerticalAlignment, (object)alignment, cascades);
		public static T VerticalTextAlignment<T>(this T view, Func<TextAlignment?> alignment, bool cascades = true) where T : View =>
			view.VerticalTextAlignment(alignment(), cascades);


	}
}
