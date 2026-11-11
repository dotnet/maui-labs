using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The one <see cref="IChatClient"/> the chat engine talks to. It forwards each request to the client selected in
/// settings, with options built from the settings for that request, and adds what the request alone carries, such as
/// the shared notes.
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
    /// as the current notes.
    /// </summary>
    public const string RequestContextKey = "chatPlayground.requestContext";

    private readonly Func<IChatClient?> _selectedClient;
    private readonly SettingsPaneViewModel _settings;
    private readonly PlaygroundTools _tools;
    private readonly Func<string?>? _requestContext;

    /// <summary>Initializes the router.</summary>
    /// <param name="selectedClient">Returns the client selected in settings, or <see langword="null"/> when none is.</param>
    /// <param name="settings">The settings that shape each request.</param>
    /// <param name="tools">The local tools the settings can enable.</param>
    /// <param name="requestContext">
    /// Returns what each request tells the model besides the conversation, such as the current notes, or
    /// <see langword="null"/> for nothing. It goes just before the user's latest message, in that request only.
    /// </param>
    public PlaygroundChatClient(
        Func<IChatClient?> selectedClient,
        SettingsPaneViewModel settings,
        PlaygroundTools tools,
        Func<string?>? requestContext = null)
    {
        ArgumentNullException.ThrowIfNull(selectedClient);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tools);
        _selectedClient = selectedClient;
        _settings = settings;
        _tools = tools;
        _requestContext = requestContext;
    }

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

        var client = _selectedClient()
            ?? throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(_settings.ClientStatus) ? "Select a chat client first." : _settings.ClientStatus);
        var descriptor = client.GetService<ChatClientDescriptor>()
            ?? throw new InvalidOperationException("The selected chat client did not expose a ChatClientDescriptor.");
        ThrowIfImagesUnsupported(request, descriptor);

        var requestOptions = CreateRequestOptions(options, isFollowUp: request.Count > 0 && IsContinuation(request[^1]));
        request = AddRequestContext(request);
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
    /// Creates the tools the settings enable for the selected client: the local tools, which run on this device, and
    /// the provider's image generation. With "Ask before running tools" on, each local tool asks for approval before
    /// it runs.
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

    /// <summary>
    /// Creates the declarations of the engine's UI actions for the selected client, when it hands tool calls back to
    /// the app, which runs them; otherwise none.
    /// </summary>
    /// <param name="engineTools">
    /// The declarations of the engine's UI actions. An action registered as an <see cref="ApprovalRequiredAIFunction"/>
    /// asks only while "Ask before running tools" is on; otherwise it is declared as a plain function, so it runs at
    /// once.
    /// </param>
    public IList<AITool> CreateUIActions(IEnumerable<AITool>? engineTools)
    {
        if (!_settings.SupportsUIActions || engineTools is null)
            return [];

        return engineTools
            .Select(tool => !_settings.RequireToolApproval &&
                tool is AIFunctionDeclaration declaration &&
                tool.GetService<ApprovalRequiredAIFunction>() is not null
                    ? AIFunctionFactory.CreateDeclaration(
                        declaration.Name, declaration.Description, declaration.JsonSchema, declaration.ReturnJsonSchema)
                    : tool)
            .ToList();
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
        return _selectedClient()?.GetService(serviceType, serviceKey)
            ?? (serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The clients belong to the app's service container.
    }

    // The settings cannot change while a response runs, so the requests that continue it, after a tool result or an
    // approval, get the same tools. Within a turn the engine continues the provider's conversation, when it keeps one.
    // A required tool mode applies to the turn's first request only, as the engine and FunctionInvokingChatClient
    // apply it: a request that sends results lets the model answer them, where requiring a tool again would make an
    // automatic UI action loop.
    private ChatOptions CreateRequestOptions(ChatOptions? engineOptions, bool isFollowUp)
    {
        var options = _settings.CreateChatOptions(CreateEnabledTools(), CreateUIActions(engineOptions?.Tools));
        options.ConversationId = engineOptions?.ConversationId;
        if (isFollowUp && options.ToolMode is RequiredChatToolMode)
            options.ToolMode = null;
        return options;
    }

    private IList<ChatMessage> AddRequestContext(IList<ChatMessage> request)
    {
        if (_requestContext?.Invoke() is not { Length: > 0 } context)
            return request;

        // Just before what the user last sent, so a continuation still answers each call right after it.
        for (var index = request.Count - 1; index >= 0; index--)
        {
            if (request[index].Role == ChatRole.User && !IsContinuation(request[index]))
            {
                var prepared = new List<ChatMessage>(request);
                prepared.Insert(index, CreateRequestContext(context));
                return prepared;
            }
        }

        return request;
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
