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
    public void ToChatMessage_SkipsFunctionCallContent()
    {
        // FunctionCallContent is handled by FunctionInvokingChatClient within a single session
        // and must not be resent in conversation history
        var vm = new ChatMessageViewModel(ChatRole.Assistant);
        var call = new FunctionCallContent("call-1", "get_weather",
            new Dictionary<string, object?> { ["city"] = "Seattle" });
        vm.Contents.Add(call);

        var msg = vm.ToChatMessage();

        // No content in the message (tool call stripped), but empty contents with no text = empty message
        Assert.Empty(msg.Contents);
    }

    [Fact]
    public void ToChatMessage_SkipsFunctionResultContent()
    {
        var vm = new ChatMessageViewModel(ChatRole.Tool);
        var result = new FunctionResultContent("call-1", "sunny, 72°F");
        vm.Contents.Add(result);

        var msg = vm.ToChatMessage();

        Assert.Empty(msg.Contents);
    }

    [Fact]
    public void ToChatMessage_KeepsTextOnly_WhenMixedWithToolContent()
    {
        // Only TextContent and DataContent should survive the round-trip
        var vm = new ChatMessageViewModel(ChatRole.Assistant);
        vm.Contents.Add(new TextContent("Here's the weather:"));
        vm.Contents.Add(new FunctionCallContent("c1", "get_weather"));
        vm.Contents.Add(new FunctionResultContent("c1", "sunny"));
        vm.Contents.Add(new TextContent("It's sunny!"));

        var msg = vm.ToChatMessage();

        Assert.Equal(2, msg.Contents.Count);
        Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.Equal("Here's the weather:", ((TextContent)msg.Contents[0]).Text);
        Assert.IsType<TextContent>(msg.Contents[1]);
        Assert.Equal("It's sunny!", ((TextContent)msg.Contents[1]).Text);
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
    public void ToChatMessage_PreservesDataContent()
    {
        // DataContent (state snapshots, etc.) should be preserved in history
        var vm = new ChatMessageViewModel(ChatRole.User);
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes("""{"recipe":"pasta"}""");
        var data = new DataContent(jsonBytes, "application/json");
        vm.Contents.Add(new TextContent("Improve this recipe"));
        vm.Contents.Add(data);

        var msg = vm.ToChatMessage();

        Assert.Equal(2, msg.Contents.Count);
        Assert.IsType<TextContent>(msg.Contents[0]);
        Assert.IsType<DataContent>(msg.Contents[1]);
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
