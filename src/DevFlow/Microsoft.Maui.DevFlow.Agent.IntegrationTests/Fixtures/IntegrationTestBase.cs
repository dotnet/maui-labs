using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Base class for all agent integration tests. Provides AgentClient access
/// plus raw HTTP helpers for endpoints not wrapped by the client.
/// </summary>
public abstract class IntegrationTestBase
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    protected AppFixture App { get; }
    protected AgentClient Client => App.Client;
    protected HttpClient Http => App.Http;
    protected ITestOutputHelper Output { get; }
    protected string Platform => App.Platform;

    protected IntegrationTestBase(AppFixture app, ITestOutputHelper output)
    {
        App = app;
        Output = output;
    }

    protected async Task<JsonElement> GetJsonAsync(string path)
    {
        var response = await Http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
    }

    protected Task<HttpResponseMessage> GetRawAsync(string path) => Http.GetAsync(path);

    protected async Task<JsonElement> PostJsonAsync(string path, object? body = null)
    {
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            : null;
        var response = await Http.PostAsync(path, content);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseBody, JsonOptions);
    }

    protected Task<HttpResponseMessage> PostRawAsync(string path, object? body = null)
    {
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            : null;
        return Http.PostAsync(path, content);
    }

    protected async Task<JsonElement> PutJsonAsync(string path, object? body = null)
    {
        var content = body != null
            ? new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            : null;
        var response = await Http.PutAsync(path, content);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseBody, JsonOptions);
    }

    protected Task<HttpResponseMessage> DeleteRawAsync(string path) => Http.DeleteAsync(path);

    protected async Task<JsonElement> DeleteJsonAsync(string path)
    {
        var response = await Http.DeleteAsync(path);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseBody, JsonOptions);
    }

    protected async Task<ElementInfo> FindElementAsync(string automationId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        List<ElementInfo>? results = null;

        while (DateTime.UtcNow < deadline)
        {
            results = await Client.QueryAsync(automationId: automationId);
            if (results.Count > 0)
                return results[0];
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Element with AutomationId '{automationId}' not found within {timeoutMs}ms. " +
            $"Last query returned {results?.Count ?? 0} results.");
    }

    protected async Task<ElementInfo?> TryFindElementAsync(string automationId)
    {
        var results = await Client.QueryAsync(automationId: automationId);
        return results.Count > 0 ? results[0] : null;
    }

    protected async Task NavigateToPageAsync(string route, string? expectedAutomationId = null, int timeoutMs = 5000)
    {
        await Client.NavigateAsync(route);

        if (expectedAutomationId != null)
            await FindElementAsync(expectedAutomationId, timeoutMs);
        else
            await Task.Delay(500);
    }

    protected Task NavigateToMainPageAsync() => NavigateToPageAsync("//native", "AddButton");

    protected async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 5000, int pollIntervalMs = 250)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(pollIntervalMs);
        }

        throw new TimeoutException($"Condition not met within {timeoutMs}ms.");
    }

    protected static Task SettleAsync(int ms = 500) => Task.Delay(ms);

    protected async Task<bool> WaitForCdpReadyAsync(int timeoutMs = 15000, int pollIntervalMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var status = await Client.GetStatusAsync();
                if (status?.Capabilities?.WebView == true)
                {
                    var probe = await Client.SendCdpCommandAsync(
                        "Runtime.evaluate",
                        JsonNode.Parse("""{"expression":"1 + 1"}"""));

                    var probeText = probe.ToString();
                    if (!probeText.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) &&
                        probeText.Contains("2", StringComparison.Ordinal))
                    {
                        var source = await Client.GetCdpSourceAsync();
                        if (!string.IsNullOrWhiteSpace(source) && source.Contains('<'))
                            return true;
                    }
                }
            }
            catch
            {
                // Not ready yet.
            }

            await Task.Delay(pollIntervalMs);
        }

        return false;
    }
}
