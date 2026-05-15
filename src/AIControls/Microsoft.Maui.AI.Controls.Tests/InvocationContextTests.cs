// Copyright (c) Microsoft. All rights reserved.

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
}
