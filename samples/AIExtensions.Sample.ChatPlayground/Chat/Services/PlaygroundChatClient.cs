using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The one <see cref="IChatClient"/> the chat engine talks to. It forwards each request to the client selected in
/// settings, with options built for that request, or to the replay client while a recording is replayed, and records
/// each live exchange.
/// </summary>
/// <remarks>
/// The engine keeps a single conversation for the whole page, so switching clients, tools, or streaming in
/// settings applies to the next request without starting a new conversation. Requests that the selected client
/// cannot accept throw before anything is sent; the engine then shows the failed turn with Retry.
/// </remarks>
public sealed class PlaygroundChatClient : IChatClient
{
    /// <summary>
    /// The key of <see cref="ChatMessage.AdditionalProperties"/> that marks a message added to one request only, such
    /// as a scenario's current state. Recordings keep the mark, so a replay leaves the message out of the conversation
    /// it shows.
    /// </summary>
    public const string RequestContextKey = "chatPlayground.requestContext";

    private readonly Func<IChatClient?> _selectedClient;
    private readonly IChatClient _replayClient;
    private readonly SettingsPaneViewModel _settings;
    private readonly PlaygroundTools? _tools;
    private readonly Func<ChatOptions?, ChatOptions> _createOptions;
    private readonly IChatRecordingSession? _recording;
    private readonly Func<IList<ChatMessage>, IList<ChatMessage>>? _prepareMessages;
    private readonly bool _reuseOptionsForContinuations;
    private ChatOptions? _lastLiveOptions;

    /// <summary>Initializes the router for the chat tab, whose requests use the settings and their tools.</summary>
    /// <param name="selectedClient">Returns the client selected in settings, or <see langword="null"/> when none is.</param>
    /// <param name="replayClient">The client that answers from the current recording.</param>
    /// <param name="settings">The settings that shape each live request.</param>
    /// <param name="tools">The local tools the settings can enable.</param>
    /// <param name="recording">Records each live exchange, or <see langword="null"/> to record nothing.</param>
    public PlaygroundChatClient(
        Func<IChatClient?> selectedClient,
        IChatClient replayClient,
        SettingsPaneViewModel settings,
        PlaygroundTools tools,
        IChatRecordingSession? recording = null)
        : this(selectedClient, replayClient, settings, recording, tools, createOptions: null)
    {
    }

    /// <summary>Initializes a router whose requests use options that <paramref name="createOptions"/> builds.</summary>
    /// <param name="selectedClient">Returns the live client to send to, or <see langword="null"/> when none is selected.</param>
    /// <param name="replayClient">The client that answers from the current recording.</param>
    /// <param name="settings">The settings that choose the client and whether responses stream.</param>
    /// <param name="createOptions">
    /// Builds the options for each live request from the options the engine sent, which carry the declarations of its
    /// UI actions and, for a continuation, the provider's conversation id. It is called again for every request round.
    /// </param>
    /// <param name="recording">Records each live exchange, or <see langword="null"/> to record nothing.</param>
    /// <param name="prepareMessages">
    /// Returns the messages to send for each live request, for example with the scenario's current state added as
    /// <see cref="CreateRequestContext"/> messages; <see langword="null"/> sends the engine's messages as they are.
    /// </param>
    public PlaygroundChatClient(
        Func<IChatClient?> selectedClient,
        IChatClient replayClient,
        SettingsPaneViewModel settings,
        Func<ChatOptions?, ChatOptions> createOptions,
        IChatRecordingSession? recording = null,
        Func<IList<ChatMessage>, IList<ChatMessage>>? prepareMessages = null)
        : this(selectedClient, replayClient, settings, recording, tools: null,
            createOptions ?? throw new ArgumentNullException(nameof(createOptions)))
    {
        _prepareMessages = prepareMessages;
    }

    private PlaygroundChatClient(
        Func<IChatClient?> selectedClient,
        IChatClient replayClient,
        SettingsPaneViewModel settings,
        IChatRecordingSession? recording,
        PlaygroundTools? tools,
        Func<ChatOptions?, ChatOptions>? createOptions)
    {
        ArgumentNullException.ThrowIfNull(selectedClient);
        ArgumentNullException.ThrowIfNull(replayClient);
        ArgumentNullException.ThrowIfNull(settings);
        if (createOptions is null)
            ArgumentNullException.ThrowIfNull(tools);
        _selectedClient = selectedClient;
        _replayClient = replayClient;
        _settings = settings;
        _tools = tools;
        _recording = recording;

        // The chat tab keeps a continuation on the options of the request that asked for it, even when the settings
        // changed in between; a scenario builds them again, so they carry its current state.
        _reuseOptionsForContinuations = createOptions is null;
        _createOptions = createOptions ?? (_ => _settings.CreateChatOptions(CreateEnabledTools()));
    }

    /// <summary>
    /// Gets or sets whether requests are answered from the recording. Replay mode is independent of the selected
    /// client, so a recording restored at startup replays even while a live client is selected.
    /// </summary>
    public bool IsReplaying { get; set; }

    /// <summary>Raised with the options built for a live request, before it is sent.</summary>
    public event EventHandler<ChatOptions>? LiveRequestStarting;

