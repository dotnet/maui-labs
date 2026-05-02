using System;
using System.Collections.Generic;
using Microsoft.Maui.Devices;

namespace Comet
{
	/// <summary>
	/// Functional helper for platform-specific values.
	/// Usage: var padding = OnPlatform.Value(iOS: 20.0, android: 16.0, windows: 24.0);
	/// </summary>
	public static class OnPlatform
	{
		public static T Value<T>(
			T defaultValue = default,
			T iOS = default,
			T android = default,
			T windows = default,
			T macCatalyst = default)
		{
			return new OnPlatform<T>
			{
				Default = defaultValue,
				iOS = iOS,
				Android = android,
				WinUI = windows,
				MacCatalyst = macCatalyst,
			};
		}
	}

	public class OnPlatform<T>
	{
		public T Default { get; set; }
		public T iOS { get; set; }
		public T Android { get; set; }
		public T WinUI { get; set; }
		public T MacCatalyst { get; set; }

		public static implicit operator T(OnPlatform<T> value)
		{
			if (value is null)
				return default;
			return value.GetValue();
		}

		public T GetValue()
		{
#if IOS
			if (!EqualityComparer<T>.Default.Equals(iOS, default))
				return iOS;
#elif ANDROID
			if (!EqualityComparer<T>.Default.Equals(Android, default))
				return Android;
#elif WINDOWS
			if (!EqualityComparer<T>.Default.Equals(WinUI, default))
				return WinUI;
#elif MACCATALYST
			if (!EqualityComparer<T>.Default.Equals(MacCatalyst, default))
				return MacCatalyst;
#endif
			return Default;
		}
	}

	/// <summary>
	/// Functional helper for device idiom-specific values.
	/// Usage: var fontSize = OnIdiom.Value(phone: 14.0, tablet: 18.0, desktop: 16.0);
	/// </summary>
	public static class OnIdiom
	{
		public static T Value<T>(
			T defaultValue = default,
			T phone = default,
			T tablet = default,
			T desktop = default)
		{
			return new OnIdiom<T>
			{
				Default = defaultValue,
				Phone = phone,
				Tablet = tablet,
				Desktop = desktop,
			};
		}
	}

	public class OnIdiom<T>
	{
		public T Default { get; set; }
		public T Phone { get; set; }
		public T Tablet { get; set; }
		public T Desktop { get; set; }

		public static implicit operator T(OnIdiom<T> value)
		{
			if (value is null)
				return default;
			return value.GetValue();
		}

		public T GetValue()
		{
			var idiom = DeviceInfo.Idiom;

			if (idiom == DeviceIdiom.Phone && !EqualityComparer<T>.Default.Equals(Phone, default))
				return Phone;

			if (idiom == DeviceIdiom.Tablet && !EqualityComparer<T>.Default.Equals(Tablet, default))
				return Tablet;

			if (idiom == DeviceIdiom.Desktop && !EqualityComparer<T>.Default.Equals(Desktop, default))
				return Desktop;

			return Default;
		}
	}
}
