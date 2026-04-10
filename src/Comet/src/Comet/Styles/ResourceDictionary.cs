using System;
using System.Collections.Generic;
using Comet.Styles;

namespace Comet
{
	public class ResourceDictionary : Dictionary<string, object>
	{
		public ResourceDictionary MergedWith { get; set; }
		public IList<ResourceDictionary> MergedDictionaries { get; } = new List<ResourceDictionary>();

		public bool TryGetResource(string key, out object value)
		{
			if (TryGetValue(key, out value))
				return true;

			foreach (var merged in MergedDictionaries)
			{
				if (merged.TryGetResource(key, out value))
					return true;
			}

			if (MergedWith is not null)
				return MergedWith.TryGetResource(key, out value);

			value = null;
			return false;
		}

		/// <summary>
		/// Retrieves a typed resource by key.
		/// Throws KeyNotFoundException if not found, InvalidCastException if wrong type.
		/// </summary>
		public T Get<T>(string key)
		{
			if (TryGetResource(key, out var value))
				return (T)value;
			throw new KeyNotFoundException($"Resource '{key}' not found.");
		}

		/// <summary>
		/// Retrieves a typed resource by key, returning a default value if not found.
		/// </summary>
		public T Get<T>(string key, T defaultValue)
		{
			if (TryGetResource(key, out var value) && value is T typed)
				return typed;
			return defaultValue;
		}

		/// <summary>
		/// Tries to retrieve a typed resource by key.
		/// </summary>
		public bool TryGet<T>(string key, out T value)
		{
			if (TryGetResource(key, out var obj) && obj is T typed)
			{
				value = typed;
				return true;
			}
			value = default;
			return false;
		}

		/// <summary>
		/// Retrieves a Style&lt;T&gt; resource and applies it to a view.
		/// </summary>
		public TView ApplyStyle<TView>(string key, TView view) where TView : View
		{
			var style = Get<Style<TView>>(key);
			return style.Apply(view);
		}
	}
}
