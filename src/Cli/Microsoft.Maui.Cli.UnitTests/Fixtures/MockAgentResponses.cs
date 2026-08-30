namespace Microsoft.Maui.Cli.UnitTests.Fixtures;

internal static class MockAgentResponses
{
    /// <summary>One violation, one observation, and one incomplete rule with partial coverage.</summary>
    public const string LayoutDiagnostics = """
        {
          "schemaVersion": "1.0",
          "ruleSetVersion": "1.0",
          "capturedUtc": "2026-04-01T12:00:00.000Z",
          "platform": "MacCatalyst",
          "scope": {
            "maxElements": 2000,
            "elementsExamined": 3,
            "truncated": false,
            "windowBounds": { "x": 0, "y": 0, "width": 1440, "height": 900 }
          },
          "coverage": {
            "overall": "partial",
            "rules": [
              { "ruleId": "layout.visible-zero-area", "support": "full", "confidence": "high", "evaluated": 3, "skipped": 0, "limitations": [] },
              { "ruleId": "layout.outside-window", "support": "partial", "confidence": "high", "evaluated": 2, "skipped": 1, "limitations": ["Requires platform-resolved window bounds."] }
            ],
            "limitations": ["Findings are derived from managed MAUI layout state only."],
            "neverCaptured": ["Element Text/Value content"]
          },
          "summary": { "violations": 1, "observations": 1, "incomplete": 1 },
          "findings": [
            {
              "id": "layout.visible-zero-area:label1:area",
              "ruleId": "layout.visible-zero-area",
              "outcome": "violation",
              "confidence": "high",
              "message": "Label is visible and realized but was arranged with a non-positive width (0x20).",
              "explanation": "A realized element whose arranged rectangle has no area occupies no space on screen.",
              "element": { "id": "label1", "type": "Label", "automationId": "Title", "sourceFile": "Views/MainPage.xaml", "sourceLine": 12 },
              "evidence": { "frame": { "x": 0, "y": 0, "width": 0, "height": 20 } },
              "limitations": ["An element that is intentionally collapsed matches this rule."]
            },
            {
              "id": "layout.desired-size-constrained:label2:desired",
              "ruleId": "layout.desired-size-constrained",
              "outcome": "observation",
              "confidence": "medium",
              "message": "Label measured 300x20 but was arranged 120x20.",
              "explanation": "This is normal whenever a parent intentionally constrains a child.",
              "element": { "id": "label2", "type": "Label" },
              "limitations": []
            },
            {
              "id": "layout.outside-window:scope:incomplete",
              "ruleId": "layout.outside-window",
              "outcome": "incomplete",
              "confidence": "high",
              "message": "layout.outside-window could not be evaluated for 1 element(s).",
              "explanation": "Managed layout state did not expose the measurements the rule requires.",
              "evidence": { "affectedElements": 1 },
              "limitations": ["An unevaluated element is reported as incomplete and must never be read as a pass."]
            }
          ]
        }
        """;

    public const string ProfilerCapabilities = """
        {
          "available": true,
          "supportedInBuild": true,
          "featureEnabled": true,
          "platform": "MacCatalyst",
          "managedMemorySupported": true,
          "nativeMemorySupported": false,
          "processMemorySupported": true,
          "gcSupported": true,
          "cpuPercentSupported": true,
          "fpsSupported": false,
          "frameTimingsEstimated": true,
          "nativeFrameTimingsSupported": false,
          "jankEventsSupported": false,
          "uiThreadStallSupported": false,
          "threadCountSupported": true
        }
        """;

    public const string ProfilerSessionEnvelope = """
        {
          "stopToken": "qa-stop-token",
          "session": {
            "sessionId": "session-1",
            "startedAtUtc": "2026-04-01T12:00:00Z",
            "sampleIntervalMs": 250,
            "isActive": true
          }
        }
        """;

