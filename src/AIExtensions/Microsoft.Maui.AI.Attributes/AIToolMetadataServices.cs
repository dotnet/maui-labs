using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Attributes;

/// <summary>
/// Provides helpers to create and initialize metadata for AI tool contexts.
/// </summary>
/// <remarks>
/// This API is for use by the output of the AI.Attributes source generator
/// and should not be called directly.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class AIToolMetadataServices
{

    /// <summary>
    /// Reads a required argument from <see cref="AIFunctionArguments"/>, converting it to
    /// <typeparamref name="T"/> using the supplied <see cref="JsonTypeInfo{T}"/>.
    /// </summary>
    public static T GetRequiredArg<T>(AIFunctionArguments args, string name, JsonTypeInfo<T> typeInfo)
    {
        if (!args.TryGetValue(name, out var value))
        {
            throw new ArgumentException($"Missing required argument '{name}'.", nameof(args));
        }
        return ConvertArg<T>(value, name, typeInfo);
    }

    /// <summary>
    /// Reads an optional argument using the supplied <see cref="JsonTypeInfo{T}"/>.
    /// If the value is missing or <see langword="null"/>, returns <paramref name="defaultValue"/>.
    /// </summary>
    public static T? GetOptionalArg<T>(AIFunctionArguments args, string name, T? defaultValue, JsonTypeInfo<T> typeInfo)
    {
        if (!args.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }
        return ConvertArg<T>(value, name, typeInfo);
    }

    private static T ConvertArg<T>(object? value, string name, JsonTypeInfo<T> typeInfo)
    {
        if (value is null)
        {
            if (default(T) is null)
            {
                return default!;
            }
            throw new ArgumentException($"Argument '{name}' is null but target type '{typeof(T)}' is non-nullable.", name);
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement je)
        {
            return je.Deserialize<T>(typeInfo)!;
        }

        if (value is JsonNode jn)
        {
            return jn.Deserialize<T>(typeInfo)!;
        }

        // If the LLM supplied a raw JSON string for a non-string target, try to parse it.
        if (value is string s && typeof(T) != typeof(string))
        {
            try
            {
                return JsonSerializer.Deserialize<T>(s, typeInfo)!;
            }
            catch
            {
                // Fall through to round-trip.
            }
        }

        // Fallback: JSON round-trip via the type's own JsonTypeInfo.
        var json = JsonSerializer.SerializeToElement(value, typeInfo.Options.GetTypeInfo(value.GetType()));
        return json.Deserialize<T>(typeInfo)!;
    }
}
