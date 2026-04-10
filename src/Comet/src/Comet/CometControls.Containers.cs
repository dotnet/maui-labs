using System;
using Microsoft.Maui;

namespace Comet
{
	public static partial class CometControls
	{
		public static VStack VStack(params View[] children)
		{
			var stack = new VStack();
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static VStack VStack(float? spacing, params View[] children)
		{
			var stack = new VStack(spacing: spacing);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static VStack VStack(LayoutAlignment alignment, params View[] children)
		{
			var stack = new VStack(alignment);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static VStack VStack(LayoutAlignment alignment, float? spacing, params View[] children)
		{
			var stack = new VStack(alignment, spacing);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static HStack HStack(params View[] children)
		{
			var stack = new HStack();
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static HStack HStack(float? spacing, params View[] children)
		{
			var stack = new HStack(spacing: spacing);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static HStack HStack(LayoutAlignment alignment, params View[] children)
		{
			var stack = new HStack(alignment);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static HStack HStack(LayoutAlignment alignment, float? spacing, params View[] children)
		{
			var stack = new HStack(alignment, spacing);
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static ZStack ZStack(params View[] children)
		{
			var stack = new ZStack();
			foreach (var child in children)
			{
				if (child is not null)
					stack.Add(child);
			}
			return stack;
		}

		public static Grid Grid(params View[] children)
		{
			var grid = new Grid();
			foreach (var child in children)
			{
				if (child is not null)
					grid.Add(child);
			}
			return grid;
		}

		public static Grid Grid(object[] columns, object[] rows, params View[] children)
		{
			var grid = new Grid(columns: columns, rows: rows);
			foreach (var child in children)
			{
				if (child is not null)
					grid.Add(child);
			}
			return grid;
		}

		public static ScrollView ScrollView(View content)
		{
			var scroll = new ScrollView();
			if (content is not null)
				scroll.Add(content);
			return scroll;
		}

		public static ScrollView ScrollView(Orientation orientation, View content)
		{
			var scroll = new ScrollView(orientation);
			if (content is not null)
				scroll.Add(content);
			return scroll;
		}

		public static NavigationView NavigationView(View content)
		{
			var nav = new NavigationView();
			if (content is not null)
				nav.Add(content);
			return nav;
		}

		public static Border Border(View content)
		{
			var border = new Border();
			if (content is not null)
				border.Add(content);
			return border;
		}
	}
}
