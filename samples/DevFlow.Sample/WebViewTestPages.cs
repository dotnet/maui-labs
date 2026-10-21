using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Dispatching;

namespace DevFlow.Sample;

public partial class AppShell
{
    // The backend samples link AppShell, but do not compile these standard-MAUI fixtures.
    partial void AddWebViewTestPages()
    {
        Items.Add(new ShellContent
        {
            Title = "WebView", Route = "webview", ContentTemplate = new DataTemplate(typeof(WebViewTestPage))
        });
        Items.Add(new ShellContent
        {
            Title = "HybridWebView", Route = "hybrid", ContentTemplate = new DataTemplate(typeof(HybridWebViewTestPage))
        });
        Items.Add(new ShellContent
        {
            Title = "Mixed WebViews", Route = "mixedwebviews", ContentTemplate = new DataTemplate(typeof(MixedWebViewTestPage))
        });
    }
}

public sealed class WebViewTestPage : ContentPage
{
    readonly Grid _grid = WebViewTestContent.CreateRows(3);
    readonly Label _status = new() { AutomationId = "StandardNavigationStatus" };
    readonly Label _documentStatus = new() { AutomationId = "StandardDocumentStatus", IsVisible = false };
    WebView? _view;

    public WebViewTestPage()
    {
        Title = "Standard WebView";
        var buttons = new HorizontalStackLayout();
        var reset = new Button { AutomationId = "ResetStandardWebViewButton", Text = "Restore HTML fixture" };
        reset.Clicked += async (_, _) => await RestoreAsync(recreate: false);
        var recreate = new Button { AutomationId = "RecreateStandardWebViewButton", Text = "Recreate host" };
        recreate.Clicked += async (_, _) => await RestoreAsync(recreate: true);
        buttons.Add(reset);
        buttons.Add(recreate);
        _grid.Add(buttons, 0, 0);
        _grid.Add(_status, 0, 1);
        _grid.Add(_documentStatus, 0, 1);
        Content = _grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_view is null)
            await RestoreAsync(recreate: true);
    }

    async Task RestoreAsync(bool recreate)
    {
        try
        {
            var source = await WebViewTestContent.LoadStandardSourceAsync("Standard WebView fixture");
            if (recreate || _view is null)
            {
                if (_view is not null)
                {
                    _grid.Remove(_view);
                    _view.Handler?.DisconnectHandler();
                }
                _view = new WebView { AutomationId = "StandardWebView" };
                _view.Navigated += async (sender, args) =>
                {
                    _status.Text = $"{args.Result}: {args.Url}";
                    if (sender is WebView view && ReferenceEquals(view, _view))
                    {
                        _documentStatus.Text = $"Inspecting native document after {args.Result}: {args.Url}";
                        await ReadDocumentStatusAsync(view);
                    }
                };
                _grid.Add(_view, 0, 2);
            }
            _status.Text = "Loading packaged HTML";
            _view.Source = source;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _status.Text = $"Unable to load WebView fixture: {ex.Message}";
        }
    }

    async Task ReadDocumentStatusAsync(WebView view)
    {
        try
        {
            await view.Dispatcher.DispatchAsync(async () =>
            {
                if (!ReferenceEquals(view, _view) || view.Handler is null)
                    return;
                _documentStatus.Text = await view.EvaluateJavaScriptAsync(
                    """
                    JSON.stringify({
                        location: location.href,
                        title: document.title,
                        readyState: document.readyState,
                        body: document.body?.innerText.slice(0, 2048)
                    })
                    """);
                Console.WriteLine($"Standard WebView document: {_documentStatus.Text}");
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to inspect the standard WebView document: {ex}");
            await _documentStatus.Dispatcher.DispatchAsync(() =>
                _documentStatus.Text = $"Native document inspection failed: {ex.Message}");
        }
    }
}

public sealed class HybridWebViewTestPage : ContentPage
{
    readonly Grid _grid = WebViewTestContent.CreateRows(3);
    readonly Label _status = new() { AutomationId = "HybridMessageStatus", Margin = 8 };
    HybridWebView? _view;

    public HybridWebViewTestPage()
    {
        Title = "HybridWebView";
        var buttons = new HorizontalStackLayout();
        var reset = new Button { AutomationId = "ResetHybridWebViewButton", Text = "Recreate packaged host" };
        reset.Clicked += (_, _) => Restore();
        var send = new Button { AutomationId = "HybridSendMessageButton", Text = "Send JSON to JavaScript" };
        send.Clicked += (_, _) => WebViewTestContent.SendMessage(_view!, _status);
        buttons.Add(reset);
        buttons.Add(send);
        _grid.Add(buttons, 0, 0);
        _grid.Add(_status, 0, 1);
        Content = _grid;
        Restore();
    }

    void Restore()
    {
        if (_view is not null)
        {
            _grid.Remove(_view);
            _view.Handler?.DisconnectHandler();
        }
        _status.Text = "Waiting for original Hybrid ready message";
        _view = WebViewTestContent.CreateHybrid("HybridWebView", _status);
        _grid.Add(_view, 0, 2);
    }
}

public sealed class MixedWebViewTestPage : ContentPage
{
    readonly Grid _grid = WebViewTestContent.CreateRows(3);
    readonly Grid _hosts = new()
    {
        ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }
    };
    readonly Label _status = new() { AutomationId = "MixedHybridMessageStatus" };
    HybridWebView? _hybrid;
    bool _loaded;

    public MixedWebViewTestPage()
    {
        Title = "Mixed WebView Hosts";
        var buttons = new HorizontalStackLayout();
        var reset = new Button { AutomationId = "ResetMixedWebViewsButton", Text = "Recreate all three hosts" };
        reset.Clicked += async (_, _) => await RestoreAsync();
        var send = new Button { AutomationId = "MixedHybridSendMessageButton", Text = "Send to Hybrid" };
        send.Clicked += (_, _) => WebViewTestContent.SendMessage(_hybrid!, _status);
        buttons.Add(reset);
        buttons.Add(send);
        var nativeClip = new AbsoluteLayout
        {
            AutomationId = "WebViewNativeClipHost",
            WidthRequest = 40,
            HeightRequest = 40,
            MinimumWidthRequest = 40,
            MinimumHeightRequest = 40,
            MaximumWidthRequest = 40,
            MaximumHeightRequest = 40,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            IsClippedToBounds = true
        };
        var clippedLabel = new Label
        {
            AutomationId = "WebViewNativeClipped",
            Text = "Clipped native fixture",
            LineBreakMode = LineBreakMode.NoWrap,
            WidthRequest = 80,
            HeightRequest = 80,
            MinimumWidthRequest = 80,
            MinimumHeightRequest = 80,
            MaximumWidthRequest = 80,
            MaximumHeightRequest = 80,
            // Fill prevents Android intrinsic-width shrinking; no background keeps WinUI sizing on the TextBlock.
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        AbsoluteLayout.SetLayoutBounds(clippedLabel, new Rect(0, 0, 80, 80));
        nativeClip.Add(clippedLabel);
        var toolbar = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }
        };
        toolbar.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = buttons }, 0, 0);
        toolbar.Add(nativeClip, 1, 0);
        _grid.Add(toolbar, 0, 0);
        _grid.Add(_status, 0, 1);
        _grid.Add(_hosts, 0, 2);
        Content = _grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
            await RestoreAsync();
    }

    async Task RestoreAsync()
    {
        try
        {
            var source = await WebViewTestContent.LoadStandardSourceAsync("Mixed standard WebView fixture");
            foreach (var child in _hosts.Children.OfType<View>().ToArray())
            {
                _hosts.Remove(child);
                child.Handler?.DisconnectHandler();
            }
            // An unnamed host and duplicate ids intentionally require actual owner correlation.
            _hosts.Add(new WebView { Source = source }, 0, 0);
            _status.Text = "Waiting for original Hybrid ready message";
            _hybrid = WebViewTestContent.CreateHybrid("MixedDuplicateWebView", _status);
            _hosts.Add(_hybrid, 1, 0);
            var blazor = new BlazorWebView
            {
                HostPage = "wwwroot/index.html",
                AutomationId = "MixedDuplicateWebView"
            };
            blazor.RootComponents.Add(new RootComponent
            {
                Selector = "#app",
                ComponentType = typeof(Components.Routes)
            });
            _hosts.Add(blazor, 2, 0);
            _loaded = true;
        }
        catch (Exception ex)
        {
            _loaded = false;
            Console.WriteLine(ex);
            _status.Text = $"Unable to load mixed fixture: {ex.Message}";
        }
    }
}

