using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class AgentJsonTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Response_PreservesPolymorphicAncestorDiscriminator(bool generated)
    {
        object value = generated
            ? new AgentJsonDerivedPayload { Name = "derived", Count = 7 }
            : new ReflectionDerivedPayload { Name = "derived", Count = 7 };
        if (!generated && !JsonSerializer.IsReflectionEnabledByDefault)
        {
            Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(value));
            return;
        }

        var baseline = CompatibilityOptions(generated);
        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(value, baseline));
        Assert.Equal("derived", expected.RootElement.GetProperty("$kind").GetString());
        var response = HttpResponse.Json(value);
        using var actual = JsonDocument.Parse(response.Body!);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), response.Body);
        if (generated)
            Assert.Equal(7, Assert.IsType<AgentJsonDerivedPayload>(
                AgentJson.Deserialize<AgentJsonBasePayload>(response.Body!)).Count);
        else
            Assert.Equal(7, Assert.IsType<ReflectionDerivedPayload>(
                AgentJson.Deserialize<ReflectionBasePayload>(response.Body!)).Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Response_PreservesBoxedMemberWriteAsString(bool generated)
    {
        object value = generated ? new AgentJsonStringNumber() : new ReflectionStringNumber();
        if (!generated && !JsonSerializer.IsReflectionEnabledByDefault)
        {
            Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(value));
            return;
        }

        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(value, CompatibilityOptions(generated)));
        Assert.Equal("12", expected.RootElement.GetProperty("Value").GetString());
        using var actual = JsonDocument.Parse(HttpResponse.Json(value).Body!);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Response_PreservesBoxedMemberNamedFloatingPoint(bool generated)
    {
        object value = generated ? new AgentJsonNamedNumber() : new ReflectionNamedNumber();
        if (!generated && !JsonSerializer.IsReflectionEnabledByDefault)
        {
            Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(value));
            return;
        }

        using var expected = JsonDocument.Parse(JsonSerializer.Serialize(value, CompatibilityOptions(generated)));
        Assert.Equal("NaN", expected.RootElement.GetProperty("Value").GetString());
        using var actual = JsonDocument.Parse(HttpResponse.Json(value).Body!);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    private static JsonSerializerOptions CompatibilityOptions(bool generated)
    {
        if (generated)
            AgentJson.RegisterContext(AgentJsonCompatibilityContext.Default);
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            TypeInfoResolver = generated
                ? AgentJsonCompatibilityContext.Default
                : new DefaultJsonTypeInfoResolver()
        };
    }

    [Fact]
    public void Response_RegisteredConvertersOverrideDynamicAncestors()
    {
        AgentJson.RegisterContext(AgentJsonCompatibilityContext.Default);
        using var response = JsonDocument.Parse(HttpResponse.Json(new object[]
        {
            new AgentJsonConvertedSequence { 1, 2 }, AgentJsonNamedChoice.Selected
        }).Body!);
        Assert.Equal("explicit:2", response.RootElement[0].GetString());
        Assert.Equal("Selected", response.RootElement[1].GetString());
    }

    [Fact]
    public void Response_DynamicAncestorsKeepNestedContracts()
    {
        AgentJson.RegisterContext(AgentJsonCompatibilityContext.Default);
        object[] values =
        [
            new AgentJsonDerivedPayload { Count = 7 }, new AgentJsonStringNumber(),
            new AgentJsonNamedNumber(), AgentJsonUnregisteredChoice.Maximum
        ];
        var envelope = new AgentJsonMemberEnvelope
        {
            Value = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(
                new Dictionary<string, object> { ["items"] = values.Select(value => value) })
        };
        using var response = JsonDocument.Parse(HttpResponse.Json(envelope).Body!);
        var items = response.RootElement.GetProperty("Value").GetProperty("items");
        Assert.Equal("derived", items[0].GetProperty("$kind").GetString());
        Assert.Equal("12", items[1].GetProperty("Value").GetString());
        Assert.Equal("NaN", items[2].GetProperty("Value").GetString());
        Assert.Equal(ulong.MaxValue, items[3].GetUInt64());
    }

    [Fact]
    public void Response_RegisteredConverterFailureIsNotReportedAsMissingMetadata()
    {
        AgentJson.RegisterContext(AgentJsonCompatibilityContext.Default);
        var error = Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(new AgentJsonFailingValue()));
        Assert.Contains("Explicit converter failure.", error.Message);
        Assert.DoesNotContain("RegisterContext", error.Message);
        Assert.DoesNotContain("[DevFlowJsonMetadata]", error.Message);
    }

    [Fact]
    public void JsonValues_MatchExistingClientAndCliContracts()
    {
        const string json = """{"status":"ok","nullable":null,"values":[1,true,"text"]}""";
        using var source = JsonDocument.Parse(json);
        foreach (var value in new object[] { JsonNode.Parse(json)!, source.RootElement, source })
        {
            using var agent = JsonDocument.Parse(AgentJson.Serialize(value));
            using var client = JsonDocument.Parse(Microsoft.Maui.DevFlow.Driver.ProtocolJson.SerializeUntyped(value));
            using var cli = JsonDocument.Parse(Microsoft.Maui.Cli.DevFlow.CliJson.SerializeUntyped(value, indented: false));
            Assert.True(JsonElement.DeepEquals(agent.RootElement, client.RootElement));
            Assert.True(JsonElement.DeepEquals(agent.RootElement, cli.RootElement));
        }
    }

    [Fact]
    public void AgentTree_DeserializesThroughExistingPortableClientContract()
    {
        var response = HttpResponse.Json(new[]
        {
            new ElementInfo { Id = "button", Type = "Button", Text = "Test", IsVisible = true, IsEnabled = true }
        });
        var elements = Microsoft.Maui.DevFlow.Driver.ProtocolJson
            .Deserialize<List<Microsoft.Maui.DevFlow.Driver.ElementInfo>>(response.Body!);
        var element = Assert.Single(Assert.IsType<List<Microsoft.Maui.DevFlow.Driver.ElementInfo>>(elements));
        Assert.Equal("button", element.Id);
        Assert.Equal("Test", element.Text);
        Assert.True(element.IsVisible);
        Assert.True(element.IsEnabled);
    }

    [Fact]
    public void Response_PreservesElementContractAndNullOmission()
    {
        using var document = JsonDocument.Parse(HttpResponse.Json(new ElementInfo
        {
            Id = "button", Type = "Button", Children = [new ElementInfo { Id = "child" }]
        }).Body!);
        var root = document.RootElement;
        Assert.Equal("button", root.GetProperty("id").GetString());
        Assert.Equal("button", root.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("automationId").ValueKind);
        Assert.False(root.TryGetProperty("nativeProperties", out _));
        Assert.Equal("child", root.GetProperty("children")[0].GetProperty("id").GetString());
    }

    [Fact]
    public void Response_DynamicEnvelopesPreserveNullsPrimitivesAndDeferredSequences()
    {
        var response = HttpResponse.Json(new Dictionary<string, object?>
        {
            ["PascalCase"] = null, ["bytes"] = new byte[] { 1, 2 },
            ["array"] = Enumerable.Range(1, 3).Select(i => new Dictionary<string, object?> { ["value"] = i }),
            ["large"] = ulong.MaxValue, ["float"] = 1.5f, ["enum"] = DayOfWeek.Friday
        });
        using var document = JsonDocument.Parse(response.Body!);
        var root = document.RootElement;
        Assert.Equal(JsonValueKind.Null, root.GetProperty("PascalCase").ValueKind);
        Assert.Equal("AQI=", root.GetProperty("bytes").GetString());
        Assert.Equal(3, root.GetProperty("array").GetArrayLength());
        Assert.Equal(ulong.MaxValue, root.GetProperty("large").GetUInt64());
        Assert.Equal(5, root.GetProperty("enum").GetInt32());
        Assert.Contains('\n', response.Body!);
    }

    [Fact]
    public void Request_UsesGeneratedMetadataAndCaseInsensitiveNames()
    {
        var request = new HttpRequest { Body = """{"ELEMENTID":"button","CAPTUREEPOCH":42}""" };
        var body = request.BodyAs<ActionRequest>();
        Assert.Equal("button", body?.ElementId);
        Assert.Equal(42, body?.CaptureEpoch);
        Assert.Null(new HttpRequest().BodyAs<ActionRequest>());
        Assert.Throws<JsonException>(() => new HttpRequest { Body = "{" }.BodyAs<ActionRequest>());
    }

    [Fact]
    public void Errors_PreserveStatusCasingAndDictionaryNulls()
    {
        var response = HttpResponse.Error("failed", 409, "conflict", new Dictionary<string, object?> { ["nullable"] = null });
        Assert.Equal(409, response.StatusCode);
        Assert.Equal("Conflict", response.StatusText);
        using var document = JsonDocument.Parse(response.Body!);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("details").GetProperty("nullable").ValueKind);
        using var ok = JsonDocument.Parse(HttpResponse.Ok().Body!);
        Assert.Equal(JsonValueKind.Null, ok.RootElement.GetProperty("message").ValueKind);
    }

    [Fact]
    public void ExtensionContext_ComposesWithoutReplacingBuiltInContracts()
    {
        AgentJson.RegisterContext(AgentJsonTestContext.Default);
        AgentJson.RegisterContext(AgentJsonTestContext.Default);
        var value = new HttpRequest { Body = """{"VALUE":7}""" }.BodyAs<AgentJsonTestPayload>();
        Assert.Equal(7, value?.Value);
        using var document = JsonDocument.Parse(HttpResponse.Json(new object[]
        {
            value!, new ActionRequest { ElementId = "built-in" }
        }).Body!);
        Assert.Equal(7, document.RootElement[0].GetProperty("Value").GetInt32());
        Assert.Equal("built-in", document.RootElement[1].GetProperty("ElementId").GetString());
        using var dictionary = JsonDocument.Parse(HttpResponse.Json(new Dictionary<int, string> { [1] = "one" }).Body!);
        Assert.Equal("one", dictionary.RootElement.GetProperty("1").GetString());
    }

    [Fact]
    public void UnregisteredType_FailsExplicitlyUnlessHostEnablesJsonReflection()
    {
        var payload = new { custom = "legacy" };
        if (JsonSerializer.IsReflectionEnabledByDefault)
        {
            using var document = JsonDocument.Parse(HttpResponse.Json(payload).Body!);
            Assert.Equal("legacy", document.RootElement.GetProperty("custom").GetString());
        }
        else
        {
            var error = Assert.ThrowsAny<NotSupportedException>(() => HttpResponse.Json(payload));
            Assert.Contains("AgentJson.RegisterContext", error.Message);
        }
    }
}

