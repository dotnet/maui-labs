using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Tool cards that update: <c>get_local_time</c> uses the built-in tool card, and <c>get_weather</c> a typed
/// <see cref="WeatherToolBlock"/> that the generator maps from the call and its result. Both show the call while the
/// tool runs and fill in when the result arrives.
/// </summary>
public sealed class ToolCardsScenario(ScenarioTools tools) : PlaygroundScenario
{
    public override string Id => "tool-cards";
    public override string Title => "Tool cards";
    public override string Glyph => "\U0001F6E0\uFE0F";
    public override string Summary => "Built-in and typed cards that update as tools run";

    public override string Description =>
        "Tools run locally with a short delay, so each card shows its call first and fills in when the result arrives. " +
        "get_local_time uses the built-in tool card; get_weather uses a typed [ToolBlock] card registered with " +
        "options.AddGeneratedToolBlocks().";

    public override string Placeholder => "Ask about the weather or the time somewhere\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "What's the weather in Seattle?",
        "What time is it in Tokyo, and how's the weather there?",
    ];

    protected override string Instructions => """
        You are a helpful assistant. Use get_weather for weather questions and get_local_time for the time in a place
        (pass an IANA time zone). After the tools return, answer in one or two short sentences: the cards already show
        the details.
        """;

    protected override IEnumerable<AITool> Tools => [tools.GetWeather, tools.GetLocalTime];

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory) =>
        new(client, options => options.AddGeneratedToolBlocks(), loggerFactory);
}

/// <summary>
/// Combines tool calls into one view: a custom <see cref="WeatherComparisonHandler"/> folds every <c>get_weather</c>
/// call of a response into one <see cref="WeatherComparisonBlock"/>, which a template shows as one card that fills in
/// city by city.
/// </summary>
public sealed class CombinedToolCallsScenario(ScenarioTools tools) : PlaygroundScenario
{
    public override string Id => "combined-tool-calls";
    public override string Title => "Combined tool calls";
    public override string Glyph => "\U0001F5C2\uFE0F";
    public override string Summary => "Several calls folded into one card";

    public override string Description =>
        "A custom ContentBlockHandler (options.AddBlockHandler) claims every get_weather call of a response and its " +
        "result, and folds them into one WeatherComparisonBlock. A BlockTemplate shows it as one card with a row per " +
        "city that fills in as each result arrives.";

    public override string Placeholder => "Compare the weather in a few cities\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "Compare the weather in Paris, Tokyo and Lima",
        "Where is it warmest right now: Oslo, Cairo, Sydney or Toronto?",
    ];

    // One call at a time, so the card visibly grows and fills in row by row; with parallel calls the results of a
    // batch arrive together.
    protected override bool? AllowMultipleToolCalls => false;

    protected override string Instructions => """
        You are a helpful weather assistant. Call get_weather once for every city the user mentions. Then answer in one
        or two short sentences, such as which city is warmest; the card already shows each city's details.
        """;

    protected override IEnumerable<AITool> Tools => [tools.GetWeather];

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory) =>
        new(client, options => options.AddBlockHandler(new WeatherComparisonHandler()), loggerFactory);
}

/// <summary>
/// Human in the loop: <c>book_meeting</c> and <c>send_email</c> always ask before they run. The built-in approval card
/// asks for the email, and a custom approval template shows the meeting.
/// </summary>
public sealed class HumanInTheLoopScenario : PlaygroundScenario
{
    /// <summary>The name of the meeting tool, which has its own approval card.</summary>
    public const string BookMeetingName = "book_meeting";

    /// <summary>The name of the email tool, which uses the built-in approval card.</summary>
    public const string SendEmailName = "send_email";

    private int _bookings;

