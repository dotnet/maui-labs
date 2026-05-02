using System;
using System.Globalization;

namespace Comet
{
	public interface IValueConverter
	{
		object Convert(object value, Type targetType, object parameter, CultureInfo culture);
		object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture);
	}

	public class FuncConverter<TSource, TTarget> : IValueConverter
	{
		private readonly Func<TSource, TTarget> _convert;
		private readonly Func<TTarget, TSource> _convertBack;

		public FuncConverter(Func<TSource, TTarget> convert, Func<TTarget, TSource> convertBack = null)
		{
			_convert = convert ?? throw new ArgumentNullException(nameof(convert));
			_convertBack = convertBack;
		}

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is null)
				return _convert(default);
			if (value is TSource sourceValue)
				return _convert(sourceValue);

			throw new ArgumentException($"Value must be of type {typeof(TSource).Name}", nameof(value));
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (_convertBack is null)
				throw new NotSupportedException("ConvertBack is not supported for this converter");

			if (value is null)
				return _convertBack(default);
			if (value is TTarget targetValue)
				return _convertBack(targetValue);

			throw new ArgumentException($"Value must be of type {typeof(TTarget).Name}", nameof(value));
		}
	}
}