internal sealed record AgentJsonTestPayload(int Value);
[JsonSerializable(typeof(AgentJsonTestPayload))]
[JsonSerializable(typeof(Dictionary<int, string>))]
internal partial class AgentJsonTestContext : JsonSerializerContext;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(AgentJsonDerivedPayload), "derived")]
internal abstract class AgentJsonBasePayload
{
    public string? Name { get; set; }
}
internal sealed class AgentJsonDerivedPayload : AgentJsonBasePayload
{
    public int Count { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(ReflectionDerivedPayload), "derived")]
internal abstract class ReflectionBasePayload
{
    public string? Name { get; set; }
}
internal sealed class ReflectionDerivedPayload : ReflectionBasePayload
{
    public int Count { get; set; }
}

internal sealed class AgentJsonStringNumber
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Value { get; set; } = 12;
}
internal sealed class ReflectionStringNumber
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public object Value { get; set; } = 12;
}
internal sealed class AgentJsonNamedNumber
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; set; } = double.NaN;
}
internal sealed class ReflectionNamedNumber
{
    [JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)]
    public object Value { get; set; } = double.NaN;
}

internal sealed class AgentJsonMemberEnvelope
{
    public object? Value { get; set; }
}

[JsonConverter(typeof(AgentJsonSequenceConverter))]
internal sealed class AgentJsonConvertedSequence : List<int>;
internal sealed class AgentJsonSequenceConverter : JsonConverter<AgentJsonConvertedSequence>
{
    public override AgentJsonConvertedSequence Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Write-only test converter.");
    public override void Write(Utf8JsonWriter writer, AgentJsonConvertedSequence value, JsonSerializerOptions options)
        => writer.WriteStringValue($"explicit:{value.Count}");
}

