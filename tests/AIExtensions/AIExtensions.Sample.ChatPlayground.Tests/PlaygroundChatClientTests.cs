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
                    ["get_current_local_datetime", "calculate", "get_connection_status", "image_generation", "propose_notes"],
                    first.Options.Tools!.Select(ToolName));
            },
            second =>
            {
                Assert.Equal("Be thorough.", second.Options!.Instructions);
                Assert.Equal(
                    ["get_current_local_datetime", "get_connection_status", "propose_notes"],
                    second.Options.Tools!.Select(ToolName));
            });
    }

    [Fact]
    public void Instructions_StartWithWhatThePlaygroundCanDo()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);

        Assert.Equal(harness.Settings.GetDefaultInstructions(), harness.Settings.Instructions);
        Assert.StartsWith(SettingsPaneViewModel.DefaultInstructions, harness.Settings.Instructions, StringComparison.Ordinal);
        Assert.Contains("set_theme", harness.Settings.Instructions, StringComparison.Ordinal);
        Assert.Contains("propose_notes", harness.Settings.Instructions, StringComparison.Ordinal);
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
        Assert.Null(harness.Router.GetService<PlaygroundChatClient>("key"));

        harness.Settings.SelectedClient = null;
        Assert.Null(harness.Router.GetService<ChatClientDescriptor>());
        Assert.Null(harness.Router.GetService<FunctionInvokingChatClient>());
        Assert.Same(harness.Router, harness.Router.GetService<PlaygroundChatClient>());
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
        Assert.Empty(harness.Router.CreateUIActions([AIFunctionFactory.Create(() => "ok", "do_something").AsDeclarationOnly()]));
        Assert.Null(harness.Settings.CreateChatOptions(harness.Router.CreateEnabledTools()).Tools);
    }

    [Fact]
    public async Task UIActions_AreLeftOutForAClientThatRunsToolsItself()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Hi"));
        using var harness = new PlaygroundChatHarness(
            model, PlaygroundChatHarness.LiveDescriptor with { SupportsImageGeneration = false, SupportsUIActions = false });

        await harness.Session.SendMessageAsync("Hello");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal(
            ["get_current_local_datetime", "calculate", "get_connection_status"],
            Assert.Single(model.Requests).Options!.Tools!.Select(ToolName));
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
    public async Task ApprovedCall_RunsAndTheContinuationHasTheSameOptions(bool streaming)
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
        harness.Session.OnStatusChanged(status =>
        {
            statuses.Add(status);
            if (status == ConversationStatus.AwaitingInput)
                harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
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

        // The approval card shows the call; the worksheet doesn't show it again once it runs.
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is CalculatorWorksheetBlock);

        // The model saw the call's result, sent with the same instructions and tools as the request that made the call.
        Assert.Equal(2, model.Requests.Count);
        Assert.Equal("Use the calculator.", model.Requests[1].Options!.Instructions);
        Assert.Equal(model.Requests[0].Options!.Tools!.Select(ToolName), model.Requests[1].Options!.Tools!.Select(ToolName));
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
        harness.Session.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single().Reject("Not now");
        });

        await harness.Session.SendMessageAsync("What is 12 \u00D7 7?");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, block => block is CalculatorWorksheetBlock);
        Assert.Equal("Okay, I will not calculate it.", harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>().Single().RawText);
        var rejection = model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single();
        Assert.Equal("call-1", rejection.CallId);
        Assert.Contains("Not now", rejection.Result?.ToString());
    }

    [Fact]
    public async Task EngineOptions_KeepTheProviderConversationAndAddTheUIActionsToTheSettings()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Done"));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.Instructions = "Be brief.";
        var action = AIFunctionFactory.Create(() => "ok", "do_something").AsDeclarationOnly();

        await harness.Router.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Hi")],
            new ChatOptions { ConversationId = "response-1", Instructions = "Ignored", Tools = [action] });

        // Within a turn the engine sends only the new messages and continues the provider's conversation, so the
        // conversation must reach the provider; everything else comes from the settings.
        var options = Assert.Single(model.Requests).Options!;
        Assert.Equal("response-1", options.ConversationId);
        Assert.Equal("Be brief.", options.Instructions);
        Assert.Equal(
            ["get_current_local_datetime", "calculate", "get_connection_status", "image_generation", "do_something"],
            options.Tools!.Select(ToolName));
        Assert.Same(action, options.Tools![4]);
    }

    [Fact]
    public async Task RequireAny_AppliesToTheFirstRequestOfATurnOnly()
    {
        // set_theme runs in the app, so its result goes to the model in a request that follows. Requiring a tool there
        // too would make the model call set_theme again, and again, until the turn ran out of rounds.
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("call-1", ThemeAction.ToolName, new Dictionary<string, object?> { ["theme"] = "dark" })])
            {
                MessageId = "call",
            }),
            ScriptedResponse.Text("It's dark now."));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        harness.Settings.ToolMode = ChatToolMode.RequireAny;

        await harness.Session.SendMessageAsync("Switch to dark mode");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal([AppTheme.Dark], harness.AppliedThemes);
        Assert.Collection(model.Requests,
            first => Assert.Same(ChatToolMode.RequireAny, first.Options!.ToolMode),
            followUp =>
            {
                Assert.Contains(followUp.Messages[^1].Contents, content => content is FunctionResultContent { CallId: "call-1" });
                Assert.Null(followUp.Options!.ToolMode);
                Assert.Contains(followUp.Options.Tools!, tool => tool.Name == ThemeAction.ToolName);
            });
        Assert.Same(ChatToolMode.RequireAny, harness.Settings.ToolMode);
    }

    [Fact]
    public void ApprovalRequiredUIAction_AsksOnlyWhileAskBeforeRunningToolsIsOn()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        var action = ThemeAction.Create(_ => { }).AsDeclarationOnly();

        var plain = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            Assert.Single(harness.Router.CreateUIActions([action])));
        Assert.Null(plain.GetService<ApprovalRequiredAIFunction>());
        Assert.IsNotAssignableFrom<AIFunction>(plain);
        Assert.Equal(action.Description, plain.Description);
        Assert.Equal(action.JsonSchema.GetRawText(), plain.JsonSchema.GetRawText());

        harness.Settings.RequireToolApproval = true;
        var asking = Assert.Single(harness.Router.CreateUIActions([action]));
        Assert.Same(action, asking);
        Assert.NotNull(asking.GetService<ApprovalRequiredAIFunction>());
    }

    private static FunctionCallContent CalculatorCall(string callId) =>
        new(callId, "calculate", new Dictionary<string, object?> { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 });

    private static string ToolName(AITool tool) => tool.Name;
}
#pragma warning restore MEAI001
