#if MACOS
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif

namespace DevFlow.Sample;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        AddWebViewTestPages();
#if MACOS
        FlyoutBehavior = FlyoutBehavior.Locked;
        MacOSShell.SetUseNativeSidebar(this, true);
#endif
    }

    partial void AddWebViewTestPages();
}
