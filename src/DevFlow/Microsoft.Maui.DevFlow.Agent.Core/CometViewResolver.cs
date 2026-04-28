using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Maui;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Resolves Comet view types from CometView wrappers using reflection.
/// No hard reference to Comet required — uses runtime type checking.
/// </summary>
internal static class CometViewResolver
{
	private static bool? _cometAvailable;
	private static Type? _cometViewType;
	private static Type? _cometHandlerType;
	private static MethodInfo? _getViewMethod;
	private static PropertyInfo? _builtViewProperty;
	private static PropertyInfo? _bodyProperty;
	private static PropertyInfo? _currentViewProperty;

	/// <summary>
	/// Checks if Comet is loaded in the current app domain.
	/// Caches the result and reflection metadata for performance.
	/// </summary>
	private static bool IsCometAvailable()
	{
		if (_cometAvailable.HasValue)
			return _cometAvailable.Value;

		try
		{
			// Look for Comet assembly and core types via reflection
			var cometAssembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == "Comet");

			if (cometAssembly == null)
			{
				_cometAvailable = false;
				return false;
			}

			// Cache Comet.View type
			_cometViewType = cometAssembly.GetType("Comet.View");
			if (_cometViewType == null)
			{
				_cometAvailable = false;
				return false;
			}

			// Cache CometViewHandler type (in Comet.Handlers namespace)
			_cometHandlerType = cometAssembly.GetType("Comet.Handlers.CometViewHandler");

			// Cache reflection metadata for key properties/methods
			_getViewMethod = _cometViewType.GetMethod("GetView", BindingFlags.Public | BindingFlags.Instance);
			_builtViewProperty = _cometViewType.GetProperty("BuiltView", BindingFlags.Public | BindingFlags.Instance);
			_bodyProperty = _cometViewType.GetProperty("Body", BindingFlags.Public | BindingFlags.Instance);

			// Cache platform-specific CometView (iOS/Android/Windows) CurrentView property
			// Platform views are in Comet.iOS.CometView, Comet.Droid.CometView, etc.
			var platformCometTypes = new[]
			{
				cometAssembly.GetType("Comet.iOS.CometView"),
				cometAssembly.GetType("Comet.Droid.CometView"),
				cometAssembly.GetType("Comet.Windows.CometView"),
			};

			foreach (var platformType in platformCometTypes)
			{
				if (platformType != null)
				{
					_currentViewProperty = platformType.GetProperty("CurrentView", BindingFlags.Public | BindingFlags.Instance);
					if (_currentViewProperty != null)
						break;
				}
			}

			_cometAvailable = true;
			return true;
		}
		catch
		{
			_cometAvailable = false;
			return false;
		}
	}

	/// <summary>
	/// Safely resolves a property by name, handling AmbiguousMatchException
	/// from generic handler types (e.g. Comet's ViewHandler&lt;View, CometView&gt;).
	/// Falls back to walking the type hierarchy with DeclaredOnly.
	/// </summary>
	public static PropertyInfo? GetPropertySafe(Type type, string name)
	{
		try
		{
			return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
		}
		catch (AmbiguousMatchException)
		{
			// Walk inheritance chain with DeclaredOnly to resolve ambiguity
			var current = type;
			while (current != null)
			{
				try
				{
					var prop = current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
					if (prop != null) return prop;
				}
				catch { /* skip this level */ }
				current = current.BaseType;
			}
			return null;
		}
	}

	/// <summary>
	/// Attempts to resolve a Comet view from a platform view or handler.
	/// Returns a tuple of (resolvedType, resolvedFullType, cometViewInstance, additionalProperties).
	/// Returns null if not a Comet view.
	/// </summary>
	public static (string Type, string FullType, object CometView, Dictionary<string, string> Properties)? TryResolveCometView(IVisualTreeElement element)
	{
		if (!IsCometAvailable())
			return null;

		// Try to get the Comet virtual view from the element
		object? cometView = null;

		try
		{
			// Strategy 1: Element is directly a Comet.View
			if (_cometViewType != null && _cometViewType.IsInstanceOfType(element))
			{
				cometView = element;
			}
			// Strategy 2: Element has a Handler that's CometViewHandler
			else if (element is IView view && view.Handler != null &&
			         _cometHandlerType != null &&
			         _cometHandlerType.IsInstanceOfType(view.Handler))
			{
				// Get VirtualView from handler — use safe reflection to avoid
				// AmbiguousMatchException on generic handlers like ViewHandler<TView, TPlatform>
				var virtualViewProp = GetPropertySafe(view.Handler.GetType(), "VirtualView");
				cometView = virtualViewProp?.GetValue(view.Handler);
			}
			// Strategy 3: Element's platform view is CometView wrapper
			else if (element is IView view2 && view2.Handler != null)
			{
				var platformViewProp = GetPropertySafe(view2.Handler.GetType(), "PlatformView");
				var platformView = platformViewProp?.GetValue(view2.Handler);
				if (platformView != null && _currentViewProperty != null)
				{
					// Check if platform view has CurrentView property (iOS/Android/Windows CometView)
					var currentViewProp = platformView.GetType().GetProperty("CurrentView", BindingFlags.Public | BindingFlags.Instance);
					if (currentViewProp != null)
					{
						cometView = currentViewProp.GetValue(platformView);
					}
				}
			}
		}
		catch
		{
			// If any reflection fails, this isn't a Comet view we can resolve
			return null;
		}

		if (cometView == null || _cometViewType == null || !_cometViewType.IsInstanceOfType(cometView))
			return null;

		// Now resolve the actual Comet control type
		var resolvedType = ResolveCometType(cometView);
		var additionalProps = ExtractCometProperties(cometView);

		return (resolvedType.Type, resolvedType.FullType, cometView, additionalProps);
	}

	/// <summary>
	/// Resolves the actual Comet control type by unwrapping Body chains.
	/// Returns the most specific type (Button, VStack, Component&lt;T&gt;, etc.)
	/// </summary>
	private static (string Type, string FullType) ResolveCometType(object cometView)
	{
		if (cometView == null)
			return ("Unknown", "Comet.Unknown");

		var viewType = cometView.GetType();

		// Check if view has a Body — if so, resolve to BuiltView/GetView()
		var hasBody = _bodyProperty?.GetValue(cometView) != null;

		if (hasBody)
		{
			// Try BuiltView first (faster, cached)
			object? builtView = null;
			if (_builtViewProperty != null)
			{
				try
				{
					builtView = _builtViewProperty.GetValue(cometView);
				}
				catch { /* BuiltView may throw if not yet built */ }
			}

			// Fallback to GetView() if BuiltView is null
			if (builtView == null && _getViewMethod != null)
			{
				try
				{
					builtView = _getViewMethod.Invoke(cometView, null);
				}
				catch { /* GetView() may throw */ }
			}

			// If we got a built view, recurse to resolve its type
			if (builtView != null && builtView != cometView)
			{
				return ResolveCometType(builtView);
			}
		}

		// No Body or couldn't resolve — use the view's actual type
		var typeName = viewType.Name;
		var fullTypeName = viewType.FullName ?? typeName;

		// For Component<T> or Component<T,TProps>, show generic parameters
		if (viewType.IsGenericType)
		{
			var genericArgs = viewType.GetGenericArguments();
			if (genericArgs.Length > 0)
			{
				var argNames = string.Join(", ", genericArgs.Select(t => t.Name));
				typeName = $"{viewType.Name.Split('`')[0]}<{argNames}>";
				fullTypeName = $"{viewType.Namespace}.{typeName}";
			}
		}

		return (typeName, fullTypeName);
	}

	/// <summary>
	/// Extracts Comet-specific properties (environment, state, etc.) for display.
	/// </summary>
	private static Dictionary<string, string> ExtractCometProperties(object cometView)
	{
		var props = new Dictionary<string, string>();

		try
		{
			var viewType = cometView.GetType();

			// Check if it's a Component with State
			var stateProperty = viewType.GetProperty("State", BindingFlags.Public | BindingFlags.Instance);
			if (stateProperty != null)
			{
				var stateValue = stateProperty.GetValue(cometView);
				if (stateValue != null)
				{
					props["CometState"] = stateValue.GetType().Name;
				}
			}

			// Check if it's a Component with Props
			var propsProperty = viewType.GetProperty("Props", BindingFlags.Public | BindingFlags.Instance);
			if (propsProperty != null)
			{
				var propsValue = propsProperty.GetValue(cometView);
				if (propsValue != null)
				{
					props["CometProps"] = propsValue.GetType().Name;
				}
			}

			// Get Body status
			if (_bodyProperty != null)
			{
				var bodyValue = _bodyProperty.GetValue(cometView);
				props["CometHasBody"] = (bodyValue != null).ToString();
			}

			// Get Id (every Comet.View has an Id property)
			var idProperty = viewType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
			if (idProperty != null)
			{
				var idValue = idProperty.GetValue(cometView);
				if (idValue != null)
				{
					props["CometId"] = idValue.ToString() ?? "null";
				}
			}
		}
		catch
		{
			// Ignore reflection errors
		}

		return props;
	}

	/// <summary>
	/// Gets the list of environment keys defined in Comet.EnvironmentKeys.
	/// Returns empty list if Comet not available or reflection fails.
	/// </summary>
	public static List<string> GetEnvironmentKeys()
	{
		var keys = new List<string>();

		if (!IsCometAvailable())
			return keys;

		try
		{
			var cometAssembly = AppDomain.CurrentDomain.GetAssemblies()
				.FirstOrDefault(a => a.GetName().Name == "Comet");

			if (cometAssembly == null)
				return keys;

			var envKeysType = cometAssembly.GetType("Comet.EnvironmentKeys");
			if (envKeysType == null)
				return keys;

			// Get all public static string fields (environment key constants)
			var fields = envKeysType.GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(f => f.FieldType == typeof(string));

			foreach (var field in fields)
			{
				var value = field.GetValue(null) as string;
				if (!string.IsNullOrEmpty(value))
					keys.Add(value);
			}

			// Also check nested classes (Fonts, Colors, etc.)
			var nestedTypes = envKeysType.GetNestedTypes(BindingFlags.Public);
			foreach (var nestedType in nestedTypes)
			{
				var nestedFields = nestedType.GetFields(BindingFlags.Public | BindingFlags.Static)
					.Where(f => f.FieldType == typeof(string));

				foreach (var field in nestedFields)
				{
					var value = field.GetValue(null) as string;
					if (!string.IsNullOrEmpty(value))
						keys.Add(value);
				}
			}
		}
		catch
		{
			// Ignore reflection errors
		}

		return keys;
	}

	/// <summary>
	/// Gets environment values from a Comet view.
	/// Returns dictionary of key -> value (as string).
	/// </summary>
	public static Dictionary<string, string> GetEnvironmentValues(object cometView)
	{
		var values = new Dictionary<string, string>();

		if (cometView == null || _cometViewType == null || !_cometViewType.IsInstanceOfType(cometView))
			return values;

		try
		{
			// Comet views have a GetEnvironment<T>(string key) method
			var getEnvMethod = _cometViewType.GetMethod("GetEnvironment", BindingFlags.Public | BindingFlags.Instance);
			if (getEnvMethod == null)
				return values;

			// Get all environment keys and query each one
			var keys = GetEnvironmentKeys();
			foreach (var key in keys)
			{
				try
				{
					// Call GetEnvironment<object>(key)
					var genericMethod = getEnvMethod.MakeGenericMethod(typeof(object));
					var value = genericMethod.Invoke(cometView, new object[] { key });
					if (value != null)
					{
						values[key] = value.ToString() ?? "null";
					}
				}
				catch
				{
					// Ignore per-key errors
				}
			}
		}
		catch
		{
			// Ignore reflection errors
		}

		return values;
	}
}
