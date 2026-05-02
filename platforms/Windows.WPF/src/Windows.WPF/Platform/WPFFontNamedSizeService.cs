using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Maps MAUI NamedSize enum to WPF point sizes.
	/// </summary>
	public class WPFFontNamedSizeService : IFontNamedSizeService
	{
		public double GetNamedSize(NamedSize size, System.Type targetElementType, bool useOldSizes)
		{
			return size switch
			{
				NamedSize.Default => 14.0,
				NamedSize.Micro => 10.0,
				NamedSize.Small => 12.0,
				NamedSize.Medium => 17.0,
				NamedSize.Large => 22.0,
				NamedSize.Body => 14.0,
				NamedSize.Header => 46.0,
				NamedSize.Title => 24.0,
				NamedSize.Subtitle => 20.0,
				NamedSize.Caption => 12.0,
				_ => 14.0,
			};
		}
	}
}
