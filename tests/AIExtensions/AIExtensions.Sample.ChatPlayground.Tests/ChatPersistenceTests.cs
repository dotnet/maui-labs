using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The saved chat: the engine writes every committed turn to a <see cref="FileConversationThread"/>, the notes are
/// saved next to it, and launching the app again restores both. New chat clears them.
/// </summary>
public sealed class ChatPersistenceTests
{
    [Fact]
    public async Task Relaunch_RestoresTheConversationAndTheNotes()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(SharedNotesTests.Propose("call-1", "Pack boots.")),
            ScriptedResponse.Text("Added boots."),
            ScriptedResponse.Text("You asked me to add boots."));
        using var harness = new PlaygroundChatHarness(model, persist: true);
        harness.Notes.Text = "Pack a raincoat.";
        using (harness.Session.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                _ = harness.Notes.DecideAsync(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single(), accept: true);
        }))
        {
            await harness.Session.SendMessageAsync("Add boots to my notes");
        }

        Assert.Equal(1, harness.Thread!.TurnCount);
        Assert.Equal("Pack boots.", File.ReadAllText(harness.NotesPath));

        await harness.ReopenAsync();

        // Restoring cannot replay the user's decision, so the notes come back from their own file.
        Assert.Equal("Pack boots.", harness.Notes.Text);
        Assert.False(harness.Notes.HasProposal);
        var turn = Assert.Single(harness.Session.Turns);
        Assert.Equal("Add boots to my notes", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
        var card = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(SharedNotes.ProposeToolName, card.ToolName);
        Assert.True(card.IsComplete);
        Assert.False(card.IsAwaitingInput);
        Assert.Equal("Added boots.", Assert.IsType<TextContentBlock>(turn.ResponseBlocks[^1]).RawText);

        // The restored conversation, and the restored notes, go with the next request.
        await harness.Session.SendMessageAsync("What did I ask?");
        var sent = model.Requests[^1].Messages;
        Assert.Contains(sent, message => message.Text == "Add boots to my notes");
        Assert.Contains(sent.SelectMany(message => message.Contents).OfType<FunctionResultContent>(),
            result => result.CallId == "call-1");
        Assert.Contains("Pack boots.", Assert.Single(sent, PlaygroundChatClient.IsRequestContext).Text, StringComparison.Ordinal);
        Assert.Equal(2, harness.Thread!.TurnCount);
    }

    [Fact]
    public async Task Relaunch_RestoresAStoppedTurnAsStoppedWithEveryCalculationOfItsWorksheet()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(
                new ChatResponseUpdate(ChatRole.Assistant, "Let me work it out.") { MessageId = "text" },
                new ChatResponseUpdate(ChatRole.Assistant, [Calculate("call-1", 12, "multiply", 7), Calculate("call-2", 84, "add", 16)])
                {
                    MessageId = "calls",
                },
                new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", 84)])) with
            {
                WaitForCancellation = true,
            },
            ScriptedResponse.Text("100."));
        using var harness = new PlaygroundChatHarness(model, persist: true);

        var send = harness.Session.SendMessageAsync("What is 12 \u00D7 7, and that plus 16?");
        await model.StreamCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Session.CancelAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await send.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, Assert.Single(harness.LastTurn.ResponseBlocks.OfType<CalculatorWorksheetBlock>()).Rows.Count);

        await harness.ReopenAsync();

        // The thread keeps the calculation the stop left without a result, so the worksheet comes back whole: the
        // finished row, and the row that will never get its result, which the stopped turn shows as "No result".
        var turn = Assert.Single(harness.Session.Turns);
        Assert.True(turn.IsStopped);
        Assert.Equal("Let me work it out.", Assert.IsType<TextContentBlock>(turn.ResponseBlocks[0]).RawText);
        var rows = Assert.Single(turn.ResponseBlocks.OfType<CalculatorWorksheetBlock>()).Rows;
        Assert.Equal(["call-1", "call-2"], rows.Select(row => row.CallId));
        Assert.Equal("84", CalculatorDisplay.FormatResult(rows[0].Result, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(rows[1].IsPending);

        // The model never hears of the call without a result, which a provider would reject.
        await harness.Session.SendMessageAsync("And the second one?");
        var sent = model.Requests[^1].Messages.SelectMany(message => message.Contents).ToList();
        Assert.Contains(sent, content => content is FunctionResultContent { CallId: "call-1" });
        Assert.DoesNotContain(sent, content => content is FunctionCallContent { CallId: "call-2" });
    }

    [Fact]
    public async Task NewChat_ClearsTheSavedConversationAndTheNotes()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Hello!"));
        using var harness = new PlaygroundChatHarness(model, persist: true);
        harness.Notes.Text = "Pack a raincoat.";
        await harness.Session.SendMessageAsync("Hi");
        Assert.True(File.Exists(harness.NotesPath));

        await harness.NewChatAsync();

        Assert.Empty(harness.Session.Turns);
        Assert.Equal(string.Empty, harness.Notes.Text);
        Assert.False(File.Exists(harness.NotesPath));

        await harness.ReopenAsync();

        Assert.Empty(harness.Session.Turns);
        Assert.Equal(0, harness.Thread!.TurnCount);
        Assert.Equal(string.Empty, harness.Notes.Text);
    }

    [Fact]
    public async Task NewChat_DeletesTheSavedNotesAlsoWhenTheNotesAreEmpty()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model, persist: true);
        Directory.CreateDirectory(Path.GetDirectoryName(harness.NotesPath)!);
        File.WriteAllText(harness.NotesPath, "Saved by an earlier run.");
        Assert.Equal(string.Empty, harness.Notes.Text);

        await harness.NewChatAsync();

        Assert.False(File.Exists(harness.NotesPath));
        await harness.ReopenAsync();
        Assert.Equal(string.Empty, harness.Notes.Text);
    }

    [Fact]
    public async Task Relaunch_KeepsTheNotesWhenTheConversationFileIsDamaged()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model, persist: true);
        harness.Notes.Text = "Pack a raincoat.";
        File.WriteAllText(harness.ThreadPath, "{ not json");

        var status = await harness.ReopenAsync();

        Assert.NotNull(harness.Thread!.LoadError);
        Assert.Equal(harness.Thread.LoadError, status);
        Assert.Empty(harness.Session.Turns);
        Assert.Equal("Pack a raincoat.", harness.Notes.Text);
        Assert.True(harness.NotesBar.IsShown);

        // The next edit builds on the saved notes instead of replacing them.
        harness.NotesBar.Text = "Pack a raincoat and boots.";
        Assert.Equal("Pack a raincoat and boots.", File.ReadAllText(harness.NotesPath));
    }

    [Fact]
    public async Task Relaunch_KeepsTheNotesWhenTheRestoreFails()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Text("Hello!"));
        using var harness = new PlaygroundChatHarness(model, persist: true);
        await harness.Session.SendMessageAsync("Hi");
        harness.Notes.Text = "Pack a raincoat.";

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.ReopenAsync(new CancellationToken(canceled: true)));

        Assert.Empty(harness.Session.Turns);
        Assert.Equal("Pack a raincoat.", harness.Notes.Text);
        Assert.Equal("Pack a raincoat.", File.ReadAllText(harness.NotesPath));
    }

    [Fact]
    public void FileConversationThread_SavesTheUpdatesOfEachCommittedTurn()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chat-playground-thread-{Guid.NewGuid():N}");
        try
        {
            var path = Path.Combine(directory, "thread.json");
            var thread = new FileConversationThread(path);
            Assert.Null(thread.LoadError);
            thread.AppendUserMessage(new ChatMessage(ChatRole.User, "What is 12 \u00D7 7?") { MessageId = "user-1" });
            thread.AppendUpdate(new ChatResponseUpdate(ChatRole.Assistant,
            [
                new TextReasoningContent("Use the calculator.") { ProtectedData = "opaque" },
                new FunctionCallContent("call-1", "calculate",
                    new Dictionary<string, object?> { ["left"] = 12, ["operation"] = "multiply", ["right"] = 7 }),
            ])
            {
                MessageId = "reply-1",
                AdditionalProperties = new() { ["marker"] = "continuation" },
            });
            thread.AppendUpdate(new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", 84)]));
            thread.AppendUpdate(new ChatResponseUpdate(ChatRole.Assistant, "It is 84.") { MessageId = "reply-2" });
            thread.CompleteTurn();

            // A turn that never commits is not saved.
            thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Unfinished"));

            var reopened = new FileConversationThread(path);

            Assert.Null(reopened.LoadError);
            Assert.Equal(1, reopened.TurnCount);
            Assert.Equal(thread.ThreadId, reopened.ThreadId);
            Assert.False(reopened.IsStateful);
            var updates = reopened.GetUpdates();
            Assert.Equal(["user-1", "reply-1", null, "reply-2"], updates.Select(update => update.MessageId));
            Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant], updates.Select(update => update.Role!.Value));
            Assert.Equal("What is 12 \u00D7 7?", updates[0].Text);
            var reasoning = Assert.IsType<TextReasoningContent>(updates[1].Contents[0]);
            Assert.Equal(("Use the calculator.", "opaque"), (reasoning.Text, reasoning.ProtectedData));
            var call = Assert.IsType<FunctionCallContent>(updates[1].Contents[1]);
            Assert.Equal(("call-1", "calculate"), (call.CallId, call.Name));
            Assert.Equal("12 \u00D7 7", CalculatorDisplay.FormatExpression(call.Arguments, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("continuation", Assert.IsType<System.Text.Json.JsonElement>(updates[1].AdditionalProperties?["marker"]).GetString());
            var result = Assert.IsType<FunctionResultContent>(Assert.Single(updates[2].Contents));
            Assert.Equal("84", CalculatorDisplay.FormatResult(result, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("It is 84.", updates[3].Text);

            reopened.Clear();
            Assert.Equal(0, new FileConversationThread(path).TurnCount);
            Assert.NotEqual(thread.ThreadId, reopened.ThreadId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileConversationThread_StartsEmptyWhenTheFileIsDamaged()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chat-playground-thread-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "thread.json");
            File.WriteAllText(path, "{ not json");

            var thread = new FileConversationThread(path);
            Assert.NotNull(thread.LoadError);
            Assert.Equal(0, thread.TurnCount);

            thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Hi") { MessageId = "user-1" });
            thread.AppendUpdate(new ChatResponseUpdate(ChatRole.Assistant, "Hello") { MessageId = "reply-1" });
            thread.CompleteTurn();

            var reopened = new FileConversationThread(path);
            Assert.Null(reopened.LoadError);
            Assert.Equal(1, reopened.TurnCount);
            Assert.Equal(["Hi", "Hello"], reopened.GetUpdates().Select(update => update.Text));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FunctionCallContent Calculate(string callId, double left, string operation, double right) =>
        new(callId, CalculatorWorksheetHandler.ToolName,
            new Dictionary<string, object?> { ["left"] = left, ["operation"] = operation, ["right"] = right });
}
