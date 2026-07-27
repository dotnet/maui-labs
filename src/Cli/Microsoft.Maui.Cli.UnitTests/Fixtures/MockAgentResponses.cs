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
            "profiler": true,
            "theme": true
          },
          "running": true,
          "cdpReady": true,
          "cdpWebViewCount": 1,
          "extensions": {
            "count": 1,
            "hash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
          }
        }
        """;

    public const string AgentCapabilities = """
        {
          "agent": {
            "name": "Microsoft.Maui.DevFlow.Agent",
            "version": "0.1.0-test",
            "framework": "maui",
            "frameworkVersion": "10.0.0"
          },
          "capabilities": {
            "ui.tree": { "version": 1, "features": ["tree", "query"] },
            "ui.actions": { "version": 2, "features": ["tap", "fill", "batch", "stale-capture-rejection"] },
            "webview": { "version": 1, "features": ["contexts", "evaluate", "source"] },
            "network": { "version": 1, "features": ["list", "detail", "clear"] },
            "logs": { "version": 1, "features": ["list", "stream"] },
            "device.sensors": { "version": 1, "features": ["list", "start", "stop"] },
            "storage.preferences": { "version": 1, "features": ["list", "get", "set", "delete", "clear"] },
            "storage.secure": { "version": 1, "features": ["get", "set", "delete", "clear"] },
            "storage.files": { "version": 1, "features": ["roots", "list", "download", "upload", "delete"] },
            "profiler": { "version": 1, "features": ["capabilities", "sessions", "samples"] },
            "theme": { "version": 1, "supported": true, "features": ["get", "set"] },
            "app.theme": { "version": 1, "supported": true, "features": ["get", "set"] },
            "com.example.diagnostics": { "version": 1, "features": ["build_info", "echo"] }
          },
          "extensions": {
            "com.example.diagnostics": {
              "version": "1.0.0",
              "description": "Sample diagnostics extension",
              "tools": [
                {
                  "name": "build_info",
                  "description": "Returns build information.",
                  "method": "GET",
                  "path": "/api/v1/ext/com.example.diagnostics/build-info",
                  "returns": { "type": "object" },
                  "annotations": {
                    "readOnly": true,
                    "idempotent": true,
                    "destructive": false,
                    "category": "diagnostics"
                  }
                },
                {
                  "name": "echo",
                  "description": "Echoes request parameters.",
                  "method": "POST",
                  "path": "/api/v1/ext/com.example.diagnostics/echo",
                  "parameters": { "type": "object" },
                  "returns": { "type": "object" },
                  "annotations": {
                    "readOnly": false,
                    "idempotent": true,
                    "destructive": false,
                    "category": "diagnostics"
                  }
                }
              ]
            }
          }
        }
        """;

    public const string LegacyAgentCapabilities = """
        {
          "agent": {
            "name": "Microsoft.Maui.DevFlow.Agent",
            "version": "0.1.0-legacy",
            "framework": "maui",
            "frameworkVersion": "10.0.0"
          },
          "capabilities": {
            "ui.tree": { "version": 1, "features": ["tree", "query"] },
            "ui.actions": { "version": 1, "features": ["tap", "fill", "batch"] }
          }
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

    public const string VisualTreeWithDetachedNativeRoot = """
        [
          {
            "id": "window-root",
            "type": "Window",
            "captureEpoch": 42,
            "registryGeneration": 7,
            "windowId": 0,
            "isVisible": true,
            "isEnabled": true,
            "windowBounds": {
              "x": 0,
              "y": 0,
              "width": 100,
              "height": 100
            },
            "children": [
              {
                "id": "page-root",
                "type": "ContentPage",
                "isVisible": true,
                "isEnabled": true,
                "windowBounds": {
                  "x": 0,
                  "y": 0,
                  "width": 100,
                  "height": 100
                },
                "children": []
              }
            ]
          },
          {
            "id": "native:window",
            "type": "Window",
            "origin": "native",
            "isVisible": false,
            "isEnabled": true,
            "children": [
              {
                "id": "native:dialog",
                "type": "Dialog",
                "origin": "native",
                "isVisible": true,
                "isEnabled": true,
                "windowBounds": {
                  "x": 10,
                  "y": 10,
                  "width": 80,
                  "height": 60
                },
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

    public const string DuplicateActionElements = """
        [
          {
            "id": "wrapper-grid",
            "type": "Grid",
            "fullType": "Microsoft.Maui.Controls.Grid",
            "automationId": "DuplicateActionTarget",
            "isVisible": true,
            "isEnabled": true,
            "captureEpoch": 42,
            "registryGeneration": 7
          },
          {
            "id": "native:registered:action-target",
            "type": "NavigationViewItem",
            "fullType": "Microsoft.UI.Xaml.Controls.NavigationViewItem",
            "automationId": "DuplicateActionTarget",
            "text": "Action target",
            "isVisible": true,
            "isEnabled": true,
            "captureEpoch": 42,
            "registryGeneration": 7,
            "capabilities": ["invoke", "set-value", "focus"]
          }
        ]
        """;

    public const string DistinctActionElements = """
        [
          {
            "id": "first-button",
            "type": "Button",
            "fullType": "Microsoft.Maui.Controls.Button",
            "text": "Shared action",
            "isVisible": true,
            "isEnabled": true,
            "captureEpoch": 51,
            "registryGeneration": 9
          },
          {
            "id": "native:registered:second-button",
            "type": "Button",
            "fullType": "Microsoft.UI.Xaml.Controls.Button",
            "origin": "native",
            "text": "Shared action",
            "isVisible": true,
            "isEnabled": true,
            "captureEpoch": 51,
            "registryGeneration": 9,
            "capabilities": ["invoke"]
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

    public static string HitTestResult(int captureEpoch, string parentId = "hit-parent") => $$"""
        {
          "captureEpoch": {{captureEpoch}},
          "registryGeneration": 7,
          "elements": [
            {
              "id": "hit-child",
              "type": "Label"
            },
            {
              "id": "{{parentId}}",
              "type": "Button"
            }
          ]
        }
        """;

    public static string ScrollableHitTestResult(int captureEpoch) => $$"""
        {
          "captureEpoch": {{captureEpoch}},
          "registryGeneration": 7,
          "elements": [
            {
              "id": "hit-child",
              "type": "Label"
            },
            {
              "id": "hit-scroll",
              "type": "ScrollView"
            },
            {
              "id": "hit-parent",
              "type": "Button"
            }
          ]
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

    public const string ThemeInfo = """
        {
          "theme": "dark",
          "requestedTheme": "dark",
          "userAppTheme": "dark",
          "effectiveTheme": "dark",
          "supportedThemes": ["light", "dark", "system"],
          "source": "app"
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
