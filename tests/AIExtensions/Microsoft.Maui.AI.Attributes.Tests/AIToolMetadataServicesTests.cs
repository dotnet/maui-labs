using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

/// <summary>
/// Tests the runtime helper methods in <see cref="AIToolMetadataServices"/>:
/// <c>GetRequiredArg</c>, <c>GetOptionalArg</c>, and the internal <c>ConvertArg</c> paths.
/// </summary>
public class AIToolMetadataServicesTests
{
    private static JsonTypeInfo<T> TypeInfo<T>() =>
        (JsonTypeInfo<T>)AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T));

    // ── GetRequiredArg ──────────────────────────────────────────────────

    [Fact]
    public void GetRequiredArg_DirectCast_ReturnsValue()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "hello" });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x", TypeInfo<string>());
        Assert.Equal("hello", result);
    }

    [Fact]
    public void GetRequiredArg_MissingKey_Throws()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>());
        var ex = Assert.Throws<ArgumentException>(
            () => AIToolMetadataServices.GetRequiredArg<string>(args, "missing", TypeInfo<string>()));
        Assert.Contains("missing", ex.Message);
        Assert.Contains("Missing required argument", ex.Message);
    }

    [Fact]
    public void GetRequiredArg_NullValueForNullableType_ReturnsNull()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var result = AIToolMetadataServices.GetRequiredArg<string?>(args, "x", TypeInfo<string?>());
        Assert.Null(result);
    }

    [Fact]
    public void GetRequiredArg_NullValueForNonNullableType_Throws()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var ex = Assert.Throws<ArgumentException>(
            () => AIToolMetadataServices.GetRequiredArg<int>(args, "x", TypeInfo<int>()));
        Assert.Contains("null", ex.Message);
        Assert.Contains("non-nullable", ex.Message);
    }

    // ── GetOptionalArg ──────────────────────────────────────────────────

    [Fact]
    public void GetOptionalArg_MissingKey_ReturnsDefault()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>());
        var result = AIToolMetadataServices.GetOptionalArg<string>(args, "missing", "fallback", TypeInfo<string>());
        Assert.Equal("fallback", result);
    }

    [Fact]
    public void GetOptionalArg_NullValue_ReturnsDefault()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = null });
        var result = AIToolMetadataServices.GetOptionalArg<int>(args, "x", 42, TypeInfo<int>());
        Assert.Equal(42, result);
    }

    [Fact]
    public void GetOptionalArg_PresentValue_ReturnsConverted()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "hello" });
        var result = AIToolMetadataServices.GetOptionalArg<string>(args, "x", "fallback", TypeInfo<string>());
        Assert.Equal("hello", result);
    }

    // ── ConvertArg paths (via GetRequiredArg) ───────────────────────────

    [Fact]
    public void ConvertArg_JsonElement_Deserializes()
    {
        var je = JsonDocument.Parse("42").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<int>(args, "x", TypeInfo<int>());
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertArg_JsonNode_Deserializes()
    {
        var jn = JsonNode.Parse("\"world\"")!;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = jn });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x", TypeInfo<string>());
        Assert.Equal("world", result);
    }

    [Fact]
    public void ConvertArg_RawJsonString_DeserializesToNonStringType()
    {
        // LLM sometimes sends a JSON string representation for non-string targets
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "42" });
        var result = AIToolMetadataServices.GetRequiredArg<int>(args, "x", TypeInfo<int>());
        Assert.Equal(42, result);
    }

    [Fact]
    public void ConvertArg_ObjectValue_FallsBackToJsonRoundTrip()
    {
        // A boxed int passed where a long is expected — not a direct cast match
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = 42 });
        var result = AIToolMetadataServices.GetRequiredArg<long>(args, "x", TypeInfo<long>());
        Assert.Equal(42L, result);
    }

    [Fact]
    public void ConvertArg_ComplexObject_DeserializesViaRoundTrip()
    {
        var obj = new Dictionary<string, string> { ["key"] = "val" };
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = obj });
        var result = AIToolMetadataServices.GetRequiredArg<Dictionary<string, string>>(args, "x", TypeInfo<Dictionary<string, string>>());
        Assert.Equal("val", result["key"]);
    }

    [Fact]
    public void ConvertArg_JsonElement_StringValue_DeserializesCorrectly()
    {
        var je = JsonDocument.Parse("\"hello\"").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<string>(args, "x", TypeInfo<string>());
        Assert.Equal("hello", result);
    }

    [Fact]
    public void ConvertArg_JsonElement_ComplexObject_Deserializes()
    {
        var je = JsonDocument.Parse("{\"Name\":\"test\"}").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<SimpleDto>(args, "x", TypeInfo<SimpleDto>());
        Assert.Equal("test", result.Name);
    }

    [Fact]
    public void ConvertArg_JsonNode_NumberValue_Deserializes()
    {
        var jn = JsonNode.Parse("3.14")!;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = jn });
        var result = AIToolMetadataServices.GetRequiredArg<double>(args, "x", TypeInfo<double>());
        Assert.Equal(3.14, result);
    }

    [Fact]
    public void ConvertArg_InvalidJsonString_FallsBackToRoundTrip()
    {
        // A string value that is NOT valid JSON for int — falls through to round-trip
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "not-a-number" });
        // Round-trip: JsonSerializer.Serialize("not-a-number") → "\"not-a-number\"" → Deserialize<int> → fail
        Assert.ThrowsAny<Exception>(
            () => AIToolMetadataServices.GetRequiredArg<int>(args, "x", TypeInfo<int>()));
    }

    // ── Enum conversion ────────────────────────────────────────────────

    [Fact]
    public void ConvertArg_JsonElement_Enum_Deserializes()
    {
        var je = JsonDocument.Parse("\"High\"").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<Priority>(args, "x", TypeInfo<Priority>());
        Assert.Equal(Priority.High, result);
    }

    [Fact]
    public void ConvertArg_JsonString_Enum_Deserializes()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = "\"Medium\"" });
        var result = AIToolMetadataServices.GetRequiredArg<Priority>(args, "x", TypeInfo<Priority>());
        Assert.Equal(Priority.Medium, result);
    }

    // ── Collection conversion ──────────────────────────────────────────

    [Fact]
    public void ConvertArg_JsonElement_List_Deserializes()
    {
        var je = JsonDocument.Parse("[\"a\",\"b\",\"c\"]").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<List<string>>(args, "x", TypeInfo<List<string>>());
        Assert.Equal(3, result.Count);
        Assert.Equal("b", result[1]);
    }

    [Fact]
    public void ConvertArg_JsonElement_Dictionary_Deserializes()
    {
        var je = JsonDocument.Parse("{\"key1\":1,\"key2\":2}").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<Dictionary<string, int>>(args, "x", TypeInfo<Dictionary<string, int>>());
        Assert.Equal(2, result.Count);
        Assert.Equal(1, result["key1"]);
    }

    // ── Complex DTO conversion ─────────────────────────────────────────

    [Fact]
    public void ConvertArg_JsonElement_ComplexDto_WithNestedProperties()
    {
        var je = JsonDocument.Parse("{\"Name\":\"Fern\"}").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetRequiredArg<SimpleDto>(args, "x", TypeInfo<SimpleDto>());
        Assert.Equal("Fern", result.Name);
    }

    [Fact]
    public void ConvertArg_JsonNode_List_Deserializes()
    {
        var jn = JsonNode.Parse("[1,2,3]")!;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = jn });
        var result = AIToolMetadataServices.GetRequiredArg<List<int>>(args, "x", TypeInfo<List<int>>());
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    // ── Nullable value type ────────────────────────────────────────────

    [Fact]
    public void GetOptionalArg_NullableInt_WithValue_ReturnsValue()
    {
        var je = JsonDocument.Parse("7").RootElement;
        var args = new AIFunctionArguments(new Dictionary<string, object?> { ["x"] = je });
        var result = AIToolMetadataServices.GetOptionalArg<int?>(args, "x", null, TypeInfo<int?>());
        Assert.Equal(7, result);
    }

    [Fact]
    public void GetOptionalArg_NullableInt_Missing_ReturnsDefault()
    {
        var args = new AIFunctionArguments(new Dictionary<string, object?>());
        var result = AIToolMetadataServices.GetOptionalArg<int?>(args, "x", null, TypeInfo<int?>());
        Assert.Null(result);
    }

    // ── Test DTO ────────────────────────────────────────────────────────

    public class SimpleDto
    {
        public string Name { get; set; } = "";
    }
}
