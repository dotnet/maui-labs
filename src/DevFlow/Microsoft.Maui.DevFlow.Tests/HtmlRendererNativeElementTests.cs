using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class HtmlRendererNativeElementTests
{
    [Fact]
    public void RenderElements_NativeMetadata_IsAvailableToInspector()
    {
        var element = new ElementInfo
        {
            Id = "native:registered:123",
            Type = "NavigationViewItem",
            FullType = "Microsoft.UI.Xaml.Controls.NavigationViewItem",
            Framework = "windows-native",
            Origin = "native",
            OwnerId = "settings",
            Discriminator = "primary",
            BoundsQuality = "exact",
            CaptureEpoch = 42,
            RegistryGeneration = 7,
            WindowId = 2,
            Role = "toolbar-item",
            NativeType = "Microsoft.UI.Xaml.Controls.NavigationViewItem",
            Capabilities = ["invoke", "focus"],
            IsVisible = true,
            IsEnabled = true,
            WindowBounds = new BoundsInfo
            {
                X = 10,
                Y = 20,
                Width = 100,
                Height = 40
            }
        };

        var html = HtmlRenderer.RenderElements([element]);

        Assert.Contains("data-id=\"native:registered:123\"", html);
        Assert.Contains("data-origin=\"native\"", html);
        Assert.Contains("data-ownerId=\"settings\"", html);
        Assert.Contains("data-discriminator=\"primary\"", html);
        Assert.Contains("data-boundsQuality=\"exact\"", html);
        Assert.Contains("data-captureEpoch=\"42\"", html);
        Assert.Contains("data-registryGeneration=\"7\"", html);
        Assert.Contains("data-windowId=\"2\"", html);
        Assert.Contains("data-capabilities=\"invoke,focus\"", html);
        Assert.Contains("left:10px;top:20px;width:100px;height:40px;", html);
    }
}
