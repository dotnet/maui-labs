#if MACOS
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif

namespace DevFlow.Sample;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
#if DEVFLOW_TOUCH_PAGES
        // The Linux, macOS and WPF heads link this shell but not these pages (SkiaSharp and
        // Syncfusion have no backend there), so they are added only in the heads that build them.
        Items.Insert(Items.Count - 2, new ShellContent { Title = "Canvas Taps", Route = "canvastaps", ContentTemplate = new DataTemplate(typeof(CanvasTapTestPage)) });
        Items.Insert(Items.Count - 2, new ShellContent { Title = "Drag and Drop", Route = "dragdrop", ContentTemplate = new DataTemplate(typeof(DragTestPage)) });
#endif
#if MACOS
        FlyoutBehavior = FlyoutBehavior.Locked;
        MacOSShell.SetUseNativeSidebar(this, true);
#endif
    }
}
