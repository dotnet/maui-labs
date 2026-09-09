// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class ReasoningContentBlockTests
{
    [Fact]
    public void AppendText_AccumulatesReasoningTokens()
    {
        var block = new ReasoningContentBlock();
        block.AppendText("Let me think...");
        block.AppendText(" 42.");

        Assert.Equal("Let me think... 42.", block.Text);
    }

    [Fact]
    public void IsEncrypted_ProtectedDataWithoutText_ReturnsTrue()
    {
        var block = new ReasoningContentBlock { ProtectedData = "protected" };

        Assert.True(block.IsEncrypted);
    }

    [Fact]
    public void IsEncrypted_VisibleTextAndProtectedData_ReturnsFalse()
    {
        var block = new ReasoningContentBlock { ProtectedData = "protected" };
        block.AppendText("visible");

        Assert.False(block.IsEncrypted);
    }

    [Fact]
    public void AppendText_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ReasoningContentBlock().AppendText(null!));
    }
}
