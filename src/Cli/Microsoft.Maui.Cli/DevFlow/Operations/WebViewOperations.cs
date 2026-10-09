using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class WebViewOperations(AgentClient client)
{
    internal async Task<string> EvaluateAsync(string expression, string? contextId = null)
    {
        var root = await client.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true
        }, contextId);

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
                throw new InvalidOperationException(exception.GetRawText());
            }
        }

        // Response might be the raw chobitsu response
        if (root.TryGetProperty("result", out var rawResult) && rawResult.TryGetProperty("value", out var rawValue))
            return rawValue.GetString() ?? rawValue.ToString();

        return root.ToString();
    }

    internal async Task<JsonElement?> BrowserGetVersionAsync(string? contextId = null) =>
        await client.SendCdpCommandAsync("Browser.getVersion", contextId: contextId);

    internal async Task<JsonElement?> DomGetDocumentAsync(string? contextId = null) =>
        await client.SendCdpCommandAsync("DOM.getDocument", contextId: contextId);

    internal Task<string> DomQuerySelectorAsync(string selector, string? contextId = null) =>
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
            ", contextId);

    internal Task<string> DomQuerySelectorAllAsync(string selector, string? contextId = null) =>
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
            ", contextId);

    internal Task<string> DomGetOuterHtmlAsync(string selector, string? contextId = null) =>
        EvaluateAsync($@"document.querySelector({CliJson.SerializeUntyped(selector, indented: false)})?.outerHTML || null", contextId);

    internal async Task PageNavigateAsync(string url, string? contextId = null) =>
        await client.SendCdpCommandAsync("Page.navigate", new JsonObject
        {
            ["url"] = url
        }, contextId);

    internal async Task PageReloadAsync(string? contextId = null) =>
        await client.SendCdpCommandAsync("Page.reload", contextId: contextId);

    internal async Task<string?> PageCaptureScreenshotAsync(string? contextId = null)
    {
        var bytes = await client.GetWebViewScreenshotAsync(contextId);
        return Convert.ToBase64String(bytes);
    }

    internal Task<string> InputDispatchClickAsync(string selector, string? contextId = null) =>
        EvaluateAsync($@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)});
                    if (!el) throw new Error('Element not found');
                    el.click();
                    return 'Clicked: ' + el.tagName.toLowerCase() + (el.id ? '#' + el.id : '');
                }})()
            ", contextId);

    internal async Task InputInsertTextAsync(string text, string? contextId = null) =>
        await client.SendCdpCommandAsync("Input.insertText", new JsonObject
        {
            ["text"] = text
        }, contextId);

    internal Task<string> InputFillAsync(string selector, string text, string? contextId = null) =>
        EvaluateAsync($@"
                (function() {{
                    const el = document.querySelector({CliJson.SerializeUntyped(selector, indented: false)});
                    if (!el) throw new Error('Element not found');

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
            ", contextId);

    internal async Task<(bool IsReady, int WebViewCount)> GetStatusAsync(string? contextId = null)
    {
        var contexts = (await client.GetCdpWebViewsAsync()).GetProperty("webviews").EnumerateArray().ToArray();
        if (contextId is not null)
        {
            var selected = contexts.SingleOrDefault(context => context.GetProperty("id").GetString() == contextId);
            if (selected.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException($"WebView context '{contextId}' not found. Use webview webviews to list canonical context IDs.");
            return (selected.GetProperty("ready").GetBoolean(), contexts.Length);
        }
        return (contexts.Any(context => context.GetProperty("ready").GetBoolean()), contexts.Length);
    }

    internal Task<JsonElement> GetWebViewsAsync() =>
        client.GetCdpWebViewsAsync();

    internal Task<string> GetSourceAsync(string? contextId = null) =>
        client.GetCdpSourceAsync(contextId);

    internal Task<string> SnapshotAsync(string? contextId = null) =>
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
            ", contextId);
}
