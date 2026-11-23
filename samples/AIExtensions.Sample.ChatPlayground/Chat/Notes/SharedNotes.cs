using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>The chat's state: notes that the user and the model share.</summary>
/// <param name="Text">The notes, free text.</param>
public sealed record NotesState(string Text)
{
    /// <summary>Initializes empty notes, the state a new chat starts with.</summary>
    public NotesState()
        : this(string.Empty)
    {
    }
}

/// <summary>
/// Connects the notes to the chat engine. The notes are the agent's state: the user edits them, every request tells
/// the model what they say, and the model changes them only by proposing new text with <c>propose_notes</c>, which the
/// user accepts or rejects.
/// </summary>
/// <remarks>
/// <para>
/// The proposal is predictive state. When the model calls <c>propose_notes</c>, the state mapper makes the proposed
/// text the state's provisional value, so the notes show it with the changes marked. The call itself is a manual UI
/// action: the turn waits until the user accepts the proposal, which commits it, or rejects it, which restores the
/// notes; the model then hears which. A turn that stops or fails rolls the proposal back.
/// </para>
/// <para>
/// A response can propose more than once. The user reviews the proposals one at a time, in order: only the first
/// becomes the provisional value, and each later one waits for its own decision, which commits its text or leaves the
/// notes as they are.
/// </para>
/// <para>
/// The engine does not save the state, and restoring the conversation cannot replay the user's own edits or their
/// decisions, so the committed notes are saved in a <see cref="NotesFile"/> whenever they change.
/// </para>
/// </remarks>
public sealed class SharedNotes : ObservableObject
{
    /// <summary>The name of the UI action through which the model proposes new notes.</summary>
    public const string ProposeToolName = "propose_notes";

    private readonly NotesFile? _file;
    private AgentState<NotesState>? _state;
    private bool? _decision;
    private string _text = string.Empty;
    private string? _proposal;

    /// <summary>Initializes the notes.</summary>
    /// <param name="file">Where the committed notes are saved, or <see langword="null"/> to keep them in memory.</param>
    public SharedNotes(NotesFile? file = null)
    {
        _file = file;
        ProposeFunction = AIFunctionFactory.Create(
            Propose,
            ProposeToolName,
            "Proposes new text for the shared notes. Pass the complete new text, not only the changes. The user " +
            "reviews the proposal and accepts or rejects it; the result says which.");
    }

    /// <summary>Gets the <c>propose_notes</c> function that <see cref="Register"/> registers as a manual UI action.</summary>
    public AIFunction ProposeFunction { get; }

    /// <summary>Gets the agent state that holds the notes, once <see cref="Register"/> connected it.</summary>
    public AgentState<NotesState>? State => _state;

    /// <summary>
    /// Gets or sets the committed notes, without a pending proposal. Setting them is the user's edit, which commits;
    /// it is ignored while a proposal waits for the user's decision.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_state is not { HasPendingPredictiveState: false } state || value == state.Value.Text)
                return;

