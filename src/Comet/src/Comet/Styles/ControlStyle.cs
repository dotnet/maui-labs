using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace Comet.Styles
{
	/// <summary>
	/// Typed style that maps environment properties for a specific control type.
	/// Values are pushed through Comet's environment system so they cascade to children.
	/// <example>
	/// <code>
	/// var buttonStyle = new ControlStyle&lt;Button&gt;()
	///     .Set(EnvironmentKeys.Colors.Background, Colors.Blue)
	///     .Set(EnvironmentKeys.Colors.Color, Colors.White);
	///
	/// theme.SetControlStyle(buttonStyle);
	/// </code>
	/// </example>
	/// </summary>
	public class ControlStyle<T> : IControlStyleApplicable where T : View
	{
		readonly Dictionary<string, object> _properties = new Dictionary<string, object>();

		/// <summary>
		/// Sets an environment property value for this control type.
		/// </summary>
		public ControlStyle<T> Set(string environmentKey, object value)
		{
			_properties[environmentKey] = value;
			return this;
		}

		/// <summary>
		/// Sets a color environment property for this control type.
		/// Convenience overload to avoid boxing.
		/// </summary>
		public ControlStyle<T> Set(string environmentKey, Color value)
		{
			_properties[environmentKey] = value;
			return this;
		}

		/// <summary>
		/// Applies this control style to the environment.
		/// When target is null, sets values on the global environment (all views of type T).
		/// When target is provided, sets values scoped to that view.
		/// </summary>
		public void Apply(ContextualObject target = null)
		{
			var controlType = typeof(T);
			foreach (var kvp in _properties)
			{
				if (target is not null)
					target.SetEnvironment(controlType, kvp.Key, kvp.Value);
				else
					View.SetGlobalEnvironment(controlType, kvp.Key, kvp.Value);
			}
		}

		/// <summary>
		/// Gets a previously set property value, or default if not set.
		/// </summary>
		public TValue Get<TValue>(string environmentKey)
		{
			if (_properties.TryGetValue(environmentKey, out var value) && value is TValue typed)
				return typed;
			return default;
		}

		/// <summary>
		/// Returns true if a property has been set for the given key.
		/// </summary>
		public bool HasProperty(string environmentKey)
			=> _properties.ContainsKey(environmentKey);

		/// <summary>
		/// Removes a property setting, reverting to environment defaults.
		/// </summary>
		public ControlStyle<T> Remove(string environmentKey)
		{
			_properties.Remove(environmentKey);
			return this;
		}

		/// <summary>
		/// Returns all configured property keys.
		/// </summary>
		public IEnumerable<string> Keys => _properties.Keys;
	}
}
