#if MACOS
using Microsoft.Maui.Platforms.MacOS.Platform;
#endif

namespace DevFlow.Sample;

public partial class ModalTestPage : ContentPage
{
    public ModalTestPage()
    {
        InitializeComponent();
#if MACOS
        MacOSPage.SetModalSheetSizesToContent(this, true);
#endif
    }

    private async void OnCloseModal(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