    public const string ProfilerStoppedSessionEnvelope = """
        {
          "session": {
            "sessionId": "session-1",
            "startedAtUtc": "2026-04-01T12:00:00Z",
            "sampleIntervalMs": 250,
            "isActive": false
          },
          "batch": {
            "sessionId": "session-1",
            "isActive": false,
            "samples": [
              { "tsUtc": "2026-04-01T12:00:00Z", "managedBytes": 1000000, "gc0": 1, "gc1": 0, "gc2": 0, "cpuPercent": 12.5, "threadCount": 20, "frameSource": "estimated", "frameQuality": "low", "jankFrameCount": 0, "uiThreadStallCount": 0, "fps": 58.0 },
              { "tsUtc": "2026-04-01T12:00:01Z", "managedBytes": 1500000, "gc0": 3, "gc1": 1, "gc2": 0, "cpuPercent": 42.0, "threadCount": 24, "frameSource": "estimated", "frameQuality": "low", "jankFrameCount": 2, "uiThreadStallCount": 1, "fps": 31.0 }
            ],
            "markers": [
              { "tsUtc": "2026-04-01T12:00:00Z", "type": "navigation", "name": "push" },
              { "tsUtc": "2026-04-01T12:00:01Z", "type": "user.action", "name": "tap" }
            ],
            "spans": [],
            "sampleCursor": 2,
            "markerCursor": 2,
            "spanCursor": 0,
            "sampleMetadata": { "oldestCursor": 0, "latestCursor": 2, "lostCount": 4, "availableCount": 2 },
            "markerMetadata": { "oldestCursor": 0, "latestCursor": 2, "lostCount": 0, "availableCount": 2 },
            "spanMetadata": { "oldestCursor": 0, "latestCursor": 0, "lostCount": 0, "availableCount": 0 }
          },
          "hotspots": [
            { "kind": "ui.operation", "name": "MainPage.Appearing", "screen": "//main", "count": 3, "errorCount": 0, "avgDurationMs": 40.0, "p95DurationMs": 90.0, "maxDurationMs": 95.0 }
          ]
        }
        """;

    public const string ProfilerBatch = """
        {
          "sessionId": "session-1",
          "isActive": true,
          "samples": [
            { "tsUtc": "2026-04-01T12:00:00Z", "managedBytes": 1000000, "gc0": 1, "gc1": 0, "gc2": 0, "cpuPercent": 12.5, "threadCount": 20, "frameSource": "estimated", "frameQuality": "low", "jankFrameCount": 0, "uiThreadStallCount": 0, "fps": 58.0 },
            { "tsUtc": "2026-04-01T12:00:01Z", "managedBytes": 1500000, "gc0": 3, "gc1": 1, "gc2": 0, "cpuPercent": 42.0, "threadCount": 24, "frameSource": "estimated", "frameQuality": "low", "jankFrameCount": 2, "uiThreadStallCount": 1, "fps": 31.0 }
          ],
          "markers": [
            { "tsUtc": "2026-04-01T12:00:00Z", "type": "navigation", "name": "push" },
            { "tsUtc": "2026-04-01T12:00:01Z", "type": "user.action", "name": "tap" }
          ],
          "spans": [],
          "sampleCursor": 2,
          "markerCursor": 2,
          "spanCursor": 0,
          "sampleMetadata": { "oldestCursor": 0, "latestCursor": 2, "lostCount": 4, "availableCount": 2 },
          "markerMetadata": { "oldestCursor": 0, "latestCursor": 2, "lostCount": 0, "availableCount": 2 },
          "spanMetadata": { "oldestCursor": 0, "latestCursor": 0, "lostCount": 0, "availableCount": 0 }
        }
        """;

    public const string ProfilerHotspots = """
        [
          { "kind": "ui.operation", "name": "MainPage.Appearing", "screen": "//main", "count": 3, "errorCount": 0, "avgDurationMs": 40.0, "p95DurationMs": 90.0, "maxDurationMs": 95.0 }
        ]
        """;

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
          "profilerSession": {
            "sessionId": "session-1",
            "startedAtUtc": "2026-04-01T12:00:00Z",
            "sampleIntervalMs": 250,
            "isActive": true
          },
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
            "ui.actions": { "version": 1, "features": ["tap", "fill", "batch"] },
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
