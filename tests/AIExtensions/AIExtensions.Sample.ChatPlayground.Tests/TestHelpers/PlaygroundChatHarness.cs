using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The chat tab without MAUI: settings, the router client, one engine session, the recording, and the replay driver,
/// wired the way <c>ChatViewModel</c> wires them.
/// </summary>
internal sealed class PlaygroundChatHarness : IDisposable
{
    public static readonly ChatClientDescriptor LiveDescriptor = new(
        "live",
        "Live",
        "A scripted live client.",
        SupportsImageInput: true,
        SupportsReasoningSummary: true,
        SupportsImageGeneration: true,
        SupportsToolCalling: true);

    public static readonly ChatClientDescriptor ReplayDescriptor = new("replay", "Replay", "Offline.", IsReplay: true);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"chat-playground-tests-{Guid.NewGuid():N}");

    /// <param name="live">The live client, or <see langword="null"/> for only Replay.</param>
    /// <param name="descriptor">The live client's descriptor; defaults to <see cref="LiveDescriptor"/>.</param>
    /// <param name="record">Whether the live client records into <see cref="Recording"/>, as in the app.</param>
    public PlaygroundChatHarness(IChatClient? live = null, ChatClientDescriptor? descriptor = null, bool record = false)
    {
        Recording = new ChatSessionService(
            NullLogger<ChatSessionService>.Instance, Path.Combine(_directory, "data"), Path.Combine(_directory, "cache"));
        ReplayRequests = new CapturingChatClient(new ReplayChatClient(Recording));
        Replay = new DescribedChatClient(ReplayRequests, ReplayDescriptor);
        if (live is not null)
        {
            // The app records outside everything else, so the recording holds what the engine sent and received.
            var described = new DescribedChatClient(live, descriptor ?? LiveDescriptor);
            Live = record ? new RecordingChatClient(described, Recording) : described;
        }

        Settings = new SettingsPaneViewModel(Live is null ? [Replay] : [Live, Replay]);
        Tools = new PlaygroundTools(() => new ConnectionStatusResult("Internet", ["WiFi"]));
        Router = new PlaygroundChatClient(() => Settings.SelectedClient, Replay, Settings, Tools);
        Session = new AgentContext(new UIAgent(Router, new ChatOptions(), NullLoggerFactory.Instance));
        Driver = new ChatReplayDriver(Session, Router, Recording);
    }

    public ChatSessionService Recording { get; }

    /// <summary>Gets what the replay client was sent; replay answers from the recording whatever it is sent.</summary>
    public CapturingChatClient ReplayRequests { get; }

    public IChatClient Replay { get; }

    public IChatClient? Live { get; }

    public SettingsPaneViewModel Settings { get; }

    public PlaygroundTools Tools { get; }

    public PlaygroundChatClient Router { get; }

    public AgentContext Session { get; }

    public ChatReplayDriver Driver { get; }

    public ConversationTurn LastTurn => Session.Turns[^1];

    public static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    /// <summary>Makes a fixture the current recording, as Import or Load example does.</summary>
    public async Task LoadAsync(string fileName)
    {
        await using var source = File.OpenRead(FixturePath(fileName));
        await Recording.LoadFileAsync(source);
    }

    public void Dispose()
    {
        Session.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}

/// <summary>Passes requests through and keeps a snapshot of each request's messages.</summary>
internal sealed class CapturingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToArray();
        Requests.Add(snapshot);
        return base.GetResponseAsync(snapshot, options, cancellationToken);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToArray();
        Requests.Add(snapshot);
        return base.GetStreamingResponseAsync(snapshot, options, cancellationToken);
    }
}
