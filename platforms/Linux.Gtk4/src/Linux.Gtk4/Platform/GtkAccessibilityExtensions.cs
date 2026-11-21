using System.Reflection;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

internal static class GtkAccessibilityExtensions
{
	static readonly MethodInfo CreateValueArray =
		typeof(GObject.Value).Assembly
			.GetType("GObject.Internal.ValueArray2OwnedHandle", throwOnError: true)!
			.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
				[typeof(GObject.Value[])])
			?? throw new MissingMethodException("GObject.Internal.ValueArray2OwnedHandle", "Create");

	static readonly MethodInfo UpdateProperty =
		typeof(Gtk.Widget).Assembly
			.GetType("Gtk.Internal.Accessible", throwOnError: true)!
			.GetMethod("UpdateProperty", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new MissingMethodException("Gtk.Internal.Accessible", "UpdateProperty");

	static readonly ConditionalWeakTable<Gtk.Widget, OriginalAccessibleRole> OriginalRoles = new();

	internal static bool SupportsPropertyUpdates =>
		CreateValueArray is not null && UpdateProperty is not null;

	internal static void SetAccessibleText(
		this Gtk.Widget widget,
		Gtk.AccessibleProperty property,
		string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			widget.ResetProperty(property);
			return;
		}

		var nativeValue = new GObject.Value(value);
		var values = new[] { nativeValue };
		var valueArray = CreateValueArray.Invoke(null, [values])
			?? throw new InvalidOperationException("GirCore did not create an accessible value array.");
		try
		{
			UpdateProperty.Invoke(null,
			[
				widget.Handle.DangerousGetHandle(),
				1,
				new[] { property },
				valueArray,
			]);
		}
		finally
		{
			(valueArray as IDisposable)?.Dispose();
			(nativeValue as IDisposable)?.Dispose();
		}
	}

	internal static void SetHeading(this Gtk.Widget widget, bool isHeading)
	{
		if (isHeading)
		{
			OriginalRoles.GetValue(widget,
				current => new OriginalAccessibleRole(current.AccessibleRole));
			widget.AccessibleRole = Gtk.AccessibleRole.Heading;
		}
		else if (OriginalRoles.TryGetValue(widget, out var original))
		{
			widget.AccessibleRole = original.Role;
			OriginalRoles.Remove(widget);
		}
	}

	sealed record OriginalAccessibleRole(Gtk.AccessibleRole Role);
}
