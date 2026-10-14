using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class WebViewOperations(AgentClient client)
{
    internal async Task<string> EvaluateAsync(string expression, string? webview = null)
    {
        var root = await client.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true
        }, webview);

        if (root.TryGetProperty("result", out var evalResult))
        {
            if (evalResult.TryGetProperty("result", out var resultProp))
            {
                if (resultProp.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString() ?? "null";
                    if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
                        return CliJson.PrettyPrint(value);
                    return value.ToString();
                }
            }
            if (evalResult.TryGetProperty("exceptionDetails", out var exception))
            {
                var text = exception.TryGetProperty("text", out var t) ? t.GetString() : "Unknown error";
                return $"Error: {text}";
            }
        }

        // Response might be the raw chobitsu response
        if (root.TryGetProperty("result", out var rawResult) && rawResult.TryGetProperty("value", out var rawValue))
            return rawValue.GetString() ?? rawValue.ToString();

        return root.ToString();
    }

    internal async Task<JsonElement?> BrowserGetVersionAsync(string? webview = null) =>
        await client.SendCdpCommandAsync("Browser.getVersion", webviewId: webview);

    internal async Task<JsonElement?> DomGetDocumentAsync(string? webview = null) =>
        await client.SendCdpCommandAsync("DOM.getDocument", webviewId: webview);

    internal Task<string> DomQuerySelectorAsync(string selector, string? webview = null) =>
        EvaluateAsync($@"
                JSON.stringify((function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)});
                    if (!el) return null;
                    return {{
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 100) || null
                    }};
                }})())
            ", webview);

    internal Task<string> DomQuerySelectorAllAsync(string selector, string? webview = null) =>
        EvaluateAsync($@"
                JSON.stringify((function() {{
                    const els = document.querySelectorAll({CliJson.SerializeUntyped(selector, indented: false)});
                    return Array.from(els).map((el, i) => ({{
                        index: i,
                        tagName: el.tagName.toLowerCase(),
                        id: el.id || null,
                        className: el.className || null,
                        textContent: el.textContent?.trim().substring(0, 50) || null
                    }}));
                }})(), null, 2)
            ", webview);

    internal Task<string> DomGetOuterHtmlAsync(string selector, string? webview = null) =>
        EvaluateAsync($@"document.querySelector({CliJson.SerializeUntyped(selector, indented: false)})?.outerHTML || null", webview);

    internal async Task PageNavigateAsync(string url, string? webview = null) =>
        await client.SendCdpCommandAsync("Page.navigate", new JsonObject
        {
            ["url"] = url
        }, webview);

    internal async Task PageReloadAsync(string? webview = null) =>
        await client.SendCdpCommandAsync("Page.reload", webviewId: webview);

    internal async Task<string?> PageCaptureScreenshotAsync(string? webview = null)
    {
        var result = await client.SendCdpCommandAsync("Page.captureScreenshot", webviewId: webview);
        if (result.TryGetProperty("result", out var resultProp) &&
            resultProp.TryGetProperty("data", out var dataProp))
        {
            return dataProp.GetString();
        }

        return CliJson.PrettyPrint(result);
    }

    internal Task<string> InputDispatchClickAsync(string selector, string? webview = null) =>
        EvaluateAsync($@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)});
                    if (!el) return 'Error: Element not found';
                    el.click();
                    return 'Clicked: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
                }})()
            ", webview);

    internal async Task InputInsertTextAsync(string text, string? webview = null) =>
        await client.SendCdpCommandAsync("Input.insertText", new JsonObject
        {
            ["text"] = text
        }, webview);

    internal Task<string> InputFillAsync(string selector, string text, string? webview = null) =>
        EvaluateAsync($@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)});
                    if (!el) return 'Error: Element not found';

                    const text = {CliJson.SerializeUntyped(text, indented: false)};
                    if (el.isContentEditable) {{
                        el.textContent = text;
                    }} else {{
                        el.value = text;
                        el.focus();
                    }}
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    return 'Filled: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + ' with ' + text.length + ' chars';
                }})()
            ", webview);

    internal async Task<(bool IsReady, int WebViewCount)> GetStatusAsync()
    {
        // AgentStatus does not expose these legacy CDP fields.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await http.GetAsync($"{client.BaseUrl}/api/v1/agent/status");
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var cdpReady = root.TryGetProperty("cdpReady", out var cdpProp) && cdpProp.GetBoolean();
        var cdpCount = root.TryGetProperty("cdpWebViewCount", out var countProp) ? countProp.GetInt32() : 0;
        return (cdpReady, cdpCount);
    }

    internal Task<JsonElement> GetWebViewsAsync() =>
        client.GetCdpWebViewsAsync();

    internal Task<string> GetSourceAsync(string? webview = null) =>
        client.GetCdpSourceAsync(webview);

    internal Task<string> SnapshotAsync(string? webview = null) =>
        EvaluateAsync(@"
                (function() {
                    function walk(node, depth) {
                        if (depth > 8) return '';
                        let result = '';
                        const indent = '  '.repeat(depth);

                        if (node.nodeType === 1) {
                            const tag = node.tagName.toLowerCase();
                            const text = node.childNodes.length === 1 && node.childNodes[0].nodeType === 3
                                ? node.textContent?.trim().substring(0, 80) : null;
                            const isClickable = node.onclick || tag === 'button' || tag === 'a' ||
                                               node.getAttribute('role') === 'button' ||
                                               (tag === 'input' && node.type === 'submit');
                            const isInput = tag === 'input' || tag === 'textarea' || tag === 'select';

                            result += indent + '<' + tag;
                            if (node.id) result += ' id=""' + node.id + '""';
                            if (node.className && typeof node.className === 'string') result += ' class=""' + node.className.split(' ').slice(0,2).join(' ') + '""';
                            if (isClickable) result += ' [clickable]';
                            if (isInput) result += ' [input]';
                            if (tag === 'a' && node.href) result += ' href=""' + node.getAttribute('href') + '""';
                            if (tag === 'input') result += ' type=""' + (node.type || 'text') + '""';
                            result += '>';
                            if (text) result += ' ' + text;
                            result += '\n';

                            for (const child of node.children) {
                                result += walk(child, depth + 1);
                            }
                        }
                        return result;
                    }

                    return 'Title: ' + document.title + '\nURL: ' + location.href + '\n\n' + walk(document.body, 0);
                })()
            ", webview);
}
