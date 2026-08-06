// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class AgentStateTests
{
    private sealed class TestState
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    [Fact]
    public void Value_NoInitialValue_CreatesDefaultState()
    {
        var agent = new UIAgent<TestState>(new DelegatingStreamingChatClient());

        Assert.NotNull(agent.State.Value);
        Assert.Equal(string.Empty, agent.State.Value.Name);
        Assert.Equal(0, agent.State.Value.Count);
    }

    [Fact]
    public void Value_InitialValue_UsesSameInstance()
    {
        var initial = new TestState { Name = "initial", Count = 5 };
        var agent = new UIAgent<TestState>(
            new DelegatingStreamingChatClient(),
            initial);

        Assert.Same(initial, agent.State.Value);
    }

    [Fact]
    public void Value_Replaced_NotifiesEveryObserver()
    {
        var agent = new UIAgent<TestState>(new DelegatingStreamingChatClient());
        var firstCount = 0;
        var secondCount = 0;
        agent.State.OnChanged(() => firstCount++);
        agent.State.OnChanged(() => secondCount++);

        var replacement = new TestState { Name = "updated" };
        agent.State.Value = replacement;

        Assert.Same(replacement, agent.State.Value);
        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void OnChanged_DisposedRegistration_StopsNotification()
    {
        var agent = new UIAgent<TestState>(new DelegatingStreamingChatClient());
        var count = 0;
        var registration = agent.State.OnChanged(() => count++);

        agent.State.Value = new TestState();
        registration.Dispose();
        agent.State.Value = new TestState();

        Assert.Equal(1, count);
    }

    [Fact]
    public void OnChanged_NullCallback_Throws()
    {
        var agent = new UIAgent<TestState>(new DelegatingStreamingChatClient());

        Assert.Throws<ArgumentNullException>(() => agent.State.OnChanged(null!));
    }

    [Fact]
    public void Value_Null_Throws()
    {
        var agent = new UIAgent<TestState>(new DelegatingStreamingChatClient());

        Assert.Throws<ArgumentNullException>(() => agent.State.Value = null!);
    }
}
