using System.Runtime.CompilerServices;
using AppKit;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Platform;
using Microsoft.Maui.Storage;

namespace MacOS.RuntimeTests.Scenarios.BundleResources;

sealed class BundleResourcesScenario : MauiRuntimeScenario
{
    static readonly Dictionary<string, string> Assets = new()
    {
        ["Data/sample.txt"] = "AppKit nested MAUI asset",
        ["Other/sample.txt"] = "AppKit other MAUI asset with the same filename",
        ["Custom/renamed.txt"] = "AppKit explicitly renamed asset",
        ["default.txt"] = "AppKit default logical name",
        ["Local/local.txt"] = "AppKit local default glob asset",
        ["Data with spaces/sample file.txt"] = "AppKit asset with spaces",
    };

    readonly Image _image = new() { Source = "appicon.png", HeightRequest = 96 };
    readonly Label _label = new() { Text = "Bundled Open Sans", FontFamily = "BundleProbeFont", FontSize = 24 };

    [ModuleInitializer]
    public static void Register() => ScenarioRegistry.Register(new(
        "bundle-resources", ExpectedCases: 4,
        CreateDelegate: context => new BundleResourcesScenario().CreateDelegate(context),
        ExpectedAssertions: 29));

    public override void Configure(MauiAppBuilder builder) =>
        builder.ConfigureFonts(fonts => fonts.AddFont("OpenSans-Regular.ttf", "BundleProbeFont"));

    public override Window CreateWindow(IActivationState? activationState) => new(new ContentPage
    {
        Content = new VerticalStackLayout { Children = { _image, _label } },
    });

    public override async Task RunAsync(RuntimeTestContext context, Window window)
    {
        await RuntimeTestContext.FlushMainQueueAsync();
        var root = NSBundle.MainBundle.ResourcePath
            ?? throw new InvalidOperationException("The running app has no resource directory.");
        var expected = new[] { "Images/appicon.png", "Fonts/OpenSans-Regular.ttf" }.Concat(Assets.Keys).ToArray();
        var inventory = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).Order().ToArray();
        context.WriteJson("bundle-files.json", new { root, expected, files = inventory });

        var imageView = _image.Handler?.PlatformView as NSImageView;
        context.Assert(imageView != null, "The MAUI image must have an AppKit view.");
        if (imageView!.Image == null)
        {
            foreach (var name in expected)
                context.Assert(!File.Exists(Path.Combine(root, name)), $"Historical bundle must omit {name}.");
            context.BaselineFailure("resources.missing", "The linked MauiImage was not loaded.");
            return;
        }

        foreach (var name in expected)
            context.Assert(File.Exists(Path.Combine(root, name)), $"Bundle must contain {name}.");
        context.Assert(!File.Exists(Path.Combine(root, "Raw", "local.txt")), "Default glob must not retain Raw/local.txt.");
        context.Pass("bundle logical paths");

        var image = imageView.Image;
        context.Assert(image != null, "The linked MauiImage must load.");
        context.Assert(image!.Size.Width == 1024 && image.Size.Height == 1024, "The native image must be 1024x1024.");
        context.Capture(imageView, "image.png");
        context.Pass("native image");

        var label = _label.Handler?.PlatformView as NSTextField;
        context.Assert(label != null, "The MAUI label must have an AppKit view.");
        using var fontKey = new NSString("NSFont");
        var attributes = label!.AttributedStringValue.GetAttributes(0, out _);
        var font = attributes?.ObjectForKey(fontKey) as NSFont;
        context.Assert(font?.FontName == "OpenSans-Regular", $"Expected OpenSans-Regular, got {font?.FontName}.");

        var services = window.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("The runtime window has no MAUI services.");
        var registrar = services.GetRequiredService<IFontRegistrar>();
        var macOSRegistrar = registrar as MacOSFontRegistrar
            ?? throw new InvalidOperationException("The AppKit font registrar was not registered.");
        macOSRegistrar.Register(
            "Embedded/OpenSans-Regular.ttf",
            "EmbeddedBundleProbeFont",
            typeof(BundleResourcesScenario).Assembly);
        context.Assert(macOSRegistrar.GetFont("EmbeddedBundleProbeFont") == "OpenSans-Regular",
            "Assembly-aware registration must load the real embedded font resource.");

        var screenshot = services.GetRequiredService<IScreenshot>();
#if NET11_0_OR_GREATER
        context.Assert(screenshot is IViewScreenshot,
            "The AppKit screenshot service must expose the typed view-capture contract.");
        var viewCapture = await ((IViewScreenshot)screenshot).CaptureViewAsync(imageView);
#else
        context.Assert(screenshot is ScreenshotImplementation,
            "The AppKit screenshot service must resolve to the native implementation.");
        var viewCapture = await ((ScreenshotImplementation)screenshot).CaptureAsync(imageView);
#endif
        context.Assert(viewCapture?.Width > 0 && viewCapture.Height > 0,
            "The existing NSView capture must produce a non-empty screenshot.");
        context.Capture(label, "font.png");
        context.Pass("native font");

        var fileSystem = services.GetRequiredService<IFileSystem>();
        foreach (var (name, content) in Assets)
        {
            context.Assert(await fileSystem.AppPackageFileExistsAsync(name), $"IFileSystem must find {name}.");
            using var stream = await fileSystem.OpenAppPackageFileAsync(name);
            using var reader = new StreamReader(stream);
            context.Assert(reader.ReadToEnd().Trim() == content, $"IFileSystem must read the expected content for {name}.");
        }
        context.WriteJson("resources.json", new
        {
            bundle = NSBundle.MainBundle.BundlePath,
            imageWidth = (double)image.Size.Width,
            imageHeight = (double)image.Size.Height,
            font = font!.FontName,
            assets = Assets,
        });
        context.Pass("raw asset contents");
    }
}
