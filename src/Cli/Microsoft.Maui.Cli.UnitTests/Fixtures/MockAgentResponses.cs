namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

internal static class MockAgentResponses
{
    public const string AgentStatus = """
        {
          "timestamp": "2026-04-01T12:00:00Z",
          "agent": {
            "name": "Microsoft.Maui.DevFlow.Agent",
            "version": "0.1.0-test",
            "framework": ".NET MAUI",
            "frameworkVersion": "10.0.0"
          },
          "device": {
            "platform": "MacCatalyst",
            "deviceType": "Physical",
            "idiom": "Desktop",
            "displayDensity": 2.0,
            "windowCount": 1,
            "windowWidth": 1440,
            "windowHeight": 900
          },
          "app": {
            "name": "TestApp",
            "packageId": "com.example.testapp",
            "version": "1.0.0",
            "build": "42"
          },
          "capabilities": {
            "ui": true,
            "screenshots": true,
            "webview": true,
            "network": true,
            "logs": true,
            "sensors": true,
            "storage": true,
            "profiler": true
          },
          "running": true,
          "cdpReady": true,
          "cdpWebViewCount": 1
        }
        """;

    public const string AgentCapabilities = """
        {
          "ui": { "supported": true, "features": ["tree", "query", "tap", "fill", "batch"] },
          "webview": { "supported": true, "features": ["contexts", "evaluate", "source"] },
          "network": { "supported": true, "features": ["list", "detail", "clear"] },
          "logs": { "supported": true, "features": ["list", "stream"] },
          "sensors": { "supported": true, "features": ["list", "start", "stop"] },
          "storage": { "supported": true, "features": ["preferences", "secure-storage", "roots", "files"] },
          "profiler": { "supported": true, "features": ["capabilities", "sessions", "samples"] }
        }
        """;

    public const string VisualTree = """
        [
          {
            "id": "el-root",
            "type": "ContentPage",
            "automationId": "MainPage",
            "text": null,
            "isVisible": true,
            "isEnabled": true,
            "children": [
              {
                "id": "el-1",
                "type": "Button",
                "automationId": "ClickMeButton",
                "text": "Click Me",
                "isVisible": true,
                "isEnabled": true,
                "children": []
              }
            ]
          }
        ]
        """;

    public const string QueryElements = """
        [
          {
            "id": "el-1",
            "type": "Button",
            "automationId": "ClickMeButton",
            "text": "Click Me",
            "isVisible": true,
            "isEnabled": true
          }
        ]
        """;

    public static string SingleElement(string id) => $$"""
        {
          "id": "{{id}}",
          "type": "Button",
          "automationId": "ClickMeButton",
          "text": "Click Me",
          "isVisible": true,
          "isEnabled": true
        }
        """;

    public const string HitTestResult = """
        {
          "id": "el-1",
          "type": "Button",
          "automationId": "ClickMeButton"
        }
        """;

    public const string ActionSuccess = """{"success":true,"message":"ok"}""";

    public const string DeviceInfo = """
        {
          "manufacturer": "Apple",
          "model": "MacBookPro18,1",
          "name": "My Mac",
          "platform": "MacCatalyst",
          "versionString": "15.0"
        }
        """;

    public const string PreferencesList = """
        ["theme", "launchCount"]
        """;

    public const string PreferenceValue = """
        {
          "key": "theme",
          "value": "dark",
          "type": "string"
        }
        """;

    public const string SecureStorageValue = """
        {
          "key": "token",
          "value": "secret-value"
        }
        """;

    public const string StorageRoots = """
        {
          "roots": [
            {
              "id": "appData",
              "displayName": "App data",
              "kind": "appData",
              "isWritable": true,
              "isReadOnly": false,
              "isPersistent": true,
              "isBackedUp": true,
              "mayBeClearedBySystem": false,
              "isUserVisible": false,
              "supportedOperations": ["list", "download", "upload", "delete"]
            }
          ]
        }
        """;

    public const string FilesList = """
        {
          "root": "appData",
          "path": "logs",
          "entries": [
            {
              "name": "app.log",
              "type": "file",
              "size": 5,
              "lastModified": "2026-04-01T12:00:00Z"
            }
          ]
        }
        """;

    public static string FileDownload(string path) => $$"""
        {
          "root": "appData",
          "path": "{{path}}",
          "size": 5,
          "lastModified": "2026-04-01T12:00:00Z",
          "contentBase64": "aGVsbG8="
        }
        """;

    public static string FileUpload(string path) => $$"""
        {
          "success": true,
          "root": "appData",
          "path": "{{path}}",
          "size": 5,
          "lastModified": "2026-04-01T12:00:00Z"
        }
        """;

    public static byte[] ScreenshotPng { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public const string WebViews = """
        [
          {
            "id": "webview-1",
            "title": "Main BlazorWebView",
            "url": "https://0.0.0.0/",
            "isReady": true
          }
        ]
        """;

    public static string WebViewEvaluate(string method) => method switch
    {
        "Browser.getVersion" => """{"result":{"protocolVersion":"1.3","product":"Chrome/120.0","userAgent":"Mozilla/5.0"}}""",
        "Runtime.evaluate" => """{"result":{"result":{"type":"number","value":2,"description":"2"}}}""",
        "DOM.getDocument" => """{"result":{"root":{"nodeId":1,"nodeType":9,"nodeName":"#document","childNodeCount":1}}}""",
        "Page.reload" => """{"result":{}}""",
        _ => """{"result":{}}"""
    };

    public const string WebViewSource = """
        <!DOCTYPE html>
        <html><body><div id="app">Hello Blazor</div></body></html>
        """;
}