    /// <summary>Initializes the scenario and its tools.</summary>
    public HumanInTheLoopScenario()
    {
        BookMeeting = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            BookMeetingCore,
            new AIFunctionFactoryOptions
            {
                Name = BookMeetingName,
                Description = "Books a meeting on the user's calendar.",
                SerializerOptions = ScenarioJson.Options,
            }));
        SendEmail = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(
            SendEmailCore,
            new AIFunctionFactoryOptions
            {
                Name = SendEmailName,
                Description = "Sends an email on the user's behalf.",
                SerializerOptions = ScenarioJson.Options,
            }));
    }

    /// <summary>Gets <c>book_meeting</c>, which needs approval.</summary>
    public AIFunction BookMeeting { get; }

    /// <summary>Gets <c>send_email</c>, which needs approval.</summary>
    public AIFunction SendEmail { get; }

    public override string Id => "human-in-the-loop";
    public override string Title => "Human in the loop";
    public override string Glyph => "\U0001F64B";
    public override string Summary => "Approve or reject before a tool runs";

    public override string Description =>
        "book_meeting and send_email are wrapped in ApprovalRequiredAIFunction, so each call waits for you. The email " +
        "uses the built-in approval card; the meeting uses a custom approval template that shows its details.";

    public override string Placeholder => "Ask to book a meeting or send an email\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "Book a 30 minute design review with Ana and Ken tomorrow at 10:00",
        "Email the team that the demo moved to Friday",
    ];

    protected override string Instructions => """
        You are a helpful scheduling assistant.

        - When the user asks to schedule something, call book_meeting right away; when they ask to send an email, call
          send_email. Never ask for confirmation in text: the app asks the user to approve each call.
        - Invent sensible details that the user did not give, such as a title or an email subject.
        - If a call comes back rejected, the user declined it. Acknowledge that in one short sentence and stop. Do not
          call the tool again for the same request.
        """;

    protected override IEnumerable<AITool> Tools => [BookMeeting, SendEmail];

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory) =>
        new(client, configure: null, loggerFactory);

    private MeetingBooking BookMeetingCore(
        [Description("A short title for the meeting.")] string title,
        [Description("When it starts, such as 'Tuesday 10:00'.")] string start,
        [Description("How long it lasts, in minutes.")] int durationMinutes,
        [Description("The people to invite.")] List<string> attendees) =>
        new()
        {
            Title = title,
            Start = start,
            DurationMinutes = durationMinutes,
            Attendees = attendees,
            Confirmation = $"MTG-{1000 + Interlocked.Increment(ref _bookings)}",
        };

    private static string SendEmailCore(
        [Description("The recipients.")] List<string> to,
        [Description("The subject line.")] string subject,
        [Description("The message.")] string body) =>
        $"Sent \u201C{subject}\u201D to {string.Join(", ", to)}.";
}

/// <summary>The result of <c>book_meeting</c>.</summary>
public sealed class MeetingBooking
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("durationMinutes")]
    public int DurationMinutes { get; set; }

    [JsonPropertyName("attendees")]
    public List<string> Attendees { get; set; } = [];

    [JsonPropertyName("confirmation")]
    public string Confirmation { get; set; } = string.Empty;
}

/// <summary>Changes the playground's look on behalf of the frontend-tools scenario.</summary>
public interface IAccentColorSurface
{
    /// <summary>Applies an accent color.</summary>
    /// <param name="color">A CSS color name or a hex color.</param>
    /// <returns>The color applied, as <c>#RRGGBB</c>, or <see langword="null"/> when it is not a color.</returns>
    string? ApplyAccentColor(string color);

    /// <summary>Goes back to the theme's accent color.</summary>
    void ResetAccentColor();
}

/// <summary>
/// Frontend tools: UI actions run in the app, not by the model's client. <c>set_accent_color</c> runs as soon as the
/// model calls it and recolors the scenario; <c>reset_accent_color</c> is a manual action that waits for the user to
/// run it.
/// </summary>
public sealed class FrontendToolsScenario(IAccentColorSurface surface) : PlaygroundScenario
{
    /// <summary>The name of the automatic UI action.</summary>
    public const string SetAccentColorName = "set_accent_color";

    /// <summary>The name of the manual UI action.</summary>
    public const string ResetAccentColorName = "reset_accent_color";

    public override string Id => "frontend-tools";
    public override string Title => "Frontend tools";
    public override string Glyph => "\U0001F3A8";
    public override string Summary => "UI actions that run in the app";

    public override string Description =>
        "options.RegisterUIAction adds tools that run in the app instead of the model's client. set_accent_color runs " +
        "as soon as the model calls it and recolors this scenario through AIChat.Color.Accent; reset_accent_color is " +
        "a manual action that waits for you to run it.";

    public override string Placeholder => "Ask for a different accent color\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "Change the accent color to rebeccapurple",
        "Make it feel like a sunset",
        "Reset the colors",
    ];

    protected override string Instructions => """
        You are a helpful assistant that can restyle the app. Call set_accent_color to change the accent color; it
        accepts a CSS color name or a hex color. To go back to the default, call reset_accent_color: the user runs it.
        Reply in one short sentence.
        """;

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory) =>
        new(client, options =>
        {
            options.RegisterUIAction(AIFunctionFactory.Create(
                ([Description("A CSS color name, such as rebeccapurple, or a hex color, such as #FF6B35.")] string color) =>
                    surface.ApplyAccentColor(color) is { } applied
                        ? $"The accent color is now {applied}."
                        : $"\u201C{color}\u201D is not a color. Use a CSS color name or a hex color.",
                new AIFunctionFactoryOptions
                {
                    Name = SetAccentColorName,
                    Description = "Sets the accent color of the app.",
                    SerializerOptions = ScenarioJson.Options,
                }));
            options.RegisterUIAction(
                AIFunctionFactory.Create(
                    () =>
                    {
                        surface.ResetAccentColor();
                        return "The accent color is back to the default.";
                    },
                    new AIFunctionFactoryOptions
                    {
                        Name = ResetAccentColorName,
                        Description = "Resets the app's accent color to the default. The user decides when it runs.",
                        SerializerOptions = ScenarioJson.Options,
                    }),
                UIActionInvocationMode.Manual);
        }, loggerFactory);
}

