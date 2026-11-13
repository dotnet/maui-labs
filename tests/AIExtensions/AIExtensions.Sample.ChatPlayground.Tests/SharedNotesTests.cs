using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The shared notes: the user edits them, every request tells the model what they say, and the model changes them
/// only by proposing new text with <c>propose_notes</c>, which the user accepts or rejects.
/// </summary>
public sealed class SharedNotesTests
{
    [Fact]
    public async Task Notes_GoWithEachRequestWhileTheyHaveText()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Text("Hello."), ScriptedResponse.Text("Something waterproof."), ScriptedResponse.Text("Boots."));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Hi");
        harness.Notes.Text = "Pack a raincoat.";
        await harness.Session.SendMessageAsync("What should I pack?");
        harness.Notes.Text = "Pack boots.";
        await harness.Session.SendMessageAsync("And now?");

        Assert.DoesNotContain(model.Requests[0].Messages, PlaygroundChatClient.IsRequestContext);

        // The notes go just before the user's message, as data rather than instructions.
        var sent = model.Requests[1].Messages;
        Assert.Equal("What should I pack?", sent[^1].Text);
        Assert.True(PlaygroundChatClient.IsRequestContext(sent[^2]));
        Assert.Equal(ChatRole.User, sent[^2].Role);
        Assert.Contains("<notes>" + Environment.NewLine + "Pack a raincoat." + Environment.NewLine + "</notes>", sent[^2].Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("raincoat", model.Requests[1].Options!.Instructions, StringComparison.Ordinal);

        // They belong to that request only: the conversation doesn't keep them, so the next request has just the
        // current notes.
        Assert.Equal("What should I pack?",
            Assert.IsType<TextContentBlock>(Assert.Single(harness.Session.Turns[1].RequestBlocks)).RawText);
        var context = Assert.Single(model.Requests[2].Messages, PlaygroundChatClient.IsRequestContext);
        Assert.Contains("Pack boots.", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Requests[2].Messages, message => message.Text.Contains("raincoat", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Proposal_IsShownWithItsChangesUntilTheUserDecides(bool accept)
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Propose("call-1", "Pack a raincoat and boots.")),
            ScriptedResponse.Text(accept ? "Added boots." : "Left them as they were."));
        using var harness = new PlaygroundChatHarness(model);
        harness.Notes.Text = "Pack a raincoat.";
        (string? Proposal, string Text, IReadOnlyList<DiffSegment> Changes)? review = null;
        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            review = (harness.Notes.Proposal, harness.Notes.Text, harness.NotesBar.Changes);
            _ = harness.Notes.DecideAsync(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single(), accept);
        });

        await harness.Session.SendMessageAsync("Add boots to my notes");

        // While the turn waits, the notes show the proposal; the committed text stays as it was.
        var (proposal, text, changes) = Assert.NotNull(review);
        Assert.Equal("Pack a raincoat and boots.", proposal);
        Assert.Equal("Pack a raincoat.", text);
        Assert.Equal(proposal, string.Concat(changes.Where(segment => segment.Kind != DiffKind.Removed).Select(segment => segment.Text)));
        Assert.Equal(text, string.Concat(changes.Where(segment => segment.Kind != DiffKind.Added).Select(segment => segment.Text)));

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.False(harness.Notes.HasProposal);
        Assert.False(harness.NotesBar.HasProposal);
        Assert.Empty(harness.NotesBar.Changes);
        Assert.Equal(accept ? "Pack a raincoat and boots." : "Pack a raincoat.", harness.Notes.Text);
        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(SharedNotes.ProposeToolName, card.ToolName);
        Assert.Equal(UIActionInvocationMode.Manual, card.Mode);
        Assert.True(card.IsComplete);

        // The model hears the decision.
        var result = Assert.Single(model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        Assert.StartsWith(accept ? "Accepted:" : "Rejected:", result.Result?.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task TwoProposals_AreReviewedInOrderAndEndTheSameInTheBarAsOnTheirCards(bool onCards, bool inOneUpdate)
    {
        const string Boots = "Pack a raincoat and boots.";
        const string Hat = "Pack a raincoat, boots and a hat.";
        ChatResponseUpdate[] proposals = inOneUpdate
            ? [new(ChatRole.Assistant, [.. Propose("call-1", Boots).Contents, .. Propose("call-2", Hat).Contents]) { MessageId = "proposal" }]
            : [Propose("call-1", Boots), Propose("call-2", Hat)];
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(proposals), ScriptedResponse.Text("Added both."));
        using var harness = new PlaygroundChatHarness(model);
        harness.Notes.Text = "Pack a raincoat.";
        var bar = harness.NotesBar;
        var reviews = new List<(string? CallId, string? Provisional, string Before, string After)>();

        async Task AcceptEachAsync()
        {
            foreach (var card in harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().ToList())
            {
                reviews.Add((
                    bar.Proposal?.Call?.CallId,
                    harness.Notes.Proposal,
                    string.Concat(bar.Changes.Where(static segment => segment.Kind != DiffKind.Added).Select(static segment => segment.Text)),
                    string.Concat(bar.Changes.Where(static segment => segment.Kind != DiffKind.Removed).Select(static segment => segment.Text))));
                await bar.AcceptProposalAction.ExecuteAsync(onCards ? card : null);
            }
        }

        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                _ = AcceptEachAsync();
        });

        await harness.Session.SendMessageAsync("Add boots, and then a hat");

        // Only the first proposal is provisional; the second is shown against the notes the first left.
        Assert.Equal(
            [("call-1", Boots, "Pack a raincoat.", Boots), ("call-2", null, Boots, Hat)],
            reviews);
        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal(Hat, harness.Notes.Text);
        Assert.False(bar.HasProposal);
        var results = model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().ToList();
        Assert.Equal(["call-1", "call-2"], results.Select(static result => result.CallId));
        Assert.All(results, static result => Assert.StartsWith("Accepted:", result.Result?.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Proposal_RollsBackWhenTheTurnStopsDuringTheReview()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Propose("call-1", "Rewritten.")));
        using var harness = new PlaygroundChatHarness(model);
        harness.Notes.Text = "Draft.";
        var proposed = false;
        using var stop = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            proposed = harness.Notes.HasProposal;
            _ = harness.Session.CancelAsync();
        });

        await harness.Session.SendMessageAsync("Rewrite my notes");

        Assert.True(proposed);
        Assert.True(harness.LastTurn.IsStopped);
        Assert.False(harness.Notes.HasProposal);
        Assert.Equal("Draft.", harness.Notes.Text);
        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.False(card.IsAwaitingInput);
        Assert.True(card.IsAbandoned);

        // A decision that arrives too late changes nothing.
        await harness.Notes.DecideAsync(card, accept: true);
        Assert.Equal("Draft.", harness.Notes.Text);
    }

    [Fact]
    public async Task Proposal_RollsBackWhenTheTurnFails()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Propose("call-1", "Rewritten.")) with
            {
                Failure = new InvalidOperationException("The connection dropped."),
            });
        using var harness = new PlaygroundChatHarness(model);
        harness.Notes.Text = "Draft.";
        var proposals = new List<string?>();
        harness.Notes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SharedNotes.Proposal))
                proposals.Add(harness.Notes.Proposal);
        };

        await harness.Session.SendMessageAsync("Rewrite my notes");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.Equal(["Rewritten.", null], proposals);
        Assert.Equal("Draft.", harness.Notes.Text);
    }

    [Fact]
    public async Task Proposal_ContinuesTheProviderConversationWithOnlyTheDecision()
    {
        // A provider that keeps the conversation, such as the Responses API, is continued with what is new.
        var call = Propose("call-1", "Pack boots.");
        call.ConversationId = "response-1";
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(call), ScriptedResponse.Text("Added boots."));
        using var harness = new PlaygroundChatHarness(model, persist: true);
        harness.Notes.Text = "Pack a raincoat.";
        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                _ = harness.Notes.DecideAsync(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single(), accept: true);
        });

        await harness.Session.SendMessageAsync("Add boots");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Null(model.Requests[0].Options!.ConversationId);
        var continuation = model.Requests[1];
        Assert.Equal("response-1", continuation.Options!.ConversationId);
        var result = Assert.Single(continuation.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        Assert.DoesNotContain(continuation.Messages, message => message.Text == "Add boots");
        Assert.DoesNotContain(continuation.Messages, PlaygroundChatClient.IsRequestContext);
        Assert.Equal("Pack boots.", harness.Notes.Text);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Proposal_InAResponseWithACallThatNeedsApproval_WaitsInTheBarBesideTheApproval(bool streaming)
    {
        // With "Ask before running tools", function invocation asks about every call of a response that has a call
        // needing approval. The proposal is still the app's own action: it waits in the bar for the user, beside the
        // approval of the other call, and the model hears both outcomes, each after its call.
        using var model = new ScriptedChatClient(
            new ScriptedResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_connection_status", new Dictionary<string, object?>()),
                new FunctionCallContent("call-2", SharedNotes.ProposeToolName, new Dictionary<string, object?> { ["text"] = "Hello" }),
            ]))),
            ScriptedResponse.Text("Done."));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        harness.Settings.UseStreaming = streaming;
        harness.Settings.RequireToolApproval = true;
        var waiting = new List<string>();
        var barHadProposal = false;
        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            waiting.AddRange(harness.LastTurn.ResponseBlocks.Select(block => block.GetType().Name));
            barHadProposal = harness.NotesBar.HasProposal;
            harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single().Approve();
            _ = harness.Notes.DecideAsync(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single(), accept: true);
        });

        await harness.Session.SendMessageAsync("Check my connection and note hello");

        Assert.Equal(ConversationStatus.Idle, harness.Session.Status);
        Assert.Equal([nameof(ToolApprovalBlock), nameof(UIActionBlock)], waiting);
        Assert.True(barHadProposal);
        Assert.Equal("Hello", harness.Notes.Text);
        var approval = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(("get_connection_status", ApprovalStatus.Approved), (approval.ToolName, approval.Status));
        Assert.True(Assert.IsType<ConnectionStatusBlock>(approval.InnerBlock).HasResult);
        var proposal = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(SharedNotes.ProposeToolName, proposal.ToolName);
        Assert.True(proposal.HasResult);
        Assert.Equal("Done.", Assert.IsType<TextContentBlock>(harness.LastTurn.ResponseBlocks[^1]).RawText);

        var sent = model.Requests[1].Messages.SelectMany(message => message.Contents).ToList();
        Assert.DoesNotContain(sent, content => content is ToolApprovalRequestContent or ToolApprovalResponseContent);
        var results = sent.OfType<FunctionResultContent>().ToList();
        Assert.Equal(["call-1", "call-2"], results.Select(result => result.CallId).Order());
        Assert.All(results, result => Assert.True(
            sent.FindIndex(content => content is FunctionCallContent call && call.CallId == result.CallId) < sent.IndexOf(result)));
    }

    [Fact]
    public async Task UserEdits_AreIgnoredWhileAProposalWaits()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Propose("call-1", "Rewritten.")), ScriptedResponse.Text("Okay."));
        using var harness = new PlaygroundChatHarness(model);
        harness.Notes.Text = "Draft.";
        using var edit = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            harness.Notes.Text = "Typed while reviewing.";
            _ = harness.Notes.DecideAsync(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single(), accept: false);
        });

        await harness.Session.SendMessageAsync("Rewrite my notes");

        Assert.Equal("Draft.", harness.Notes.Text);
    }

    [Fact]
    public async Task NotesBar_ShowsItselfWhenTheNotesChangeAndStaysHiddenUntilTheNextChange()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Propose("call-1", "Pack boots.")), ScriptedResponse.Text("Done."));
        using var harness = new PlaygroundChatHarness(model);
        var bar = new NotesViewModel(harness.Notes, harness.Session);
        Assert.False(bar.IsShown);
        Assert.Equal("Show notes", bar.ToggleShownText);
        Assert.Equal("Empty \u2014 tap to write", bar.Summary);

        bar.Text = "Pack a raincoat.\nAnd a hat.";
        Assert.True(bar.IsShown);
        Assert.Equal("Hide notes", bar.ToggleShownText);
        Assert.Equal("Pack a raincoat.", bar.Summary);

        bar.ToggleShownAction.Execute(null);
        Assert.False(bar.IsShown);
        bar.Text = "Pack a raincoat.";
        Assert.True(bar.IsShown);

        // A proposal shows the bar expanded and read-only, and its Accept answers the waiting card.
        bar.ToggleShownAction.Execute(null);
        (bool Shown, bool Expanded, bool ReadOnly, string Summary)? review = null;
        using var decide = harness.Session.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;

            review = (bar.IsShown, bar.IsExpanded, bar.IsReadOnly, bar.Summary);
            bar.AcceptProposalAction.Execute(null);
        });

        await harness.Session.SendMessageAsync("Make it boots");

        Assert.Equal((true, true, true, "Proposed changes \u2014 review them"), review);
        Assert.Equal("Pack boots.", harness.Notes.Text);
        Assert.False(bar.IsReadOnly);
        Assert.False(bar.HasProposal);
    }

    [Fact]
    public void NotesBar_LoadsSavedNotesCollapsed()
    {
        using var model = new ScriptedChatClient();
        using var harness = new PlaygroundChatHarness(model);
        var bar = new NotesViewModel(harness.Notes, harness.Session) { IsExpanded = true };

        bar.Load("Saved notes");
        Assert.True(bar.IsShown);
        Assert.False(bar.IsExpanded);
        Assert.Equal("Saved notes", harness.Notes.Text);

        bar.Load(string.Empty);
        Assert.False(bar.IsShown);
    }

    [Fact]
    public void NotesFile_KeepsTheNotesAndRemovesEmptyOnes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"chat-playground-notes-{Guid.NewGuid():N}");
        try
        {
            var file = new NotesFile(Path.Combine(directory, "notes.txt"));
            Assert.Equal(string.Empty, file.Read());

            file.Write("Pack a raincoat.\n");
            Assert.Equal("Pack a raincoat.\n", file.Read());
            Assert.Equal("Pack a raincoat.\n", new NotesFile(file.FilePath).Read());

            file.Write(string.Empty);
            Assert.False(File.Exists(file.FilePath));
            Assert.Equal(string.Empty, file.Read());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    internal static ChatResponseUpdate Propose(string callId, string text) =>
        new(ChatRole.Assistant,
            [new FunctionCallContent(callId, SharedNotes.ProposeToolName, new Dictionary<string, object?> { ["text"] = text })])
        {
            MessageId = "proposal",
        };
}
