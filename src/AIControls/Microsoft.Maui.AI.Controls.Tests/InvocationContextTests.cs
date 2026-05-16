// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;

namespace Microsoft.Maui.AI.Controls.Tests;

public class InvocationContextTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var args = new Dictionary<string, object?> { ["city"] = "Tokyo", ["units"] = "celsius" };
        var ctx = new InvocationContext("call-1", "get_weather", args);

        Assert.Equal("call-1", ctx.CallId);
        Assert.Equal("get_weather", ctx.Name);
        Assert.Same(args, ctx.Arguments);
        Assert.False(ctx.HasResult);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public void GetArgument_ReturnsTypedArgument()
    {
        var args = new Dictionary<string, object?> { ["city"] = "Tokyo", ["temp"] = 25 };
        var ctx = new InvocationContext("call-1", "get_weather", args);

        Assert.Equal("Tokyo", ctx.GetArgument<string>("city"));
        Assert.Equal(25, ctx.GetArgument<int>("temp"));
    }

    [Fact]
    public void GetArgument_MissingKey_ReturnsDefault()
    {
        var ctx = new InvocationContext("call-1", "get_weather", new Dictionary<string, object?>());
        Assert.Null(ctx.GetArgument<string>("missing"));
        Assert.Equal(0, ctx.GetArgument<int>("missing"));
    }

    [Fact]
    public void GetArgument_NullArguments_ReturnsDefault()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        Assert.Null(ctx.GetArgument<string>("city"));
    }

    [Fact]
    public void GetArgument_JsonElement_DeserializesCorrectly()
    {
        // M.E.AI tools pass arguments as JsonElement — this is the most common case
        var element = JsonSerializer.Deserialize<JsonElement>("\"Seattle\"");
        var args = new Dictionary<string, object?> { ["city"] = element };
        var ctx = new InvocationContext("call-1", "get_weather", args);

        Assert.Equal("Seattle", ctx.GetArgument<string>("city"));
    }

    [Fact]
    public void GetArgument_JsonElement_Number_DeserializesCorrectly()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("42");
        var args = new Dictionary<string, object?> { ["count"] = element };
        var ctx = new InvocationContext("call-1", "test", args);

        Assert.Equal(42, ctx.GetArgument<int>("count"));
    }

    [Fact]
    public void GetArgument_JsonElement_Bool_DeserializesCorrectly()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("true");
        var args = new Dictionary<string, object?> { ["flag"] = element };
        var ctx = new InvocationContext("call-1", "test", args);

        Assert.True(ctx.GetArgument<bool>("flag"));
    }

    [Fact]
    public void SetResult_SetsHasResultAndValue()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        ctx.SetResult("sunny, 25°C");

        Assert.True(ctx.HasResult);
        Assert.Equal("sunny, 25°C", ctx.Result);
    }

    [Fact]
    public void SetResult_FiresResultArrivedEvent()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        bool eventFired = false;
        ctx.ResultArrived += () => eventFired = true;

        ctx.SetResult("result");

        Assert.True(eventFired);
    }

    [Fact]
    public void GetResult_ReturnsTypedResult()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        ctx.SetResult(42);

        Assert.Equal(42, ctx.GetResult<int>());
    }

    [Fact]
    public void GetResult_NoResult_ReturnsDefault()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        Assert.Null(ctx.GetResult<string>());
        Assert.Equal(0, ctx.GetResult<int>());
    }

    [Fact]
    public void GetResult_NullResult_ReturnsDefault()
    {
        var ctx = new InvocationContext("call-1", "get_weather", null);
        ctx.SetResult(null);

        Assert.True(ctx.HasResult);
        Assert.Null(ctx.GetResult<string>());
    }

    [Fact]
    public void GetResult_JsonElement_DeserializesCorrectly()
    {
        var ctx = new InvocationContext("call-1", "test", null);
        var element = JsonSerializer.Deserialize<JsonElement>("""{"temp":72,"desc":"sunny"}""");
        ctx.SetResult(element);

        // Can't deserialize to anonymous type, but can get as JsonElement
        var result = ctx.GetResult<JsonElement>();
        Assert.Equal(72, result.GetProperty("temp").GetInt32());
    }
}
