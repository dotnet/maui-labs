using System;
using Microsoft.Maui;

namespace Comet
{
	public static partial class CometControls
	{
		// Spacer
		public static Spacer Spacer() => new Spacer();

		// Image
		public static Image Image() => new Image();
		public static Image Image(string source) => new Image(source);
		public static Image Image(Func<string> source) => new Image(source);
		public static Image Image(IImageSource imageSource) => new Image(imageSource);
		public static Image Image(Func<IImageSource> imageSource) => new Image(imageSource);

		// TabView
		public static TabView TabView() => new TabView();

		public static TabView TabView(params (string title, View content)[] tabs)
		{
			var tv = new TabView();
			foreach (var (title, content) in tabs)
				tv.AddTab(title, content);
			return tv;
		}

		public static TabView TabView(View first, params View[] rest)
		{
			var tv = new TabView();
			if (first is not null)
				tv.Add(first);
			foreach (var child in rest)
			{
				if (child is not null)
					tv.Add(child);
			}
			return tv;
		}

		// Picker
		public static Picker Picker(int selectedIndex, params string[] items) => new Picker(selectedIndex, items);
		public static Picker Picker(params string[] items) => new Picker(0, items);

		// ContentView
		public static ContentView ContentView(View content)
		{
			var cv = new ContentView();
			if (content is not null)
				cv.Add(content);
			return cv;
		}
	}
}
