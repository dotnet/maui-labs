using System;
using System.Collections.Generic;
using System.Globalization;

namespace Comet.Converters
{
	/// <summary>
	/// Common value converters for use in Comet views.
	/// These provide common transformations instead of using XAML converters.
	/// </summary>
	public static class ValueConverters
	{
		/// <summary>
		/// Convert bool to inverse bool (NOT)
		/// </summary>
		public static bool Not(bool value) => !value;

		/// <summary>
		/// Convert nullable to bool (has value)
		/// </summary>
		public static bool HasValue<T>(T? value) where T : struct
			=> value.HasValue;

		/// <summary>
		/// Convert empty collection to bool
		/// </summary>
		public static bool IsEmpty<T>(IEnumerable<T> items)
		{
			if (items is null) return true;
			foreach (var _ in items)
				return false;
			return true;
		}

		/// <summary>
		/// Convert empty string to bool
		/// </summary>
		public static bool IsEmpty(string text) => string.IsNullOrWhiteSpace(text);

		/// <summary>
		/// Convert count to bool (has items)
		/// </summary>
		public static bool HasItems<T>(IEnumerable<T> items) => !IsEmpty(items);

		/// <summary>
		/// Convert string to bool (not empty)
		/// </summary>
		public static bool HasText(string text) => !IsEmpty(text);

		/// <summary>
		/// Convert int to bool (greater than zero)
		/// </summary>
		public static bool IsPositive(int value) => value > 0;

		/// <summary>
		/// Convert double to bool (greater than zero)
		/// </summary>
		public static bool IsPositive(double value) => value > 0;

		/// <summary>
		/// Convert enum to bool (matches value)
		/// </summary>
		public static bool Equals<T>(T value, T target) where T : struct
			=> EqualityComparer<T>.Default.Equals(value, target);

		/// <summary>
		/// Convert enum to bool (does not match value)
		/// </summary>
		public static bool NotEquals<T>(T value, T target) where T : struct
			=> !EqualityComparer<T>.Default.Equals(value, target);

		/// <summary>
		/// Format decimal with specific decimal places
		/// </summary>
		public static string FormatDecimal(decimal value, int decimalPlaces = 2)
			=> value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);

		/// <summary>
		/// Format double with specific decimal places
		/// </summary>
		public static string FormatDouble(double value, int decimalPlaces = 2)
			=> value.ToString($"F{decimalPlaces}", CultureInfo.InvariantCulture);

		/// <summary>
		/// Format int as currency
		/// </summary>
		public static string FormatCurrency(int value, string currencySymbol = "$")
			=> $"{currencySymbol}{value.ToString("N0", CultureInfo.InvariantCulture)}";

		/// <summary>
		/// Format decimal as currency
		/// </summary>
		public static string FormatCurrency(decimal value, string currencySymbol = "$")
			=> $"{currencySymbol}{value.ToString("N2", CultureInfo.InvariantCulture)}";

		/// <summary>
		/// Format double as percentage
		/// </summary>
		public static string FormatPercentage(double value, int decimalPlaces = 0)
		{
			var format = "P" + decimalPlaces;
			return value.ToString(format);
		}

		/// <summary>
		/// Convert bytes to human-readable size
		/// </summary>
		public static string FormatFileSize(long bytes)
		{
			string[] sizes = { "B", "KB", "MB", "GB", "TB" };
			double len = bytes;
			int order = 0;
			while (len >= 1024 && order < sizes.Length - 1)
			{
				order++;
				len = len / 1024;
			}
			return $"{len:0.##} {sizes[order]}";
		}

		/// <summary>
		/// Convert DateTime to relative time string (e.g., "2 hours ago")
		/// </summary>
		public static string FormatRelativeTime(DateTime value)
		{
			var span = DateTime.UtcNow - value.ToUniversalTime();
			if (span.TotalSeconds < 60)
				return "just now";
			if (span.TotalMinutes < 60)
				return $"{(int)span.TotalMinutes}m ago";
			if (span.TotalHours < 24)
				return $"{(int)span.TotalHours}h ago";
			if (span.TotalDays < 7)
				return $"{(int)span.TotalDays}d ago";
			return value.ToString("MMM d");
		}

		/// <summary>
		/// Convert DateTime to time only (HH:mm)
		/// </summary>
		public static string FormatTime(DateTime value) => value.ToString("HH:mm");

		/// <summary>
		/// Convert DateTime to date only (MMM d, yyyy)
		/// </summary>
		public static string FormatDate(DateTime value) => value.ToString("MMM d, yyyy");

		/// <summary>
		/// Convert int to ordinal string (1st, 2nd, 3rd, etc.)
		/// </summary>
		public static string FormatOrdinal(int value)
		{
			if (value % 100 >= 11 && value % 100 <= 13)
				return value + "th";
			return (value % 10) switch
			{
				1 => value + "st",
				2 => value + "nd",
				3 => value + "rd",
				_ => value + "th"
			};
		}

		/// <summary>
		/// Pluralize string based on count
		/// </summary>
		public static string Pluralize(int count, string singular, string plural)
			=> count == 1 ? singular : plural;

		/// <summary>
		/// Abbreviate text to max length with ellipsis
		/// </summary>
		public static string Abbreviate(string text, int maxLength = 50)
		{
			if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
				return text;
			return text.Substring(0, maxLength - 3) + "...";
		}

		/// <summary>
		/// Convert null to fallback value
		/// </summary>
		public static T Coalesce<T>(T value, T fallback) where T : class
			=> value ?? fallback;

		/// <summary>
		/// Map enum value to display string
		/// </summary>
		public static string MapEnum(Enum value)
		{
			// Insert spaces before capital letters
			var name = value.ToString();
			var result = string.Empty;
			foreach (var c in name)
			{
				if (char.IsUpper(c) && !string.IsNullOrEmpty(result))
					result += " ";
				result += c;
			}
			return result;
		}
	}

	/// <summary>
	/// Chainable converter builder for complex transformations.
	/// </summary>
	public class ValueConverter<TIn, TOut>
	{
		private Func<TIn, TOut> _convert;

		public ValueConverter(Func<TIn, TOut> convert)
		{
			_convert = convert;
		}

		public ValueConverter<TOut, TNext> Then<TNext>(Func<TOut, TNext> next)
			=> new ValueConverter<TOut, TNext>(v => next(_convert((TIn)(object)v)));

		public TOut Convert(TIn value) => _convert(value);

		public static implicit operator Func<TIn, TOut>(ValueConverter<TIn, TOut> converter)
			=> converter.Convert;
	}
}