[JsonConverter(typeof(JsonStringEnumConverter<AgentJsonNamedChoice>))]
internal enum AgentJsonNamedChoice { Selected }
internal enum AgentJsonUnregisteredChoice : ulong { Maximum = ulong.MaxValue }

[JsonConverter(typeof(AgentJsonFailingConverter))]
internal sealed class AgentJsonFailingValue;
internal sealed class AgentJsonFailingConverter : JsonConverter<AgentJsonFailingValue>
{
    public override AgentJsonFailingValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("Explicit converter failure.");
    public override void Write(Utf8JsonWriter writer, AgentJsonFailingValue value, JsonSerializerOptions options)
        => throw new NotSupportedException("Explicit converter failure.");
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(AgentJsonBasePayload))]
[JsonSerializable(typeof(AgentJsonDerivedPayload))]
[JsonSerializable(typeof(AgentJsonStringNumber))]
[JsonSerializable(typeof(AgentJsonNamedNumber))]
[JsonSerializable(typeof(AgentJsonMemberEnvelope))]
[JsonSerializable(typeof(AgentJsonConvertedSequence))]
[JsonSerializable(typeof(AgentJsonNamedChoice))]
[JsonSerializable(typeof(AgentJsonFailingValue))]
internal partial class AgentJsonCompatibilityContext : JsonSerializerContext;
