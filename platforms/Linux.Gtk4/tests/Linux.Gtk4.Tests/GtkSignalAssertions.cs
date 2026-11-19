namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

internal static class GtkSignalAssertions
{
	internal static bool HasVisualStateSignals(Gtk.EventController controller) => controller switch
	{
		Gtk.EventControllerMotion motion => HasSignal(motion, Gtk.EventControllerMotion.GetGType(), "enter") ||
			HasSignal(motion, Gtk.EventControllerMotion.GetGType(), "leave"),
		Gtk.GestureClick click => HasSignal(click, Gtk.GestureClick.GetGType(), "pressed") ||
			HasSignal(click, Gtk.GestureClick.GetGType(), "released"),
		Gtk.EventControllerFocus focus => HasSignal(focus, Gtk.EventControllerFocus.GetGType(), "enter") ||
			HasSignal(focus, Gtk.EventControllerFocus.GetGType(), "leave"),
		_ => false,
	};

	internal static bool HasSignal(GObject.Object instance, GObject.Type type, string signal)
	{
		var id = GObject.Functions.SignalLookup(signal, type);
		Assert.NotEqual(0u, id);
		return GObject.Functions.SignalHasHandlerPending(instance, id, new GLib.Quark(0), true);
	}
}
