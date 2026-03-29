using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Platform.Maui.Linux.Gtk4.Platform;

/// <summary>
/// Maps MAUI NamedSize enum values to pixel sizes for Linux/GTK4.
/// Registered via DependencyService for XAML FontSize="Title" etc.
/// </summary>
public class GtkFontNamedSizeService : IFontNamedSizeService
{
	public double GetNamedSize(NamedSize size, Type targetElementType, bool useOldSizes)
	{
		return size switch
		{
			NamedSize.Default => 14,
			NamedSize.Micro => 10,
			NamedSize.Small => 12,
			NamedSize.Medium => 14,
			NamedSize.Large => 18,
			NamedSize.Body => 14,
			NamedSize.Header => 24,
			NamedSize.Title => 20,
			NamedSize.Subtitle => 16,
			NamedSize.Caption => 12,
			_ => 14,
		};
	}
}
