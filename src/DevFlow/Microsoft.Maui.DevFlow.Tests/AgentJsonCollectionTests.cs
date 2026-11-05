using System.Collections;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Tests;

public class AgentJsonCollectionTests(ITestOutputHelper output)
{
    [Fact]
    public void Response_MemberNumberSettingsDoNotLeakBetweenCollections()
    {
        AgentJson.RegisterContext(AgentCollectionContext.Default);
        var value = new MixedCollectionPayload();
        var options = new JsonSerializerOptions { TypeInfoResolver = CollectionControlContext.Default };
        var expected = JsonSerializer.Serialize<object>(value, options);
        var response = HttpResponse.Json(value);
        AssertJsonEqual(expected, response.Body!);
        using var document = JsonDocument.Parse(response.Body!);
        Assert.Equal("12", document.RootElement.GetProperty("Strings")[0].GetString());
        Assert.Equal("NaN", document.RootElement.GetProperty("Named")[0].GetString());
        Assert.Equal(12, document.RootElement.GetProperty("Strict")[0].GetInt32());
    }

    [Fact]
    public void Serialize_DynamicDictionaryPreservesNullAndNamingPolicies()
    {
        AgentJson.RegisterContext(AgentCollectionContext.Default);
        var value = new UnannotatedNumberPayload(new ReadOnlyNumberDictionary(
            new Dictionary<string, object> { ["NullValue"] = null!, ["NumberValue"] = 12 }));
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var expected = JsonSerializer.Serialize<object>(value, new JsonSerializerOptions(options)
        {
            TypeInfoResolver = CollectionControlContext.Default
        });
        var actual = AgentJson.Serialize(value, options);
        AssertJsonEqual(expected, actual);
        using var document = JsonDocument.Parse(actual);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("value").GetProperty("nullValue").ValueKind);
    }

    [Fact]
    public void Serialize_UserCollectionContractPrecedesBuiltInAncestors()
    {
        var value = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { ["Number"] = 12 });
        var options = new JsonSerializerOptions { TypeInfoResolver = new ReadOnlyContractResolver() };
        Assert.Equal("\"explicit-read-only\"", AgentJson.Serialize(value, options));
    }

    [Fact]
    public void Response_MultipleDictionaryInterfacesRequireConcreteMetadata()
    {
        var value = new MultipleInterfaceDictionary { ["Number"] = 12 };
        var options = new JsonSerializerOptions { TypeInfoResolver = CollectionAncestorControlContext.Default };
        var baseline = Assert.ThrowsAny<NotSupportedException>(() => JsonSerializer.Serialize<object>(value, options));
        output.WriteLine($"STJ ancestor control: {baseline.Message}");

        if (!JsonSerializer.IsReflectionEnabledByDefault)
        {
            var actual = Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(value));
            output.WriteLine($"Agent ancestor resolution: {actual.Message}");
        }
        else
        {
            using var reflected = JsonDocument.Parse(HttpResponse.Json(value).Body!);
            Assert.Equal(12, reflected.RootElement.GetProperty("Number").GetInt32());
        }

        AgentJson.RegisterContext(ExplicitDictionaryContext.Default);
        using var response = JsonDocument.Parse(HttpResponse.Json(value).Body!);
        Assert.Equal(12, response.RootElement.GetProperty("Number").GetInt32());
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var shape in new[]
        {
            "iterator", "array", "array-list", "known-list", "unknown-list",
            "dictionary", "known-dictionary", "unknown-dictionary", "read-only",
            "read-only-interface", "expando", "hashtable", "nested-sequence",
            "dictionary-sequence", "sequence-dictionary", "sequence-object", "converter"
        })
        {
            yield return [shape, false];
            yield return [shape, true];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Response_ContainingMemberMatchesStjCollectionTraversal(string shape, bool named)
    {
        AgentJson.RegisterContext(AgentCollectionContext.Default);
        object number = named ? (object)double.NaN : 12;
        var collection = CreateCollection(shape, number);
        object payload = named ? new NamedCollectionPayload(collection) : new StringCollectionPayload(collection);
        var controlOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = CollectionControlContext.Default
        };
        string? expected = null;
        var controlError = Record.Exception(() => expected = JsonSerializer.Serialize(payload, controlOptions));
        output.WriteLine($"Generated STJ: {expected ?? controlError!.GetType().Name + ": " + controlError.Message}");

        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            string? reflection = null;
            var reflectionError = Record.Exception(() => reflection = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }));
            output.WriteLine($"Reflection STJ: {reflection ?? reflectionError!.GetType().Name + ": " + reflectionError.Message}");
            Assert.Equal(reflectionError?.GetType(), controlError?.GetType());
            if (reflectionError is null)
                AssertJsonEqual(reflection!, expected!);
        }

        if (controlError is not null)
        {
            var actualError = Record.Exception(() => HttpResponse.Json(payload));
            Assert.NotNull(actualError);
            Assert.Equal(controlError.GetType(), actualError.GetType());
            return;
        }

        var response = HttpResponse.Json(payload);
        output.WriteLine($"Agent: {response.Body}");
        AssertJsonEqual(expected!, response.Body!);
    }

    private static void AssertJsonEqual(string expected, string actual)
    {
        using var left = JsonDocument.Parse(expected);
        using var right = JsonDocument.Parse(actual);
        Assert.True(JsonElement.DeepEquals(left.RootElement, right.RootElement), $"Expected {expected}; actual {actual}");
    }

    private static object CreateCollection(string shape, object number)
    {
        var dictionary = new Dictionary<string, object> { ["Number"] = number };
        return shape switch
        {
            "iterator" => new[] { number }.Select(x => x),
            "array" => new[] { number },
            "array-list" => new ArrayList { number },
            "known-list" => new KnownNumberList { number },
            "unknown-list" => new UnknownNumberList { number },
            "dictionary" => dictionary,
            "known-dictionary" => new KnownNumberDictionary { ["Number"] = number },
            "unknown-dictionary" => new UnknownNumberDictionary { ["Number"] = number },
            "read-only" => new ReadOnlyDictionary<string, object>(dictionary),
            "read-only-interface" => new ReadOnlyNumberDictionary(dictionary),
            "expando" => CreateExpando(number),
            "hashtable" => new Hashtable { ["Number"] = number },
            "nested-sequence" => new object[] { new[] { number }.Select(x => x) }.Select(x => x),
            "dictionary-sequence" => new Dictionary<string, object> { ["Items"] = new[] { number }.Select(x => x) },
            "sequence-dictionary" => new object[] { dictionary }.Select(x => x),
            "sequence-object" => new object[] { new UnannotatedNumberPayload(number) }.Select(x => x),
            "converter" => new ConvertedNumberCollection { number },
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
    }

    private static ExpandoObject CreateExpando(object number)
    {
        var result = new ExpandoObject();
        ((IDictionary<string, object?>)result)["Number"] = number;
        return result;
    }
}

