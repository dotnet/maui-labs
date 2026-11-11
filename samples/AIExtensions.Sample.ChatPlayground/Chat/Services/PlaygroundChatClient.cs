using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The one <see cref="IChatClient"/> the chat engine talks to. It forwards each request to the client selected in
/// settings, with options built from the settings for that request, or to the replay client while a recording is
/// replayed.
/// </summary>
/// <remarks>
/// The engine keeps a single conversation for the whole page, so switching clients, tools, or streaming in
/// settings applies to the next request without starting a new conversation. Requests that the selected client
/// cannot accept throw before anything is sent; the engine then shows the failed turn with Retry.
/// </remarks>
public sealed class PlaygroundChatClient : IChatClient
{
    private readonly Func<IChatClient?> _selectedClient;
    private readonly IChatClient _replayClient;
    private readonly SettingsPaneViewModel _settings;
    private readonly PlaygroundTools _tools;
    private ChatOptions? _lastLiveOptions;

    /// <summary>Initializes the router.</summary>
    /// <param name="selectedClient">Returns the client selected in settings, or <see langword="null"/> when none is.</param>
    /// <param name="replayClient">The client that answers from the current recording.</param>
    /// <param name="settings">The settings that shape each live request.</param>
    /// <param name="tools">The local tools the settings can enable.</param>
    public PlaygroundChatClient(
        Func<IChatClient?> selectedClient,
        IChatClient replayClient,
        SettingsPaneViewModel settings,
        PlaygroundTools tools)
    {
        ArgumentNullException.ThrowIfNull(selectedClient);
        ArgumentNullException.ThrowIfNull(replayClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);
        _selectedClient = selectedClient;
        _replayClient = replayClient;
        _settings = settings;
        _tools = tools;
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

        var requestOptions = CreateRequestOptions(request);
        LiveRequestStarting?.Invoke(this, requestOptions);

        if (!_settings.UseStreaming)
        {
            var response = await client.GetResponseAsync(request, requestOptions, cancellationToken);
            foreach (var update in response.ToChatResponseUpdates())
                yield return update;
            yield break;
        }

        await foreach (var update in client.GetStreamingResponseAsync(request, requestOptions, cancellationToken)
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
        if (!_settings.SupportsToolCalling)
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

    private ChatOptions CreateRequestOptions(IList<ChatMessage> request)
    {
        // An approval or tool-result continuation belongs to the request that asked for it, so it keeps its tools.
        if (_lastLiveOptions is { } previous && request.Count > 0 && IsContinuation(request[^1]))
            return previous;

        var options = _settings.CreateChatOptions(CreateEnabledTools());
        _lastLiveOptions = options;
        return options;
    }

    private static bool IsContinuation(ChatMessage message) =>
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
