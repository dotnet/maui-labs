using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// A compact, model-friendly projection of an OpenAPI document. Structural plumbing is stripped, but
/// every authored description and meaning-bearing constraint is preserved verbatim (never clipped).
/// This is the shape handed to an AI agent for discovery.
/// </summary>
public sealed record ReducedSpec
{
    /// <summary>The API's operations, one row per method+path.</summary>
    public required IReadOnlyList<ReducedEndpoint> Endpoints { get; init; }

    /// <summary>Named models referenced by the endpoints, keyed by model name.</summary>
    public required IReadOnlyDictionary<string, ReducedModel> Models { get; init; }
}

/// <summary>A single API operation in the reduced spec.</summary>
public sealed record ReducedEndpoint
{
    /// <summary>Stable operation handle used to invoke it — authored, or synthesized from method+path.</summary>
    public required string OperationId { get; init; }

    /// <summary>Uppercase HTTP method (GET, POST, PUT, PATCH, DELETE).</summary>
    public required string Method { get; init; }

    /// <summary>Route template, e.g. <c>/products/{sku}</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Short operation summary (verbatim), when authored.</summary>
    public string? Summary { get; init; }

    /// <summary>Full operation description (verbatim, never clipped), when authored.</summary>
    public string? Description { get; init; }

    /// <summary>Operation tags, when any.</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Path/query/header parameters, when any.</summary>
    public IReadOnlyList<ReducedParameter>? Parameters { get; init; }

    /// <summary>Name of the request-body model, when the operation has a JSON body.</summary>
    public string? RequestModel { get; init; }

    /// <summary>Name of the primary success response model, when there is one.</summary>
    public string? ResponseModel { get; init; }
}

/// <summary>A single operation parameter.</summary>
public sealed record ReducedParameter
{
    public required string Name { get; init; }

    /// <summary>Where the value goes: <c>path</c>, <c>query</c>, <c>header</c>, or <c>cookie</c>.</summary>
    public required string In { get; init; }

    /// <summary>Scalar type name (string, integer, number, boolean).</summary>
    public required string Type { get; init; }

    public bool Required { get; init; }

    /// <summary>Full parameter description (verbatim), when authored.</summary>
    public string? Description { get; init; }
}

/// <summary>A named model schema.</summary>
public sealed record ReducedModel
{
    /// <summary>Full model description (verbatim, never clipped), when authored.</summary>
    public string? Description { get; init; }

    /// <summary>The model's immediate properties.</summary>
    public required IReadOnlyList<ReducedProperty> Properties { get; init; }
}

/// <summary>A single model property.</summary>
public sealed record ReducedProperty
{
    public required string Name { get; init; }

    /// <summary>
    /// Type name: a scalar (string/integer/number/boolean/object/array), a referenced model name
    /// (e.g. <c>CartItem</c>), or an array of a model (e.g. <c>CartItem[]</c>).
    /// </summary>
    public required string Type { get; init; }

    public bool Required { get; init; }

    /// <summary>True when the value may be null (nullable union in the source schema).</summary>
    public bool Nullable { get; init; }

    /// <summary>Full property description (verbatim), when authored.</summary>
    public string? Description { get; init; }

    /// <summary>Format hint (e.g. <c>double</c>, <c>date-time</c>), when present.</summary>
    public string? Format { get; init; }

    /// <summary>Allowed values, when the property is an enum.</summary>
    public IReadOnlyList<string>? Enum { get; init; }
}

/// <summary>
/// Source-generated serialization for <see cref="ReducedSpec"/>. Absent (null) members are omitted
/// so the emitted JSON stays compact — the reduced spec never contains null placeholder keys.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(ReducedSpec))]
[JsonSerializable(typeof(ReducedEndpoint))]
[JsonSerializable(typeof(ReducedModel))]
public partial class ReducedSpecJsonContext : JsonSerializerContext;
