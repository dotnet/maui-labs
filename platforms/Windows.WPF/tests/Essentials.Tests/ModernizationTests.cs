using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public class ModernizationTests
{
    [Fact]
    public void UseWpfEssentials_RegistersDesktopCapabilities()
    {
        var builder = MauiApp.CreateBuilder().UseWPFEssentials();

        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IBattery) && service.ImplementationType == typeof(WPFBattery));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IMediaPicker) && service.ImplementationType == typeof(WPFMediaPicker));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(ITextToSpeech) && service.ImplementationType == typeof(WPFTextToSpeech));
    }

    [Fact]
    public async Task AppActions_UnsupportedSetIsExplicit()
    {
        var appActions = new WPFAppActions(new TestJumpListProvider(false), Array.Empty<string>());

        Assert.False(appActions.IsSupported);
        await Assert.ThrowsAsync<FeatureNotSupportedException>(() =>
            appActions.SetAsync(Array.Empty<AppAction>()));
    }

    [Fact]
    public async Task AppActions_PackagedJumpListDispatchesActivation()
    {
        var provider = new TestJumpListProvider(true);
        var appActions = new WPFAppActions(provider, new[] { "app.exe", "--maui-app-action", "open%20recent" });
        AppAction? activated = null;
        appActions.AppActionActivated += (_, args) => activated = args.AppAction;

        await appActions.SetAsync(new[] { new AppAction("open recent", "Open recent") });

        Assert.Equal("open recent", activated?.Id);
        Assert.Single(await appActions.GetAsync());
    }

    [Fact]
    public void AppActions_ArgumentsRoundTrip()
    {
        var action = new AppAction(
            "open 100% / \u96ea",
            "Open 50% \u96ea report");
        var parsed = WPFAppActions.ParseArguments(WPFAppActions.CreateArguments(action));

        Assert.Equal(action.Id, parsed?.Id);
        Assert.Equal(action.Title, parsed?.Title);
        Assert.Null(WPFAppActions.ParseArguments("--unrelated value"));
    }

    [Fact]
    public void DeviceInfo_ReportsWpfPlatform()
    {
        var deviceInfo = new WPFDeviceInfo();

        Assert.Equal(DevicePlatform.Create("WPF"), deviceInfo.Platform);
        Assert.Equal(DeviceIdiom.Desktop, deviceInfo.Idiom);
    }

    sealed class TestJumpListProvider(bool isSupported) : IWpfJumpListProvider
    {
        IReadOnlyList<AppAction> _actions = [];

        public bool IsSupported => isSupported;

        public Task<IReadOnlyList<AppAction>> GetAsync() => Task.FromResult(_actions);

        public Task SetAsync(IReadOnlyList<AppAction> actions)
        {
            _actions = actions;
            return Task.CompletedTask;
        }
    }
}
