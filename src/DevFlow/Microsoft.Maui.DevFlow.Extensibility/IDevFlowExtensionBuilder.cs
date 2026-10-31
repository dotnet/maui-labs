using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>
/// Declares what a <see cref="DevFlowExtension"/> exposes. Paths are relative to the plugin's
/// namespace: <c>/status</c> is served at <c>/api/v1/ext/{namespace}/status</c>.
/// </summary>
/// <remarks>
/// Every route is advertised as a tool. <see cref="MapGet"/> and the other plain mappings get a generated
/// name such as <c>get_status</c> and no description; use <see cref="MapTool"/> to name and describe it.
/// </remarks>
public interface IDevFlowExtensionBuilder
{
    /// <summary>Maps a read-only GET route.</summary>
    void MapGet(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler);

    /// <summary>Maps a POST route. It requires the DevFlow mutation lease.</summary>
    void MapPost(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler);

    /// <summary>Maps a PUT route. It requires the DevFlow mutation lease.</summary>
    void MapPut(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler);

    /// <summary>Maps a DELETE route. It requires the DevFlow mutation lease.</summary>
    void MapDelete(string path, Func<DevFlowRequest, Task<DevFlowResponse>> handler);

    /// <summary>
    /// Maps a route and describes it as a tool, so the CLI, MCP server and inspector can call it.
    /// GET tools receive their parameters as query values; other methods receive a JSON body.
    /// </summary>
    /// <param name="name">Tool name, unique within the plugin, e.g. <c>status</c>.</param>
    /// <param name="description">What the tool returns or does, written for an AI agent.</param>
    /// <param name="method">HTTP method: GET, POST, PUT or DELETE.</param>
    /// <param name="path">Route path relative to the plugin namespace.</param>
    /// <param name="handler">Handles the call.</param>
    /// <param name="parameters">JSON schema of the parameters object.</param>
    /// <param name="returns">JSON schema of the result.</param>
    /// <param name="annotations">Behaviour hints. A tool marked read-only skips the mutation lease.</param>
    void MapTool(
        string name,
        string description,
        string method,
        string path,
        Func<DevFlowRequest, Task<DevFlowResponse>> handler,
        JsonElement? parameters = null,
        JsonElement? returns = null,
        DevFlowToolAnnotations? annotations = null);

    /// <summary>
    /// Publishes an event stream. The agent serves it live at
    /// <c>/ws/v1/ext/{namespace}/streams/{name}</c> and for polling at
    /// <c>/api/v1/ext/{namespace}/streams/{name}</c>, which is also exposed as a read-only tool named
    /// <c>{name}_events</c> (or just <c>{name}</c> when the name already ends in "events").
    /// </summary>
    /// <param name="name">Stream name, unique within the plugin, e.g. <c>events</c>.</param>
    /// <param name="description">What the stream carries.</param>
    /// <param name="stream">The buffer the plugin publishes into.</param>
    /// <param name="columns">Event fields a viewer should show as columns, in order.</param>
    void MapStream(string name, string description, DevFlowEventStream stream, IEnumerable<string>? columns = null);
}
