using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// A live turn as the chat tab shows it: the router sends the request with the settings, and the engine turns the
/// response into the blocks that the chat view renders.
/// </summary>
#pragma warning disable MEAI001 // Image generation content is experimental in the installed SDK.
public sealed class PlaygroundChatTurnTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CalculationAndReasoning_ShowReasoningThenTheWorksheetWithItsResultThenTheAnswer(bool streaming)
    {
        var call = new FunctionCallContent("call-1", "calculate",
            new Dictionary<string, object?> { ["left"] = 2, ["operation"] = "add", ["right"] = 2 });
        var result = new FunctionResultContent("call-1", 4);
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Calculating"), call]),
                new ChatMessage(ChatRole.Tool, [result]),
                new ChatMessage(ChatRole.Assistant, "Answer: 4"),
            ]),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Calculating"), call]),
                new ChatResponseUpdate(ChatRole.Assistant, [call]),
                new ChatResponseUpdate(ChatRole.Tool, [result]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Answer: ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("4")]),
            ]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;
        harness.Settings.Instructions = "Answer briefly";

        await harness.Session.SendMessageAsync("What is 2+2?");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        var request = Assert.Single(model.Requests);
        Assert.Equal(streaming, request.Streaming);
        Assert.Equal("What is 2+2?", Assert.Single(request.Messages).Text);
        Assert.Equal("Answer briefly", request.Options!.Instructions);

        var turn = Assert.Single(harness.Session.Turns);
        Assert.Equal("What is 2+2?", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
        Assert.Equal(3, turn.ResponseBlocks.Count);
        Assert.Equal("Calculating", Assert.Single(turn.ResponseBlocks.OfType<ReasoningContentBlock>()).Text);

        // The repeated call is one row of the worksheet, with its result, and the answer comes last. The order of the
        // reasoning and the call, which arrive together, is covered by BlocksFromOneMessage_FollowTheContentOrder.
        var worksheet = Assert.Single(turn.ResponseBlocks.OfType<CalculatorWorksheetBlock>());
        var row = Assert.Single(worksheet.Rows);
        Assert.Equal("2 + 2", CalculatorDisplay.FormatExpression(row.Call.Arguments, CultureInfo.InvariantCulture));
        Assert.Equal("4", CalculatorDisplay.FormatResult(row.Result, CultureInfo.InvariantCulture));
        Assert.DoesNotContain(turn.ResponseBlocks, block => block is FunctionInvocationContentBlock);
        Assert.Equal("Answer: 4", Assert.IsType<TextContentBlock>(turn.ResponseBlocks[^1]).RawText);
        Assert.All(turn.ResponseBlocks, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StructuredJson_IsRequestedAndShownByTheStructuredTemplateFromTheFirstChunk(bool streaming)
    {
        const string json = """{"summary":"Done","keyPoints":["one"],"category":"test","sentiment":"neutral"}""";
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, json)),
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(json[..20])]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(json[20..])]),
            ]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;
        harness.Settings.UseStructuredJson = true;
        var structured = new StructuredResponseMatch();
        var selectedWhenAdded = new List<bool>();
        harness.Session.OnBlockAdded((_, block) => selectedWhenAdded.Add(structured.Matches(block)));

        await harness.Session.SendMessageAsync("Respond with JSON");

        Assert.NotNull(Assert.Single(model.Requests).Options!.ResponseFormat);
        var block = Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks));
        Assert.Equal(json, block.RawText);
        Assert.True(structured.Matches(block));

        // The user's message is not selected; the answer is, as soon as it is added.
        Assert.Equal([false, true], selectedWhenAdded);
        Assert.True(StructuredResponseFormatter.TryReadResponse(block.RawText, out var response));
        Assert.Equal("Done", response.Summary);
        Assert.Equal(["one"], response.KeyPoints);
        Assert.Equal("test", response.Category);
        Assert.Equal(PlaygroundSentiment.Neutral, response.Sentiment);
        Assert.Contains('\n', StructuredResponseFormatter.TryFormat(block.RawText)!);
    }

    [Fact]
    public async Task ImageInAndOut_ShowBothImagesAndKeepThemInTheHistory()
    {
        var image = File.ReadAllBytes(PlaygroundChatHarness.FixturePath("playground_sample.png"));
        var generated = new ImageGenerationToolResultContent("image-1") { Outputs = [new DataContent(image, "image/png")] };
        using var model = new ScriptedChatClient(
            new ScriptedResponse(new ChatResponse(
            [
                new ChatMessage(ChatRole.Tool, [generated]),
                new ChatMessage(ChatRole.Assistant, "Created."),
            ])),
            ScriptedResponse.Text("A robot."));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = false;

        await harness.Session.SendMessageAsync(
            new ChatMessage(ChatRole.User, [new TextContent("Edit this"), new DataContent(image, "image/png")]));
        await harness.Session.SendMessageAsync("Describe it");

        var turn = harness.Session.Turns[0];
        Assert.Equal(2, turn.RequestBlocks.Count);
        Assert.Equal("Edit this", Assert.Single(turn.RequestBlocks.OfType<TextContentBlock>()).RawText);
        Assert.Equal(image, ImageBytes(Assert.Single(turn.RequestBlocks.OfType<MediaContentBlock>())));
        Assert.Collection(turn.ResponseBlocks,
            block => Assert.Equal(image, ImageBytes(block)),
            block => Assert.Equal("Created.", Assert.IsType<TextContentBlock>(block).RawText));

        // The attachment reached the model, and both images stay in the history the next request sends.
        Assert.Contains(model.Requests[0].Messages.SelectMany(message => message.Contents).OfType<DataContent>(),
            data => data.Data.ToArray().SequenceEqual(image));
        var history = model.Requests[1].Messages.SelectMany(message => message.Contents).ToArray();
        Assert.Contains(history.OfType<DataContent>(), data => data.Data.ToArray().SequenceEqual(image));
        Assert.Contains(history.OfType<ImageGenerationToolResultContent>(),
            result => result.Outputs!.OfType<DataContent>().Any(data => data.Data.ToArray().SequenceEqual(image)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedNativeReasoning_DoesNotReachTheNextRequest(bool streaming)
    {
        var nativeItem = new object();
        var firstReasoning = new TextReasoningContent("Preparing the image")
        {
            ProtectedData = "opaque reasoning",
            RawRepresentation = nativeItem,
        };
        var repeatedReasoning = new TextReasoningContent("Image prepared")
        {
            ProtectedData = "opaque reasoning",
            RawRepresentation = nativeItem,
        };
        var finalText = new TextContent("Done.") { RawRepresentation = new object() };
        using var model = new ScriptedChatClient(
            new ScriptedResponse(
                new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant, [firstReasoning]) { RawRepresentation = nativeItem },
                    new ChatMessage(ChatRole.Assistant, [repeatedReasoning, finalText]) { RawRepresentation = nativeItem },
                ]),
                [
                    new ChatResponseUpdate(ChatRole.Assistant, [firstReasoning]) { RawRepresentation = nativeItem },
                    new ChatResponseUpdate(ChatRole.Assistant, [repeatedReasoning, finalText]) { RawRepresentation = nativeItem },
                ]),
            ScriptedResponse.Text("It is a robot."));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync("Create an image");
        harness.Settings.UseStreaming = false;
        await harness.Session.SendMessageAsync("Describe it");

        var history = model.Requests[1].Messages;
        Assert.Equal(2, history.SelectMany(message => message.Contents)
            .OfType<TextReasoningContent>().Count(content => content.ProtectedData == "opaque reasoning"));
        Assert.All(history, message =>
        {
            Assert.Null(message.RawRepresentation);
            Assert.All(message.Contents, content => Assert.Null(content.RawRepresentation));
        });
    }

    [Fact]
    public async Task InterruptedStream_FailsTheTurnAndStopsItsPartialAnswer()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Partial")) with
            {
                Failure = new InvalidOperationException("The stream failed."),
            });
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Say hello");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.Equal("The stream failed.", harness.Session.Error?.Message);
        var partial = Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks));
        Assert.Equal("Partial", partial.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, partial.LifecycleState);
    }

    [Fact]
    public async Task Stop_CancelsTheProviderStreamAndKeepsThePartialAnswer()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Partial")) with { WaitForCancellation = true });
        using var harness = new PlaygroundChatHarness(model);

        var send = harness.Session.SendMessageAsync("Count");
        await model.StreamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Session.CancelAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await send.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(model.StreamCancelled.Task.IsCompletedSuccessfully);
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.True(harness.LastTurn.IsStopped);
        var partial = Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks));
        Assert.Equal("Partial", partial.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, partial.LifecycleState);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ModelIdAndUsage_DescribeTheResponseInItsFooter(bool streaming, bool structuredJson)
    {
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var contents = new AIContent[]
        {
            new TextContent(structuredJson ? """{"answer":4}""" : "Answer: 4"),
            new TextReasoningContent("Checked the time"),
            new FunctionCallContent("call", "get_current_local_datetime"),
            new FunctionResultContent("call", "10:00"),
            image,
            new ImageGenerationToolResultContent("image") { Outputs = [image] },
        };
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)])
            {
                ModelId = "actual-model",
                Usage = new UsageDetails { InputTokenCount = 1_024, OutputTokenCount = 5 },
            },
            [
                new ChatResponseUpdate(ChatRole.Assistant, contents) { ModelId = "actual-model" },
                new ChatResponseUpdate
                {
                    Contents = [new UsageContent(new UsageDetails { InputTokenCount = 1_024, OutputTokenCount = 5 })],
                },
            ]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;
        harness.Settings.UseStructuredJson = structuredJson;

        await harness.Session.SendMessageAsync("Question");

        var turn = harness.LastTurn;
        Assert.Equal("actual-model", turn.ModelId);
        Assert.Equal("actual-model \u00B7 1,024 in \u00B7 5 out",
            TurnFooterConverter.Format(turn.ModelId, turn.Usage, CultureInfo.InvariantCulture));
        Assert.Equal("Question", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);

        // Every provider output is part of the response: the text, the reasoning, the call with its result, and both
        // images.
        var text = Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>());
        Assert.Equal(structuredJson, new StructuredResponseMatch().Matches(text));
        Assert.Equal("Checked the time", Assert.Single(turn.ResponseBlocks.OfType<ReasoningContentBlock>()).Text);
        Assert.True(Assert.Single(turn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).HasResult);
        Assert.Equal(2, turn.ResponseBlocks.OfType<MediaContentBlock>().Count());
    }

    [Fact]
    public async Task OrphanToolResult_IsNotShownWithoutItsCall()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            new ChatResponseUpdate(ChatRole.Tool,
                [new FunctionResultContent("unknown", null) { Exception = new InvalidOperationException("Tool failed") }]),
            new ChatResponseUpdate(ChatRole.Assistant, "Answer")));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal("Answer", Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks)).RawText);
    }

    [Fact]
    public async Task ModelId_BelongsToItsOwnTurn()
    {
        using var model = new ScriptedChatClient(
            new ScriptedResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "First")) { ModelId = "first-model" }),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Second") { ModelId = "second-model" }),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Third")));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = false;
        await harness.Session.SendMessageAsync("One");
        harness.Settings.UseStreaming = true;
        await harness.Session.SendMessageAsync("Two");
        await harness.Session.SendMessageAsync("Three");

        Assert.Equal(["first-model", "second-model", null], harness.Session.Turns.Select(turn => turn.ModelId));
        Assert.Equal(string.Empty, TurnFooterConverter.Format(harness.LastTurn.ModelId, harness.LastTurn.Usage));
    }

    [Fact]
    public async Task MetadataOnlyUpdate_DoesNotAddABlock()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            new ChatResponseUpdate { ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Answer")]) { ModelId = "actual-model" }));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal("Answer", Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks)).RawText);
        Assert.Equal("actual-model", harness.LastTurn.ModelId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdjacentAssistantMessages_StaySeparateInTheTurnAndTheHistory(bool streaming)
    {
        using var model = new ScriptedChatClient(
            new ScriptedResponse(
                new ChatResponse(
                [
                    new ChatMessage(ChatRole.Assistant, "First") { MessageId = "first" },
                    new ChatMessage(ChatRole.Assistant, "Second") { MessageId = "second" },
                ]) { ModelId = "actual-model" },
                [
                    new ChatResponseUpdate(ChatRole.Assistant, "First") { MessageId = "first", ModelId = "actual-model" },
                    new ChatResponseUpdate(ChatRole.Assistant, "Second") { MessageId = "second", ModelId = "actual-model" },
                ]),
            ScriptedResponse.Text("Next"));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync("Question");
        await harness.Session.SendMessageAsync("Again");

        var blocks = harness.Session.Turns[0].ResponseBlocks.Cast<TextContentBlock>().ToArray();
        Assert.Equal(["First", "Second"], blocks.Select(block => block.RawText));
        Assert.All(blocks, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
        var history = model.Requests[1].Messages.Where(message => message.Role == ChatRole.Assistant).ToArray();
        Assert.Equal(["First", "Second"], history.Select(message => message.Text));
        Assert.Equal(["first", "second"], history.Select(message => message.MessageId));
    }

    [Fact]
    public async Task SameMessageIdFragments_CombineIntoOneBlockAndOneHistoryMessage()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(
                new ChatResponseUpdate(ChatRole.Assistant, "First ") { MessageId = "message", ModelId = "actual-model" },
                new ChatResponseUpdate { MessageId = "message", ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, "second") { MessageId = "message", ModelId = "actual-model" }),
            ScriptedResponse.Text("Next"));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");
        await harness.Session.SendMessageAsync("Again");

        Assert.Equal("First second",
            Assert.IsType<TextContentBlock>(Assert.Single(harness.Session.Turns[0].ResponseBlocks)).RawText);
        Assert.Equal("actual-model", harness.Session.Turns[0].ModelId);
        Assert.Equal("First second", string.Concat(model.Requests[1].Messages
            .Where(message => message.Role == ChatRole.Assistant).Select(message => message.Text)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentMessageIds_KeepReasoningSummariesSeparate(bool streaming)
    {
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse(
            [
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("First thought")]) { MessageId = "first" },
                new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Second thought")]) { MessageId = "second" },
            ]) { ModelId = "actual-model" },
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("First ")])
                    { MessageId = "first", ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thought")])
                    { MessageId = "first", ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("Second thought")])
                    { MessageId = "second", ModelId = "actual-model" },
                new ChatResponseUpdate { MessageId = "third" },
            ]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync("Question");

        var reasoning = harness.LastTurn.ResponseBlocks.Cast<ReasoningContentBlock>().ToArray();
        Assert.Equal(["First thought", "Second thought"], reasoning.Select(block => block.Text));
        Assert.All(reasoning, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
    }

    [Fact]
    public async Task ProtectedReasoningCompletion_SeparatesSummariesWithoutChangingTheirText()
    {
        const string first = "**First heading**\n\nFirst body";
        const string second = "**Second heading**\n\nSecond body";
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(first)]) { ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new TextReasoningContent("") { ProtectedData = "completed-first-item" }]) { ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(second)]) { ModelId = "actual-model" },
                new ChatResponseUpdate(ChatRole.Assistant,
                    [new TextReasoningContent("") { ProtectedData = "completed-second-item" }]) { ModelId = "actual-model" }),
            ScriptedResponse.Text("Next"));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");
        await harness.Session.SendMessageAsync("Again");

        Assert.Equal([first, second],
            harness.Session.Turns[0].ResponseBlocks.Cast<ReasoningContentBlock>().Select(block => block.Text));
        Assert.Equal(["completed-first-item", "completed-second-item"], model.Requests[1].Messages
            .SelectMany(message => message.Contents).OfType<TextReasoningContent>()
            .Where(content => content.ProtectedData is not null).Select(content => content.ProtectedData));
    }

    [Fact]
    public async Task TextBeforeAToolWithoutFinalText_ShowsTheTextAndTheToolAndCompletes()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            new ChatResponseUpdate(ChatRole.Assistant,
                [new TextContent("Checking"), new FunctionCallContent("call", "get_current_local_datetime")]) { ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call", "10:00")]) { ModelId = "actual-model" }));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal(2, harness.LastTurn.ResponseBlocks.Count);
        Assert.Equal("Checking", Assert.Single(harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
        Assert.True(Assert.Single(harness.LastTurn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).HasResult);
        Assert.All(harness.LastTurn.ResponseBlocks, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
        Assert.Equal("actual-model", harness.LastTurn.ModelId);
    }

    [Fact]
    public async Task TrailingWhitespaceMessage_CompletesEveryBlock()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            new ChatResponseUpdate(ChatRole.Assistant, "Answer") { MessageId = "first", ModelId = "actual-model" },
            new ChatResponseUpdate(ChatRole.Assistant, " ") { MessageId = "second", ModelId = "actual-model" }));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal(["Answer", " "], harness.LastTurn.ResponseBlocks.Cast<TextContentBlock>().Select(block => block.RawText));
        Assert.All(harness.LastTurn.ResponseBlocks, block => Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState));
    }

    [Fact]
    public async Task CompleteResponseWithRepeatedToolContent_ShowsOneCallWithItsResult()
    {
        var call = new FunctionCallContent("call", "get_current_local_datetime");
        var result = new FunctionResultContent("call", "10:00");
        using var model = new ScriptedChatClient(new ScriptedResponse(new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new TextContent("Before"), call, call]),
            new ChatMessage(ChatRole.Tool, [result, result]),
            new ChatMessage(ChatRole.Assistant, [call, new TextContent("After")]),
        ]) { ModelId = "actual-model" }));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = false;

        await harness.Session.SendMessageAsync("Question");

        Assert.True(Assert.Single(harness.LastTurn.ResponseBlocks.OfType<FunctionInvocationContentBlock>()).HasResult);
        Assert.Equal(["Before", "After"],
            harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>().Select(block => block.RawText));
    }

    [Fact]
    public async Task ReasoningWhitespaceFragments_ArePreservedExactly()
    {
        string[] fragments = ["**Heading**", "", "\n\n", "Text here", "  ", "\t", "\n\n", "**Heading again**", "\n\n", "More text"];
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            [.. fragments.Select(fragment => new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(fragment)])
                { ModelId = "actual-model", MessageId = "reasoning" })]));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal(string.Concat(fragments),
            Assert.IsType<ReasoningContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks)).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextWhitespaceFragments_ArePreservedExactly(bool streaming)
    {
        string[] fragments = ["First", "", "  ", "second", "\n\n", "\t", "Third"];
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(fragments))) { ModelId = "actual-model" },
            [.. fragments.Select(fragment => new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(fragment)])
                { ModelId = "actual-model", MessageId = "text" })]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync("Question");

        Assert.Equal(string.Concat(fragments),
            Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks)).RawText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhitespaceOnlyText_IsStillContent(bool streaming)
    {
        const string whitespace = "  \n\t";
        using var model = new ScriptedChatClient(new ScriptedResponse(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, whitespace)) { ModelId = "actual-model" },
            [new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(whitespace)]) { ModelId = "actual-model" }]));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync("Question");

        var block = Assert.IsType<TextContentBlock>(Assert.Single(harness.LastTurn.ResponseBlocks));
        Assert.Equal(whitespace, block.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlocksFromOneMessage_FollowTheContentOrder(bool streaming)
    {
        // A complete response delivers each message as one update; so can a provider that streams whole items.
        var call = new FunctionCallContent("call-1", "calculate",
            new Dictionary<string, object?> { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 });
        var response = new ChatResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("Planning"), new TextContent("Let me calculate that."), call]),
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-1", 84)]),
            new ChatMessage(ChatRole.Assistant, "It is 84."),
        ]);
        using var model = new ScriptedChatClient(new ScriptedResponse(response));
        using var harness = new PlaygroundChatHarness(model);
        harness.Settings.UseStreaming = streaming;

        await harness.Session.SendMessageAsync(new ChatMessage(ChatRole.User,
            [new TextContent("What is 12 \u00D7 7?"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")]));

        Assert.Collection(harness.LastTurn.RequestBlocks,
            block => Assert.IsType<TextContentBlock>(block),
            block => Assert.IsType<MediaContentBlock>(block));
        Assert.Collection(harness.LastTurn.ResponseBlocks,
            block => Assert.Equal("Planning", Assert.IsType<ReasoningContentBlock>(block).Text),
            block => Assert.Equal("Let me calculate that.", Assert.IsType<TextContentBlock>(block).RawText),
            block => Assert.True(Assert.Single(Assert.IsType<CalculatorWorksheetBlock>(block).Rows).HasResult),
            block => Assert.Equal("It is 84.", Assert.IsType<TextContentBlock>(block).RawText));
    }

    private static byte[] ImageBytes(ContentBlock block) =>
        Assert.IsType<DataContent>(Assert.IsType<MediaContentBlock>(block).Content).Data.ToArray();
}
#pragma warning restore MEAI001
