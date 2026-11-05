using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

namespace Comet
{
	internal static class NavigationParameterHelper
	{
		[RequiresUnreferencedCode(
			"Property-based navigation parameters require runtime property metadata. " +
			"Use GoToAsync<TView, TParameters> in trimmed applications.")]
		internal static Task NavigateLegacy(
			CometShell shell,
			string route,
			object parameters)
			=> shell.GoToLegacyCore(
				route,
				parameters,
				() => ToDictionary(parameters));

		internal static Func<View> CreateRouteFactory(
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type pageType)
		{
			var constructor = pageType.GetConstructor(Type.EmptyTypes);
			if (constructor is null)
				throw new ArgumentException(
					$"Route type must expose a public parameterless constructor. Type: {pageType.Name}",
					nameof(pageType));

			return () => constructor.Invoke(null) as View
				?? throw new InvalidOperationException($"Failed to create instance of {pageType.Name}");
		}

		[RequiresUnreferencedCode(
			"Property-based navigation parameters require runtime property metadata. " +
			"Use the generic GoToAsync<TView, TParameters> or Navigate<TView, TParameters> overload in trimmed applications.")]
		public static void Apply(View view, object parameters, Dictionary<string, string> queryParameters = null)
		{
			if (view is null)
				return;

			if (parameters is not null)
				TryApplyProps(view, parameters);

			if (view is not IQueryAttributable)
				return;

			ApplyQueryAttributes(
				view,
				queryParameters is { Count: > 0 } || parameters is null
					? queryParameters
					: ToDictionary(parameters));
		}

		public static void Apply<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			View view,
			TParameters parameters,
			Dictionary<string, string> queryParameters = null)
		{
			if (view is null)
				return;

			if (parameters is not null)
				TryApplyProps(view, parameters);

			if (view is not IQueryAttributable)
				return;

			ApplyQueryAttributes(
				view,
				queryParameters is { Count: > 0 } || parameters is null
					? queryParameters
					: ToDictionary(parameters));
		}

		[RequiresUnreferencedCode(
			"Property-based navigation parameters require runtime property metadata. " +
			"Use the generic GoToAsync<TView, TParameters> or Navigate<TView, TParameters> overload in trimmed applications.")]
		public static string BuildRoute(string route, object parameters)
		{
			if (string.IsNullOrWhiteSpace(route) || parameters is null)
				return route;

			var queryString = ToQueryString(parameters);
			if (string.IsNullOrEmpty(queryString))
				return route;

			var separator = route.Contains("?") ? "&" : "?";
			return $"{route}{separator}{queryString}";
		}

		public static string BuildRoute<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			string route,
			TParameters parameters)
		{
			if (string.IsNullOrWhiteSpace(route) || parameters is null)
				return route;

			var queryString = ToQueryString(parameters);
			if (string.IsNullOrEmpty(queryString))
				return route;

			var separator = route.Contains("?") ? "&" : "?";
			return $"{route}{separator}{queryString}";
		}

		[RequiresUnreferencedCode(
			"Property-based navigation parameters require runtime property metadata. " +
			"Use the generic GoToAsync<TView, TParameters> or Navigate<TView, TParameters> overload in trimmed applications.")]
		public static Dictionary<string, string> ToDictionary(object parameters)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);

			if (parameters is null)
				return values;

			if (parameters is IEnumerable<KeyValuePair<string, string>> stringPairs)
			{
				foreach (var pair in stringPairs)
					Add(values, pair.Key, pair.Value);
				return values;
			}

			if (parameters is IEnumerable<KeyValuePair<string, object>> objectPairs)
			{
				foreach (var pair in objectPairs)
					Add(values, pair.Key, pair.Value);
				return values;
			}

			return ReflectToDictionary(parameters, parameters.GetType(), values);
		}

		public static Dictionary<string, string> ToDictionary<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			TParameters parameters)
		{
			var values = new Dictionary<string, string>(StringComparer.Ordinal);

			if (parameters is null)
				return values;

			if (parameters is IEnumerable<KeyValuePair<string, string>> stringPairs)
			{
				foreach (var pair in stringPairs)
					Add(values, pair.Key, pair.Value);
				return values;
			}

			if (parameters is IEnumerable<KeyValuePair<string, object>> objectPairs)
			{
				foreach (var pair in objectPairs)
					Add(values, pair.Key, pair.Value);
				return values;
			}

			if (parameters.GetType() != typeof(TParameters))
			{
				if (!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
					throw new NotSupportedException(
						$"Query parameter runtime type '{parameters.GetType()}' differs from declared type '{typeof(TParameters)}'. " +
						"Use the concrete runtime type as TParameters or a dictionary when dynamic code is disabled.");

				// Untrimmed compatibility only. Dynamic-code support does not establish
				// metadata preservation; keep the legacy trim warning visible.
				return ToDictionary((object)parameters);
			}

			return ReflectToDictionary(parameters, typeof(TParameters), values);
		}

		static Dictionary<string, string> ReflectToDictionary(
			object parameters,
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type parameterType,
			Dictionary<string, string> values)
		{
			foreach (var property in parameterType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
			{
				if (!property.CanRead || property.GetIndexParameters().Length > 0)
					continue;

				Add(values, property.Name, property.GetValue(parameters));
			}

			return values;
		}

		[RequiresUnreferencedCode(
			"Property-based navigation parameters require runtime property metadata. " +
			"Use the generic GoToAsync<TView, TParameters> or Navigate<TView, TParameters> overload in trimmed applications.")]
		public static string ToQueryString(object parameters)
		{
			var values = ToDictionary(parameters);
			if (values.Count == 0)
				return string.Empty;

			return BuildQueryString(values);
		}

		public static string ToQueryString<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			TParameters parameters)
		{
			var values = ToDictionary(parameters);
			if (values.Count == 0)
				return string.Empty;

			return BuildQueryString(values);
		}

		static string BuildQueryString(Dictionary<string, string> values)
		{
			var encoded = new List<string>(values.Count);
			foreach (var pair in values)
			{
				encoded.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
			}

			return string.Join("&", encoded);
		}

		internal static void ApplyQueryAttributes(View view, Dictionary<string, string> values)
		{
			if (view is IQueryAttributable queryAttributable && values is { Count: > 0 })
				queryAttributable.ApplyQueryAttributes(values);
		}

		static void Add(IDictionary<string, string> values, string key, object value)
		{
			if (string.IsNullOrWhiteSpace(key) || value is null)
				return;

			var stringValue = ConvertToString(value);
			if (stringValue is null)
				return;

			values[key] = stringValue;
		}

		static string ConvertToString(object value)
		{
			if (value is null)
				return null;

			return value switch
			{
				string stringValue => stringValue,
				DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
				DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
				IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
				_ => value.ToString(),
			};
		}

		internal static bool TryApplyProps(View view, object parameters)
		{
			var propsProperty = view.GetType().GetProperty("Props", BindingFlags.Instance | BindingFlags.Public);
			if (propsProperty is null || !propsProperty.CanWrite)
				return false;

			if (!propsProperty.PropertyType.IsInstanceOfType(parameters))
				return false;

			propsProperty.SetValue(view, parameters);
			return true;
		}
	}
}
