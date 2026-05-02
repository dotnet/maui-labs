using System;
using System.Collections.Generic;
using Comet.Internal;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
namespace Comet
{
	public static class ControlsExtensions
	{
		// TextField / SecureField extensions
		public static T PlaceholderColor<T>(this T view, Color color) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Entry.PlaceholderColor, color, false);
			return view;
		}

		// TextEditor placeholder text
		public static TextEditor Placeholder(this TextEditor view, string placeholder)
		{
			view.SetEnvironment("Placeholder", (object)placeholder, false);
			return view;
		}

		public static T Keyboard<T>(this T view, Microsoft.Maui.Keyboard keyboard) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Entry.Keyboard, keyboard, false);
			return view;
		}

		public static T ReturnType<T>(this T view, ReturnType returnType) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Entry.ReturnType, returnType, false);
			return view;
		}

		public static T IsPassword<T>(this T view, bool isPassword = true) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Entry.IsPassword, isPassword, false);
			return view;
		}

		// Slider extensions
		public static Slider MinimumTrackColor(this Slider view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Slider.ProgressColor, color, false);
			return view;
		}

		public static Slider MaximumTrackColor(this Slider view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Slider.TrackColor, color, false);
			return view;
		}

		public static Slider ThumbColor(this Slider view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Slider.ThumbColor, color, false);
			return view;
		}

		// Toggle/Switch extensions
		public static Toggle OnColor(this Toggle view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Switch.OnColor, color, false);
			return view;
		}

		public static Toggle ThumbColor(this Toggle view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Switch.ThumbColor, color, false);
			return view;
		}

		// Image extensions
		public static Image Aspect(this Image view, Microsoft.Maui.Aspect aspect)
		{
			view.SetEnvironment(EnvironmentKeys.Image.Aspect, aspect, false);
			return view;
		}

		// ProgressBar extensions
		public static ProgressBar ProgressColor(this ProgressBar view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.ProgressBar.ProgressColor, color, false);
			return view;
		}

		public static ProgressBar TrackColor(this ProgressBar view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.ProgressBar.TrackColor, color, false);
			return view;
		}

		// DatePicker extensions
		public static DatePicker Format(this DatePicker view, string format)
		{
			view.SetEnvironment(EnvironmentKeys.DatePicker.Format, (object)format, false);
			return view;
		}

		public static DatePicker TextColor(this DatePicker view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.DatePicker.TextColor, color, false);
			return view;
		}

		// CollectionView fluent extensions
		public static CollectionView<T> Header<T>(this CollectionView<T> view, View header)
		{
			view.Header = header;
			return view;
		}

		public static CollectionView<T> Footer<T>(this CollectionView<T> view, View footer)
		{
			view.Footer = footer;
			return view;
		}

		public static CollectionView<T> EmptyView<T>(this CollectionView<T> view, View emptyView)
		{
			view.EmptyView = emptyView;
			return view;
		}

		public static CollectionView<T> ItemTemplate<T>(this CollectionView<T> view, Func<T, View> template)
		{
			view.ViewFor = template;
			return view;
		}

		public static CollectionView<T> OnRemainingItemsThresholdReached<T>(this CollectionView<T> view, int threshold, Action action)
		{
			view.RemainingItemsThreshold = threshold;
			view.RemainingItemsThresholdReached = action;
			return view;
		}

		// Grid extensions
		public static Grid ColumnSpacing(this Grid view, float spacing)
		{
			var layout = (Layout.GridLayoutManager)view.LayoutManager;
			layout.ColumnSpacing = spacing;
			return view;
		}

		public static Grid RowSpacing(this Grid view, float spacing)
		{
			var layout = (Layout.GridLayoutManager)view.LayoutManager;
			layout.RowSpacing = spacing;
			return view;
		}

		// Button styling extensions
		public static Button CornerRadius(this Button view, int radius)
		{
			view.SetEnvironment(EnvironmentKeys.Button.CornerRadius, radius, false);
			return view;
		}

		public static Button BorderWidth(this Button view, double width)
		{
			view.SetEnvironment(EnvironmentKeys.Button.BorderWidth, width, false);
			return view;
		}

		public static Button BorderColor(this Button view, Color color)
		{
			view.SetEnvironment(EnvironmentKeys.Button.BorderColor, color, false);
			return view;
		}

		// TextField/TextEditor text change callback
		public static T OnTextChanged<T>(this T view, Action<string> callback) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.Entry.TextChanged, callback, false);
			return view;
		}

		// Slider value change callback
		public static Slider OnValueChanged(this Slider view, Action<double> callback)
		{
			view.SetEnvironment(EnvironmentKeys.Slider.ValueChanged, callback, false);
			return view;
		}

		// Toggle/Switch toggled callback
		public static Toggle OnToggled(this Toggle view, Action<bool> callback)
		{
			view.SetEnvironment(EnvironmentKeys.Switch.Toggled, callback, false);
			return view;
		}

		// CheckBox checked change callback
		public static CheckBox OnCheckedChanged(this CheckBox view, Action<bool> callback)
		{
			view.SetEnvironment(EnvironmentKeys.CheckBox.IsCheckedChanged, callback, false);
			return view;
		}

		// Stepper value change callback
		public static Stepper OnValueChanged(this Stepper view, Action<double> callback)
		{
			view.SetEnvironment(EnvironmentKeys.Stepper.ValueChanged, callback, false);
			return view;
		}

		// Picker selection change callback
		public static Picker OnSelectedIndexChanged(this Picker view, Action<int> callback)
		{
			view.SetEnvironment(EnvironmentKeys.Picker.SelectedIndexChanged, callback, false);
			return view;
		}
	}
}
