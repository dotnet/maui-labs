using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The chat tab without MAUI: settings, the router client, the shared notes, and one engine session with the
/// playground's blocks and UI actions, wired the way <c>ChatViewModel</c> wires them.
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

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"chat-playground-tests-{Guid.NewGuid():N}");

    /// <param name="live">The live client, or <see langword="null"/> for none.</param>
    /// <param name="descriptor">The live client's descriptor; defaults to <see cref="LiveDescriptor"/>.</param>
    /// <param name="persist">
    /// Whether the session saves the conversation in <see cref="Thread"/> and the notes in <see cref="NotesPath"/>, as
    /// in the app.
    /// </param>
    public PlaygroundChatHarness(IChatClient? live = null, ChatClientDescriptor? descriptor = null, bool persist = false)
    {
        if (live is not null)
            Live = new DescribedChatClient(live, descriptor ?? LiveDescriptor);

        Persist = persist;
        Settings = new SettingsPaneViewModel(Live is null ? [] : [Live]);
        Tools = new PlaygroundTools(() => new ConnectionStatusResult("Internet", ["WiFi"]));
        Notes = CreateNotes();
        Router = new PlaygroundChatClient(() => Settings.SelectedClient, Settings, Tools, () => Notes.CreateRequestContext());
        (Thread, Session) = Open();
        NotesBar = new NotesViewModel(Notes, Session);
    }

    /// <summary>Gets whether the session saves the conversation and the notes.</summary>
    public bool Persist { get; }

    /// <summary>Gets the file the conversation is saved in when the harness persists it.</summary>
    public string ThreadPath => Path.Combine(_directory, "conversation.json");

    /// <summary>Gets the file the notes are saved in when the harness persists them.</summary>
    public string NotesPath => Path.Combine(_directory, "notes.txt");

    /// <summary>Gets the saved conversation, or <see langword="null"/> when the harness doesn't persist it.</summary>
    public FileConversationThread? Thread { get; private set; }

    public IChatClient? Live { get; }

    public SettingsPaneViewModel Settings { get; }

    public PlaygroundTools Tools { get; }

    public PlaygroundChatClient Router { get; }

    /// <summary>Gets the notes that the user and the model share.</summary>
    public SharedNotes Notes { get; private set; }

    /// <summary>Gets the notes bar, which reviews the model's proposals one at a time.</summary>
    public NotesViewModel NotesBar { get; private set; }

    public AgentContext Session { get; private set; }

    /// <summary>Gets each theme that <c>set_theme</c> applied, in order.</summary>
    public List<AppTheme> AppliedThemes { get; } = [];

    public ConversationTurn LastTurn => Session.Turns[^1];

    public static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    /// <summary>
    /// Starts over with a new session over the saved conversation and notes, as launching the app again does, and
    /// restores them the way <c>ChatViewModel.RestoreChatAsync</c> does.
    /// </summary>
    /// <returns>What the chat tells the user about the restore.</returns>
    public async Task<string?> ReopenAsync(CancellationToken cancellationToken = default)
    {
        Session.Dispose();
        Notes = CreateNotes();
        (Thread, Session) = Open();
        NotesBar = new NotesViewModel(Notes, Session);
        return await SavedChat.RestoreAsync(Session, Thread!, NotesBar, cancellationToken);
    }

    /// <summary>Starts a new chat the way the chat's New chat does.</summary>
    public Task NewChatAsync() => SavedChat.ClearAsync(Session, NotesBar);

    private SharedNotes CreateNotes() => new(Persist ? new NotesFile(NotesPath) : null);

    private (FileConversationThread? Thread, AgentContext Session) Open()
    {
        var notes = Notes;
        var thread = Persist ? new FileConversationThread(ThreadPath) : null;
        var session = new AgentContext(new UIAgent<NotesState>(Router, options =>
        {
            options.Thread = thread;
            options.AddGeneratedToolBlocks();
            options.AddBlockHandler(new CalculatorWorksheetHandler());

            // Every client of the app invokes functions, which set_theme needs to ask for approval. A scripted client
            // without function invocation answers as if its calls already ran, so it gets no set_theme.
            if (Live?.GetService<FunctionInvokingChatClient>() is not null)
                options.RegisterUIAction(ThemeAction.Create(AppliedThemes.Add));
            notes.Register(options);
        }, NullLoggerFactory.Instance));
        return (thread, session);
    }

    public void Dispose()
    {
        Session.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
