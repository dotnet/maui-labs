using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.WPF;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.TestScenarios;

internal static class RegisteredFontsScenario
{
    const string Alias = "OpenSansRegular";
    const string Text = "ABC";
    const string FontHash = "335326D5DF8AA9704C0466F2C772ABF2A8C65D0046F1921BD4668D46331B23AB";

    public static int Run()
    {
        var results = new List<CaseResult>();
        try
        {
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            DispatcherProvider.SetCurrent(new WPFDispatcherProvider());
            var loader = new ObservedFontLoader();
            var logger = new FontLogger();
            var builder = MauiApp.CreateBuilder().UseMauiAppWPF<Application>()
                .ConfigureFonts(SampleFontRegistration.Configure);
            builder.Services.AddSingleton<IEmbeddedFontLoader>(loader);
            builder.Services.AddSingleton<ILogger<WPFFontManager>>(logger);
            using var app = builder.Build();
            var context = new WPFMauiContext(app.Services);
            var expectedPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts", "OpenSans-Regular.ttf");
            var registrar = app.Services.GetRequiredService<IFontRegistrar>();
            Console.WriteLine($"BaseDirectory: {AppContext.BaseDirectory}");
            Console.WriteLine($"WorkingDirectory: {Environment.CurrentDirectory}");
            Console.WriteLine($"Registered path: {registrar.GetFont(Alias)}");
            Console.WriteLine($"Expected native font: {expectedPath}");
            Console.WriteLine("Manifest resources: " + string.Join(", ", typeof(RegisteredFontsScenario).Assembly.GetManifestResourceNames()));

            var label = new LabelHandler();
            var image = new ImageHandler();
            try
            {
                label.SetMauiContext(context);
                label.SetVirtualView(new Label { Text = Text, FontFamily = Alias, FontSize = 32 });
                image.SetMauiContext(context);
                image.SetVirtualView(new Image
                {
                    Source = new FontImageSource { Glyph = "A", FontFamily = Alias, Size = 32 },
                });
                var renderedLabel = Render(label.PlatformView);
                var labelPixels = Pixels(renderedLabel);
                var runs = SnapshotRuns(VisualTreeHelper.GetDrawing(label.PlatformView));
                var hasNative = new Typeface(label.PlatformView.FontFamily, System.Windows.FontStyles.Normal,
                    System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal).TryGetGlyphTypeface(out var native);
                var nativeUri = hasNative ? native.FontUri : null;
                var nativeHash = hasNative ? HashFont(native) : null;
                var nativeFamilies = hasNative ? native.FamilyNames.Values.ToArray() : [];
                var nativeCmapValid = hasNative && Text.All(c => native.CharacterToGlyphMap.TryGetValue(c, out var glyph) && glyph != 0);
                var imageBitmap = image.PlatformView.Source as BitmapSource;
                var imagePixels = imageBitmap == null ? null : Pixels(imageBitmap);
                var imageWidth = imageBitmap?.PixelWidth ?? 0;
                var imageHeight = imageBitmap?.PixelHeight ?? 0;
                var embeddedPaths = loader.Paths.ToArray();
                var embeddedCalls = loader.Calls;
                var warnings = logger.Warnings.ToArray();
                Console.WriteLine($"Label family: {label.PlatformView.FontFamily.Source}");
                Console.WriteLine($"Label native URI: {nativeUri?.AbsoluteUri ?? "<none>"}; font SHA256={nativeHash ?? "<none>"}");
                foreach (var run in runs)
                    Console.WriteLine($"GlyphRun: {run.Uri}; font SHA256={run.FontHash}; indices={string.Join(",", run.Indices)}");
                Console.WriteLine($"Actual label pixel SHA256: {Convert.ToHexString(SHA256.HashData(labelPixels))}");
                Console.WriteLine($"Font image: {(imagePixels != null ? $"{imageWidth}x{imageHeight}" : "null")}");
                Console.WriteLine("ACTUAL_CAPTURE_COMPLETE: no control font has been loaded.");

                NativeControl? control = null;
                Check("trusted-native-control", "fixture", () => control = LoadControl());
                Check("native-font-identity", "rendering", () =>
                {
                    Require(runs.Length > 0 && runs.All(run => run.FontHash == FontHash),
                        "The drawn font bytes differ from trusted OpenSans.");
                    Require(!hasNative || nativeHash == FontHash, "The Label's physical typeface differs from trusted OpenSans.");
                });
                Check("native-family-and-cmap", "rendering", () =>
                {
                    Require(runs.Length > 0 && runs.All(run => run.Families.Contains("Open Sans") && run.HasTextGlyphs),
                        "The drawn native font must declare Open Sans and contain the text's cmap entries.");
                    Require(!hasNative || (nativeFamilies.Contains("Open Sans") && nativeCmapValid),
                        "Unexpected physical typeface family or cmap.");
                });
                Check("label-glyph-runs", "rendering", () =>
                {
                    Require(runs.Length > 0, "The native Label produced no GlyphRuns.");
                    foreach (var run in runs)
                    {
                        Require(run.FontHash == FontHash, "Drawn GlyphRun used another font's bytes.");
                        Require(run.Indices.All(index => index != 0), "Drawn GlyphRun contained a missing glyph.");
                    }
                    var reference = RequireControl(control);
                    Require(runs.SelectMany(run => run.Indices).SequenceEqual(reference.Runs.SelectMany(run => run.Indices)),
                        "Actual and direct-native glyph indices differ.");
                });
                Check("label-native-pixels", "rendering", () => Require(labelPixels.SequenceEqual(RequireControl(control).LabelPixels),
                    "The actual MAUI Label differs from the independently validated trusted native control."));
                Check("font-image-native-metrics", "rendering", () =>
                {
                    Require(imagePixels != null, "FontImageSource produced no bitmap.");
                    var reference = RequireControl(control).Image;
                    Require(imageWidth == reference.PixelWidth && imageHeight == reference.PixelHeight,
                        "Font image dimensions differ from the native reference.");
                    var pixels = imagePixels!;
                    Require(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0) > 30,
                        "Font image has no meaningful glyph pixels.");
                });
                Check("font-image-native-pixels", "rendering", () =>
                {
                    Require(imagePixels != null, "FontImageSource produced no bitmap.");
                    Require(imagePixels!.SequenceEqual(Pixels(RequireControl(control).Image)),
                        "Font image differs from the direct-native glyph.");
                });
                Check("portable-font-origin", "portability", () =>
                {
                    var uris = runs.Select(run => run.Uri).ToList();
                    if (nativeUri != null)
                        uris.Add(nativeUri);
                    Require(uris.Count > 0, "Portability is unproven: no native font URI.");
                    foreach (var uri in uris.Distinct())
                    {
                        var origin = PortableOrigin(uri, embeddedPaths);
                        Console.WriteLine($"Font origin: {uri}; {origin ?? "UNPROVEN"}");
                        Require(origin != null,
                            $"Portability needs investigation: {uri} is not proven app-local or app-embedded. This alone is not a rendering failure.");
                    }
                });
                Check("route-diagnostics", "diagnostic", () =>
                {
                    string? deployedHash = null;
                    if (File.Exists(expectedPath))
                    {
                        using var stream = File.OpenRead(expectedPath);
                        deployedHash = Convert.ToHexString(SHA256.HashData(stream));
                    }
                    Console.WriteLine("ROUTE_DIAGNOSTICS:" + JsonSerializer.Serialize(new
                    {
                        RegistrarPath = registrar.GetFont(Alias),
                        ConventionalPath = expectedPath,
                        ConventionalFileHash = deployedHash,
                        EmbeddedLoaderCalls = embeddedCalls,
                        EmbeddedPaths = embeddedPaths,
                        Warnings = warnings,
                    }));
                });
            }
            finally
            {
                ((IElementHandler)image).DisconnectHandler();
                ((IElementHandler)label).DisconnectHandler();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            results.Add(new("scenario-initialization", "fixture", false, exception.ToString()));
        }

        Console.WriteLine("FONT_SCENARIO_RESULT:" + JsonSerializer.Serialize(results));
        if (results.Count != 9 || results.Any(result => (result.Category is "fixture" or "diagnostic") && !result.Passed))
            return 3;
        if (results.Any(result => result.Category == "rendering" && !result.Passed))
            return 1;
        return results.Any(result => result.Category == "portability" && !result.Passed) ? 4 : 0;

        void Check(string name, string category, Action assertion)
        {
            try
            {
                assertion();
                results.Add(new(name, category, true, null));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"FAIL {name}: {exception}");
                results.Add(new(name, category, false, exception.ToString()));
            }
        }
    }

    static bool SamePath(string? actual, string expected)
        => actual != null && string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    static NativeControl RequireControl(NativeControl? control)
        => control ?? throw new InvalidOperationException("Fixture failure: trusted native control did not pass.");

    static NativeControl LoadControl()
    {
        var path = Environment.GetEnvironmentVariable("MAUI_WPF_CONTROL_FONT");
        Require(!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path),
            "Fixture failure: supply an absolute MAUI_WPF_CONTROL_FONT path.");
        path = Path.GetFullPath(path!);
        Require(!IsUnder(path, AppContext.BaseDirectory) && !IsUnder(path, Environment.CurrentDirectory),
            "Fixture failure: the control must be outside the app base and test working directory.");
        using (var stream = File.OpenRead(path))
            Require(Convert.ToHexString(SHA256.HashData(stream)) == FontHash, "Fixture failure: control font hash mismatch.");
        var family = Fonts.GetFontFamilies(new Uri(path).AbsoluteUri).Single(candidate =>
            candidate.GetTypefaces().Any(face => face.TryGetGlyphTypeface(out var glyph) && SamePath(glyph.FontUri.LocalPath, path)));
        var typeface = new Typeface(family, System.Windows.FontStyles.Normal,
            System.Windows.FontWeights.Normal, System.Windows.FontStretches.Normal);
        Require(typeface.TryGetGlyphTypeface(out var native), "Fixture failure: no physical control typeface.");
        Require(SamePath(native.FontUri.LocalPath, path) && native.FamilyNames.Values.Contains("Open Sans"),
            "Fixture failure: control resolved the wrong native font.");
        Require(Text.All(c => native.CharacterToGlyphMap.TryGetValue(c, out var glyph) && glyph != 0),
            "Fixture failure: control cmap is missing text glyphs.");
        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = Text,
            FontFamily = family,
            FontSize = 32,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };
        var label = Render(textBlock);
        var runs = SnapshotRuns(VisualTreeHelper.GetDrawing(textBlock));
        Require(runs.Length > 0 && runs.All(run => run.Uri.IsFile && SamePath(run.Uri.LocalPath, path) &&
            run.FontHash == FontHash && run.Indices.All(index => index != 0)),
            "Fixture failure: control GlyphRuns did not use its physical font.");
        var labelPixels = Pixels(label);
        Require(Enumerable.Range(0, labelPixels.Length / 4).Count(i => labelPixels[i * 4] < 250 && labelPixels[i * 4 + 3] > 0) > 30,
            "Fixture failure: control Label contains no rendered text.");

        var text = new FormattedText("A", CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            typeface, 32, Brushes.Black, 1);
        var image = new RenderTargetBitmap((int)Math.Ceiling(text.WidthIncludingTrailingWhitespace) + 2,
            (int)Math.Ceiling(text.Height) + 2, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawText(text, new System.Windows.Point(1, 1));
        image.Render(visual);
        image.Freeze();
        var pixels = Pixels(image);
        Require(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 3] > 0) > 30,
            "Fixture failure: control image contains no rendered glyph.");
        Console.WriteLine($"CONTROL font SHA256: {FontHash}; family={string.Join(",", native.FamilyNames.Values)}; URI={native.FontUri}");
        foreach (var run in runs)
            Console.WriteLine($"CONTROL GlyphRun: {run.Uri}; font SHA256={run.FontHash}; indices={string.Join(",", run.Indices)}");
        Console.WriteLine($"CONTROL label pixel SHA256: {Convert.ToHexString(SHA256.HashData(labelPixels))}");
        return new(labelPixels, image, runs);
    }

    static bool IsUnder(string path, string directory)
    {
        var root = Path.GetFullPath(directory);
        if (!Path.EndsInDirectorySeparator(root))
            root += Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    static NativeRun[] SnapshotRuns(Drawing? drawing)
        => GlyphRuns(drawing).Select(run => new NativeRun(run.GlyphTypeface.FontUri, HashFont(run.GlyphTypeface),
            run.GlyphIndices.ToArray(), run.GlyphTypeface.FamilyNames.Values.ToArray(),
            Text.All(c => run.GlyphTypeface.CharacterToGlyphMap.TryGetValue(c, out var glyph) && glyph != 0))).ToArray();

    static string HashFont(GlyphTypeface font)
    {
        using var stream = font.GetFontStream();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    static string? PortableOrigin(Uri uri, string[] embeddedPaths)
    {
        if (uri.IsFile)
        {
            var file = new FileInfo(uri.LocalPath);
            var physicalPath = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? file.FullName;
            if (IsUnder(physicalPath, AppContext.BaseDirectory))
                return "app-local-file";
            if (embeddedPaths.Any(path => SamePath(path, file.FullName)))
                return "observed-embedded-extraction";
            return null;
        }

        const string prefix = "pack://application:,,,/";
        if (!uri.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var resource = Uri.UnescapeDataString(uri.AbsoluteUri[prefix.Length..]);
        var component = resource.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        var assembly = component < 0
            ? typeof(RegisteredFontsScenario).Assembly
            : AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, resource[..component].Split(';')[0], StringComparison.Ordinal));
        return assembly != null && !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location) &&
            IsUnder(assembly.Location, AppContext.BaseDirectory)
            ? "app-assembly-resource"
            : null;
    }

    static BitmapSource Render(System.Windows.Controls.TextBlock text)
    {
        text.Foreground = Brushes.Black;
        text.Background = Brushes.White;
        text.Measure(new System.Windows.Size(160, 64));
        text.Arrange(new System.Windows.Rect(0, 0, 160, 64));
        var bitmap = new RenderTargetBitmap(160, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(text);
        bitmap.Freeze();
        return bitmap;
    }

    static IEnumerable<GlyphRun> GlyphRuns(Drawing? drawing)
    {
        if (drawing is GlyphRunDrawing glyph)
            yield return glyph.GlyphRun;
        else if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var run in GlyphRuns(child))
                    yield return run;
    }

    static byte[] Pixels(BitmapSource bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    sealed record CaseResult(string Name, string Category, bool Passed, string? Error);
    sealed record NativeRun(Uri Uri, string FontHash, ushort[] Indices, string[] Families, bool HasTextGlyphs);
    sealed record NativeControl(byte[] LabelPixels, BitmapSource Image, NativeRun[] Runs);

    sealed class ObservedFontLoader : IEmbeddedFontLoader
    {
        public int Calls { get; private set; }
        public List<string> Paths { get; } = [];
        public string? LoadFont(EmbeddedFont font)
        {
            Calls++;
            var path = new WPFEmbeddedFontLoader().LoadFont(font);
            if (path != null)
                Paths.Add(path);
            return path;
        }
    }

    sealed class FontLogger : ILogger<WPFFontManager>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (level < LogLevel.Warning)
                return;
            var message = formatter(state, exception) + (exception == null ? "" : " " + exception.Message);
            Warnings.Add(message);
            Console.Error.WriteLine(message);
        }
    }
}
