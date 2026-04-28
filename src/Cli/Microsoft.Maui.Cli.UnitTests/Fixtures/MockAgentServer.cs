using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

public sealed class MockAgentServer : IAsyncDisposable
{
    private readonly List<RecordedRequest> _recordedRequests = [];
    private readonly object _lock = new();
    private WebApplication? _app;

    public int Port { get; private set; }

    public IReadOnlyList<RecordedRequest> RecordedRequests
    {
        get
        {
            lock (_lock)
                return _recordedRequests.ToList();
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();

        _app = builder.Build();

        _app.Use(async (context, next) =>
        {
            string? body = null;
            if (context.Request.ContentLength > 0 || context.Request.ContentType is not null)
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
                body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }

            lock (_lock)
            {
                _recordedRequests.Add(new RecordedRequest
                {
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? string.Empty,
                    QueryString = context.Request.QueryString.Value ?? string.Empty,
                    Body = body
                });
            }

            await next();
        });

        RegisterAgentEndpoints(_app);
        RegisterUiEndpoints(_app);
        RegisterDeviceEndpoints(_app);
        RegisterStorageEndpoints(_app);
        RegisterWebViewEndpoints(_app);
        RegisterNetworkEndpoints(_app);

        await _app.StartAsync();
        Port = _app.Urls.Select(url => new Uri(url).Port).First();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null)
            return;

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public void ClearRecordedRequests()
    {
        lock (_lock)
            _recordedRequests.Clear();
    }

    private static void RegisterAgentEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/agent/status", () => Results.Content(MockAgentResponses.AgentStatus, "application/json"));
        app.MapGet("/api/v1/agent/capabilities", () => Results.Content(MockAgentResponses.AgentCapabilities, "application/json"));
    }

    private static void RegisterUiEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/ui/tree", () => Results.Content(MockAgentResponses.VisualTree, "application/json"));
        app.MapGet("/api/v1/ui/elements", () => Results.Content(MockAgentResponses.QueryElements, "application/json"));
        app.MapGet("/api/v1/ui/elements/{id}", (string id) => Results.Content(MockAgentResponses.SingleElement(id), "application/json"));
        app.MapGet("/api/v1/ui/elements/{id}/properties/{name}", (string id, string name) =>
            Results.Content($$"""{"id":"{{id}}","property":"{{name}}","value":"Hello, World!"}""", "application/json"));
        app.MapPut("/api/v1/ui/elements/{id}/properties/{name}", () =>
            Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapGet("/api/v1/ui/hit-test", () => Results.Content(MockAgentResponses.HitTestResult, "application/json"));
        app.MapGet("/api/v1/ui/screenshot", () => Results.File(MockAgentResponses.ScreenshotPng, "image/png"));

        foreach (var action in new[] { "tap", "fill", "clear", "focus", "navigate", "scroll", "resize", "back", "key", "gesture", "batch" })
            app.MapPost($"/api/v1/ui/actions/{action}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterDeviceEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/device/info", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/app", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/display", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/battery", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/connectivity", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/geolocation", () => Results.Content(MockAgentResponses.DeviceInfo, "application/json"));
        app.MapGet("/api/v1/device/version-tracking", () =>
            Results.Content("""{"currentVersion":"1.0.0","previousVersion":null,"firstInstalledVersion":"1.0.0"}""", "application/json"));
        app.MapGet("/api/v1/device/permissions", () =>
            Results.Content("""{"camera":"granted","location":"granted"}""", "application/json"));
        app.MapGet("/api/v1/device/permissions/{name}", (string name) =>
            Results.Content($$"""{"name":"{{name}}","status":"granted"}""", "application/json"));
        app.MapGet("/api/v1/device/sensors", () => Results.Content("""["accelerometer","gyroscope"]""", "application/json"));
        app.MapPost("/api/v1/device/sensors/{sensor}/start", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapPost("/api/v1/device/sensors/{sensor}/stop", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterStorageEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/storage/preferences", () => Results.Content(MockAgentResponses.PreferencesList, "application/json"));
        app.MapGet("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.PreferenceValue, "application/json"));
        app.MapPut("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.PreferenceValue, "application/json"));
        app.MapDelete("/api/v1/storage/preferences/{key}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapDelete("/api/v1/storage/preferences", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));

        app.MapGet("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.SecureStorageValue, "application/json"));
        app.MapPut("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.SecureStorageValue, "application/json"));
        app.MapDelete("/api/v1/storage/secure/{key}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapDelete("/api/v1/storage/secure", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));

        app.MapGet("/api/v1/storage/roots", () => Results.Content(MockAgentResponses.StorageRoots, "application/json"));
        app.MapGet("/api/v1/storage/files", () => Results.Content(MockAgentResponses.FilesList, "application/json"));
        app.MapGet("/api/v1/storage/files/{path}", (string path) => Results.Content(MockAgentResponses.FileDownload(path), "application/json"));
        app.MapPut("/api/v1/storage/files/{path}", (string path) => Results.Content(MockAgentResponses.FileUpload(path), "application/json"));
        app.MapDelete("/api/v1/storage/files/{path}", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
    }

    private static void RegisterWebViewEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/webview/contexts", () => Results.Content(MockAgentResponses.WebViews, "application/json"));
        app.MapGet("/api/v1/webview/source", () => Results.Content(MockAgentResponses.WebViewSource, "text/html"));
        app.MapGet("/api/v1/webview/dom", () => Results.Content("""{"root":{"tag":"html"}}""", "application/json"));
        app.MapGet("/api/v1/webview/dom/query", () => Results.Content("""{"matches":[{"tag":"div","id":"app"}]}""", "application/json"));
        app.MapGet("/api/v1/webview/network", () => Results.Content("""{"entries":[]}""", "application/json"));
        app.MapGet("/api/v1/webview/console", () => Results.Content("""{"entries":[]}""", "application/json"));
        app.MapGet("/api/v1/webview/screenshot", () => Results.File(MockAgentResponses.ScreenshotPng, "image/png"));

        app.MapPost("/api/v1/webview/evaluate", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var method = JsonDocument.Parse(body).RootElement.GetProperty("method").GetString() ?? string.Empty;
            return Results.Content(MockAgentResponses.WebViewEvaluate(method), "application/json");
        });
    }

    private static void RegisterNetworkEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/network/requests", () => Results.Content("""[]""", "application/json"));
        app.MapGet("/api/v1/network/requests/{id}", (string id) => Results.Content($$"""{"id":"{{id}}"}""", "application/json"));
        app.MapDelete("/api/v1/network/requests", () => Results.Content(MockAgentResponses.ActionSuccess, "application/json"));
        app.MapGet("/api/v1/logs", () => Results.Content("""[{"level":"info","message":"ok"}]""", "application/json"));
    }
}

public sealed class RecordedRequest
{
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string QueryString { get; init; } = string.Empty;
    public string? Body { get; init; }
}
