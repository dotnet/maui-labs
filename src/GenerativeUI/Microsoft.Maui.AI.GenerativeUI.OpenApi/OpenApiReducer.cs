using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Parses an OpenAPI document (via <c>Microsoft.OpenApi</c>) and projects it into a compact
/// <see cref="ReducedSpec"/>. Reduction strips structural plumbing (envelopes, media-type maps,
/// <c>$ref</c> machinery) but preserves every authored description and meaning-bearing constraint
/// verbatim — text is never clipped.
/// </summary>
public static class OpenApiReducer
{
    private const string Json = "json";
    private const string JsonMediaType = "application/json";

    /// <summary>Reduces an OpenAPI document supplied as a JSON string.</summary>
    /// <exception cref="ArgumentException"><paramref name="openApiJson"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">The document could not be parsed.</exception>
    public static ReducedSpec Reduce(string openApiJson)
    {
        if (string.IsNullOrWhiteSpace(openApiJson))
            throw new ArgumentException("OpenAPI JSON must be provided.", nameof(openApiJson));

        var settings = new OpenApiReaderSettings();
        settings.AddJsonReader();

        var (document, diagnostic) = OpenApiDocument.Parse(openApiJson, Json, settings);
        if (document is null)
        {
            var errors = diagnostic?.Errors is { Count: > 0 }
                ? string.Join("; ", diagnostic.Errors)
                : "unknown error";
            throw new InvalidOperationException($"Failed to parse the OpenAPI document: {errors}");
        }

        return Reduce(document);
    }

    /// <summary>Reduces an already-parsed <see cref="OpenApiDocument"/>.</summary>
    public static ReducedSpec Reduce(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var endpoints = new List<ReducedEndpoint>();
        if (document.Paths is not null)
        {
            foreach (var (path, pathItem) in document.Paths)
            {
                if (pathItem?.Operations is null)
                    continue;

                foreach (var (httpMethod, operation) in pathItem.Operations)
                {
                    var method = httpMethod.Method.ToUpperInvariant();
                    endpoints.Add(ReduceEndpoint(path, method, operation, pathItem));
                }
            }
        }

        var models = new Dictionary<string, ReducedModel>(StringComparer.Ordinal);
        if (document.Components?.Schemas is { } schemas)
        {
            foreach (var (name, schema) in schemas)
                models[name] = ReduceModel(schema);
        }

        return new ReducedSpec { Endpoints = endpoints, Models = models };
    }

    private static ReducedEndpoint ReduceEndpoint(string path, string method, OpenApiOperation operation, IOpenApiPathItem pathItem)
    {
        var parameters = new List<ReducedParameter>();
        foreach (var p in EnumerateParameters(pathItem, operation))
        {
            var (type, _) = p.Schema is null ? ("string", false) : MapType(p.Schema);
            parameters.Add(new ReducedParameter
            {
                Name = p.Name ?? string.Empty,
                In = (p.In?.ToString() ?? "Query").ToLowerInvariant(),
                Type = type,
                Required = p.Required,
                Description = NullIfEmpty(p.Description),
            });
        }

        return new ReducedEndpoint
        {
            OperationId = OperationIdSynthesizer.Resolve(operation.OperationId, method, path),
            Method = method,
            Path = path,
            Summary = NullIfEmpty(operation.Summary),
            Description = NullIfEmpty(operation.Description),
            Tags = ReduceTags(operation),
            Parameters = parameters.Count > 0 ? parameters : null,
            RequestModel = ModelName(JsonSchema(operation.RequestBody?.Content)),
            ResponseModel = ModelName(PrimarySuccessSchema(operation.Responses)),
        };
    }

    private static ReducedModel ReduceModel(IOpenApiSchema schema)
    {
        var required = schema.Required ?? (ISet<string>)new HashSet<string>();
        var properties = new List<ReducedProperty>();

        if (schema.Properties is { } props)
        {
            foreach (var (name, propSchema) in props)
                properties.Add(ReduceProperty(name, propSchema, required.Contains(name)));
        }

        return new ReducedModel
        {
            Description = NullIfEmpty(schema.Description),
            Properties = properties,
        };
    }

