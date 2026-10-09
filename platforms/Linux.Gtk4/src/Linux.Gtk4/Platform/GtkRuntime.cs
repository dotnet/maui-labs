using System.Runtime.InteropServices;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

/// <summary>
/// Guards against running on an unsupported GTK runtime. Every supported startup
/// path (<see cref="GtkMauiApplication.Run"/> and
/// <c>GtkBlazorWebView.InitializeWebKit</c>) calls <see cref="EnsureSupported"/>
/// before any GirCore GTK/WebKit initialization.
/// </summary>
/// <remarks>
/// The version is read by P/Invoking GTK's own stable ABI directly
/// (<c>gtk_get_*_version</c>, present since GTK 4.0; soname <c>libgtk-4.so.1</c>),
/// so the check is independent of GirCore's initialization order — it can run
/// before the WebKit/GTK GirCore modules are initialized.
/// </remarks>
internal static class GtkRuntime
{
	// The backend calls gtk_css_provider_load_from_string (GTK 4.12+) and uses
	// Gtk.FileDialog (GTK 4.10+). On older GTK these resolve to missing native
	// entry points and the app crashes with a cryptic EntryPointNotFoundException.
	private const uint MinimumGtkMajor = 4;
	private const uint MinimumGtkMinor = 12;

	// Log prefix derived from the assembly name instead of a hardcoded string.
	private static readonly string LogPrefix = $"[{typeof(GtkRuntime).Assembly.GetName().Name}]";

	private static bool _verified;

	/// <summary>
	/// Throws <see cref="PlatformNotSupportedException"/> with a friendly, logged
	/// message if GTK is missing or older than the minimum supported version
	/// (currently 4.12). Safe to call from multiple entry points; only the first
	/// call performs the check.
	/// </summary>
	internal static void EnsureSupported()
	{
		if (_verified)
			return;

		uint major, minor, micro;
		try
		{
			major = gtk_get_major_version();
			minor = gtk_get_minor_version();
			micro = gtk_get_micro_version();
		}
		catch (DllNotFoundException ex)
		{
			Fail("GTK 4 was not found. Please install GTK 4.12 or newer and try again.", ex);
			return; // unreachable; keeps definite-assignment analysis happy
		}

		if (major < MinimumGtkMajor || (major == MinimumGtkMajor && minor < MinimumGtkMinor))
		{
			Fail(
				$"This app needs GTK {MinimumGtkMajor}.{MinimumGtkMinor} or newer, but the installed " +
				$"version is {major}.{minor}.{micro}. Please update GTK and try again.",
				inner: null);
		}

		_verified = true;
	}

	private static void Fail(string message, Exception? inner)
	{
		Console.Error.WriteLine($"{LogPrefix} {message}");
		throw inner is null
			? new PlatformNotSupportedException(message)
			: new PlatformNotSupportedException(message, inner);
	}

	private const string GtkLibrary = "libgtk-4.so.1";

	[DllImport(GtkLibrary)]
	private static extern uint gtk_get_major_version();

	[DllImport(GtkLibrary)]
	private static extern uint gtk_get_minor_version();

	[DllImport(GtkLibrary)]
	private static extern uint gtk_get_micro_version();
}
