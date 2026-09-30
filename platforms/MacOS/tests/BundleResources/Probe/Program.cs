using AppKit;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Storage;
using System.Text.Json;

namespace BundleResourceProbe;

static class Program
{
    static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new ProbeDelegate();
        NSApplication.Main(args);
    }
}

[Register("BundleResourceProbeDelegate")]
public sealed class ProbeDelegate : MacOSMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiApp.CreateBuilder()
        .UseMauiAppMacOS<ProbeApp>()
        .AddMacOSEssentials()
        .ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "BundleProbeFont"))
        .Build();

    protected override void OnStarted() => BeginInvokeOnMainThread(CheckResources);

    void CheckResources()
    {
        try
        {
            var app = (ProbeApp)Application;
            var imageView = app.Image.Handler?.PlatformView as NSImageView
                ?? throw new InvalidOperationException("The MAUI image has no AppKit view.");
            var image = imageView.Image
                ?? throw new InvalidOperationException("The linked MauiImage was not loaded.");
            if (image.Size.Width <= 0 || image.Size.Height <= 0)
                throw new InvalidOperationException("The linked MauiImage has no pixels.");

            var label = app.Label.Handler?.PlatformView as NSTextField
                ?? throw new InvalidOperationException("The MAUI label has no AppKit view.");
            using var fontKey = new NSString("NSFont");
            var attributes = label.AttributedStringValue.GetAttributes(0, out _);
            var font = attributes[fontKey] as NSFont
                ?? throw new InvalidOperationException("The label has no attributed font.");
            if (!font.FontName.StartsWith("OpenSans", StringComparison.Ordinal))
                throw new InvalidOperationException($"Custom font fell back to {font.FontName}.");

            var fileSystem = Services.GetRequiredService<IFileSystem>();
            var assets = new Dictionary<string, string>
            {
                ["Data/sample.txt"] = "AppKit nested MAUI asset",
                ["Other/sample.txt"] = "AppKit other MAUI asset with the same filename",
                ["Custom/renamed.txt"] = "AppKit explicitly renamed asset",
                ["default.txt"] = "AppKit default logical name",
                ["Local/local.txt"] = "AppKit local default glob asset",
            };
            foreach (var (name, expected) in assets)
            {
                if (!fileSystem.AppPackageFileExistsAsync(name).GetAwaiter().GetResult())
                    throw new FileNotFoundException($"Bundled MauiAsset is missing: {name}");
                using var stream = fileSystem.OpenAppPackageFileAsync(name).GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                if (reader.ReadToEnd().Trim() != expected)
                    throw new InvalidOperationException($"Wrong content for {name}.");
            }

            var result = JsonSerializer.Serialize(new
            {
                result = "PASS",
                bundle = NSBundle.MainBundle.BundlePath,
                imageWidth = (double)image.Size.Width,
                imageHeight = (double)image.Size.Height,
                font = font.FontName,
                assets = assets.Keys,
            });
            Console.WriteLine(result);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            Environment.Exit(1);
        }
    }
}

public sealed class ProbeApp : Application
{
    public Image Image { get; } = new() { Source = "appicon.png", HeightRequest = 96 };
    public Label Label { get; } = new()
    {
        Text = "Bundled Open Sans",
        FontFamily = "BundleProbeFont",
        FontSize = 24,
    };

    protected override Window CreateWindow(IActivationState? activationState) => new(new ContentPage
    {
        Content = new VerticalStackLayout { Children = { Image, Label } },
    });
}
