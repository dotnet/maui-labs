using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class CopilotChatMapperTests
{
    [Fact]
    public void BuildSystemInstructions_orders_config_options_and_system_messages()
    {
        var config = new CopilotSdkConfiguration { SystemInstructions = "cfg" };
        var options = new ChatOptions { Instructions = "opt" };
        List<ChatMessage> messages =
        [
            new ChatMessage(new ChatRole("developer"), "dev"),
            new ChatMessage(ChatRole.System, "sys"),
            new ChatMessage(ChatRole.User, "hi"),
        ];

        var result = CopilotChatMapper.BuildSystemInstructions(config, options, messages);

        Assert.Equal("cfg\n\nopt\n\ndev\n\nsys", result);
    }

    [Fact]
    public void BuildSystemInstructions_is_null_when_nothing_to_say()
    {
        var result = CopilotChatMapper.BuildSystemInstructions(
            new CopilotSdkConfiguration(), options: null, [new ChatMessage(ChatRole.User, "hi")]);

        Assert.Null(result);
    }

    [Fact]
    public void BuildSystemInstructions_includes_json_schema_when_present()
    {
        var format = ChatResponseFormat.ForJsonSchema(
            System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" }));
        var options = new ChatOptions { ResponseFormat = format };

        var result = CopilotChatMapper.BuildSystemInstructions(new CopilotSdkConfiguration(), options, []);

        Assert.Contains("JSON schema", result);
        Assert.Contains("\"type\"", result!);
    }

    [Theory]
    [InlineData(ReasoningEffort.Low, "low")]
    [InlineData(ReasoningEffort.Medium, "medium")]
    [InlineData(ReasoningEffort.High, "high")]
    [InlineData(ReasoningEffort.ExtraHigh, "xhigh")]
    public void ResolveReasoningEffort_maps_options_reasoning(ReasoningEffort effort, string expected)
    {
        var options = new ChatOptions { Reasoning = new ReasoningOptions { Effort = effort } };
        Assert.Equal(expected, CopilotChatMapper.ResolveReasoningEffort(new CopilotSdkConfiguration(), options));
    }

    [Fact]
    public void ResolveReasoningEffort_is_null_when_unset()
    {
        Assert.Null(CopilotChatMapper.ResolveReasoningEffort(new CopilotSdkConfiguration(), options: null));
    }

    [Fact]
    public void BuildTools_produces_pending_proxy_and_custom_allowlist()
    {
        var tool = AIFunctionFactory.Create((string a) => a, "echo");
        var options = new ChatOptions { Tools = [tool] };

        var mapping = CopilotChatMapper.BuildTools(
            options,
            new PendingToolCoordinator());

        var declaration = Assert.Single(mapping.Declarations);
        Assert.IsType<PendingToolAIFunction>(declaration);
        Assert.NotSame(tool, declaration);
        Assert.Equal("echo", declaration.Name);
        Assert.Equal(["custom:echo"], mapping.AvailableTools);
        Assert.Empty(mapping.ExcludedTools);
        Assert.Contains("echo", mapping.AllowedToolNames);
    }

    [Fact]
    public void BuildTools_excludes_builtins_when_no_tools()
    {
        var mapping = CopilotChatMapper.BuildTools(
            new ChatOptions(),
            new PendingToolCoordinator());

        Assert.Empty(mapping.Declarations);
        Assert.Empty(mapping.AvailableTools);
        Assert.Equal(["builtin:*"], mapping.ExcludedTools);
    }

    [Fact]
    public void BuildInitialPrompt_frames_history_and_current_message()
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, "ignored"),
            new ChatMessage(ChatRole.User, "one"),
            new ChatMessage(ChatRole.Assistant, "two"),
            new ChatMessage(ChatRole.User, "three"),
        ];

        var prompt = CopilotChatMapper.BuildInitialPrompt(messages);

        Assert.Contains("User: one", prompt);
        Assert.Contains("Assistant: two", prompt);
        Assert.Contains("Current message:", prompt);
        Assert.EndsWith("three", prompt);
        Assert.DoesNotContain("ignored", prompt);
    }

    [Fact]
    public void BuildInitialPrompt_returns_bare_text_for_single_message()
    {
        var prompt = CopilotChatMapper.BuildInitialPrompt([new ChatMessage(ChatRole.User, "solo")]);
        Assert.Equal("solo", prompt);
    }

    [Fact]
    public void BuildFollowUpPrompt_returns_last_user_text()
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, "old"),
            new ChatMessage(ChatRole.Assistant, "reply"),
            new ChatMessage(ChatRole.User, "new"),
        ];

        Assert.Equal("new", CopilotChatMapper.BuildFollowUpPrompt(messages));
    }

    [Fact]
    public void IsToolContinuation_only_considers_the_final_request_message()
    {
        List<ChatMessage> withResults = [new ChatMessage(ChatRole.Tool, [new FunctionResultContent("c1", "r")])];
        List<ChatMessage> withoutResults = [new ChatMessage(ChatRole.User, "hi")];
        List<ChatMessage> historicalResult =
        [
            new ChatMessage(
                ChatRole.Tool,
                [new FunctionResultContent("c1", "r")]),
            new ChatMessage(ChatRole.User, "next"),
        ];

        Assert.True(CopilotChatMapper.IsToolContinuation(withResults));
        Assert.False(CopilotChatMapper.IsToolContinuation(withoutResults));
        Assert.False(CopilotChatMapper.IsToolContinuation(historicalResult));
        Assert.Equal("c1", Assert.Single(CopilotChatMapper.GetToolResults(withResults)).CallId);
    }

    [Fact]
    public void BuildAttachments_maps_image_bytes()
    {
        var bytes = new byte[] { 1, 2, 3 };
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, [new DataContent(bytes, "image/png")]),
        ];

        var attachment = Assert.Single(CopilotChatMapper.BuildAttachments(messages));
        var blob = Assert.IsType<AttachmentBlob>(attachment);
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal(Convert.ToBase64String(bytes), blob.Data);
    }

    [Fact]
    public void BuildAttachments_ignores_non_image_content()
    {
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.User, [new TextContent("hi"), new DataContent(new byte[] { 1 }, "application/pdf")]),
        ];

        Assert.Empty(CopilotChatMapper.BuildAttachments(messages));
    }
}
