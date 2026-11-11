using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// The notes bar: the shared notes the user edits, the model's pending proposal with its changes marked, and whether
/// the bar is shown and expanded.
/// </summary>
/// <remarks>
/// <para>
/// The bar starts hidden. It shows itself when the notes get text or change, and when the model proposes new notes;
/// once the user hides it, it stays hidden until the next such change.
/// </para>
/// <para>
/// The bar reviews one proposal at a time: the oldest <c>propose_notes</c> call of the response that still waits for
/// the user's decision. Once it is decided, in the bar or on its card in the chat, the bar shows the next.
/// </para>
/// </remarks>
public sealed partial class NotesViewModel : ObservableObject
{
    private readonly SharedNotes _notes;
    private readonly AgentContext _session;
    private UIActionBlock? _proposal;

    /// <summary>Initializes the notes bar.</summary>
    /// <param name="notes">The notes the bar shows.</param>
    /// <param name="session">The chat whose responses can propose new notes.</param>
    public NotesViewModel(SharedNotes notes, AgentContext session)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(session);
        _notes = notes;
        _session = session;
        notes.PropertyChanged += NotesChanged;
        session.PropertyChanged += SessionChanged;
        Proposal = FindPendingProposal();
    }

    /// <summary>Gets or sets whether the bar is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleShownText))]
    private bool isShown;

    /// <summary>Gets or sets whether the bar shows its editor on a narrow screen, where it is collapsed by default.</summary>
    [ObservableProperty] private bool isExpanded;

    /// <summary>Gets or sets the notes. Setting them is the user's edit.</summary>
    public string Text
    {
        get => _notes.Text;
        set => _notes.Text = value;
    }

    /// <summary>Gets the model's proposal that the bar reviews: its oldest that waits for the user's decision.</summary>
    public UIActionBlock? Proposal
    {
        get => _proposal;
        private set
        {
            if (ReferenceEquals(_proposal, value))
                return;

            if (_proposal is not null)
                _proposal.PropertyChanged -= ProposalChanged;
            _proposal = value;
            if (_proposal is not null)
                _proposal.PropertyChanged += ProposalChanged;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProposal));
            OnPropertyChanged(nameof(Changes));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(Summary));
            if (value is not null)
            {
                IsShown = true;
                IsExpanded = true;
            }
        }
    }

    /// <summary>Gets whether the model proposed new notes that wait for the user's decision.</summary>
    public bool HasProposal => Proposal is not null;

    /// <summary>Gets what the proposal changes in the notes, word by word, which the bar shows marked.</summary>
    public IReadOnlyList<DiffSegment> Changes =>
        Proposal is null ? [] : TextDiff.Compute(Text, SharedNotes.GetProposedText(Proposal.Call));

    /// <summary>
    /// Gets whether the user cannot edit the notes now: while a response runs, which could roll the notes back, and
    /// while a proposal waits.
    /// </summary>
    public bool IsReadOnly => HasProposal ||
        _session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    /// <summary>Gets what the collapsed bar says: the first line of the notes, or that a proposal waits.</summary>
    public string Summary
    {
        get
        {
            if (HasProposal)
                return "Proposed changes \u2014 review them";

            var text = Text.TrimStart();
            var end = text.IndexOfAny(['\r', '\n']);
            return text.Length == 0 ? "Empty \u2014 tap to write" : end < 0 ? text : text[..end];
        }
    }

    /// <summary>Gets the text of the menu item that shows or hides the bar.</summary>
    public string ToggleShownText => IsShown ? "Hide notes" : "Show notes";

    // The XAML source generator does not see the commands that [RelayCommand] generates, so the views bind to these.
    public IRelayCommand ToggleShownAction => ToggleShownCommand;
    public IRelayCommand ToggleExpandedAction => ToggleExpandedCommand;
    public IAsyncRelayCommand<UIActionBlock> AcceptProposalAction => AcceptProposalCommand;
    public IAsyncRelayCommand<UIActionBlock> RejectProposalAction => RejectProposalCommand;

    /// <summary>Reads the notes saved by an earlier run, before a restore resets them.</summary>
    public string ReadSaved() => _notes.ReadSaved();

    /// <summary>Deletes the saved notes, so a new chat starts without them.</summary>
    public void DeleteSaved() => _notes.DeleteSaved();

    /// <summary>Puts back the notes saved by an earlier run, after the conversation was restored.</summary>
    /// <param name="text">The saved notes.</param>
    public void Load(string text)
    {
        _notes.Text = text;
        IsShown = Text.Length > 0;
        IsExpanded = false;
    }

    [RelayCommand]
    private void ToggleShown() => IsShown = !IsShown;

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    /// <summary>Accepts a proposal: the given card's, or the one the bar reviews.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task AcceptProposalAsync(UIActionBlock? proposal) => DecideAsync(proposal, accept: true);

    /// <summary>Rejects a proposal: the given card's, or the one the bar reviews.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RejectProposalAsync(UIActionBlock? proposal) => DecideAsync(proposal, accept: false);

    private Task DecideAsync(UIActionBlock? proposal, bool accept) =>
        (proposal ?? Proposal) is { } block
            ? _notes.DecideAsync(block, accept)
            : Task.CompletedTask;

    private UIActionBlock? FindPendingProposal() =>
        _session.Turns.Count == 0
            ? null
            : _session.Turns[^1].ResponseBlocks.OfType<UIActionBlock>().FirstOrDefault(static block =>
                block.IsAwaitingInput && block.ToolName == SharedNotes.ProposeToolName);

    private void NotesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SharedNotes.Text))
            return;

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Changes));
        if (Text.Length > 0)
            IsShown = true;
    }

    // A response's proposals arrive as it waits for the user, and the turn's end leaves none waiting.
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AgentContext.Status))
            return;

        Proposal = FindPendingProposal();
        OnPropertyChanged(nameof(IsReadOnly));
    }

    // Once decided, the proposal no longer waits, and the bar moves on to the next.
    private void ProposalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(UIActionBlock.IsAwaitingInput))
            Proposal = FindPendingProposal();
    }
}
