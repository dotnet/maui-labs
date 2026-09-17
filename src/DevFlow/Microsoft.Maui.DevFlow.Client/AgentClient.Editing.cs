using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Driver;

// Design-time editing: structural changes, clearing properties, XAML reload and in-app selection.
public partial class AgentClient
{
    /// <summary>
    /// Inflate a single view from <paramref name="xaml"/> (e.g. <c>&lt;Label Text="Hi" /&gt;</c>) and
    /// insert it into <paramref name="parentId"/>. Layouts insert at <paramref name="index"/> (appending
    /// when omitted); single-content parents such as <c>ContentPage</c> or <c>Border</c> must be empty.
    /// </summary>
    public Task<ElementEditResult> AddElementAsync(
        string parentId,
        string xaml,
        int? index = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject { ["xaml"] = xaml };
        if (index.HasValue)
            payload["index"] = index.Value;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return SendEditAsync(HttpMethod.Post, $"{UiApi}/elements/{Uri.EscapeDataString(parentId)}/children", payload);
    }

    /// <summary>Remove an element from its parent.</summary>
    public Task<ElementEditResult> RemoveElementAsync(
        string elementId,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject();
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return SendEditAsync(HttpMethod.Delete, $"{UiApi}/elements/{Uri.EscapeDataString(elementId)}", payload);
    }

    /// <summary>
    /// Move an element to <paramref name="parentId"/> (which may be its current parent, to reorder).
    /// <paramref name="index"/> is the element's final position; omitted appends.
    /// </summary>
    public Task<ElementEditResult> MoveElementAsync(
        string elementId,
        string parentId,
        int? index = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject { ["parentId"] = parentId };
        if (index.HasValue)
            payload["index"] = index.Value;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return SendEditAsync(HttpMethod.Post, $"{UiApi}/elements/{Uri.EscapeDataString(elementId)}/move", payload);
    }

    /// <summary>Clear a locally set bindable property so it falls back to its style or default value.</summary>
    public async Task<bool> ClearPropertyAsync(string elementId, string propertyName)
        => (await ClearPropertyResultAsync(elementId, propertyName)).Success;

