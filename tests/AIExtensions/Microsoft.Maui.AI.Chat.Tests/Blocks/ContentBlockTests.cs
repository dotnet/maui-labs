// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class ContentBlockTests
{
    private class TestBlock : ContentBlock
    {
        public void TriggerChanged() => NotifyChanged();
    }

    [Fact]
    public void NotifyChanged_RaisesPropertyChangedForEveryProperty()
    {
        var block = new TestBlock();
        PropertyChangedEventArgs? raised = null;
        block.PropertyChanged += (sender, e) =>
        {
            Assert.Same(block, sender);
            raised = e;
        };

        block.TriggerChanged();

        Assert.Equal(string.Empty, raised?.PropertyName);
    }

    [Fact]
    public void NotifyChanged_ReachesEveryHandler()
    {
        var block = new TestBlock();
        var count = 0;
        block.PropertyChanged += (_, _) => count++;
        block.PropertyChanged += (_, _) => count++;

        block.TriggerChanged();

        Assert.Equal(2, count);
    }

    [Fact]
    public void NotifyChanged_DoesNotReachARemovedHandler()
    {
        var block = new TestBlock();
        var count = 0;
        PropertyChangedEventHandler handler = (_, _) => count++;
        block.PropertyChanged += handler;

        block.TriggerChanged();
        Assert.Equal(1, count);

        block.PropertyChanged -= handler;
        block.TriggerChanged();
        Assert.Equal(1, count);
    }
}
