using CommunityToolkit.Mvvm.Messaging;
using ControlGallery.Common.Messages;

namespace ControlGallery.Common.Views;

public partial class FlyoutHeader : Grid
{
    public FlyoutHeader()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<ToggleFlyoutHeaderMsg>(this, (r, m) =>
		{
            // this.IsVisible = m.IsVisible;
            // this.HeightRequest = m.IsVisible ? 200 : 0;
            this.RowDefinitions[0].Height = m.IsVisible ? 120 : 0;
            
		});
    }
}