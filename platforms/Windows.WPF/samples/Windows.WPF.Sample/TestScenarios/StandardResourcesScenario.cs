using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios;

internal static class StandardResourcesScenario
{
    const string AssetContents = "WPF standard MauiAsset pipeline";

    public static int Run()
    {
        var root = AppContext.BaseDirectory;
        var image = Path.Combine(root, "Resources", "Images", "dotnet_bot.png");
        var imageSource = Path.Combine(root, "Resources", "Images", "dotnet_bot.svg");
        var font = Path.Combine(root, "Resources", "Fonts", "OpenSans-Regular.ttf");
        var asset = Path.Combine(root, "Pipeline", "payload.txt");
        var icon = Path.Combine(root, "Resources", "AppIcon", "appicon_modern.png");
        var iconSource = Path.Combine(root, "Resources", "AppIcon", "appicon_modern.svg");
        var splash = Path.Combine(root, "Resources", "Splash", "dotnet_bot.svg");

        var failures = new List<string>();
        Check(File.Exists(image), $"processed MauiImage missing: {image}");
        Check(!File.Exists(imageSource), $"raw MauiImage SVG was deployed beside the processed bitmap: {imageSource}");
        Check(File.Exists(font), $"MauiFont missing: {font}");
        Check(File.Exists(asset), $"MauiAsset missing: {asset}");
        Check(File.Exists(icon), $"processed MauiIcon missing: {icon}");
        Check(!File.Exists(iconSource), $"raw MauiIcon SVG was deployed instead of its processed bitmap: {iconSource}");
        Check(File.Exists(splash), $"MauiSplashScreen source missing: {splash}");

        if (File.Exists(image))
        {
            using var stream = File.OpenRead(image);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Check(frame.PixelWidth > 0 && frame.PixelHeight > 0,
                $"processed MauiImage has invalid dimensions: {frame.PixelWidth}x{frame.PixelHeight}");
            Console.WriteLine($"MauiImage: {frame.PixelWidth}x{frame.PixelHeight}, SHA256={Hash(image)}");
        }

        if (File.Exists(icon))
        {
            using var stream = File.OpenRead(icon);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.Single();
            Check(frame.PixelWidth > 0 && frame.PixelHeight > 0,
                $"processed MauiIcon has invalid dimensions: {frame.PixelWidth}x{frame.PixelHeight}");
            Console.WriteLine($"MauiIcon: {frame.PixelWidth}x{frame.PixelHeight}, SHA256={Hash(icon)}");
        }

        if (File.Exists(asset))
            Check(File.ReadAllText(asset).Trim() == AssetContents, "MauiAsset contents changed.");

        foreach (var failure in failures)
            Console.Error.WriteLine($"FAIL: {failure}");
        Console.WriteLine($"Standard MAUI resource cases: 5; failures: {failures.Count}; root: {root}");
        return failures.Count == 0 ? 0 : 1;

        void Check(bool condition, string message)
        {
            if (!condition)
                failures.Add(message);
        }
    }

    static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
