// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class TextContentBlockTests
{
    [Fact]
    public void AppendText_AccumulatesTokens()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");
        block.AppendText(", ");
        block.AppendText("world!");

        Assert.Equal("Hello, world!", block.RawText);
    }

    [Fact]
    public void RawText_EmptyByDefault()
    {
        var block = new TextContentBlock();
        Assert.Equal(string.Empty, block.RawText);
    }

    [Fact]
    public void RawText_CachesResult()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");

        var first = block.RawText;
        var second = block.RawText;

        Assert.Same(first, second);
    }

    [Fact]
    public void RawText_InvalidatesCacheOnAppend()
    {
        var block = new TextContentBlock();
        block.AppendText("Hello");
        var first = block.RawText;

        block.AppendText(" world");
        var second = block.RawText;

        Assert.NotSame(first, second);
        Assert.Equal("Hello world", second);
    }
}
