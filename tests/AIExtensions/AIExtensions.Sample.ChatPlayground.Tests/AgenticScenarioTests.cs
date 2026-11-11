using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The agentic scenarios run end to end against a scripted model behind function invocation, as the app's clients
/// are: the cards and activities they show, how they update in place, and the state they keep.
/// </summary>
public class AgenticScenarioTests
{
    private static readonly ScenarioTools Tools = new(TimeSpan.Zero);

    [Fact]
    public void Catalog_CreatesEveryScenarioOnceWithItsOwnOptions()
    {
        var scenarios = ScenarioCatalog.CreateAll(Tools, new PlanTools(TimeSpan.Zero), new FakeAccent(), "thread.json");

        Assert.Equal(scenarios.Count, scenarios.Select(static scenario => scenario.Id).Distinct().Count());
        using var router = new ScriptedChatClient();
        var capabilities = new ScenarioClientCapabilities("model", SupportsToolCalling: true, SupportsReasoning: true);
        foreach (var scenario in scenarios)
        {
            scenario.CreateAgent(router, loggerFactory: null);
            var options = scenario.CreateRequestOptions(engineOptions: null, capabilities);

            Assert.False(string.IsNullOrWhiteSpace(options.Instructions));
            Assert.Equal("model", options.ModelId);
            Assert.NotEmpty(scenario.Suggestions);
            Assert.Equal(scenario is not ReasoningScenario, scenario.RequiresToolCalling);
            if (!scenario.RequiresToolCalling)
                Assert.Null(options.Tools);
            Assert.Equal(scenario.UsesReasoning, options.Reasoning is not null);
        }
    }