    /// <summary>
    /// Clear a locally set bindable property so it falls back to its style or default value, reporting
    /// failures through the returned <see cref="ActionResult"/>.
    /// </summary>
    public Task<ActionResult> ClearPropertyResultAsync(
        string elementId,
        string propertyName,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject();
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);
        return SendActionResultAsync(
            HttpMethod.Delete,
            $"{UiApi}/elements/{Uri.EscapeDataString(elementId)}/properties/{Uri.EscapeDataString(propertyName)}",
            payload);
    }

    /// <summary>
    /// Re-inflate live pages or views from <paramref name="xaml"/>. By default every live instance of the
    /// document's <c>x:Class</c> is reloaded; pass <paramref name="elementId"/> to reload one instance.
    /// The agent's source map for the type is replaced so element source locations follow the new text;
    /// <paramref name="sourceFile"/> names the project-relative file when the app has no map for it yet.
    /// </summary>
    public async Task<XamlReloadResult> ReloadXamlAsync(
        string xaml,
        string? className = null,
        string? elementId = null,
        string? sourceFile = null,
        long? captureEpoch = null,
        long? registryGeneration = null)
    {
        var payload = new JsonObject { ["xaml"] = xaml };
        if (className is not null)
            payload["className"] = className;
        if (elementId is not null)
            payload["elementId"] = elementId;
        if (sourceFile is not null)
            payload["sourceFile"] = sourceFile;
        AddCaptureMetadata(payload, captureEpoch, registryGeneration);

        var (statusCode, body, failure) = await SendForBodyAsync(HttpMethod.Post, $"{UiApi}/xaml/reload", payload);
        if (failure is not null)
            return new XamlReloadResult { Success = false, StatusCode = statusCode, Reason = failure.Value.Reason, Error = failure.Value.Error };

        var result = TryDeserialize<XamlReloadResult>(body) ?? new XamlReloadResult();
        result.StatusCode = statusCode;
        result.Success = statusCode is >= 200 and < 300 && result.Success;
        result.Error ??= result.Success ? null : $"XAML reload failed with HTTP {statusCode}.";
        return result;
    }

    /// <summary>Draw the selection adorner around an element inside the running app, or clear it with null.</summary>
    public Task<ActionResult> HighlightElementAsync(string? elementId)
        => SendActionResultAsync(
            HttpMethod.Put,
            $"{UiApi}/highlight",
            new JsonObject { ["elementId"] = elementId });

    /// <summary>
    /// Turn pick mode on or off. While on, the next tap in the app selects the element under it instead of
    /// interacting with it, highlights it, and publishes an <c>elementPicked</c> event on
    /// <c>/ws/v1/ui/events</c>. Pick mode turns itself off after one pick.
    /// </summary>
    public Task<ActionResult> SetPickModeAsync(bool enabled)
        => SendActionResultAsync(
            HttpMethod.Post,
            $"{UiApi}/pick",
            new JsonObject { ["enabled"] = enabled });

    /// <summary>
    /// Turn pick mode on and wait for the person using the app to tap an element. Returns the picked
    /// element, or null when <paramref name="timeout"/> elapses (pick mode is turned off again).
    /// </summary>
    public async Task<PickedElement?> PickElementAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var socket = new ClientWebSocket();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        var wsUrl = new UriBuilder(_baseUrl) { Scheme = _baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws", Path = "/ws/v1/ui/events" }.Uri;
        await socket.ConnectAsync(wsUrl, cts.Token);
        var subscribe = Encoding.UTF8.GetBytes("{\"type\":\"subscribe\",\"data\":{\"events\":[\"elementPicked\"]}}");
        await socket.SendAsync(new ArraySegment<byte>(subscribe), WebSocketMessageType.Text, true, cts.Token);

        var enabled = await SetPickModeAsync(true);
        if (!enabled.Success)
            throw new InvalidOperationException(enabled.Error ?? $"Could not enable pick mode (HTTP {enabled.StatusCode}).");

        try
        {
            var buffer = new byte[8192];
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        return null;
                    message.Write(buffer, 0, received.Count);
                }
                while (!received.EndOfMessage);

                var root = ProtocolJson.ParseElement(Encoding.UTF8.GetString(message.ToArray()));
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("type", out var type) && type.GetString() == "elementPicked"
                    && root.TryGetProperty("data", out var data)
                    && data.TryGetProperty("elementId", out var id))
                {
                    var elementType = data.TryGetProperty("elementType", out var t) ? t.GetString() : null;
                    return new PickedElement(id.GetString() ?? string.Empty, elementType);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            // Restore normal tap behaviour whatever happened - the timeout, the caller cancelling,
            // or the socket failing - so the app is never left stuck in pick mode.
            try
            {
                await SetPickModeAsync(false);
            }
            catch (Exception)
            {
                // best effort: the original outcome is what the caller cares about
            }
        }
    }

    private async Task<ElementEditResult> SendEditAsync(HttpMethod method, string path, JsonNode payload)
    {
        var (statusCode, body, failure) = await SendForBodyAsync(method, path, payload);
        if (failure is not null)
            return new ElementEditResult { Success = false, StatusCode = statusCode, Reason = failure.Value.Reason, Error = failure.Value.Error };

        var result = TryDeserialize<ElementEditResult>(body) ?? new ElementEditResult();
        result.StatusCode = statusCode;
        result.Success = statusCode is >= 200 and < 300 && result.Success;
        result.Error ??= result.Success ? null : $"Edit failed with HTTP {statusCode}.";
        return result;
    }

    private async Task<(int? StatusCode, string Body, (string Reason, string Error)? Failure)> SendForBodyAsync(
        HttpMethod method,
        string path,
        JsonNode payload)
    {
        try
        {
            using var response = await SendWithTransientRetriesAsync(method, async () =>
            {
                using var content = ProtocolJson.CreateJsonContent(payload);
                using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}") { Content = content };
                return await _http.SendAsync(request);
            });
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(), null);
        }
        catch (NotSupportedByAgentException ex)
        {
            return ((int)HttpStatusCode.NotImplemented, string.Empty, ("not_supported", ex.Message));
        }
        catch (Exception ex) when (IsExpectedClientException(ex))
        {
            return (null, string.Empty, ("transport-failure", ex.Message));
        }
    }

    private static T? TryDeserialize<T>(string body) where T : class
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            return ProtocolJson.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
