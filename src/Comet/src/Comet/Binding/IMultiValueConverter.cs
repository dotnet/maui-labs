using System;
using System.Globalization;

namespace Comet
{
	public interface IMultiValueConverter
	{
		object Convert(object[] values, Type targetType, object parameter, CultureInfo culture);
		object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture);
	}

	public class FuncMultiConverter<TTarget> : IMultiValueConverter
	{
		private readonly Func<object[], TTarget> _convert;
		private readonly Func<TTarget, object[]> _convertBack;

		public FuncMultiConverter(Func<object[], TTarget> convert, Func<TTarget, object[]> convertBack = null)
		{
			_convert = convert ?? throw new ArgumentNullException(nameof(convert));
			_convertBack = convertBack;
		}

		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			return _convert(values);
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
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
