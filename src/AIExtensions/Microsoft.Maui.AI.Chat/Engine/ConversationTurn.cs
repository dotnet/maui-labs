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
    private static readonly PropertyChangedEventArgs s_isFailedChanged = new(nameof(IsFailed));
    private static readonly PropertyChangedEventArgs s_modelIdChanged = new(nameof(ModelId));
    private static readonly PropertyChangedEventArgs s_usageChanged = new(nameof(Usage));

    private readonly ObservableList<ContentBlock> _requestBlocks = new();
    private readonly ObservableList<ContentBlock> _responseBlocks = new();
    private string _id = Guid.NewGuid().ToString("N");
    private bool _isStopped;
    private bool _isFailed;
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
    /// running shows no result when it returns, because the result is never sent. The change is raised
    /// once the context is <see cref="ConversationStatus.Idle"/>, before its status change.
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
    /// Gets whether the turn's last attempt failed. A failed turn committed nothing to the history.
    /// </summary>
    /// <remarks>
    /// Set before the <see cref="ConversationStatus.Error"/> status change that reports the failure is
    /// raised, and cleared when <see cref="AgentContext.RetryAsync"/> runs the turn again, before the
    /// <see cref="ConversationStatus.Streaming"/> status change is raised. Its change is raised once the
    /// context's status and error are in place, before their own changes. A failed turn that is not the
    /// last turn can no longer be retried: another message was sent after it.
    /// </remarks>
    public bool IsFailed
    {
        get => _isFailed;
        internal set
        {
            if (SetIsFailedSilently(value))
                OnPropertyChanged(s_isFailedChanged);
        }
    }

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
        _requestBlocks.Add(block);
    }

    internal void AddResponseBlock(ContentBlock block)
    {
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

    /// <summary>Sets <see cref="IsFailed"/>; the caller raises the change once the context's state is final.</summary>
    /// <returns>Whether the value changed.</returns>
    internal bool SetIsFailedSilently(bool value)
    {
        if (_isFailed == value)
            return false;

        _isFailed = value;
        return true;
    }

    internal void RaiseIsStoppedChanged() => OnPropertyChanged(s_isStoppedChanged);

    internal void RaiseIsFailedChanged() => OnPropertyChanged(s_isFailedChanged);

    private void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
}
