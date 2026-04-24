using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Comet
{

	public static class EnvironmentKeys
	{
		public const string DocumentsFolder = "DocumentsFolder";
		public const string UserFolder = "UserFolder";
		public const string OS = "OS";

		public static class Fonts
		{
			//public const string Font = "Font";
			public const string Size = "Font.Size";
			public const string Weight = "Font.Weight";
			public const string Family = "Font.Family";
			public const string Slant = "Font.Slant";
			//public const string Attributes = "Font.Attributes";
		}

		public static class LineBreakMode
		{
			public const string Mode = "Mode";
		}

		public static class Colors
		{
			public const string Color = "Color";
			public const string Background = nameof(Background);
		}

		public static class Animations
		{
			public const string Animation = "Animation";
		}

		public static class Layout
		{
			public const string Margin = "Layout.Margin";
			public const string Padding = "Layout.Padding";
			public const string Constraints = "Layout.Constraints";
			public const string HorizontalLayoutAlignment = "Layout.HorizontalSizing";
			public const string VerticalLayoutAlignment = "Layout.VerticalSizing";
			public const string FrameConstraints = "Layout.FrameConstraints";
			public const string IgnoreSafeArea = "Layout.IgnoreSafeArea";
		}

		public static class View
		{
			public const string ClipShape = "ClipShape";
			public const string Shadow = "Shadow";
			public const string Title = "Title";
			public const string Border = "Border";
			public const string StyleId = "StyleId";
			public const string AutomationId = nameof(AutomationId);
			public const string Opacity = nameof(Microsoft.Maui.IView.Opacity);
			public const string Key = "View.Key";
			public const string ToolbarItems = "View.ToolbarItems";
		}

		public static class Shape
		{
			public const string LineWidth = "Shape.LineWidth";
			public const string StrokeColor = "Shape.StrokeColor";
			public const string Fill = "Shape.Fill";
			public const string DrawingStyle = "Shape.Style";
		}

		public static class TabView
		{
			public const string Image = "TabView.Item.Image";
			public const string Title = "TabView.Item.Title";
			public const string BarBackgroundColor = "TabView.BarBackgroundColor";
			public const string BarTintColor = "TabView.BarTintColor";
			public const string BarUnselectedColor = "TabView.BarUnselectedColor";
		}

		public static class Text
		{
			public const string HorizontalAlignment = nameof(Microsoft.Maui.ITextAlignment.HorizontalTextAlignment);
			public const string VerticalAlignment = nameof(Microsoft.Maui.ITextAlignment.VerticalTextAlignment);
			public static class Style
			{
				public const string H1 = nameof(H1);
				public const string H2 = nameof(H2);
				public const string H3 = nameof(H3);
				public const string H4 = nameof(H4);
				public const string H5 = nameof(H5);
				public const string H6 = nameof(H6);
				public const string Subtitle1 = nameof(Subtitle1);
				public const string Subtitle2 = nameof(Subtitle2);
				public const string Body1 = nameof(Body1);
				public const string Body2 = nameof(Body2);
				public const string Caption = nameof(Caption);
				public const string Overline = nameof(Overline);
			}
		}
		public static class Navigation
		{
			public const string BackgroundColor = "NavigationBackgroundColor";
			public const string TextColor = "NavigationTextColor";
			public const string PrefersLargeTitles = "NavigationPrefersLargeTitles";
		}
		public static class Slider
		{
			public const string TrackColor = "SliderTrackColor";
			public const string ProgressColor = "SliderProgressColor";
			public const string ThumbColor = "SliderThumbColor";
			public const string ValueChanged = "SliderValueChanged";
		}
		public static class ProgressBar
		{
			public const string TrackColor = "ProgressBarTrackColor";
			public const string ProgressColor = "ProgressBarProgressColor";
		}
		public static class Entry
		{
			public const string PlaceholderColor = "EntryPlaceholderColor";
			public const string CursorColor = "EntryCursorColor";
			public const string Keyboard = "EntryKeyboard";
			public const string ReturnType = "EntryReturnType";
			public const string IsPassword = "EntryIsPassword";
			public const string TextChanged = "EntryTextChanged";
		}
		public static class Switch
		{
			public const string OnColor = "SwitchOnColor";
			public const string ThumbColor = "SwitchThumbColor";
			public const string Toggled = "SwitchToggled";
		}
		public static class CheckBox
		{
			public const string IsCheckedChanged = "CheckBoxIsCheckedChanged";
		}
		public static class Stepper
		{
			public const string ValueChanged = "StepperValueChanged";
		}
		public static class Picker
		{
			public const string SelectedIndexChanged = "PickerSelectedIndexChanged";
		}
		public static class Image
		{
			public const string Aspect = "ImageAspect";
		}
		public static class DatePicker
		{
			public const string Format = "DatePickerFormat";
			public const string TextColor = "DatePickerTextColor";
		}
		public static class Editor
		{
			public const string PlaceholderColor = "EditorPlaceholderColor";
			public const string Placeholder = "EditorPlaceholder";
		}
		public static class Button
		{
			public const string CornerRadius = "ButtonCornerRadius";
			public const string BorderWidth = "ButtonBorderWidth";
			public const string BorderColor = "ButtonBorderColor";
		}

		public static class ThemeColor
		{
			public const string Primary = "Theme.Primary";
			public const string OnPrimary = "Theme.OnPrimary";
			public const string PrimaryContainer = "Theme.PrimaryContainer";
			public const string OnPrimaryContainer = "Theme.OnPrimaryContainer";

			public const string Secondary = "Theme.Secondary";
			public const string OnSecondary = "Theme.OnSecondary";
			public const string SecondaryContainer = "Theme.SecondaryContainer";
			public const string OnSecondaryContainer = "Theme.OnSecondaryContainer";

			public const string Tertiary = "Theme.Tertiary";
			public const string OnTertiary = "Theme.OnTertiary";
			public const string TertiaryContainer = "Theme.TertiaryContainer";
			public const string OnTertiaryContainer = "Theme.OnTertiaryContainer";

			public const string Error = "Theme.Error";
			public const string OnError = "Theme.OnError";
			public const string ErrorContainer = "Theme.ErrorContainer";
			public const string OnErrorContainer = "Theme.OnErrorContainer";

			public const string Background = "Theme.Background";
			public const string OnBackground = "Theme.OnBackground";
			public const string Surface = "Theme.Surface";
			public const string OnSurface = "Theme.OnSurface";
			public const string SurfaceVariant = "Theme.SurfaceVariant";
			public const string OnSurfaceVariant = "Theme.OnSurfaceVariant";

			public const string Outline = "Theme.Outline";
			public const string OutlineVariant = "Theme.OutlineVariant";

			public const string InverseSurface = "Theme.InverseSurface";
			public const string InverseOnSurface = "Theme.InverseOnSurface";
			public const string InversePrimary = "Theme.InversePrimary";
		}
	}

	[AttributeUsage(AttributeTargets.Field)]
	public class EnvironmentAttribute : StateAttribute
	{

		public EnvironmentAttribute(string key = null)
		{
			Key = key;
		}

		public string Key { get; }
	}

	class EnvironmentData
	{
		internal Dictionary<string, object> dictionary = new Dictionary<string, object>();

		static readonly Dictionary<string, System.ComponentModel.PropertyChangedEventArgs> _argsCache
			= new Dictionary<string, System.ComponentModel.PropertyChangedEventArgs>();

		static System.ComponentModel.PropertyChangedEventArgs GetCachedArgs(string propertyName)
		{
			if (!_argsCache.TryGetValue(propertyName, out var args))
			{
				args = new System.ComponentModel.PropertyChangedEventArgs(propertyName);
				_argsCache[propertyName] = args;
			}
			return args;
		}

		public event System.ComponentModel.PropertyChangedEventHandler PropertyRead;
		public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

		public EnvironmentData()
		{
			isStatic = true;
		}
		bool isStatic = false;
		public EnvironmentData(ContextualObject contextualObject)
		{
			View = contextualObject as View;
		}

		WeakReference _viewRef;
		public View View
		{
			get => _viewRef?.Target as View;
			private set => _viewRef = new WeakReference(value);
		}

		internal (bool hasValue, object value) GetValueInternal(string propertyName)
		{
			if (string.IsNullOrWhiteSpace(propertyName))
				return (false, null);
			var hasValue = dictionary.TryGetValue(propertyName, out var val);
			return (hasValue, val);
		}

		public T GetValue<T>(string key)
		{
			try
			{
				var value = GetValue(key);
				return (T)value;
			}
			catch
			{
				return default;
			}
		}

		public object GetValue(string key)
		{
			try
			{
				var value = GetValueInternal(key).value;
				return value;
			}
			catch
			{
				return null;
			}
		}

		protected bool SetProperty(object value, string propertyName)
		{
			if (dictionary.TryGetValue(propertyName, out object val))
			{
				if (Equals(val, value))
					return false;
			}

			dictionary[propertyName] = value;
			CallPropertyChanged(propertyName, value);
			return true;
		}

		protected void CallPropertyRead(string propertyName)
		{
			// Track environment reads in the reactive system so that body evaluations
			// running inside a ReactiveScope automatically discover environment dependencies.
			ContextualObject.ReactiveEnv.TrackRead(propertyName);

			PropertyRead?.Invoke(this, GetCachedArgs(propertyName));
		}

		protected void CallPropertyChanged(string propertyName, object value)
		{
			PropertyChanged?.Invoke(this, GetCachedArgs(propertyName));
		}

		public bool SetValue(string key, object value, bool cascades)
		{
			//if Nothing changed, don't send on notifications
			if (!SetProperty(value, key))
				return false;
			if (!cascades)
				return true;

			// Notify the reactive system so views tracking this environment key
			// via ReactiveScope are automatically scheduled for rebuild.
			ContextualObject.ReactiveEnv.SetValue(key, value);

			return true;
		}
		internal void Clear()
		{
			dictionary.Clear();
		}

		internal bool SetPropertyInternal(object value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
		{
			dictionary[propertyName] = value;
			CallPropertyChanged(propertyName, value);
			return true;
		}
	}
}
