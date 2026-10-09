using System.Text.Json;
using System.Text.Json.Serialization;
#if MACOS
using Microsoft.Maui.Platforms.MacOS.Controls;
#else
using Microsoft.AspNetCore.Components.WebView.Maui;
#endif

namespace DevFlow.Sample;

public sealed class WebViewTestPage : ContentPage
{
    readonly WebView _webView = new() { AutomationId = "StandardWebView" };
    bool _loaded;

    public WebViewTestPage()
    {
        Title = "Standard WebView";
        var reset = new Button
        {
            AutomationId = "ResetStandardWebViewButton",
            Text = "Restore HTML fixture"
        };
        reset.Clicked += async (_, _) => await LoadFixtureAsync();
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        grid.Children.Add(reset);
        Grid.SetRow(_webView, 1);
        grid.Children.Add(_webView);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        await LoadFixtureAsync();
    }

    async Task LoadFixtureAsync()
    {
        try
        {
            _webView.Source = await WebViewTestContent.LoadStandardSourceAsync("Standard WebView fixture", "standard");
        }
        catch (Exception ex)
        {
            _loaded = false;
            Console.WriteLine(ex);
            Content = new Label { Text = $"Unable to load WebView fixture: {ex.Message}" };
        }
    }
}

public sealed class HybridWebViewTestPage : ContentPage
{
    public HybridWebViewTestPage()
    {
        Title = "HybridWebView";
        var view = WebViewTestContent.CreateHybrid("HybridWebView");
        var status = new Label
        {
            AutomationId = "HybridMessageStatus",
            Text = "No message received",
            Margin = 8
        };
        view.RawMessageReceived += (_, args) =>
        {
            var message = JsonSerializer.Deserialize(
                args.Message ?? throw new JsonException("Missing Hybrid message."),
                WebViewTestJsonContext.Default.WebViewTestMessage)
                ?? throw new JsonException("Missing Hybrid message value.");
            status.Text = $"{message.Action}: {message.Value}";
        };
        var send = new Button
        {
            AutomationId = "HybridSendMessageButton",
            Text = "Send JSON to JavaScript"
        };
        send.Clicked += (_, _) => view.SendRawMessage(JsonSerializer.Serialize(
            new WebViewTestMessage("native-message", "native-to-js-ok"),
            WebViewTestJsonContext.Default.WebViewTestMessage));
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        grid.Children.Add(status);
        Grid.SetRow(send, 1);
        grid.Children.Add(send);
        Grid.SetRow(view, 2);
        grid.Children.Add(view);
        Content = grid;
    }
}

public sealed class MixedWebViewTestPage : ContentPage
{
    readonly WebView _standard = new();
    bool _loaded;

    public MixedWebViewTestPage()
    {
        Title = "Mixed WebView Hosts";
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            }
        };
        grid.Children.Add(_standard);
        var hybrid = WebViewTestContent.CreateHybrid("MixedHybridWebView");
        Grid.SetColumn(hybrid, 1);
        grid.Children.Add(hybrid);
#if MACOS
        var blazor = new MacOSBlazorWebView
#else
        var blazor = new BlazorWebView
#endif
        {
            HostPage = "wwwroot/index.html",
            AutomationId = "MixedBlazorWebView"
        };
#if MACOS
        blazor.RootComponents.Add(new BlazorRootComponent
#else
        blazor.RootComponents.Add(new RootComponent
#endif
        {
            Selector = "#app",
            ComponentType = typeof(Components.Routes)
        });
        Grid.SetColumn(blazor, 2);
        grid.Children.Add(blazor);
        Content = grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        try
        {
            _standard.Source = await WebViewTestContent.LoadStandardSourceAsync("Mixed standard WebView fixture", "mixed");
        }
        catch (Exception ex)
        {
            _loaded = false;
            Console.WriteLine(ex);
            Content = new Label { Text = $"Unable to load mixed fixture: {ex.Message}" };
        }
    }
}

static class WebViewTestContent
{
    public static HybridWebView CreateHybrid(string automationId)
        => new()
        {
            AutomationId = automationId,
            HybridRoot = "webview-fixture",
            DefaultFile = "index.html"
        };

    public static async Task<HtmlWebViewSource> LoadStandardSourceAsync(string title, string fixtureName)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("webview-fixture/index.html");
        using var reader = new StreamReader(stream);
        var html = (await reader.ReadToEndAsync())
            .Replace("""<script src="_framework/hybridwebview.js"></script>""", "")
            .Replace("<title>Hybrid WebView fixture</title>", $"<title>{title}</title>");
        var directory = Path.Combine(FileSystem.CacheDirectory, "webview-fixture", fixtureName);
        Directory.CreateDirectory(directory);
        using var secondStream = await FileSystem.OpenAppPackageFileAsync("webview-fixture/second.html");
        using var secondReader = new StreamReader(secondStream);
        var second = (await secondReader.ReadToEndAsync())
            .Replace("""<script src="_framework/hybridwebview.js"></script>""", "");
        await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), html);
        await File.WriteAllTextAsync(Path.Combine(directory, "second.html"), second);
        return new HtmlWebViewSource
        {
            Html = html,
            BaseUrl = directory + Path.DirectorySeparatorChar
        };
    }
}

public sealed record WebViewTestMessage(string Action, string? Value);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebViewTestMessage))]
internal partial class WebViewTestJsonContext : JsonSerializerContext;
