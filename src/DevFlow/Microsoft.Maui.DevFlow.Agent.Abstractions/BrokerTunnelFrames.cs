using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// An HTTP request the broker forwards to a tunneled agent over the agent's WebSocket.
/// Tunneling serves the agent API where the agent cannot listen on a TCP port (WebAssembly in a
/// browser): the broker listens on the agent's port and relays each request as one frame.
/// </summary>
internal sealed class BrokerTunnelRequest
{
    public const string FrameType = "http-request";

    /// <summary>
    /// Sent by the broker, with the <see cref="Id"/> of a request in flight, when that request's
    /// client disconnects or the broker stops waiting for it. The agent cancels the request.
    /// </summary>
    public const string CancelFrameType = "http-cancel";

    [JsonPropertyName("type")]
    public string Type { get; set; } = FrameType;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "GET";

    /// <summary>The request target: path plus optional query string.</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "/";

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }
}

/// <summary>
/// The agent's reply to a <see cref="BrokerTunnelRequest"/>. The body travels as base64 so
/// binary responses such as screenshots survive the text frame.
/// </summary>
internal sealed class BrokerTunnelResponse
{
    public const string FrameType = "http-response";

    [JsonPropertyName("type")]
    public string Type { get; set; } = FrameType;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string>? Headers { get; set; }

    [JsonPropertyName("bodyBase64")]
    public string? BodyBase64 { get; set; }
}