static class WebViewTestContent
{
    public static Grid CreateRows(int count)
    {
        var grid = new Grid();
        for (var row = 0; row < count; row++)
            grid.RowDefinitions.Add(new RowDefinition(row == count - 1 ? GridLength.Star : GridLength.Auto));
        return grid;
    }

    public static HybridWebView CreateHybrid(string automationId, Label status)
    {
        void UpdateStatus(string text)
        {
            if (!status.Dispatcher.IsDispatchRequired)
                status.Text = text;
            else if (!status.Dispatcher.Dispatch(() => status.Text = text))
                throw new InvalidOperationException("Unable to dispatch the Hybrid message to its UI.");
        }

        var view = new HybridWebView
        {
            AutomationId = automationId,
            HybridRoot = "webview-fixture",
            DefaultFile = "index.html"
        };
#if WINDOWS
        view.WebViewInitializing += (_, args) =>
        {
            var languages = global::Windows.Globalization.ApplicationLanguages.Languages;
            if (languages.Count > 0)
            {
                // Match WinUI's default environment shared by the standard and Blazor WebViews.
                var platformArgs = args.PlatformArgs
                    ?? throw new InvalidOperationException("Windows HybridWebView initialization requires platform arguments.");
                var options = platformArgs.EnvironmentOptions
                    ??= new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions();
                options.Language = languages[0];
            }
#if DEBUG
            var language = languages.Count > 0 ? languages[0] : "<default: no application language>";
            WinUI.App.WriteRuntimeDiagnostic(
                $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} Hybrid '{automationId}' environment language={language}");
#endif
        };
#endif
#if WINDOWS && DEBUG
        Microsoft.UI.Xaml.Controls.WebView2? observed = null;
        global::Windows.Foundation.TypedEventHandler<Microsoft.UI.Xaml.Controls.WebView2,
            Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs> initialized = (native, args) =>
            WinUI.App.WriteRuntimeDiagnostic(
                $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} Hybrid '{automationId}' CoreWebView2Initialized: " +
                $"corePresent={native.CoreWebView2 is not null}, loaded={native.IsLoaded}, exception={args.Exception}");
        view.HandlerChanging += (_, _) =>
        {
            if (observed is not null)
                observed.CoreWebView2Initialized -= initialized;
            observed = null;
        };
        view.HandlerChanged += (_, _) =>
        {
            if (view.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native)
                return;
            observed = native;
            native.CoreWebView2Initialized += initialized;
            WinUI.App.WriteRuntimeDiagnostic(
                $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} Hybrid '{automationId}' handler attached: " +
                $"corePresent={native.CoreWebView2 is not null}, loaded={native.IsLoaded}");
        };
