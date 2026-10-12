namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

internal sealed class GtkCssStyles
{
	readonly Dictionary<nint, OrderedDictionary<(string Selector, string Property), string>> _widgets = new();

	public string Update(nint widget, string selector, string property, string? css)
	{
		if (!_widgets.TryGetValue(widget, out var rules))
		{
			if (string.IsNullOrWhiteSpace(css))
				return string.Empty;

			rules = new();
			_widgets.Add(widget, rules);
		}

		var key = (selector, property);
		// Equal-specificity declarations follow the latest mapper update.
		rules.Remove(key);
		if (!string.IsNullOrWhiteSpace(css))
			rules.Add(key, css);

		if (rules.Count == 0)
		{
			_widgets.Remove(widget);
			return string.Empty;
		}

		return string.Join("\n", rules.Select(rule => $"{rule.Key.Selector} {{ {rule.Value} }}"));
	}

	public void Clear() => _widgets.Clear();
}
