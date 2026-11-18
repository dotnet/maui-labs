using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>
/// JSON contracts for agent transports. Extensions returning custom CLR types must register
/// their source-generated context before using those types in requests or responses.
/// Common dynamic dictionaries, sequences, JSON values, and primitives use built-in contracts.
/// Custom collections with ambiguous ancestor contracts require explicit metadata.
/// </summary>
public static class DevFlowJson
{
    private static readonly object Gate = new();
    private static readonly List<IJsonTypeInfoResolver> Resolvers = [DevFlowJsonContext.Default];
    private static ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> _options = new();
    private static readonly JsonSerializerOptions Defaults = new(JsonSerializerDefaults.Web);
    private const string MetadataDiagnostic = "[DevFlowJsonMetadata]";

    /// <summary>Web defaults with the currently registered JSON contracts.</summary>
    public static JsonSerializerOptions Options => GetOptions(null);

    /// <summary>
    /// Adds source-generated metadata without introducing a dependency on the extension assembly.
    /// Register at startup; existing serializers finish with their original metadata snapshot.
    /// </summary>
    public static void RegisterContext(JsonSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (Gate)
        {
            if (Resolvers.Contains(context))
                return;
            Resolvers.Add(context);
            _options = new();
        }
    }

    public static string Serialize(object? value) => Serialize(value, null);

    public static string Serialize(object? value, JsonSerializerOptions? options)
    {
        try
        {
            var configured = GetOptions(options);
            return JsonSerializer.Serialize(value, (JsonTypeInfo<object?>)GetTypeInfo(typeof(object), configured));
        }
        catch (NotSupportedException ex)
        {
            throw new UnsupportedJsonException(ex);
        }
    }

    public static JsonElement SerializeToElement(object? value) => SerializeToElement(value, null);

    public static JsonElement SerializeToElement(object? value, JsonSerializerOptions? options)
    {
        try
        {
            return JsonSerializer.SerializeToElement(value, (JsonTypeInfo<object?>)GetTypeInfo(typeof(object), GetOptions(options)));
        }
        catch (NotSupportedException ex)
        {
            throw new UnsupportedJsonException(ex);
        }
    }

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        try
        {
            return JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)GetTypeInfo(typeof(T), GetOptions(options)));
        }
        catch (NotSupportedException ex)
        {
            throw new UnsupportedJsonException(ex);
        }
    }

    internal static bool IsUnsupportedJson(Exception exception) => exception is UnsupportedJsonException;

    private sealed class UnsupportedJsonException(NotSupportedException inner) : NotSupportedException(inner.Message, inner);

    internal static bool IsMissingMetadata(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is MissingMetadataException ||
                current is NotSupportedException && current.Message.Contains(MetadataDiagnostic, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        try
        {
            return options.GetTypeInfo(type);
        }
        catch (NotSupportedException ex)
        {
            throw new MissingMetadataException(type, ex);
        }
    }

    private sealed class MissingMetadataException(Type type, Exception inner) : NotSupportedException(
        $"DevFlow JSON metadata is missing for '{type}'. Register a source-generated context with DevFlowJson.RegisterContext (or AgentJson.RegisterContext), or return a JSON node/element or string-keyed dictionary.", inner);

    private static JsonSerializerOptions GetOptions(JsonSerializerOptions? options)
    {
        lock (Gate)
            return _options.GetValue(options ?? Defaults, CreateOptions);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification =
        "The reflection resolver is used only when the System.Text.Json reflection feature switch is enabled. Trimmed/AOT hosts use generated metadata exclusively.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification =
        "The System.Text.Json reflection feature switch is disabled by default in NativeAOT. It is not enabled by DevFlow.")]
    private static JsonSerializerOptions CreateOptions(JsonSerializerOptions template)
    {
        var resolvers = Resolvers.ToList();
        if (template.TypeInfoResolver is { } resolver)
            resolvers.Add(resolver);
        // Preserve the pre-existing public HttpResponse.Json(object)/BodyAs<T> behavior in
        // reflection-enabled hosts. Do not use IsDynamicCodeSupported: trimmed JIT needs
        // exactly the same explicit metadata contract as NativeAOT.
        if (JsonSerializer.IsReflectionEnabledByDefault)
            resolvers.Add(new DefaultJsonTypeInfoResolver());
        resolvers.Add(DevFlowCollectionJsonContext.Default);
        var stringEnums = ReferenceEquals(template, Defaults);
        resolvers.Add(new DynamicEnumResolver(stringEnums));
        var options = new JsonSerializerOptions(template)
        {
            TypeInfoResolver = new AgentTypeInfoResolver(JsonTypeInfoResolver.Combine(resolvers.ToArray()))
        };
        if (JsonSerializer.IsReflectionEnabledByDefault && stringEnums)
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly();
        return options;
    }

    private sealed class AgentTypeInfoResolver(IJsonTypeInfoResolver resolver) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) => resolver.GetTypeInfo(type, options);

        // STJ includes the resolver in missing-metadata diagnostics. Use our own invariant
        // marker, not localized framework text, to identify those errors at the HTTP boundary.
        public override string ToString()
            => $"{MetadataDiagnostic} Register a source-generated context with DevFlowJson.RegisterContext (or AgentJson.RegisterContext), or return a JSON node/element or string-keyed dictionary.";
    }

    private sealed class DynamicEnumResolver(bool stringEnums) : IJsonTypeInfoResolver
    {
        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            // Collections use generated STJ contracts, including their native write stack.
            if (type == typeof(Enum))
                return JsonMetadataServices.CreateValueInfo<Enum>(options, new DynamicEnumConverter(stringEnums));
            return null;
        }
    }

    private sealed class DynamicEnumConverter(bool stringEnums) : JsonConverter<Enum>
    {
        public override Enum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => throw new NotSupportedException("Register a concrete enum contract with AgentJson.RegisterContext for deserialization.");

        public override void Write(Utf8JsonWriter writer, Enum value, JsonSerializerOptions options)
        {
            if (stringEnums)
            {
                writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
                return;
            }
            if (Enum.GetUnderlyingType(value.GetType()) == typeof(ulong))
                writer.WriteNumberValue(Convert.ToUInt64(value));
            else
                writer.WriteNumberValue(Convert.ToInt64(value));
        }
    }
}
