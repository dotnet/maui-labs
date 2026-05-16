// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Controls.Tests;

public class ChatMessageViewModelTests
{
    [Fact]
    public void ToChatMessage_PreservesTextContent()
    {
        var vm = new ChatMessageViewModel(ChatRole.User, "Hello");
        vm.Contents.Add(new TextContent("Hello"));

        var msg = vm.ToChatMessage();

        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Single(msg.Contents);
        Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.Equal("Hello", ((TextContent)msg.Contents[0]).Text);
    }

    [Fact]
    public void ToChatMessage_PreservesFunctionCallContent()
    {
        var vm = new ChatMessageViewModel(ChatRole.Assistant);
        var call = new FunctionCallContent("call-1", "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle" });
        vm.Contents.Add(call);

        var msg = vm.ToChatMessage();

        Assert.Single(msg.Contents);
        var roundTripped = Assert.IsType<FunctionCallContent>(msg.Contents[0]);
        Assert.Equal("call-1", roundTripped.CallId);
        Assert.Equal("get_weather", roundTripped.Name);
    }

    [Fact]
    public void ToChatMessage_PreservesFunctionResultContent()
    {
        var vm = new ChatMessageViewModel(ChatRole.Tool);
        var result = new FunctionResultContent("call-1", "sunny, 72°F");
        vm.Contents.Add(result);

        var msg = vm.ToChatMessage();

        Assert.Single(msg.Contents);
        var roundTripped = Assert.IsType<FunctionResultContent>(msg.Contents[0]);
        Assert.Equal("call-1", roundTripped.CallId);
    }

    [Fact]
    public void ToChatMessage_PreservesMultipleMixedContent()
    {
        var vm = new ChatMessageViewModel(ChatRole.Assistant);
        vm.Contents.Add(new TextContent("Here's the weather:"));
        vm.Contents.Add(new FunctionCallContent("c1", "get_weather"));
        vm.Contents.Add(new FunctionResultContent("c1", "sunny"));
        vm.Contents.Add(new TextContent("It's sunny!"));

        var msg = vm.ToChatMessage();

        Assert.Equal(4, msg.Contents.Count);
        Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.IsType<FunctionCallContent>(msg.Contents[1]);
        Assert.IsType<FunctionResultContent>(msg.Contents[2]);
        Assert.IsType<TextContent>(msg.Contents[3]);
    }

    [Fact]
    public void ToChatMessage_FallsBackToText_WhenNoContents()
    {
        var vm = new ChatMessageViewModel(ChatRole.User, "Hello world");
        // No Contents added — should create TextContent from Text

        var msg = vm.ToChatMessage();

        Assert.Single(msg.Contents);
        var text = Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.Equal("Hello world", text.Text);
    }

    [Fact]
    public void FromChatMessage_RoundTrips()
    {
        var original = new ChatMessage(ChatRole.User, "test message") { AuthorName = "Bob" };
        var vm = ChatMessageViewModel.FromChatMessage(original);

        Assert.Equal(ChatRole.User, vm.Role);
        Assert.Equal("test message", vm.Text);
        Assert.Equal("Bob", vm.AuthorName);
    }

    [Fact]
    public void HasReasoning_TrueWhenReasoningTextSet()
    {
        var vm = new ChatMessageViewModel(ChatRole.Assistant);
        Assert.False(vm.HasReasoning);

        vm.ReasoningText = "Let me think...";
        Assert.True(vm.HasReasoning);
    }

    [Fact]
    public void IsUser_IsAssistant_ChangeWithRole()
    {
        var vm = new ChatMessageViewModel(ChatRole.User);
        Assert.True(vm.IsUser);
        Assert.False(vm.IsAssistant);

        vm.Role = ChatRole.Assistant;
        Assert.False(vm.IsUser);
        Assert.True(vm.IsAssistant);
    }
}
