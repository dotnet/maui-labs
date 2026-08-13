using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class OptionsMappingTests
{
    private static FakeCopilotSession IdleSession() => new()
    {
        OnSend = (s, _) =>
        {
            s.EmitAll(SdkEvents.Delta("ok", "m1"), SdkEvents.Idle());
            return Task.CompletedTask;
        },
    };

    private static async Task<RecordedSessionCall> RunAndCaptureAsync(
        CopilotSdkConfiguration configuration,
        ChatOptions? options,
        params ChatMessage[] messages)
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(IdleSession());
        await using var client = TestChatClient.Create(backend, configuration);
        await client.GetResponseAsync(messages.Length == 0 ? TestExtensions.UserMessage("hi") : [.. messages], options);
        return backend.Calls[0];
    }

    [Fact]
    public async Task System_instructions_combine_config_options_and_system_messages_in_order()
    {
        var call = await RunAndCaptureAsync(
            new CopilotSdkConfiguration { SystemInstructions = "From config." },
            new ChatOptions { Instructions = "From options." },
            new ChatMessage(ChatRole.System, "From system message."),
            new ChatMessage(ChatRole.User, "hi"));

        Assert.Equal("From config.\n\nFrom options.\n\nFrom system message.", call.Parameters.SystemInstructions);
    }

    [Fact]
    public async Task Reasoning_effort_prefers_options_reasoning()
    {
        var call = await RunAndCaptureAsync(
            new CopilotSdkConfiguration { ReasoningEffort = "low" },
            new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh } });

        Assert.Equal("xhigh", call.Parameters.ReasoningEffort);
    }

    [Fact]
    public async Task Reasoning_effort_falls_back_to_additional_property_then_config()
    {
        var viaProperty = await RunAndCaptureAsync(
            new CopilotSdkConfiguration { ReasoningEffort = "low" },
            new ChatOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["ReasoningEffort"] = "max" } });
        Assert.Equal("max", viaProperty.Parameters.ReasoningEffort);

        var viaConfig = await RunAndCaptureAsync(new CopilotSdkConfiguration { ReasoningEffort = "medium" }, options: null);
        Assert.Equal("medium", viaConfig.Parameters.ReasoningEffort);
    }

    [Fact]
    public async Task Json_response_format_adds_a_json_instruction()
    {
        var call = await RunAndCaptureAsync(
            new CopilotSdkConfiguration(),
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json });

        Assert.Contains("JSON", call.Parameters.SystemInstructions);
    }

    [Fact]
    public async Task Model_id_from_options_overrides_configuration()
    {
        var call = await RunAndCaptureAsync(
            new CopilotSdkConfiguration { Model = "config-model" },
            new ChatOptions { ModelId = "options-model" });

        Assert.Equal("options-model", call.Parameters.Model);
    }

    [Fact]
    public async Task Unsupported_options_are_ignored_without_error()
    {
        var options = new ChatOptions
        {
            Temperature = 0.9f,
            MaxOutputTokens = 100,
            TopP = 0.5f,
            TopK = 40,
            FrequencyPenalty = 1.0f,
            PresencePenalty = 1.0f,
            Seed = 42,
            StopSequences = ["STOP"],
        };

        var call = await RunAndCaptureAsync(new CopilotSdkConfiguration(), options);

        // No mapping and no crash: the request completed and produced a session.
        Assert.Equal(RecordedSessionCallKind.Create, call.Kind);
    }

    [Fact]
    public async Task Image_attachment_bytes_are_mapped_to_a_blob()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(IdleSession());
        await using var client = TestChatClient.Create(backend);

        var bytes = new byte[] { 1, 2, 3, 4 };
        var message = new ChatMessage(ChatRole.User, [new TextContent("look"), new DataContent(bytes, "image/png")]);
        await client.GetResponseAsync([message]);

        var attachment = Assert.Single(session.SentMessages[0].Attachments!);
        var blob = Assert.IsType<AttachmentBlob>(attachment);
        Assert.Equal("image/png", blob.MimeType);
        Assert.Equal(Convert.ToBase64String(bytes), blob.Data);
    }

    [Fact]
    public async Task Image_attachment_data_uri_is_mapped_to_a_blob()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(IdleSession());
        await using var client = TestChatClient.Create(backend);

        var base64 = Convert.ToBase64String(new byte[] { 9, 8, 7 });
        var uri = new Uri($"data:image/jpeg;base64,{base64}");
        var message = new ChatMessage(ChatRole.User, [new UriContent(uri, "image/jpeg")]);
        await client.GetResponseAsync([message]);

        var attachment = Assert.Single(session.SentMessages[0].Attachments!);
        var blob = Assert.IsType<AttachmentBlob>(attachment);
        Assert.Equal("image/jpeg", blob.MimeType);
        Assert.Equal(base64, blob.Data);
    }
}