#endif
        view.RawMessageReceived += (_, args) =>
        {
            try
            {
                var message = JsonSerializer.Deserialize(
                    args.Message ?? throw new JsonException("Missing Hybrid message."),
                    WebViewTestJsonContext.Default.WebViewTestMessage)
                    ?? throw new JsonException("Missing Hybrid message value.");
                UpdateStatus($"{message.Action}: {message.Value}");
            }
            catch (JsonException ex)
            {
                Console.WriteLine(ex);
                UpdateStatus($"Invalid Hybrid message: {ex.Message}");
            }
        };
        return view;
    }

    public static void SendMessage(HybridWebView view, Label status)
    {
        status.Text = "Sending native message";
        view.SendRawMessage(JsonSerializer.Serialize(
            new WebViewTestMessage("native-message", "native-to-js-ok"),
            WebViewTestJsonContext.Default.WebViewTestMessage));
    }

    public static async Task<HtmlWebViewSource> LoadStandardSourceAsync(string title)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync("webview-fixture/index.html");
        using var reader = new StreamReader(stream);
        return new HtmlWebViewSource
        {
            Html = (await reader.ReadToEndAsync())
                .Replace("""<script src="_framework/hybridwebview.js"></script>""", "")
                .Replace("<title>Hybrid WebView fixture</title>", $"<title>{title}</title>")
                .Replace("data-host=\"hybrid\"", "data-host=\"standard\"")
        };
    }
}

public sealed record WebViewTestMessage(string Action, string? Value);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebViewTestMessage))]
internal partial class WebViewTestJsonContext : JsonSerializerContext;