    /// <summary>Gets the client that receives the next request.</summary>
    public IChatClient? ActiveClient => IsReplaying ? _replayClient : _selectedClient();

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken)
            .ToChatResponseAsync(cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var request = messages as IList<ChatMessage> ?? messages.ToList();
        ClearProviderMetadata(request);

        if (IsReplaying)
        {
            // The replay client answers with the next recorded response and ignores what it is sent.
            await foreach (var update in _replayClient.GetStreamingResponseAsync(request, options, cancellationToken)
                .WithCancellation(cancellationToken))
            {
                yield return update;
            }

            yield break;
        }

        var client = _selectedClient()
            ?? throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(_settings.ClientStatus) ? "Select a chat client first." : _settings.ClientStatus);
        var descriptor = client.GetService<ChatClientDescriptor>()
            ?? throw new InvalidOperationException("The selected chat client did not expose a ChatClientDescriptor.");
        if (descriptor.IsReplay)
            throw new InvalidOperationException("Replay is read-only. Use Play all or Next turn.");
        ThrowIfImagesUnsupported(request, descriptor);

        var requestOptions = CreateRequestOptions(request, options);
        LiveRequestStarting?.Invoke(this, requestOptions);
        request = _prepareMessages?.Invoke(request) ?? request;

        // Recorded around the selected client's whole pipeline, so the recording holds the exchange the engine saw:
        // the calls its function invocation ran and their results.
        var target = _recording is null ? client : new RecordingChatClient(client, _recording);
        if (!_settings.UseStreaming)
        {
            var response = await target.GetResponseAsync(request, requestOptions, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
            yield break;
        }

        await foreach (var update in target.GetStreamingResponseAsync(request, requestOptions, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    /// <summary>
    /// Creates the tools the settings enable for the selected client. With "Ask before running tools" on, each
    /// function is wrapped so the chat asks for approval before it runs.
    /// </summary>
    public IList<AITool> CreateEnabledTools()
    {
        var tools = new List<AITool>();
        if (_tools is null || !_settings.SupportsToolCalling)
            return tools;
        if (_settings.UseDateTimeTool)
            tools.Add(_tools.CurrentLocalDateTime);
        if (_settings.UseCalculatorTool)
            tools.Add(_tools.Calculator);
        if (_settings.UseConnectionStatusTool)
            tools.Add(_tools.ConnectionStatus);
        if (_settings.RequireToolApproval)
        {
            for (var index = 0; index < tools.Count; index++)
            {
                if (tools[index] is AIFunction function)
                    tools[index] = new ApprovalRequiredAIFunction(function);
            }
        }
        if (_settings.UseImageGenerationTool && _settings.SelectedDescriptor?.SupportsImageGeneration == true)
            tools.Add(new HostedImageGenerationTool());
        return tools;
    }

    /// <summary>Creates a message that only the request it is added to carries, marked with <see cref="RequestContextKey"/>.</summary>
    /// <param name="text">What the model reads.</param>
    public static ChatMessage CreateRequestContext(string text) =>
        new(ChatRole.User, text) { AdditionalProperties = new() { [RequestContextKey] = true } };

    /// <summary>Gets whether a message was added to one request only, by <see cref="CreateRequestContext"/>.</summary>
    public static bool IsRequestContext(ChatMessage message) =>
        message.AdditionalProperties?.ContainsKey(RequestContextKey) == true;

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return ActiveClient?.GetService(serviceType, serviceKey)
            ?? (serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The clients belong to the app's service container.
    }

    private ChatOptions CreateRequestOptions(IList<ChatMessage> request, ChatOptions? engineOptions)
    {
        // An approval or tool-result continuation belongs to the request that asked for it, so it keeps its tools.
        if (_reuseOptionsForContinuations && _lastLiveOptions is { } previous && request.Count > 0 &&
            IsContinuation(request[^1]))
        {
            return previous;
        }

        var options = _createOptions(engineOptions);
        _lastLiveOptions = options;
        return options;
    }

    /// <summary>Gets whether a request message only answers approvals or tool calls, continuing an earlier request.</summary>
    internal static bool IsContinuation(ChatMessage message) =>
        message.Contents.Count > 0 &&
        message.Contents.All(static content => content is ToolApprovalResponseContent or FunctionResultContent);

    private static void ThrowIfImagesUnsupported(IList<ChatMessage> request, ChatClientDescriptor descriptor)
    {
        if (descriptor.SupportsImageInput || request.Count == 0)
            return;

        if (request.Take(request.Count - 1).Any(ContainsImage))
        {
            throw new NotSupportedException($"This chat contains images. {descriptor.DisplayName} cannot send them; " +
                "clear the chat or switch to an image-capable client.");
        }

        if (ContainsImage(request[^1]))
        {
            throw new NotSupportedException($"{descriptor.DisplayName} does not support image input. " +
                "Switch to an image-capable client or remove the image.");
        }
    }

    private static bool ContainsImage(ChatMessage message) =>
        message.Contents.Any(static content => content is DataContent ||
            content is ImageGenerationToolResultContent { Outputs: { } outputs } &&
                outputs.OfType<DataContent>().Any());

    private static void ClearProviderMetadata(IEnumerable<ChatMessage> messages)
    {
        // Image-tool responses can repeat native reasoning items; only portable content belongs in subsequent requests.
        foreach (var message in messages)
        {
            message.RawRepresentation = null;
            foreach (var content in message.Contents)
            {
                content.RawRepresentation = null;
                if (content is ImageGenerationToolResultContent { Outputs: { } outputs })
                {
                    foreach (var output in outputs)
                        output.RawRepresentation = null;
                }
            }
        }
    }
}