            state.Value = new NotesState(value);
        }
    }

    /// <summary>Gets the proposed text that the state holds provisionally, until the user decides.</summary>
    public string? Proposal => _proposal;

    /// <summary>Gets whether the state holds a proposal provisionally.</summary>
    public bool HasProposal => _proposal is not null;

    /// <summary>
    /// Connects the notes to an agent: the state mapper that turns <c>propose_notes</c> calls into proposals, and the
    /// manual UI action that answers them.
    /// </summary>
    /// <param name="options">The options of the agent that holds the notes.</param>
    public void Register(UIAgentOptions<NotesState> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_state is not null)
            throw new InvalidOperationException("The notes are already connected to an agent.");

        _state = options.State;
        _state.PropertyChanged += StateChanged;
        options.StateMapper = MapState;
        options.RegisterUIAction(ProposeFunction, UIActionInvocationMode.Manual);
        StateChanged(null, new PropertyChangedEventArgs(null));
    }

    /// <summary>Accepts or rejects a <c>propose_notes</c> call, which the turn waits for.</summary>
    /// <param name="proposal">The call's block.</param>
    /// <param name="accept">Whether the proposal becomes the notes.</param>
    /// <returns>A task that completes when the decision was sent, or the turn ended without it.</returns>
    public async Task DecideAsync(UIActionBlock proposal, bool accept)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (!proposal.IsAwaitingInput || proposal.ToolName != ProposeToolName)
            return;

        // The action runs synchronously when it is invoked, so it reads the decision before this returns.
        Task<AIContent> result;
        _decision = accept;
        try
        {
            result = proposal.InvokeAsync();
        }
        finally
        {
            _decision = null;
        }

        try
        {
            await result;
        }
        catch (OperationCanceledException)
        {
            // The turn ended while the decision was on its way, which rolled the proposal back.
        }
    }

    /// <summary>
    /// Creates what each request tells the model about the notes, or returns <see langword="null"/> when they are
    /// empty, so a request without notes carries nothing extra.
    /// </summary>
    public string? CreateRequestContext() =>
        string.IsNullOrWhiteSpace(_text)
            ? null
            : "The notes currently say the following." +
                Environment.NewLine + "<notes>" + Environment.NewLine + _text + Environment.NewLine + "</notes>";

    /// <summary>Reads the notes saved by an earlier run, before a restore resets the state.</summary>
    public string ReadSaved() => _file?.Read() ?? string.Empty;

    /// <summary>Deletes the saved notes, so a new chat starts without them.</summary>
    public void DeleteSaved() => _file?.Write(string.Empty);

    /// <summary>Gets the text a <c>propose_notes</c> call proposes, or <see langword="null"/> for any other call.</summary>
    /// <param name="call">The call.</param>
    public static string? GetProposedText(FunctionCallContent? call) =>
        call is { Name: ProposeToolName } && call.Arguments?.TryGetValue("text", out var text) == true
            ? ReadText(text)
            : null;

    // Only one proposal at a time is provisional: a later one waits until the first is decided.
    private void MapState(StateMapperContext context)
    {
        if (_state is not { HasPendingPredictiveState: false })
            return;

        foreach (var content in context.UnhandledContents)
        {
            var call = content switch
            {
                FunctionCallContent functionCall => functionCall,
                ToolApprovalRequestContent { ToolCall: FunctionCallContent functionCall } => functionCall,
                _ => null,
            };
            if (GetProposedText(call) is { } text)
            {
                context.SetPredictiveState(new NotesState(text));
                return;
            }
        }
    }

    private string Propose(
        [Description("The complete new text of the notes.")] string text)
    {
        var accept = _decision == true;
        if (_state is not { } state)
            return "Error: the notes are not available.";

        var isPending = state.HasPendingPredictiveState && state.Value.Text == text;
        if (accept)
        {
            if (isPending)
                state.AcceptPredictiveState();
            else
                state.Value = new NotesState(text);
            return "Accepted: the proposed text is now the shared notes.";
        }

        if (isPending)
            state.RejectPredictiveState();
        return "Rejected: the notes did not change.";
    }

    private void StateChanged(object? sender, PropertyChangedEventArgs e)
    {
        var state = _state!;
        var proposal = state.HasPendingPredictiveState ? state.Value.Text : null;
        var text = state.HasPendingPredictiveState ? _text : state.Value.Text;
        var textChanged = text != _text;
        var proposalChanged = proposal != _proposal;
        _text = text;
        _proposal = proposal;

        if (textChanged)
        {
            _file?.Write(text);
            OnPropertyChanged(nameof(Text));
        }

        if (proposalChanged)
        {
            OnPropertyChanged(nameof(Proposal));
            OnPropertyChanged(nameof(HasProposal));
        }
    }

    private static string ReadText(object? value) =>
        value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
            JsonElement element => element.GetRawText(),
            null => string.Empty,
            _ => value.ToString() ?? string.Empty,
        };
}
