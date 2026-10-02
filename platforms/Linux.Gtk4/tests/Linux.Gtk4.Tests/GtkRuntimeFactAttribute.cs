namespace Microsoft.Maui.Platforms.Linux.Gtk4.Tests;

internal sealed class GtkRuntimeFactAttribute : FactAttribute
{
	public GtkRuntimeFactAttribute()
	{
		if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("RUN_GTK_RUNTIME_TESTS") != "1")
			Skip = "Requires Linux, GTK 4.12+, a display, and RUN_GTK_RUNTIME_TESTS=1.";
	}
}

[CollectionDefinition("GTK runtime", DisableParallelization = true)]
public sealed class GtkRuntimeCollection;
