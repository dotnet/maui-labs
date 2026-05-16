// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.AI.Controls.Tests;

public class JsonPatchTests
{
    [Fact]
    public void Apply_AddProperty_CreatesNewProperty()
    {
        var doc = JsonNode.Parse("""{"name":"test"}""");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "add",
                Path = "/age",
                Value = JsonSerializer.Deserialize<JsonElement>("25")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal(25, result["age"]!.GetValue<int>());
        Assert.Equal("test", result["name"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_ReplaceProperty_UpdatesExistingValue()
    {
        var doc = JsonNode.Parse("""{"status":"pending"}""");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "replace",
                Path = "/status",
                Value = JsonSerializer.Deserialize<JsonElement>("\"completed\"")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal("completed", result["status"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_RemoveProperty_DeletesIt()
    {
        var doc = JsonNode.Parse("""{"name":"test","age":25}""");
        var ops = new[]
        {
            new JsonPatchOperation { Op = "remove", Path = "/age" }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal("test", result["name"]!.GetValue<string>());
        Assert.Null(result["age"]);
    }

    [Fact]
    public void Apply_NestedPath_NavigatesCorrectly()
    {
        var doc = JsonNode.Parse("""{"steps":[{"status":"pending"}]}""");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "replace",
                Path = "/steps/0/status",
                Value = JsonSerializer.Deserialize<JsonElement>("\"done\"")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        var steps = result["steps"]!.AsArray();
        Assert.Equal("done", steps[0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_MultipleOperations_AppliesInOrder()
    {
        var doc = JsonNode.Parse("""{"count":0}""");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "replace",
                Path = "/count",
                Value = JsonSerializer.Deserialize<JsonElement>("1")
            },
            new JsonPatchOperation
            {
                Op = "add",
                Path = "/name",
                Value = JsonSerializer.Deserialize<JsonElement>("\"hello\"")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal(1, result["count"]!.GetValue<int>());
        Assert.Equal("hello", result["name"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_FromBytes_DeserializesAndApplies()
    {
        var doc = JsonNode.Parse("""{"value":"old"}""");
        var patchJson = """[{"op":"replace","path":"/value","value":"new"}]""";
        var patchBytes = Encoding.UTF8.GetBytes(patchJson);

        var result = JsonPatch.Apply(doc, patchBytes);

        Assert.NotNull(result);
        Assert.Equal("new", result["value"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_NullRoot_AddCreatesDocument()
    {
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "add",
                Path = "/name",
                Value = JsonSerializer.Deserialize<JsonElement>("\"test\"")
            }
        };

        var result = JsonPatch.Apply(null, ops);

        Assert.NotNull(result);
        Assert.Equal("test", result["name"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_RootReplace_ReplacesEntireDocument()
    {
        var doc = JsonNode.Parse("""{"old":"data"}""");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "replace",
                Path = "/",
                Value = JsonSerializer.Deserialize<JsonElement>("""{"new":"data"}""")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal("data", result["new"]!.GetValue<string>());
    }

    [Fact]
    public void Apply_AddToNestedPath_CreatesIntermediateObjects()
    {
        var doc = JsonNode.Parse("{}");
        var ops = new[]
        {
            new JsonPatchOperation
            {
                Op = "add",
                Path = "/a/b",
                Value = JsonSerializer.Deserialize<JsonElement>("42")
            }
        };

        var result = JsonPatch.Apply(doc, ops);

        Assert.NotNull(result);
        Assert.Equal(42, result["a"]!["b"]!.GetValue<int>());
    }
}
