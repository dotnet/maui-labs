using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

#pragma warning disable MEAI001 // Image generation and approval content are experimental in the installed SDK.
public sealed class PlaygroundChatClientTests
{
    [Fact]
    public async Task LiveRequest_BuildsOptionsFromTheSettingsForEachRequest()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("One"), ScriptedResponse.Text("Two"));
        using var harness = new PlaygroundChatHarness(model);
        var started = new List<ChatOptions>();
        harness.Router.LiveRequestStarting += (_, options) => started.Add(options);

        harness.Settings.Instructions = "  Be brief.  ";
        await harness.Session.SendMessageAsync("First");
        harness.Settings.Instructions = "Be thorough.";
        harness.Settings.UseCalculatorTool = false;
        harness.Settings.UseImageGenerationTool = false;
        await harness.Session.SendMessageAsync("Second");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Collection(model.Requests,
            first =>
            {
                Assert.Equal("Be brief.", first.Options!.Instructions);
                Assert.Equal(
                    ["get_current_local_datetime", "calculate", "get_connection_status", "image_generation"],
                    first.Options.Tools!.Select(ToolName));
            },
            second =>
            {
                Assert.Equal("Be thorough.", second.Options!.Instructions);
                Assert.Equal(["get_current_local_datetime", "get_connection_status"], second.Options.Tools!.Select(ToolName));
            });
        Assert.Equal(model.Requests.Select(request => request.Options), started);
    }

    [Fact]
    public async Task StreamingOff_AsksForTheWholeResponseAndYieldsItAsUpdates()
    {
        using var model = new ScriptedChatClient(new ScriptedResponse(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "Whole answer") { MessageId = "message" })
        {
            ModelId = "whole-model",
            Usage = new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2 },
        }));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = false;

        await harness.Session.SendMessageAsync("Hello");

        Assert.False(Assert.Single(model.Requests).Streaming);
        var turn = harness.LastTurn;
        Assert.Equal("Whole answer", Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText);
        Assert.Equal("whole-model", turn.ModelId);
        Assert.Equal(2, turn.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task StreamingOn_PassesProviderUpdatesThroughUnchanged()
    {
        var call = new FunctionCallContent("call", "calculate");
        var result = new FunctionResultContent("call", 4);
        var usage = new UsageContent(new UsageDetails { OutputTokenCount = 5 });
        var source = new ChatResponseUpdate(ChatRole.Assistant, [call, new TextContent("First"), result])
        {
            MessageId = "message",
            ModelId = "actual-model",
            ResponseId = "response",
            ConversationId = "conversation",
            AuthorName = "author",
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = ChatFinishReason.Stop,
            RawRepresentation = new object(),
            AdditionalProperties = new() { ["provider"] = "value" },
        };
        var repeated = new ChatResponseUpdate(ChatRole.Assistant, [call, usage, new TextContent("Second"), result])
            { MessageId = "message", ModelId = "actual-model" };
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(source, repeated));
        using var harness = new PlaygroundChatHarness(model);

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in harness.Router.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "Hi")]))
            updates.Add(update);

        // The engine projects the provider's own updates; the router neither copies nor filters them.
        Assert.Equal([source, repeated], updates);
        Assert.Equal(3, source.Contents.Count);
        Assert.Equal(4, repeated.Contents.Count);
        Assert.Same(source.RawRepresentation, updates[0].RawRepresentation);
        Assert.Same(source.AdditionalProperties, updates[0].AdditionalProperties);
    }

    [Fact]
    public async Task Request_StripsProviderMetadataFromTheHistoryItSends()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Done"));
        using var harness = new PlaygroundChatHarness(model);
        var nativeItem = new object();
        var generated = new ImageGenerationToolResultContent("image")
        {
            Outputs = [new DataContent(new byte[] { 1, 2, 3 }, "image/png") { RawRepresentation = nativeItem }],
            RawRepresentation = nativeItem,
        };
        ChatMessage[] history =
        [
            new(ChatRole.User, "Draw a cat") { RawRepresentation = nativeItem },
            new(ChatRole.Assistant, [new TextReasoningContent("Drawing") { ProtectedData = "opaque", RawRepresentation = nativeItem }])
            {
                RawRepresentation = nativeItem,
            },
            new(ChatRole.Tool, [generated]) { RawRepresentation = nativeItem },
            new(ChatRole.User, "Describe it"),
        ];

        await harness.Router.GetResponseAsync(history);

        var sent = Assert.Single(model.Requests).Messages;
        Assert.All(sent, message =>
        {
            Assert.Null(message.RawRepresentation);
            Assert.All(message.Contents, content => Assert.Null(content.RawRepresentation));
        });
        Assert.Null(Assert.Single(Assert.IsType<ImageGenerationToolResultContent>(sent[2].Contents.Single()).Outputs!).RawRepresentation);
        Assert.Equal("opaque", Assert.IsType<TextReasoningContent>(sent[1].Contents.Single()).ProtectedData);
    }

    [Fact]
    public void GetService_AsksTheActiveClientFirst()
    {
        using var model = new ScriptedChatClient();
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);

        Assert.Same(PlaygroundChatHarness.LiveDescriptor, harness.Router.GetService<ChatClientDescriptor>());
        Assert.NotNull(harness.Router.GetService<FunctionInvokingChatClient>());
        Assert.Same(harness.Router, harness.Router.GetService<PlaygroundChatClient>());

        harness.Router.IsReplaying = true;
        Assert.Same(PlaygroundChatHarness.ReplayDescriptor, harness.Router.GetService<ChatClientDescriptor>());
        Assert.Null(harness.Router.GetService<FunctionInvokingChatClient>());
        Assert.Same(harness.Router, harness.Router.GetService<PlaygroundChatClient>());
        Assert.Null(harness.Router.GetService<PlaygroundChatClient>("key"));
    }

    [Fact]
    public async Task ReplayMode_AnswersFromTheRecordingWhileALiveClientIsSelected()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        await harness.LoadAsync("no-tools.json");
        Assert.Same(harness.Live, harness.Settings.SelectedClient);

        harness.Router.IsReplaying = true;
        var response = await harness.Router.GetResponseAsync([new ChatMessage(ChatRole.User, "Anything")]);

        Assert.NotEmpty(response.Text);
        Assert.Empty(model.Requests);
        Assert.Equal(1, harness.Recording.ReplayPosition);
    }

    [Fact]
    public async Task LiveRequest_WithReplaySelected_IsRejectedAsReadOnly()
    {
        using var harness = new PlaygroundChatHarness();
        await harness.LoadAsync("no-tools.json");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Router.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]));

        Assert.Equal("Replay is read-only. Use Play all or Next turn.", error.Message);
        Assert.Equal(0, harness.Recording.ReplayPosition);
    }

    [Fact]
    public async Task LiveRequest_WithoutAClient_ExplainsWhatToDo()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.SelectedClient = null;
        harness.Settings.ClientStatus = "No chat client is configured.";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Router.GetResponseAsync([new ChatMessage(ChatRole.User, "Hello")]));

        Assert.Equal("No chat client is configured.", error.Message);
    }

    [Fact]
    public async Task InvalidSetting_FailsTheTurnWithTheSettingsMessage()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Hello!"));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.EnableTemperature = true;
        harness.Settings.Temperature = "warm";

        await harness.Session.SendMessageAsync("Hello");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        var error = Assert.IsType<ArgumentException>(harness.Session.Error);
        Assert.Equal("Temperature must be a number using '.' as the decimal separator.", error.Message);
        Assert.Empty(model.Requests);

        // Retry sends the same message again with the corrected settings.
        harness.Settings.Temperature = "0.5";
        await harness.Session.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal(0.5f, Assert.Single(model.Requests).Options!.Temperature);
        Assert.Equal("Hello!", Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks)).RawText);
    }

    [Fact]
    public async Task TextOnlyClient_RejectsANewImage()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model, PlaygroundChatHarness.LiveDescriptor with { SupportsImageInput = false });

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => harness.Router.GetResponseAsync(
            [new ChatMessage(ChatRole.User, [new TextContent("What is this?"), new DataContent(new byte[] { 1 }, "image/png")])]));

        Assert.Equal("Live does not support image input. Switch to an image-capable client or remove the image.", error.Message);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public async Task TextOnlyClient_RejectsAChatThatAlreadyHasImages()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model, PlaygroundChatHarness.LiveDescriptor with { SupportsImageInput = false });
        var generated = new ImageGenerationToolResultContent("image") { Outputs = [new DataContent(new byte[] { 1 }, "image/png")] };

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => harness.Router.GetResponseAsync(
        [
            new ChatMessage(ChatRole.User, "Draw a cat"),
            new ChatMessage(ChatRole.Tool, [generated]),
            new ChatMessage(ChatRole.User, "Describe it"),
        ]));

        Assert.Equal(
            "This chat contains images. Live cannot send them; clear the chat or switch to an image-capable client.",
            error.Message);
    }

    [Fact]
    public void EnabledTools_FollowTheSettingsAndTheClient()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(
            model, PlaygroundChatHarness.LiveDescriptor with { SupportsImageGeneration = false });

        Assert.Equal(
            ["get_current_local_datetime", "calculate", "get_connection_status"],
            harness.Router.CreateEnabledTools().Select(ToolName));

        harness.Settings.UseDateTimeTool = false;
        harness.Settings.UseConnectionStatusTool = false;
        Assert.Equal(["calculate"], harness.Router.CreateEnabledTools().Select(ToolName));
    }

    [Fact]
    public void EnabledTools_AreEmptyForAClientWithoutToolCalling()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(
            model, PlaygroundChatHarness.LiveDescriptor with { SupportsToolCalling = false });

        Assert.Empty(harness.Router.CreateEnabledTools());
        Assert.Null(harness.Settings.CreateChatOptions(harness.Router.CreateEnabledTools()).Tools);
    }

    [Fact]
    public void AskBeforeRunningTools_WrapsEachFunctionForApproval()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.RequireToolApproval = true;

        var tools = harness.Router.CreateEnabledTools();

        Assert.Equal(
            ["get_current_local_datetime", "calculate", "get_connection_status", "image_generation"],
            tools.Select(ToolName));
        Assert.All(tools.Take(3), tool => Assert.IsType<ApprovalRequiredAIFunction>(tool));
        Assert.IsType<HostedImageGenerationTool>(tools[3]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApprovedCall_RunsAndTheContinuationKeepsTheRequestOptions(bool streaming)
    {
        using var model = new ScriptedChatClient(
            new ScriptedResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, [CalculatorCall("call-1")]))),
            ScriptedResponse.Text("12 \u00D7 7 = 84."));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        harness.Settings.UseStreaming = streaming;
        harness.Settings.RequireToolApproval = true;
        harness.Settings.Instructions = "Use the calculator.";
        var statuses = new List<ConversationStatus>();
        harness.Session.RegisterOnStatusChanged(status =>
        {
            statuses.Add(status);
            if (status == ConversationStatus.AwaitingInput)
            {
                // Settings changed while the approval waits do not apply to the request being continued.
                harness.Settings.Instructions = "Changed while waiting.";
                harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
            }
        });

        await harness.Session.SendMessageAsync("What is 12 \u00D7 7?");

        Assert.Equal(
            [ConversationStatus.Streaming, ConversationStatus.AwaitingInput, ConversationStatus.Streaming, ConversationStatus.Idle],
            statuses);
        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal("calculate", approval.ToolName);
        Assert.Equal("84", CalculatorDisplay.FormatResult(approval.Result, CultureInfo.InvariantCulture));
        Assert.Equal("12 \u00D7 7 = 84.", harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>().Single().RawText);

        // The model saw the call's result, sent with the same options (and tools) as the request that made the call.
        Assert.Equal(2, model.Requests.Count);
        Assert.Same(model.Requests[0].Options, model.Requests[1].Options);
        Assert.Equal("Use the calculator.", model.Requests[1].Options!.Instructions);
        var sent = model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("call-1", sent.CallId);
        Assert.Equal("84", CalculatorDisplay.FormatResult(sent, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task RejectedCall_DoesNotRunAndTellsTheModel()
    {
        using var model = new ScriptedChatClient(
            new ScriptedResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, [CalculatorCall("call-1")]))),
            ScriptedResponse.Text("Okay, I will not calculate it."));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        harness.Settings.RequireToolApproval = true;
        harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single().Reject("Not now");
        });

        await harness.Session.SendMessageAsync("What is 12 \u00D7 7?");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        var rejection = model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("call-1", rejection.CallId);
        Assert.Contains("Not now", rejection.Result?.ToString());
    }

    private static FunctionCallContent CalculatorCall(string callId) =>
        new(callId, "calculate", new Dictionary<string, object?> { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 });

    private static string ToolName(AITool tool) => tool.Name;
}
#pragma warning restore MEAI001
