// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// One exchange in a conversation: the request blocks (the user message) and the response blocks
/// produced for it.
/// </summary>
/// <remarks>
/// Created and populated by <see cref="AgentContext"/>. The turn raises <see cref="PropertyChanged"/> when
/// one of its properties changes, and <see cref="RequestBlocks"/> and <see cref="ResponseBlocks"/> raise
/// <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/> when a block is
/// added, or a <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/> when
/// <see cref="AgentContext.RetryAsync"/> removes the response blocks, so a view can bind to the turn. Events
/// are raised on the thread that drives the <see cref="AgentContext"/>, once the change is in place.
/// </remarks>
public class ConversationTurn : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_isStoppedChanged = new(nameof(IsStopped));
    private static readonly PropertyChangedEventArgs s_isRespondingChanged = new(nameof(IsResponding));
    private static readonly PropertyChangedEventArgs s_isFailedChanged = new(nameof(IsFailed));
    private static readonly PropertyChangedEventArgs s_errorChanged = new(nameof(Error));
    private static readonly PropertyChangedEventArgs s_modelIdChanged = new(nameof(ModelId));
    private static readonly PropertyChangedEventArgs s_usageChanged = new(nameof(Usage));

    private readonly ObservableList<ContentBlock> _requestBlocks = new();
    private readonly ObservableList<ContentBlock> _responseBlocks = new();
    private string _id = Guid.NewGuid().ToString("N");
    private bool _isStopped;
    private bool _isResponding;
    private Exception? _error;
    private string? _modelId;
    private UsageDetails? _usage;

    /// <summary>Initializes a new instance of the <see cref="ConversationTurn"/> class.</summary>
    public ConversationTurn()
    {
        RequestBlocks = new ReadOnlyObservableCollection<ContentBlock>(_requestBlocks);
        ResponseBlocks = new ReadOnlyObservableCollection<ContentBlock>(_responseBlocks);
    }

    /// <summary>Occurs after a property of the turn changed.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        internal set
        {
            if (string.Equals(_id, value, StringComparison.Ordinal))
                return;

            _id = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Id)));
        }
    }

    /// <summary>Gets the blocks of the request: the user's message.</summary>
    /// <remarks>
    /// Raises <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/> when
    /// a block is added. Cast it to <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> to
    /// subscribe from code; XAML bindings find the event themselves.
    /// </remarks>
    public ReadOnlyObservableCollection<ContentBlock> RequestBlocks { get; }

    /// <summary>Gets the blocks of the response.</summary>
    /// <remarks>
    /// Raises <see cref="System.Collections.Specialized.INotifyCollectionChanged.CollectionChanged"/> when
    /// a block is added, and a <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/>
    /// when <see cref="AgentContext.RetryAsync"/> removes every block before it runs the turn again. Cast it
    /// to <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> to subscribe from code.
    /// </remarks>
    public ReadOnlyObservableCollection<ContentBlock> ResponseBlocks { get; }

    /// <summary>
    /// Gets whether the response was stopped before it completed, by
    /// <see cref="AgentContext.CancelAsync()"/> or the caller's cancellation token.
    /// </summary>
    /// <remarks>
    /// A stopped turn keeps its request and partial response. Approvals and UI actions that were
    /// still waiting for input when the turn stopped no longer respond, and a UI action that was still
    /// running shows no result when it returns, because the result is never sent. Its calls without a
    /// result are <see cref="FunctionInvocationContentBlock.IsAbandoned"/>. The change is raised once the
    /// context is <see cref="ConversationStatus.Idle"/>, before its status change.
    /// </remarks>
    public bool IsStopped
    {
        get => _isStopped;
        internal set
        {
            if (SetIsStoppedSilently(value))
                OnPropertyChanged(s_isStoppedChanged);
        }
    }

    /// <summary>
    /// Gets whether the turn is responding: <see langword="true"/> from the send that adds it, or the retry that runs
    /// it again, until its response completed, stopped or failed.
    /// </summary>
    /// <remarks>
    /// A turn joins <see cref="AgentContext.Turns"/> already responding, and a restored turn never responds, so a view
    /// can show how a turn ended once it no longer responds. On a retry, the change is raised after the changes of
    /// <see cref="Error"/> and <see cref="IsFailed"/>, before the <see cref="ConversationStatus.Streaming"/> status
    /// change. When the turn ends, it is raised once the turn's outcome is final: after the changes of
    /// <see cref="IsStopped"/>, <see cref="Error"/> and <see cref="IsFailed"/>, and before the status change that
    /// reports the end of the turn.
    /// </remarks>
    public bool IsResponding => _isResponding;

    /// <summary>
    /// Gets whether the turn's last attempt failed: <see langword="true"/> while <see cref="Error"/> is set.
    /// A failed turn committed nothing to the history.
    /// </summary>
    /// <remarks>
    /// Set before the <see cref="ConversationStatus.Error"/> status change that reports the failure is
    /// raised, and cleared when <see cref="AgentContext.RetryAsync"/> runs the turn again, before the
    /// <see cref="ConversationStatus.Streaming"/> status change is raised. Its change is raised once the
    /// context's status and error are in place, after the change of <see cref="Error"/> and before the
    /// context's own changes. The failed attempt's calls without a result are
    /// <see cref="FunctionInvocationContentBlock.IsAbandoned"/>; a retry replaces its response blocks. A
    /// failed turn that is not the last turn can no longer be retried: another message was sent after it.
    /// </remarks>
    public bool IsFailed => _error is not null;

    /// <summary>
    /// Gets the exception that failed the turn's last attempt, or <see langword="null"/> when it did not
    /// fail.
    /// </summary>
    /// <remarks>
    /// Set and cleared together with <see cref="IsFailed"/>. <see cref="AgentContext.Error"/> only reports
    /// the last attempt and the next send clears it, but the turn keeps its error until
    /// <see cref="AgentContext.RetryAsync"/> runs it again, so a view can show why each failed turn failed.
    /// A retry that fails again replaces it. Its change is raised once the context's status and error are
    /// in place, before the change of <see cref="IsFailed"/>.
    /// </remarks>
    public Exception? Error => _error;

    /// <summary>
    /// Gets the identifier of the model that answered, as reported by
    /// <see cref="ChatResponseUpdate.ModelId"/>, or <see langword="null"/> when the provider does not
    /// report it.
    /// </summary>
    /// <remarks>
    /// The last model identifier reported during the turn wins. It is updated while the response
    /// streams, before the blocks of the reporting update are added, and restored from a thread.
    /// </remarks>
    public string? ModelId
    {
        get => _modelId;
        internal set
        {
            if (string.Equals(_modelId, value, StringComparison.Ordinal))
                return;

            _modelId = value;
            OnPropertyChanged(s_modelIdChanged);
        }
    }

    /// <summary>
    /// Gets the token usage reported for the response, summed over every request the turn made, or
    /// <see langword="null"/> when the provider reports no <see cref="UsageContent"/>.
    /// </summary>
    /// <remarks>
    /// Usage is commonly reported at the end of a response, so it is complete when the turn is no
    /// longer streaming. It is restored from a thread. Each report replaces the value with a new
    /// <see cref="UsageDetails"/>, so a binding to one of its counts refreshes.
    /// </remarks>
    public UsageDetails? Usage
    {
        get => _usage;
        internal set
        {
            if (ReferenceEquals(_usage, value))
                return;

            _usage = value;
            OnPropertyChanged(s_usageChanged);
        }
    }

    internal void AddRequestBlock(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.Turn = this;
        _requestBlocks.Add(block);
    }

    internal void AddResponseBlock(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        block.Turn = this;
        _responseBlocks.Add(block);
    }

    /// <summary>
    /// Removes the response blocks and the response's metadata, raising a reset of
    /// <see cref="ResponseBlocks"/> once both are gone and then the metadata's changes.
    /// </summary>
    internal void ClearResponseBlocks()
    {
        var modelIdChanged = _modelId is not null;
        var usageChanged = _usage is not null;
        _modelId = null;
        _usage = null;
        _responseBlocks.Reset(Array.Empty<ContentBlock>());
        if (modelIdChanged)
            OnPropertyChanged(s_modelIdChanged);
        if (usageChanged)
            OnPropertyChanged(s_usageChanged);
    }

    internal void ObserveResponse(ChatResponseUpdate update)
    {
        ModelId = ResponseMetadata.GetModelId(update, ModelId);
        Usage = ResponseMetadata.AddUsage(update, Usage);
    }

    /// <summary>Sets <see cref="IsStopped"/>; the caller raises the change once the context's state is final.</summary>
    /// <returns>Whether the value changed.</returns>
    internal bool SetIsStoppedSilently(bool value)
    {
        if (_isStopped == value)
            return false;

        _isStopped = value;
        return true;
    }

    /// <summary>
    /// Sets <see cref="Error"/>, and with it <see cref="IsFailed"/>; the caller raises the changes with
    /// <see cref="RaiseFailureChanged"/> once the context's state is final.
    /// </summary>
    /// <returns>Which of the two values changed.</returns>
    internal FailureChange SetErrorSilently(Exception? error)
    {
        if (ReferenceEquals(_error, error))
            return default;

        var wasFailed = IsFailed;
        _error = error;
        return new FailureChange(ErrorChanged: true, IsFailedChanged: wasFailed != IsFailed);
    }

    internal void RaiseIsStoppedChanged() => OnPropertyChanged(s_isStoppedChanged);

    /// <summary>Sets <see cref="IsResponding"/>; the caller raises the change once the context's state is in place.</summary>
    /// <returns>Whether the value changed.</returns>
    internal bool SetIsRespondingSilently(bool value)
    {
        if (_isResponding == value)
            return false;

        _isResponding = value;
        return true;
    }

    internal void RaiseIsRespondingChanged() => OnPropertyChanged(s_isRespondingChanged);

    /// <summary>Raises the change of <see cref="Error"/> and then of <see cref="IsFailed"/>, as far as they changed.</summary>
    internal void RaiseFailureChanged(FailureChange change)
    {
        try
        {
            if (change.ErrorChanged)
                OnPropertyChanged(s_errorChanged);
        }
        finally
        {
            if (change.IsFailedChanged)
                OnPropertyChanged(s_isFailedChanged);
        }
    }

    private void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    /// <summary>Which of <see cref="Error"/> and <see cref="IsFailed"/> a change of the turn's error changed.</summary>
    internal readonly record struct FailureChange(bool ErrorChanged, bool IsFailedChanged);
}
