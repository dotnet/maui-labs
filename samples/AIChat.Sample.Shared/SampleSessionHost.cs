using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Presentation;
using Microsoft.Maui.AI.Chat.Recording;
using Microsoft.Maui.Chat;

namespace AIChat.Sample.Shared;

/// <summary>
/// Owns the single session/presentation/composer trio shared by the native and Razor chat surfaces.
/// </summary>
public sealed class SampleSessionHost : IDisposable
{
    private readonly Func<string, ChatSampleMode, IChatClient> _clientFactory;
    private readonly Action<UIAgentOptions, string> _configureAgent;
    private readonly Action<ChatRecording, string>? _recordingCompleted;
    private readonly Func<string, string?> _recordingPathFactory;
    private readonly IChatAttachmentPicker? _attachmentPicker;
    private readonly IChatAudioRecorder? _audioRecorder;
    private readonly IChatSpeechRecognizer? _speechRecognizer;
    private IChatClient? _client;
    private IDisposable? _statusRegistration;
    private bool _disposed;

    public SampleSessionHost(
        ChatSampleMode mode,
        Func<string, ChatSampleMode, IChatClient> clientFactory,
        Action<UIAgentOptions, string>? configureAgent = null,
        Action<ChatRecording, string>? recordingCompleted = null,
        Func<string, string?>? recordingPathFactory = null,
        IChatAttachmentPicker? attachmentPicker = null,
        IChatAudioRecorder? audioRecorder = null,
        IChatSpeechRecognizer? speechRecognizer = null,
        SampleUiState? state = null,
        string initialScenario = "basic")
    {
        Mode = mode;
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _configureAgent = configureAgent ?? ((_, _) => { });
        _recordingCompleted = recordingCompleted;
        _recordingPathFactory = recordingPathFactory ?? (_ => null);
        _attachmentPicker = attachmentPicker;
        _audioRecorder = audioRecorder;
        _speechRecognizer = speechRecognizer;
        State = state ?? new SampleUiState();
        ReplaceSession(initialScenario);
    }

    public ChatSampleMode Mode { get; }
    public string ScenarioId { get; private set; } = "basic";
    public AgentContext Session { get; private set; } = null!;
    public AgentChatPresentation Presentation { get; private set; } = null!;
    public ChatComposerController ComposerController { get; private set; } = null!;
    public SampleUiState State { get; }
    public string Status => Session.Status.ToString();
    public JsonObject? SanitizerManifest { get; private set; }
    public string? RecordingPath { get; private set; }
    public string? RecordingError { get; private set; }
    public event Action? Changed;

    public bool ReplaceSession(string scenarioId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(scenarioId))
            throw new ArgumentException("A scenario identifier is required.", nameof(scenarioId));
        if (Session is not null && Session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput)
        {
            RecordingError = "Finish or cancel the active conversation before changing scenarios.";
            Changed?.Invoke();
            return false;
        }

        IChatClient? client = null;
        AgentContext? session = null;
        AgentChatPresentation? presentation = null;
        ChatComposerController? composer = null;
        try
        {
            client = _clientFactory(scenarioId, Mode);
            var agent = new UIAgent(client, options =>
            {
                options.AddBlockHandler(new DocumentProposalHandler(State));
                _configureAgent(options, scenarioId);
            });
            session = new AgentContext(agent);
            presentation = new AgentChatPresentation(session);
            composer = new ChatComposerController
            {
                Conversation = presentation,
                AllowAttachments = true,
                AllowAudioCapture = true,
                AllowLiveSpeech = true,
                AttachmentPicker = _attachmentPicker,
                AudioRecorder = _audioRecorder,
                SpeechRecognizer = _speechRecognizer,
            };
        }
        catch
        {
            composer?.Dispose();
            presentation?.Dispose();
            session?.Dispose();
            client?.Dispose();
            throw;
        }

        var recordingError = DisposeCurrent();
        _client = client;
        Session = session;
        Presentation = presentation;
        ComposerController = composer;
        ScenarioId = scenarioId;
        RecordingError = recordingError;
        State.Reset();
        _statusRegistration = Session.RegisterOnStatusChanged(_ => Changed?.Invoke());
        SanitizerManifest = client is RecordingChatClient recording ? recording.Session.Recording.Manifest : null;
        Changed?.Invoke();
        return true;
    }

    public void Clear() => Session.Clear();

    public Task RetryAsync(CancellationToken cancellationToken = default) =>
        Session.RetryAsync(cancellationToken);

    /// <summary>Sets the explicit predictive-decision argument and invokes the pending UI action once.</summary>
    public async Task ResolveDocumentProposalAsync(bool accepted, CancellationToken cancellationToken = default)
    {
        var action = Session.Turns
            .SelectMany(static turn => turn.ResponseBlocks)
            .OfType<UIActionBlock>()
            .LastOrDefault(block => block.Mode == UIActionInvocationMode.Manual
                && !block.IsComplete
                && string.Equals(block.Call?.Name, "propose_document", StringComparison.Ordinal));
        if (action?.Call?.Arguments is null || State.PendingDocument is null)
            return;

        action.Call.Arguments["accepted"] = accepted;
        await action.InvokeAsync(cancellationToken);
        if (accepted)
            State.AcceptDocumentProposal();
        else
            State.RejectDocumentProposal();
    }

    public void AssertReplayFullyConsumed()
    {
        if (_client is ReplayChatClient replay)
            replay.Session.AssertFullyReplayed();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        RecordingError = DisposeCurrent();
    }

    private string? DisposeCurrent()
    {
        string? recordingError = null;
        var session = Session;
        var isActive = session is not null &&
            session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;
        _statusRegistration?.Dispose();
        _statusRegistration = null;
        ComposerController?.Dispose();
        Presentation?.Dispose();
        if (isActive)
        {
            _ = session!.CancelAsync();
            recordingError =
                "The active conversation was canceled; its incomplete recording was not saved.";
        }
        session?.Dispose();
        var client = _client;
        _client = null;
        try
        {
            if (!isActive && client is RecordingChatClient recording)
            {
                SanitizerManifest = recording.Session.Recording.Manifest;
                RecordingPath = _recordingPathFactory(ScenarioId);
                if (!string.IsNullOrWhiteSpace(RecordingPath))
                    _recordingCompleted?.Invoke(recording.Session.Recording, RecordingPath);
            }
        }
        catch
        {
            recordingError = "The recording could not be saved.";
        }
        finally
        {
            client?.Dispose();
        }

        return recordingError;
    }
}
