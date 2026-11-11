using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// One agentic scenario of the playground: the agent it builds, with its tools, block handlers, UI actions and state,
/// and the options it sends with every request. An instance belongs to one conversation.
/// </summary>
/// <remarks>
/// Scenarios talk to the model through the playground's router client, which sends each request to the selected client
/// with the options <see cref="CreateRequestOptions"/> builds and the messages <see cref="PrepareMessages"/> returns for
/// that request. The agent's own options stay empty, so every request carries the scenario's current instructions and
/// tools, and its current state, such as a recipe the user edited.
/// </remarks>
public abstract class PlaygroundScenario
{
    private UIAgent? _agent;

    /// <summary>Gets the scenario's stable identifier, used for its recordings and saved files.</summary>
    public abstract string Id { get; }

    /// <summary>Gets the scenario's name.</summary>
    public abstract string Title { get; }

    /// <summary>Gets the emoji shown next to the name.</summary>
    public abstract string Glyph { get; }

    /// <summary>Gets one line that says what the scenario shows.</summary>
    public abstract string Summary { get; }

    /// <summary>Gets what the scenario demonstrates and how it is built.</summary>
    public abstract string Description { get; }

    /// <summary>Gets the composer's placeholder.</summary>
    public virtual string Placeholder => "Message\u2026";

    /// <summary>Gets prompts that show off the scenario.</summary>
    public virtual IReadOnlyList<string> Suggestions => [];

    /// <summary>Gets whether the scenario needs a model that can call tools.</summary>
    public virtual bool RequiresToolCalling => true;

    /// <summary>Gets whether the scenario asks the model for a reasoning summary.</summary>
    public virtual bool UsesReasoning => false;

    /// <summary>Gets the agent, once <see cref="CreateAgent"/> created it.</summary>
    public UIAgent Agent => _agent ?? throw new InvalidOperationException("Create the scenario's agent first.");

    /// <summary>
    /// Gets whether the model may ask for several tool calls at once; <see langword="null"/> leaves it to the provider.
    /// </summary>
    protected virtual bool? AllowMultipleToolCalls => null;

    /// <summary>Gets the system instructions sent with every request.</summary>
    protected abstract string Instructions { get; }

    /// <summary>Gets the tools that the selected client's function invocation runs.</summary>
    protected virtual IEnumerable<AITool> Tools => [];

    /// <summary>Creates the agent for a new conversation. Call it again to start over with a new agent.</summary>
    /// <param name="client">The client the agent sends its requests to.</param>
    /// <param name="loggerFactory">Logs the engine's work.</param>
    public UIAgent CreateAgent(IChatClient client, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(client);
        _agent = CreateAgentCore(client, loggerFactory);
        return _agent;
    }

    /// <summary>
    /// Builds the options for one live request: the scenario's instructions and tools, the declarations of the
    /// agent's UI actions, and the provider conversation that a continuation belongs to.
    /// </summary>
    /// <param name="engineOptions">The options the engine sent, which carry its UI actions and conversation id.</param>
    /// <param name="client">What the selected client can do.</param>
    /// <exception cref="NotSupportedException">The scenario needs tools, but the selected client cannot call them.</exception>
    public ChatOptions CreateRequestOptions(ChatOptions? engineOptions, ScenarioClientCapabilities client)
    {
        var tools = Tools.Concat(engineOptions?.Tools ?? []).ToList();
        if (tools.Count > 0 && !client.SupportsToolCalling)
        {
            throw new NotSupportedException(
                $"\u201C{Title}\u201D needs a model that can call tools. Select a tool-capable client, such as Azure OpenAI.");
        }

        var options = new ChatOptions
        {
            ModelId = client.ModelId,
            ConversationId = engineOptions?.ConversationId,
            Instructions = Instructions,
            Tools = tools.Count > 0 ? tools : null,
            AllowMultipleToolCalls = tools.Count > 0 ? AllowMultipleToolCalls : null,
        };
        if (UsesReasoning && client.SupportsReasoning)
        {
            options.Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.Medium,
                Output = ReasoningOutput.Summary,
            };
        }

        return options;
    }

    /// <summary>
    /// Prepares the messages of one live request. A scenario whose state the model must see adds it just before the
    /// user's latest message, as data rather than instructions: the model then reads the state the user sees now,
    /// including the user's own edits, rather than a version from earlier in the conversation.
    /// </summary>
    /// <param name="messages">The messages the engine sends.</param>
    /// <returns>The messages to send.</returns>
    public IList<ChatMessage> PrepareMessages(IList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Only a new user message: a continuation that answers calls must keep each answer right after its call.
        if (messages.Count == 0 || !IsUserMessage(messages[^1]) || GetStateMessage() is not { } state)
            return messages;

        var prepared = new List<ChatMessage>(messages);
        prepared.Insert(prepared.Count - 1, PlaygroundChatClient.CreateRequestContext(state));
        return prepared;
    }

    /// <summary>
    /// Gets the state the model must see with the user's next message, or <see langword="null"/> when the scenario has
    /// none.
    /// </summary>
    protected virtual string? GetStateMessage() => null;

    /// <summary>Creates the scenario's agent, registering its handlers, UI actions, state mapper and thread.</summary>
    protected abstract UIAgent CreateAgentCore(IChatClient client, ILoggerFactory? loggerFactory);

    private static bool IsUserMessage(ChatMessage message) =>
        message.Role == ChatRole.User &&
        message.Contents.Count > 0 &&
        message.Contents.All(static content => content is TextContent or DataContent or UriContent);
}

/// <summary>What the client selected for a scenario can do.</summary>
/// <param name="ModelId">The model to request, or <see langword="null"/> for the client's default.</param>
/// <param name="SupportsToolCalling">Whether the client can call tools.</param>
/// <param name="SupportsReasoning">Whether the client can return a reasoning summary.</param>
public readonly record struct ScenarioClientCapabilities(string? ModelId, bool SupportsToolCalling, bool SupportsReasoning);