    [Fact]
    public async Task RequestOptions_CarryTheScenarioToolsTheUIActionsAndTheConversation()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Text("Hello.")));
        using var harness = new ScenarioHarness(_ => new FrontendToolsScenario(new FakeAccent()), model);

        await harness.Session.SendMessageAsync("Hi");

        var options = Assert.Single(model.Requests).Options!;
        Assert.Equal("test-model", options.ModelId);
        Assert.Contains("set_accent_color", options.Instructions);
        Assert.Equal(
            [FrontendToolsScenario.ResetAccentColorName, FrontendToolsScenario.SetAccentColorName],
            options.Tools!.Select(static tool => tool.Name).Order());

        // A continuation of a provider conversation keeps its id.
        var continued = harness.Scenario.CreateRequestOptions(
            new ChatOptions { ConversationId = "resp_1" }, harness.Capabilities);
        Assert.Equal("resp_1", continued.ConversationId);
    }

    [Fact]
    public async Task RequestOptions_ForAClientThatCannotCallTools_FailTheTurnWithAClearMessage()
    {
        using var model = new ScriptedChatClient();
        using var harness = new ScenarioHarness(
            _ => new ToolCardsScenario(Tools),
            model,
            new ScenarioClientCapabilities(null, SupportsToolCalling: false, SupportsReasoning: false));

        await harness.Session.SendMessageAsync("What's the weather in Oslo?");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.IsType<NotSupportedException>(harness.Session.Error);
        Assert.Contains("needs a model that can call tools", harness.Session.Error!.Message);
        Assert.Empty(model.Requests);
    }

    [Fact]
    public void Reasoning_IsRequestedOnlyFromAClientThatSupportsIt()
    {
        var scenario = new ReasoningScenario();

        var supported = scenario.CreateRequestOptions(null, new(null, SupportsToolCalling: false, SupportsReasoning: true));
        var unsupported = scenario.CreateRequestOptions(null, new(null, SupportsToolCalling: false, SupportsReasoning: false));

        Assert.Equal(ReasoningOutput.Summary, supported.Reasoning?.Output);
        Assert.Null(unsupported.Reasoning);
        Assert.Null(supported.Tools);
    }

    [Fact]
    public async Task ToolCards_WeatherShowsTheTypedCardBeforeItsResultAndFillsItIn()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(
                new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "get_weather", new Dictionary<string, object?> { ["city"] = "Seattle" }),
                    new FunctionCallContent("call-2", "get_local_time",
                        new Dictionary<string, object?> { ["timeZone"] = "Asia/Tokyo" }),
                ]) { MessageId = "calls" }),
            ScriptedResponse.Stream(Text("It's mild in Seattle.")));
        using var harness = new ScenarioHarness(_ => new ToolCardsScenario(Tools), model);
        var shownWithoutResult = new List<string>();
        using var added = harness.Session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is FunctionInvocationContentBlock { HasResult: false } call)
                shownWithoutResult.Add(call.ToolName!);
        });

        await harness.Session.SendMessageAsync("What's the weather in Seattle and the time in Tokyo?");

        Assert.Equal(["get_weather", "get_local_time"], shownWithoutResult);
        var expected = ScenarioTools.SimulateWeather("Seattle");
        var weather = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<WeatherToolBlock>());
        Assert.Equal("Seattle", weather.City);
        Assert.Equal(expected.Temperature, weather.Temperature);
        Assert.Equal(expected.FeelsLike, weather.FeelsLike);
        Assert.Equal(expected.Conditions, weather.Conditions);
        Assert.Equal(expected.Humidity, weather.Humidity);
        Assert.Equal(expected.WindSpeed, weather.WindSpeed);

        // The other tool keeps the built-in card.
        var time = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<FunctionInvocationContentBlock>(),
            static block => block is not WeatherToolBlock);
        Assert.Equal("get_local_time", time.ToolName);
        Assert.Equal("Asia/Tokyo", ScenarioJson.Read<LocalTimeReport>(time.Result!.Result)?.TimeZone);
        Assert.Equal("It's mild in Seattle.", Assert.Single(harness.LastTurn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public void SimulatedWeather_IsTheSameForTheSameCity()
    {
        Assert.Equal(ScenarioTools.SimulateWeather("Paris"), ScenarioTools.SimulateWeather(" paris ") with { City = "Paris" });
        Assert.NotEqual(ScenarioTools.SimulateWeather("Paris"), ScenarioTools.SimulateWeather("Lima"));
        Assert.All(["Oslo", "Cairo", "Lima", "Tokyo", "Reykjavik"], city =>
        {
            var report = ScenarioTools.SimulateWeather(city);
            Assert.InRange(report.Temperature, -4, 31);
            Assert.InRange(report.Humidity, 35, 94);
            Assert.NotEqual("\u2026", WeatherGlyphs.For(report.Conditions));
        });
    }

    [Fact]
    public async Task CombinedToolCalls_FoldParallelCallsIntoOneCard()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant,
                [WeatherCall("call-1", "Paris"), WeatherCall("call-2", "Tokyo"), WeatherCall("call-3", "Lima")])
            {
                MessageId = "calls",
            }),
            ScriptedResponse.Stream(Text("Lima is the warmest.")));
        using var harness = new ScenarioHarness(_ => new CombinedToolCallsScenario(Tools), model);
        var progress = TrackComparison(harness);

        await harness.Session.SendMessageAsync("Compare the weather in Paris, Tokyo and Lima");

        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<WeatherComparisonBlock>());
        Assert.Equal(["Paris", "Tokyo", "Lima"], card.Rows.Select(static row => row.City));
        Assert.All(card.Rows, row => Assert.Equal(ScenarioTools.SimulateWeather(row.City), row.Report));
        Assert.Equal(BlockLifecycleState.Inactive, card.LifecycleState);
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, static block => block is FunctionInvocationContentBlock);
        Assert.Equal(["3 rows, 0 done", "3 rows, 3 done"], progress);
    }

    [Fact]
    public async Task CombinedToolCalls_AddEachSequentialCallToTheSameCard()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(WeatherCall("call-1", "Paris"))),
            ScriptedResponse.Stream(Update(WeatherCall("call-2", "Tokyo"))),
            ScriptedResponse.Stream(Text("Tokyo is warmer.")));
        using var harness = new ScenarioHarness(_ => new CombinedToolCallsScenario(Tools), model);
        var progress = TrackComparison(harness);

        await harness.Session.SendMessageAsync("Paris or Tokyo?");

        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<WeatherComparisonBlock>());
        Assert.Equal(["Paris", "Tokyo"], card.Rows.Select(static row => row.City));
        Assert.Equal(["1 rows, 0 done", "1 rows, 1 done", "2 rows, 1 done", "2 rows, 2 done"], progress);
        Assert.False(model.Requests[0].Options!.AllowMultipleToolCalls ?? true);
    }

    [Fact]
    public async Task HumanInTheLoop_RunsAnApprovedCallAndNeverARejectedOne()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-1", HumanInTheLoopScenario.BookMeetingName,
                new Dictionary<string, object?>
                {
                    ["title"] = "Design review",
                    ["start"] = "Tomorrow 10:00",
                    ["durationMinutes"] = 30,
                    ["attendees"] = new List<string> { "Ana", "Ken" },
                }))),
            ScriptedResponse.Stream(Text("Booked.")),
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-2", HumanInTheLoopScenario.SendEmailName,
                new Dictionary<string, object?>
                {
                    ["to"] = new List<string> { "team@example.com" },
                    ["subject"] = "Demo moved",
                    ["body"] = "The demo moved to Friday.",
                }))),
            ScriptedResponse.Stream(Text("Okay, I won't send it.")));
        using var harness = new ScenarioHarness(_ => new HumanInTheLoopScenario(), model);
        using var decide = harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;
            var approval = harness.LastTurn.ResponseBlocks.OfType<ToolApprovalBlock>().Single();
            if (approval.ToolName == HumanInTheLoopScenario.BookMeetingName)
                approval.Approve();
            else
                approval.Reject("Not now");
        });

        await harness.Session.SendMessageAsync("Book a design review with Ana and Ken");
        await harness.Session.SendMessageAsync("Email the team that the demo moved");

        var meeting = Assert.Single(harness.Session.Turns[0].ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, meeting.Status);
        var booking = ScenarioJson.Read<MeetingBooking>(meeting.Result!.Result);
        Assert.Equal("Design review", booking?.Title);
        Assert.Equal(["Ana", "Ken"], booking?.Attendees);
        Assert.StartsWith("MTG-", booking?.Confirmation);

        var email = Assert.Single(harness.Session.Turns[1].ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Rejected, email.Status);
        Assert.DoesNotContain("Sent", ScenarioJson.AsString(email.Result?.Result) ?? string.Empty);
        Assert.Equal("Okay, I won't send it.", TextOf(harness.Session.Turns[1]));
    }

    [Fact]
    public async Task FrontendTools_RunTheAutomaticActionAtOnceAndWaitForTheUserToRunTheManualOne()
    {
        var accent = new FakeAccent();
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-1", FrontendToolsScenario.SetAccentColorName,
                new Dictionary<string, object?> { ["color"] = "rebeccapurple" }))),
            ScriptedResponse.Stream(Text("It's purple now.")),
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-2", FrontendToolsScenario.ResetAccentColorName))),
            ScriptedResponse.Stream(Text("Back to the default.")));
        using var harness = new ScenarioHarness(_ => new FrontendToolsScenario(accent), model);
        var resetsBeforeRun = -1;
        using var run = harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;
            resetsBeforeRun = accent.Resets;
            _ = harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single().InvokeAsync();
        });

        await harness.Session.SendMessageAsync("Make it purple");

        Assert.Equal(["rebeccapurple"], accent.Applied);
        Assert.Equal(-1, resetsBeforeRun);
        var automatic = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.True(automatic.IsComplete);
        Assert.Contains("#663399", ScenarioJson.AsString(automatic.Result!.Result));
        var sent = Assert.Single(model.Requests[1].Messages[^1].Contents.OfType<FunctionResultContent>());
        Assert.Equal("call-1", sent.CallId);

        await harness.Session.SendMessageAsync("Reset the colors");

        Assert.Equal(0, resetsBeforeRun);
        Assert.Equal(1, accent.Resets);
        var manual = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.Equal(UIActionInvocationMode.Manual, manual.Mode);
        Assert.True(manual.IsComplete);
        Assert.Equal("Back to the default.", TextOf(harness.LastTurn));
    }

    [Fact]
    public async Task PlanProgress_ShowsOneActivityThatUpdatesInPlaceAndKeepsThePlanAsState()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-1", PlanTools.CreatePlanName,
                new Dictionary<string, object?> { ["steps"] = new List<string> { "Book flights", "Find a hotel", "Plan days" } }))),
            ScriptedResponse.Stream(Update(StepCall("call-2", 0))),
            ScriptedResponse.Stream(Update(StepCall("call-3", 1))),
            ScriptedResponse.Stream(Update(StepCall("call-4", 2))),
            ScriptedResponse.Stream(Text("Your trip is planned.")));
        using var harness = new ScenarioHarness(_ => new PlanProgressScenario(new PlanTools(TimeSpan.Zero)), model);
        var scenario = (PlanProgressScenario)harness.Scenario;
        var activity = new List<string>();
        var state = new List<string>();
        using var added = harness.Session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is not PlanActivityBlock plan)
                return;
            Snapshot(activity, plan.Steps);
            plan.OnChanged(() => Snapshot(activity, plan.Steps));
        });
        using var changed = scenario.PlanAgent.State.OnChanged(() => Snapshot(state, scenario.PlanAgent.State.Value.Steps));

        await harness.Session.SendMessageAsync("Plan a weekend trip");

        var block = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<PlanActivityBlock>());
        Assert.True(block.IsDone);
        Assert.Equal("plan", block.ActivityType);
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
        Assert.Equal(["Book flights", "Find a hotel", "Plan days"], block.Steps.Select(static step => step.Description));
        Assert.DoesNotContain(harness.LastTurn.ResponseBlocks, static block => block is FunctionInvocationContentBlock);
        Assert.Equal("Your trip is planned.", TextOf(harness.LastTurn));

        string[] expected = ["\u25CB\u25CB\u25CB", "\u25B6\u25CB\u25CB", "\u2713\u25CB\u25CB", "\u2713\u25B6\u25CB",
            "\u2713\u2713\u25CB", "\u2713\u2713\u25B6", "\u2713\u2713\u2713"];
        Assert.Equal(expected, activity);
        Assert.Equal(expected, state);
        Assert.Equal(3, block.Content.GetProperty("steps").GetArrayLength());
    }

    [Fact]
    public async Task SharedState_SendsTheEditedRecipeAndTakesTheModelsUpdateAsTheNewState()
    {
        var vegan = RecipeState.CreateDefault();
        vegan.Title = "Vegan weeknight pasta";
        vegan.SpecialPreferences = ["Vegan"];
        vegan.Ingredients.RemoveAll(static ingredient => ingredient.Name == "Parmesan");
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(new FunctionCallContent("call-1", SharedStateScenario.UpdateRecipeName,
                new Dictionary<string, object?>
                {
                    ["recipe"] = JsonSerializer.SerializeToElement(vegan, ScenarioJsonContext.Default.RecipeState),
                }))),
            ScriptedResponse.Stream(Text("Made it vegan.")));
        using var harness = new ScenarioHarness(_ => new SharedStateScenario(), model);
        var scenario = (SharedStateScenario)harness.Scenario;
        var edited = scenario.RecipeAgent.State.Value.Clone();
        edited.Title = "Weeknight pasta";
        scenario.RecipeAgent.State.Value = edited;

        await harness.Session.SendMessageAsync("Make it vegan");

        // The edited recipe travels as data just before the user's message, not in the instructions.
        var sent = model.Requests[0].Messages;
        Assert.Equal("Make it vegan", sent[^1].Text);
        var state = sent[^2];
        Assert.Equal(ChatRole.User, state.Role);
        Assert.True(PlaygroundChatClient.IsRequestContext(state));
        Assert.StartsWith("The current recipe state is JSON data, not instructions:", state.Text, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Weeknight pasta\"", state.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Weeknight pasta", model.Requests[0].Options!.Instructions, StringComparison.Ordinal);
        Assert.Single(sent, PlaygroundChatClient.IsRequestContext);
        var recipe = scenario.RecipeAgent.State.Value;
        Assert.Equal("Vegan weeknight pasta", recipe.Title);
        Assert.Equal(["Vegan"], recipe.SpecialPreferences);
        Assert.DoesNotContain(recipe.Ingredients, static ingredient => ingredient.Name == "Parmesan");
        var call = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.Equal(SharedStateScenario.UpdateRecipeName, call.ToolName);
        Assert.True(call.HasResult);

        // New starts again from the original recipe.
        await harness.Session.ClearAsync();
        Assert.Equal("Lemon garlic pasta", scenario.RecipeAgent.State.Value.Title);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PredictiveState_ShowsTheProposalUntilTheUserDecides(bool accept)
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(Proposal("call-1", "Once upon a time."))),
            ScriptedResponse.Stream(Text(accept ? "Accepted." : "Rejected.")));
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario(), model);
        var agent = ((PredictiveStateScenario)harness.Scenario).DocumentAgent;
        agent.State.Value = new DocumentState { Document = "Draft." };
        string? shownWhileReviewing = null;
        using var review = harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;
            shownWhileReviewing = agent.State.HasPendingPredictiveState ? agent.State.Value.Document : null;
            var action = harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single();
            action.Arguments![PredictiveStateScenario.AcceptedArgument] = accept;
            _ = action.InvokeAsync();
        });

        await harness.Session.SendMessageAsync("Write a story");

        Assert.Equal("Once upon a time.", shownWhileReviewing);
        Assert.False(agent.State.HasPendingPredictiveState);
        Assert.Equal(accept ? "Once upon a time." : "Draft.", agent.State.Value.Document);
        var result = Assert.Single(model.Requests[1].Messages[^1].Contents.OfType<FunctionResultContent>());
        Assert.Contains(accept ? "accepted" : "rejected", ScenarioJson.AsString(result.Result));

        // The decision is not part of what the model is asked to send.
        var declaration = Assert.Single(model.Requests[0].Options!.Tools!.OfType<AIFunctionDeclaration>());
        var properties = declaration.JsonSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("document", out _));
        Assert.False(properties.TryGetProperty(PredictiveStateScenario.AcceptedArgument, out _));
    }

    [Fact]
    public async Task PredictiveState_RollsBackWhenTheTurnStopsDuringTheReview()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Update(Proposal("call-1", "Rewritten."))));
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario(), model);
        var agent = ((PredictiveStateScenario)harness.Scenario).DocumentAgent;
        agent.State.Value = new DocumentState { Document = "Draft." };
        var pending = false;
        using var stop = harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;
            pending = agent.State.HasPendingPredictiveState;
            _ = harness.Session.CancelAsync();
        });

        await harness.Session.SendMessageAsync("Rewrite it");

        Assert.True(pending);
        Assert.True(harness.LastTurn.IsStopped);
        Assert.False(agent.State.HasPendingPredictiveState);
        Assert.Equal("Draft.", agent.State.Value.Document);
    }

    [Fact]
    public async Task PredictiveState_RollsBackWhenTheTurnFails()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(Proposal("call-1", "Rewritten."))) with
            {
                Failure = new InvalidOperationException("The connection dropped."),
            });
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario(), model, functionInvocation: false);
        var agent = ((PredictiveStateScenario)harness.Scenario).DocumentAgent;
        agent.State.Value = new DocumentState { Document = "Draft." };
        var predicted = new List<string>();
        using var changed = agent.State.OnChanged(() => predicted.Add(agent.State.Value.Document));

        await harness.Session.SendMessageAsync("Rewrite it");

        Assert.Equal(ConversationStatus.Error, harness.Session.Status);
        Assert.Equal(["Rewritten.", "Draft."], predicted);
        Assert.False(agent.State.HasPendingPredictiveState);
    }

    [Fact]
    public void DocumentDiff_KeepsBothVersionsInItsSegments()
    {
        const string before = "The cat sat on the mat.\n\nIt was warm.";
        const string after = "The black cat sat on the mat.\n\nIt was very warm.";

        var segments = DocumentDiff.Compute(before, after);

        Assert.Equal(after, string.Concat(segments.Where(static s => s.Kind != DiffKind.Removed).Select(static s => s.Text)));
        Assert.Equal(before, string.Concat(segments.Where(static s => s.Kind != DiffKind.Added).Select(static s => s.Text)));
        Assert.Equal(["black ", "very "], segments.Where(static s => s.Kind == DiffKind.Added).Select(static s => s.Text));
        Assert.DoesNotContain(segments, static s => s.Kind == DiffKind.Removed);
        Assert.Equal([new DiffSegment(DiffKind.Added, "New text")], DocumentDiff.Compute(null, "New text"));
    }

    [Fact]
    public void DocumentDiff_KeepsAWordWhoseOnlyChangeIsTheWhitespaceAfterIt()
    {
        // A new paragraph after the last one: the old last word gains a line break but is the same word.
        var segments = DocumentDiff.Compute("They were safe.", "They were safe.\n\nThe end.");

        Assert.Equal(
            [new DiffSegment(DiffKind.Unchanged, "They were safe.\n\n"), new DiffSegment(DiffKind.Added, "The end.")],
            segments);
    }

    [Fact]
    public async Task Persistence_RestoresTheSavedConversationInANewAgent()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(WeatherCall("call-1", "Oslo"))),
            ScriptedResponse.Stream(Text("It's chilly in Oslo.")),
            ScriptedResponse.Stream(Text("You asked about Oslo.")));
        using var harness = new ScenarioHarness(
            harness => new PersistenceScenario(Tools, Path.Combine(harness.TempDirectory, "thread.json")), model);
        var scenario = (PersistenceScenario)harness.Scenario;

        await harness.Session.SendMessageAsync("Weather in Oslo?");
        Assert.Equal(1, scenario.Thread!.TurnCount);
        Assert.True(File.Exists(scenario.ThreadPath));

        harness.Reopen();
        await harness.Session.RestoreAsync();

        var turn = Assert.Single(harness.Session.Turns);
        Assert.Equal("Weather in Oslo?", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
        var card = Assert.Single(turn.ResponseBlocks.OfType<WeatherToolBlock>());
        Assert.Equal("Oslo", card.City);
        Assert.Equal(ScenarioTools.SimulateWeather("Oslo").Temperature, card.Temperature);
        Assert.Equal("It's chilly in Oslo.", TextOf(turn));

        // The restored conversation is the history of the next request.
        await harness.Session.SendMessageAsync("What did I ask?");
        var history = model.Requests[^1].Messages;
        Assert.Contains(history, static message => message.Text == "Weather in Oslo?");
        Assert.Contains(history.SelectMany(static message => message.Contents).OfType<FunctionResultContent>(),
            static result => result.CallId == "call-1");
        Assert.Equal(2, scenario.Thread.TurnCount);
    }

    [Fact]
    public async Task Persistence_RestoresAStoppedTurnAsStopped()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "Let me think") { MessageId = "partial" }) with
            {
                WaitForCancellation = true,
            });
        using var harness = new ScenarioHarness(
            harness => new PersistenceScenario(Tools, Path.Combine(harness.TempDirectory, "thread.json")), model);
        using (harness.Session.RegisterOnBlockAdded((turn, block) =>
        {
            if (turn.ResponseBlocks.Contains(block))
                _ = harness.Session.CancelAsync();
        }))
        {
            await harness.Session.SendMessageAsync("Tell me a long story");
        }

        harness.Reopen();
        await harness.Session.RestoreAsync();

        var turn = Assert.Single(harness.Session.Turns);
        Assert.True(turn.IsStopped);
        Assert.Equal("Let me think", TextOf(turn));
    }

    [Fact]
    public async Task Persistence_NewConversationClearsTheFile()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Text("Hello!")));
        using var harness = new ScenarioHarness(
            harness => new PersistenceScenario(Tools, Path.Combine(harness.TempDirectory, "thread.json")), model);

        await harness.Session.SendMessageAsync("Hi");
        await harness.Session.ClearAsync();
        harness.Reopen();
        await harness.Session.RestoreAsync();

        Assert.Empty(harness.Session.Turns);
        Assert.Equal(0, ((PersistenceScenario)harness.Scenario).Thread!.TurnCount);
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
            Assert.Equal(thread.ThreadId, reopened.ThreadId);
            Assert.Equal(["Hi", "Hello"], reopened.GetUpdates().Select(static update => update.Text));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_AnswersAManualUIActionWithTheRecordedDecision()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Update(Proposal("call-1", "A new story."))),
            ScriptedResponse.Stream(Text("Accepted.")));
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario(), model);
        var scenario = (PredictiveStateScenario)harness.Scenario;
        using (harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status != ConversationStatus.AwaitingInput)
                return;
            var action = harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>().Single();
            action.Arguments![PredictiveStateScenario.AcceptedArgument] = true;
            _ = action.InvokeAsync();
        }))
        {
            await harness.Session.SendMessageAsync("Write a story");
        }

        Assert.Equal(2, harness.Recording.InteractionCount);

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(1, result.Turns);
        Assert.Empty(result.UnansweredApprovals);
        Assert.Equal("A new story.", scenario.DocumentAgent.State.Value.Document);
        Assert.False(scenario.DocumentAgent.State.HasPendingPredictiveState);
        var action = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<UIActionBlock>());
        Assert.True(action.IsComplete);
        Assert.Equal(true, ScenarioJson.AsBool(action.Arguments![PredictiveStateScenario.AcceptedArgument]));
        Assert.Equal("Accepted.", TextOf(harness.LastTurn));
    }

    [Fact]
    public async Task Replay_StopsAManualUIActionThatTheRecordingNeverRan()
    {
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(Update(Proposal("call-1", "A new story."))));
        using var harness = new ScenarioHarness(_ => new PredictiveStateScenario(), model);
        var scenario = (PredictiveStateScenario)harness.Scenario;
        using (harness.Session.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                _ = harness.Session.CancelAsync();
        }))
        {
            await harness.Session.SendMessageAsync("Write a story");
        }

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal([PredictiveStateScenario.ProposeDocumentName], result.UnansweredApprovals);
        Assert.True(harness.LastTurn.IsStopped);
        Assert.Equal(string.Empty, scenario.DocumentAgent.State.Value.Document);
    }

    [Fact]
    public async Task Replay_PlaysEveryTurnLiveAndLeavesTheStateMessagesOut()
    {
        // Each request carries the recipe just before its user message, so no request's history matches the one
        // before it word for word. The replay must still recognize that each continues what it shows.
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(Text("It serves two.")),
            ScriptedResponse.Stream(Text("About thirty minutes.")));
        using var harness = new ScenarioHarness(_ => new SharedStateScenario(), model);
        await harness.Session.SendMessageAsync("How many does it serve?");
        await harness.Session.SendMessageAsync("How long does it take?");
        Assert.All(model.Requests, static request => Assert.Single(request.Messages, PlaygroundChatClient.IsRequestContext));

        var result = await harness.Driver.ReplayAsync(playAll: true);

        Assert.Equal(2, result.Turns);
        Assert.Equal(0, result.RestoredHistory);
        Assert.Equal(0, result.RestoredInteractions);
        Assert.Equal(["How many does it serve?", "How long does it take?"],
            harness.Session.Turns.Select(static turn =>
                string.Concat(turn.RequestBlocks.OfType<TextContentBlock>().Select(static block => block.RawText))));
        Assert.Equal("About thirty minutes.", TextOf(harness.LastTurn));
    }

    internal static ChatResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, text) { MessageId = "text-" + Guid.NewGuid().ToString("N") };

    internal static ChatResponseUpdate Update(AIContent content) =>
        new(ChatRole.Assistant, [content]) { MessageId = "message-" + Guid.NewGuid().ToString("N") };

    internal static FunctionCallContent WeatherCall(string callId, string city) =>
        new(callId, "get_weather", new Dictionary<string, object?> { ["city"] = city });

    internal static FunctionCallContent Proposal(string callId, string document) =>
        new(callId, PredictiveStateScenario.ProposeDocumentName, new Dictionary<string, object?> { ["document"] = document });

    internal static string TextOf(ConversationTurn turn) =>
        string.Concat(turn.ResponseBlocks.OfType<TextContentBlock>().Select(static block => block.RawText));

    private static FunctionCallContent StepCall(string callId, int index) =>
        new(callId, PlanTools.UpdatePlanStepName,
            new Dictionary<string, object?> { ["index"] = index, ["status"] = PlanStep.Completed });

    internal static List<string> TrackComparison(ScenarioHarness harness)
    {
        var progress = new List<string>();
        harness.Session.RegisterOnBlockAdded((_, block) =>
        {
            if (block is not WeatherComparisonBlock card)
                return;
            Record();
            card.OnChanged(Record);

            void Record()
            {
                var snapshot = $"{card.Rows.Count} rows, {card.CompletedCount} done";
                if (progress.Count == 0 || progress[^1] != snapshot)
                    progress.Add(snapshot);
            }
        });
        return progress;
    }

    internal static void Snapshot(List<string> snapshots, IEnumerable<PlanStep> steps)
    {
        var snapshot = string.Concat(steps.Select(static step =>
            step.IsCompleted ? "\u2713" : step.IsRunning ? "\u25B6" : "\u25CB"));
        if (snapshots.Count == 0 || snapshots[^1] != snapshot)
            snapshots.Add(snapshot);
    }

    private sealed class FakeAccent : IAccentColorSurface
    {
        public List<string> Applied { get; } = [];

        public int Resets { get; private set; }

        public string? ApplyAccentColor(string color)
        {
            Applied.Add(color);
            return color == "rebeccapurple" ? "#663399" : null;
        }

        public void ResetAccentColor() => Resets++;
    }
}
