using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// The AI-facing server-API tools. They let a model discover a REST API described by a
/// <see cref="ReducedSpec"/> and call it generically. Reads and writes are split into two tools so
/// that only <see cref="WriteApiAsync"/> is approval-gated and a read can never perform a write.
/// </summary>
public sealed class OpenApiExplorerTools
{
    private static readonly JsonSerializerOptions RelaxedJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ReducedSpec _spec;
    private readonly ApiInvoker _invoker;

    public OpenApiExplorerTools(ReducedSpec spec, ApiInvoker invoker)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    [ExportAIFunction("list_endpoints")]
    [Description("List the API's operations as a compact index (operationId, method, path, summary, tags). Optionally filter by tag or a free-text query over the path, summary, tags, and operationId.")]
    public string ListEndpoints(
        [Description("Only include operations carrying this tag.")] string? tag = null,
        [Description("Only include operations whose path, summary, tags, or operationId contains this text.")] string? query = null)
    {
        var rows = new JsonArray();
        foreach (var endpoint in _spec.Endpoints)
        {
            if (tag is not null && (endpoint.Tags is null || !endpoint.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (query is not null && !MatchesQuery(endpoint, query))
                continue;

            var row = new JsonObject
            {
                ["operationId"] = endpoint.OperationId,
                ["method"] = endpoint.Method,
                ["path"] = endpoint.Path,
            };
            if (endpoint.Summary is not null)
                row["summary"] = endpoint.Summary;
            if (endpoint.Tags is { Count: > 0 })
                row["tags"] = new JsonArray(endpoint.Tags.Select(t => (JsonNode)t!).ToArray());
            rows.Add(row);
        }

        return rows.ToJsonString(RelaxedJson);
    }

    [ExportAIFunction("describe_endpoint")]
    [Description("Full detail for one operation: parameters plus the request and response schemas inlined one level deep (immediate properties; nested models are referenced by name — use describe_model to expand them).")]
    public string DescribeEndpoint(
        [Description("The operationId to describe (from list_endpoints).")] string operationId)
    {
        var endpoint = _spec.Endpoints.FirstOrDefault(e => string.Equals(e.OperationId, operationId, StringComparison.Ordinal));
        if (endpoint is null)
            return Error($"No operation with id '{operationId}'.");

        var node = JsonSerializer.SerializeToNode(endpoint, ReducedSpecJsonContext.Default.ReducedEndpoint)!.AsObject();
        InlineModel(node, "requestSchema", endpoint.RequestModel);
        InlineModel(node, "responseSchema", endpoint.ResponseModel);
        return node.ToJsonString(RelaxedJson);
    }

    [ExportAIFunction("describe_model")]
    [Description("The resolved schema for one model: its properties (name, type, required, nullable, description). Nested/array property types name other models you can describe_model in turn.")]
    public string DescribeModel(
        [Description("The model name to describe.")] string name)
    {
        if (!_spec.Models.TryGetValue(ModelKey(name), out var model))
            return Error($"No model named '{name}'.");

        return JsonSerializer.Serialize(model, ReducedSpecJsonContext.Default.ReducedModel);
    }

    [ExportAIFunction("read_api")]
    [Description("Invoke a safe (GET) operation by operationId and return its JSON response. Path and query values go as flat top-level keys in args (e.g. { \"sku\": \"basil-seeds\" }). Never mutates; not approval-gated.")]
    public async Task<string> ReadApiAsync(
        [Description("The GET operationId to invoke.")] string operationId,
        [Description("Path/query values as flat top-level keys.")] JsonObject? args = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _invoker.InvokeAsync(_spec, operationId, args, ApiAccess.Read, cancellationToken).ConfigureAwait(false);
        return result.ToResponseJson();
    }

    [ExportAIFunction("write_api", ApprovalRequired = true)]
    [Description("Invoke a mutating (POST/PUT/PATCH/DELETE) operation by operationId. Path/query values go as flat top-level keys in args; the request payload goes under an explicit \"body\" key (e.g. { \"sku\": \"tomato-seeds\", \"body\": { \"quantity\": 5 } }). Requires user approval before executing.")]
    public async Task<string> WriteApiAsync(
        [Description("The POST/PUT/PATCH/DELETE operationId to invoke.")] string operationId,
        [Description("Path/query values as flat top-level keys; the request payload under \"body\".")] JsonObject? args = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _invoker.InvokeAsync(_spec, operationId, args, ApiAccess.Write, cancellationToken).ConfigureAwait(false);
        return result.ToResponseJson();
    }

    private void InlineModel(JsonObject node, string key, string? modelName)
    {
        if (modelName is null)
            return;
        if (_spec.Models.TryGetValue(ModelKey(modelName), out var model))
            node[key] = JsonSerializer.SerializeToNode(model, ReducedSpecJsonContext.Default.ReducedModel);
    }

    private static string ModelKey(string name) => name.EndsWith("[]", StringComparison.Ordinal) ? name[..^2] : name;

    private static bool MatchesQuery(ReducedEndpoint endpoint, string query)
    {
        if (endpoint.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (endpoint.OperationId.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        if (endpoint.Summary is not null && endpoint.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;
        return endpoint.Tags is not null && endpoint.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string Error(string message) =>
        new JsonObject { ["error"] = new JsonObject { ["title"] = "not_found", ["detail"] = message } }.ToJsonString(RelaxedJson);
}
