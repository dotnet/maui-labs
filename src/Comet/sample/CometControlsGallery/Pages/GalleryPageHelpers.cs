using System;
using System.Collections.Generic;
using Comet;
using Comet.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometControlsGallery.Pages
{
	internal static class GalleryPageHelpers
	{
		static readonly Color PageBackground = Colors.White;

		/// <summary>True when running on a phone-class device.</summary>
		public static bool IsPhone => DeviceInfo.Idiom == DeviceIdiom.Phone;

		public static View Scaffold(string title, params View[] sections) =>
			ScrollView(Orientation.Vertical,
				VStack(IsPhone ? 12f : 16f, sections)
					.Padding(new Thickness(IsPhone ? 16 : 24))
			)
			.Background(PageBackground)
			.Title(title);

		/// <summary>
		/// Button row that stacks vertically on phone, horizontal on desktop.
		/// </summary>
		public static View ButtonRow(float spacing, params View[] views) =>
			IsPhone ? (View)VStack(spacing / 2, views) : HStack(spacing, views);

		public static View Section(string title, params View[] content)
		{
			var views = new List<View> { SectionHeader(title) };
			views.AddRange(content);
			views.Add(Separator());
			return VStack(10, views.ToArray());
		}

		public static View Section(string title, string _description, params View[] content)
		{
			return Section(title, content);
		}

		public static View SectionHeader(string title) =>
			Text(title)
				.FontSize(16)
				.FontWeight(FontWeight.Bold)
				.Color(Colors.CornflowerBlue);

		public static View Separator() =>
			new ShapeView(new Rectangle())
				.Background(Colors.Grey)
				.Frame(height: 1)
				.Opacity(0.3f);

		public static View BodyText(string value) =>
			Text(value)
				.FontSize(14);

		public static View Caption(string value) =>
			Text(value)
				.FontSize(12)
				.Color(Colors.Grey);

		public static View NavButton(string label, Action action) =>
			Button(label, action)
				.ButtonStyle(ButtonStyles.Outlined);

		public static View ColorBlock(string label, Color background, Color textColor, float height = 56) =>
			Border(
				Text(label)
					.FontWeight(FontWeight.Bold)
					.Color(textColor)
			)
			.Background(background)
			.CornerRadius(16)
			.Frame(height: height)
			.Padding(new Thickness(16, 12));
	}
}