internal sealed class StringCollectionPayload(object value)
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Value { get; } = value;
}

internal sealed class NamedCollectionPayload(object value)
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; } = value;
}

internal sealed class UnannotatedNumberPayload(object value)
{
    public object Value { get; } = value;
}

internal sealed class MixedCollectionPayload
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Strings { get; } = new[] { 12 }.Select(x => (object)x);
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Named { get; } = new[] { double.NaN }.Select(x => (object)x);
    public object Strict { get; } = new[] { 12 }.Select(x => (object)x);
}

internal sealed class KnownNumberList : List<object>;
internal sealed class UnknownNumberList : List<object>;
internal sealed class KnownNumberDictionary : Dictionary<string, object>;
internal sealed class UnknownNumberDictionary : Dictionary<string, object>;
internal sealed class ReadOnlyNumberDictionary(IReadOnlyDictionary<string, object> values) : IReadOnlyDictionary<string, object>
{
    public object this[string key] => values[key];
    public IEnumerable<string> Keys => values.Keys;
    public IEnumerable<object> Values => values.Values;
    public int Count => values.Count;
    public bool ContainsKey(string key) => values.ContainsKey(key);
    public bool TryGetValue(string key, out object value) => values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class MultipleInterfaceDictionary : IDictionary<string, object>, IReadOnlyDictionary<string, object>
{
    private readonly Dictionary<string, object> _values = [];
    public object this[string key] { get => _values[key]; set => _values[key] = value; }
    public ICollection<string> Keys => _values.Keys;
    public ICollection<object> Values => _values.Values;
    IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => Keys;
    IEnumerable<object> IReadOnlyDictionary<string, object>.Values => Values;
    public int Count => _values.Count;
    public bool IsReadOnly => false;
    public void Add(string key, object value) => _values.Add(key, value);
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool Remove(string key) => _values.Remove(key);
    public bool TryGetValue(string key, out object value) => _values.TryGetValue(key, out value!);
    public void Add(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Add(item);
    public void Clear() => _values.Clear();
    public bool Contains(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Contains(item);
    public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, object>>)_values).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<string, object> item) => ((ICollection<KeyValuePair<string, object>>)_values).Remove(item);
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[JsonConverter(typeof(NumberCollectionConverter))]
internal sealed class ConvertedNumberCollection : List<object>;
internal sealed class NumberCollectionConverter : JsonConverter<ConvertedNumberCollection>
{
    public override ConvertedNumberCollection Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Write-only test converter.");
    public override void Write(Utf8JsonWriter writer, ConvertedNumberCollection value, JsonSerializerOptions options)
        => writer.WriteStringValue("explicit");
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(StringCollectionPayload))]
[JsonSerializable(typeof(NamedCollectionPayload))]
[JsonSerializable(typeof(UnannotatedNumberPayload))]
[JsonSerializable(typeof(MixedCollectionPayload))]
[JsonSerializable(typeof(KnownNumberList))]
[JsonSerializable(typeof(KnownNumberDictionary))]
[JsonSerializable(typeof(ConvertedNumberCollection))]
internal partial class AgentCollectionContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(IEnumerable))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(ArrayList))]
[JsonSerializable(typeof(Hashtable))]
[JsonSerializable(typeof(ExpandoObject))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(ReadOnlyDictionary<string, object>))]
[JsonSerializable(typeof(ReadOnlyNumberDictionary))]
[JsonSerializable(typeof(StringCollectionPayload))]
[JsonSerializable(typeof(NamedCollectionPayload))]
[JsonSerializable(typeof(UnannotatedNumberPayload))]
[JsonSerializable(typeof(MixedCollectionPayload))]
[JsonSerializable(typeof(KnownNumberList))]
[JsonSerializable(typeof(UnknownNumberList))]
[JsonSerializable(typeof(KnownNumberDictionary))]
[JsonSerializable(typeof(UnknownNumberDictionary))]
[JsonSerializable(typeof(ConvertedNumberCollection))]
internal partial class CollectionControlContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(IEnumerable))]
[JsonSerializable(typeof(IDictionary))]
[JsonSerializable(typeof(IDictionary<string, object>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
internal partial class CollectionAncestorControlContext : JsonSerializerContext;

[JsonSerializable(typeof(MultipleInterfaceDictionary))]
internal partial class ExplicitDictionaryContext : JsonSerializerContext;

internal sealed class ReadOnlyContractResolver : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        => type == typeof(ReadOnlyDictionary<string, object>)
            ? JsonMetadataServices.CreateValueInfo<ReadOnlyDictionary<string, object>>(options, new ReadOnlyContractConverter())
            : null;
}
internal sealed class ReadOnlyContractConverter : JsonConverter<ReadOnlyDictionary<string, object>>
{
    public override ReadOnlyDictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Write-only test converter.");
    public override void Write(Utf8JsonWriter writer, ReadOnlyDictionary<string, object> value, JsonSerializerOptions options)
        => writer.WriteStringValue("explicit-read-only");
}