    private static ReducedProperty ReduceProperty(string name, IOpenApiSchema schema, bool required)
    {
        // A property that is itself a model reference (or an array of one) carries the model name.
        if (ModelName(schema) is { } referencedModel)
        {
            return new ReducedProperty
            {
                Name = name,
                Type = referencedModel,
                Required = required,
                Description = NullIfEmpty(schema.Description),
            };
        }

        var (type, nullable) = MapType(schema);
        if (type == "array" && schema.Items is { } items)
            type = (ModelName(items) ?? MapType(items).Type) + "[]";

        IReadOnlyList<string>? enumValues = null;
        if (schema.Enum is { Count: > 0 } e)
            enumValues = e.Select(v => v?.ToString() ?? "null").ToList();

        return new ReducedProperty
        {
            Name = name,
            Type = type,
            Required = required,
            Nullable = nullable,
            Description = NullIfEmpty(schema.Description),
            Format = NullIfEmpty(schema.Format),
            Enum = enumValues,
        };
    }

    private static IEnumerable<IOpenApiParameter> EnumerateParameters(IOpenApiPathItem pathItem, OpenApiOperation operation)
    {
        if (pathItem.Parameters is { } shared)
            foreach (var p in shared)
                yield return p;
        if (operation.Parameters is { } own)
            foreach (var p in own)
                yield return p;
    }

    private static IReadOnlyList<string>? ReduceTags(OpenApiOperation operation)
    {
        if (operation.Tags is not { Count: > 0 })
            return null;
        var tags = operation.Tags
            .Select(t => t.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();
        return tags.Count > 0 ? tags : null;
    }

    // Content values are the concrete OpenApiMediaType in Microsoft.OpenApi 2.0.0 (the interface
    // IOpenApiMediaType only exists in 3.x). This is the sole 2.0.0-specific member in the reducer.
    private static IOpenApiSchema? JsonSchema(IDictionary<string, OpenApiMediaType>? content)
    {
        if (content is null || content.Count == 0)
            return null;
        return content.TryGetValue(JsonMediaType, out var media)
            ? media.Schema
            : content.Values.FirstOrDefault()?.Schema;
    }

    private static IOpenApiSchema? PrimarySuccessSchema(OpenApiResponses? responses)
    {
        if (responses is null)
            return null;
        foreach (var code in responses.Keys.Where(k => k.StartsWith('2')).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (responses[code] is { } response && JsonSchema(response.Content) is { } schema)
                return schema;
        }
        return null;
    }

    /// <summary>
    /// The referenced model name for a schema that is a <c>$ref</c> (or an array of one), else null.
    /// </summary>
    private static string? ModelName(IOpenApiSchema? schema)
    {
        switch (schema)
        {
            case null:
                return null;
            case OpenApiSchemaReference reference:
                return reference.Reference?.Id;
        }

        var core = CoreType(schema.Type);
        if (core.HasFlag(JsonSchemaType.Array) && schema.Items is OpenApiSchemaReference itemReference)
            return itemReference.Reference?.Id + "[]";

        return null;
    }

    private static (string Type, bool Nullable) MapType(IOpenApiSchema schema)
    {
        var type = schema.Type;
        bool nullable = type.HasValue && type.Value.HasFlag(JsonSchemaType.Null);
        var core = CoreType(type);

        string name =
            core.HasFlag(JsonSchemaType.Object) ? "object" :
            core.HasFlag(JsonSchemaType.Array) ? "array" :
            core.HasFlag(JsonSchemaType.Number) ? "number" :
            core.HasFlag(JsonSchemaType.Integer) ? "integer" :
            core.HasFlag(JsonSchemaType.Boolean) ? "boolean" :
            core.HasFlag(JsonSchemaType.String) ? "string" :
            "string";

        return (name, nullable);
    }

    private static JsonSchemaType CoreType(JsonSchemaType? type)
        => type.HasValue ? type.Value & ~JsonSchemaType.Null : default;

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
