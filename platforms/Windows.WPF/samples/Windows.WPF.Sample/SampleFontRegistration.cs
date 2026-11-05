using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample;

internal static class SampleFontRegistration
{
    internal static void Configure(IFontCollection fonts)
    {
        fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
    }
}
