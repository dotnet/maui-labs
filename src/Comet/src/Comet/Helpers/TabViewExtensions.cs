using System;
using Microsoft.Maui.Graphics;
namespace Comet
{
	public static class TabViewExtensions
	{
		public static T TabIcon<T>(this T view, string image) where T : View
			=> view.SetEnvironment(EnvironmentKeys.TabView.Image, (object)image);
		public static T TabIcon<T>(this T view, Func<string> image) where T : View
			=> view.TabIcon(image());

		public static T TabText<T>(this T view, string text) where T : View
			=> view.SetEnvironment(EnvironmentKeys.TabView.Title, (object)text);
		public static T TabText<T>(this T view, Func<string> text) where T : View
			=> view.TabText(text());

		public static T Tab<T>(this T view, string text, string image) where T : View
			=> view.TabIcon(image).TabText(text);
		public static T Tab<T>(this T view, Func<string> text, Func<string> image) where T : View
			=> view.TabIcon(image()).TabText(text());

		public static TabView TabBarBackgroundColor(this TabView view, Color color)
			=> (TabView)view.SetEnvironment(EnvironmentKeys.TabView.BarBackgroundColor, color, false);
		public static TabView TabBarTintColor(this TabView view, Color color)
			=> (TabView)view.SetEnvironment(EnvironmentKeys.TabView.BarTintColor, color, false);
		public static TabView TabBarUnselectedColor(this TabView view, Color color)
			=> (TabView)view.SetEnvironment(EnvironmentKeys.TabView.BarUnselectedColor, color, false);
	}
}
