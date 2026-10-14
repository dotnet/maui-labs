using System.Globalization;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

internal static class WebViewReadiness
{
    internal static async Task<string> WaitForMainContextAsync(
        Func<Task<JsonElement>> getContexts,
        Func<string, string, Task<JsonElement>> evaluate,
        int timeoutMs = 45000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        JsonElement lastContexts = default;
        string? target = null;
        string? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            lastContexts = await getContexts();
            var selected = SelectNewestActiveMain(lastContexts);
            target = GetSelector(selected);
            if (target is not null && IsReady(selected))
            {
                try
                {
                    var responsive = await evaluate(target, "1 + 1");
                    var rendered = await evaluate(target,
                        "!!document.querySelector('.todo-container') ? 'ready' : 'waiting'");
                    if (HasValue(responsive, JsonValueKind.Number, "2") &&
                        HasValue(rendered, JsonValueKind.String, "ready"))
                    {
                        lastContexts = await getContexts();
                        var current = SelectNewestActiveMain(lastContexts);
                        if (GetSelector(current) == target && IsReady(current))
                            return target;
                        lastError = "The active Main registration changed during its readiness probes.";
                    }
                    else
                    {
                        lastError = $"Read-only probes: responsive={responsive}; rendered={rendered}";
                    }
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("WebView not ready", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("CDP not ready", StringComparison.OrdinalIgnoreCase) ||
                    ex.Message.Contains("WebView not initialized", StringComparison.OrdinalIgnoreCase))
                {
                    lastError = ex.Message;
                }
            }
            else
            {
                lastError = target is null
                    ? "No active Main registration is listed."
                    : "The newest active Main registration is not ready.";
            }
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Main WebView readiness failed within {timeoutMs}ms. Target: {target ?? "(none)"}. " +
            $"Last read-only error: {lastError}. Last contexts: {lastContexts}");
    }

    static JsonElement SelectNewestActiveMain(JsonElement payload)
    {
        var contexts = payload.ValueKind == JsonValueKind.Array
            ? payload
            : payload.GetProperty("webviews");
        return contexts.EnumerateArray().LastOrDefault(context =>
            context.GetProperty("automationId").GetString() == "BlazorWebView" &&
            context.GetProperty("active").ValueKind == JsonValueKind.True);
    }

    static string? GetSelector(JsonElement context)
    {
        if (context.ValueKind == JsonValueKind.Undefined)
            return null;
        if (context.TryGetProperty("id", out var id))
            return id.GetString();
        return context.GetProperty("index").GetInt32().ToString(CultureInfo.InvariantCulture);
    }

    static bool IsReady(JsonElement context)
        => context.ValueKind != JsonValueKind.Undefined &&
            (context.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True ||
             context.TryGetProperty("isReady", out var legacyReady) && legacyReady.ValueKind == JsonValueKind.True);

    static bool HasValue(JsonElement response, JsonValueKind kind, string expected)
        => response.GetProperty("result").GetProperty("result").TryGetProperty("value", out var value) &&
            value.ValueKind == kind && value.ToString() == expected;
}