/// <summary>
/// Dynamic activity progress: an <see cref="ActivityHandler{TBlock}"/> turns the plan's tool calls into one in-chat
/// activity that updates in place, and a state mapper keeps <see cref="PlanState"/> as the agent's typed state for the
/// panel beside the chat.
/// </summary>
public sealed class PlanProgressScenario(PlanTools tools) : PlaygroundScenario
{
    /// <summary>Gets the typed agent, whose state is the plan.</summary>
    public UIAgent<PlanState> PlanAgent => (UIAgent<PlanState>)Agent;

    public override string Id => "plan-progress";
    public override string Title => "Activity progress";
    public override string Glyph => "\U0001F4CB";
    public override string Summary => "A plan that updates in place as the agent works";

    public override string Description =>
        "An ActivityHandler maps create_plan and update_plan_step into one PlanActivityBlock that updates in place, " +
        "so no tool cards appear. A state mapper keeps the plan as the agent's typed state (UIAgent<PlanState>), " +
        "shown in the panel.";

    public override string Placeholder => "Ask the agent to plan something\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "Plan a weekend trip to Lisbon",
        "Plan a birthday party in five steps",
    ];

    protected override bool? AllowMultipleToolCalls => false;

    protected override string Instructions => """
        When planning, use the tools only, without other messages.
        - Use create_plan to set out three to six short steps.
        - Then work through the steps in order: call update_plan_step with status completed for each step, one at a
          time, until every step is completed. Do not leave a plan unfinished.
        - Do not repeat or summarize the plan in a message, and do not ask the user anything.
        - When every step is completed, reply with one short sentence.
        Only one plan can be active at a time.
        """;

    protected override IEnumerable<AITool> Tools => [tools.CreatePlan, tools.UpdatePlanStep];

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory)
    {
        var mapper = new PlanStateMapper();
        UIAgent<PlanState>? agent = null;
        agent = new UIAgent<PlanState>(client, options =>
        {
            options.StateMapper = context => mapper.Map(context, agent!.State.Value);
            options.AddBlockHandler(new PlanActivityHandler(() => agent!.State.Value));
        }, loggerFactory);
        return agent;
    }
}

/// <summary>Reasoning: the built-in view shows the model's reasoning summary above its answer.</summary>
public sealed class ReasoningScenario : PlaygroundScenario
{
    public override string Id => "reasoning";
    public override string Title => "Reasoning";
    public override string Glyph => "\U0001F4A1";
    public override string Summary => "The model's reasoning summary, then its answer";
    public override bool RequiresToolCalling => false;
    public override bool UsesReasoning => true;

    public override string Description =>
        "Requests ask for a reasoning summary (ChatOptions.Reasoning), so a reasoning model, such as one behind the " +
        "Azure OpenAI Responses API, streams its thinking before the answer. The built-in reasoning view shows it, " +
        "collapsible, above the answer.";

    public override string Placeholder => "Ask something that takes some thought\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "A bat and a ball cost $1.10 together. The bat costs $1.00 more than the ball. How much is the ball?",
        "Which is larger, 9.11 or 9.9? Explain briefly.",
    ];

    protected override string Instructions =>
        "You are a careful assistant. Think the problem through, then answer in a few plain sentences.";

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory) =>
        new(client, configure: null, loggerFactory);
}

/// <summary>
/// Conversation persistence: a <see cref="FileConversationThread"/> saves every committed turn to a file, and the
/// scenario restores it when it opens, so the conversation survives quitting the app.
/// </summary>
public sealed class PersistenceScenario(ScenarioTools tools, string threadPath) : PlaygroundScenario
{
    /// <summary>Gets the thread that the current agent saves to.</summary>
    public FileConversationThread? Thread { get; private set; }

    /// <summary>Gets the file the conversation is saved in.</summary>
    public string ThreadPath { get; } = threadPath;

    public override string Id => "persistence";
    public override string Title => "Persistence";
    public override string Glyph => "\U0001F4BE";
    public override string Summary => "A conversation that survives restarting the app";

    public override string Description =>
        "options.Thread is a file-backed IConversationThread that saves each committed turn's raw updates as JSON. " +
        "Opening the scenario calls RestoreAsync(), so the conversation, its tool cards included, comes back after " +
        "you quit the app. New clears the thread.";

    public override string Placeholder => "Say something worth remembering\u2026";

    public override IReadOnlyList<string> Suggestions =>
    [
        "What's the weather in Reykjavik?",
        "Remember that my favorite color is teal",
    ];

    protected override string Instructions => """
        You are a helpful assistant with a long memory. Use get_weather for weather questions and get_local_time for the
        time in a place. Keep answers short.
        """;

    protected override IEnumerable<AITool> Tools => [tools.GetWeather, tools.GetLocalTime];

    protected override UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory)
    {
        // A new agent reads the file again, as the app does when it starts.
        Thread = new FileConversationThread(ThreadPath);
        var thread = Thread;
        return new UIAgent(client, options =>
        {
            options.Thread = thread;
            options.AddGeneratedToolBlocks();
        }, loggerFactory);
    }
}
