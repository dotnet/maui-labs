// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for every renderable unit in a conversation. Owns an <see cref="Id"/>, a
/// <see cref="LifecycleState"/>, a <see cref="Role"/>, the <see cref="Turn"/> it belongs to, and the change
/// notifications used to stream incremental updates to the UI.
/// </summary>
/// <remarks>
/// <para>
/// Blocks are produced by a <see cref="ContentBlockHandler{TState}"/> from raw
/// Microsoft.Extensions.AI content, grouped into a <see cref="ConversationTurn"/> by
/// <see cref="AgentContext"/>, and surfaced to the UI for rendering.
/// </para>
/// <para>
/// A block raises <see cref="PropertyChanged"/>, so a view can bind to it. The engine's own blocks raise
/// the names of the properties that changed. A block of any other type, such as an app's custom block or
/// a <c>[ToolBlock]</c>, also raises it with an empty property name, meaning that every property may have
/// changed, whenever its handler updates or completes it or it calls <see cref="NotifyChanged"/>; its
/// author can raise specific names with <see cref="OnPropertyChanged(string)"/> and
/// <see cref="SetProperty{T}(ref T, T, string)"/>. Events are raised synchronously on the thread that
/// changes the block, which is the thread that drives the <see cref="AgentContext"/>.
/// </para>
/// <para>
/// A binding refreshes when the property it reads raises <see cref="PropertyChanged"/>, so a binding that
/// hands a view a list the block changes in place keeps its first value: expose such a list as an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>, or read it again in a
/// <see cref="PropertyChanged"/> handler.
/// </para>
/// </remarks>
public abstract class ContentBlock : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_allPropertiesChanged = new(string.Empty);
    private static readonly PropertyChangedEventArgs s_turnChanged = new(nameof(Turn));

    private readonly bool _reportsAllPropertiesOnChange;
    private string _id = string.Empty;
    private BlockLifecycleState _lifecycleState;
    private ChatRole? _role;
    private string? _authorName;
    private DateTimeOffset? _createdAt;
    private ConversationTurn? _turn;

    /// <summary>Initializes a new instance of the <see cref="ContentBlock"/> class.</summary>
    protected ContentBlock()
    {
        // The engine's blocks name the properties they change. The engine cannot see which properties of
        // another block type a handler changed, so such a block reports them all when it notifies.
        _reportsAllPropertiesOnChange = GetType().Assembly != typeof(ContentBlock).Assembly;
    }

    /// <summary>Occurs after a property of the block changed.</summary>
    /// <remarks>
    /// An empty <see cref="PropertyChangedEventArgs.PropertyName"/> means that every property may have
    /// changed. Raised on the thread that changes the block.
    /// </remarks>
    public event PropertyChangedEventHandler? PropertyChanged;

    // Public setter so custom block handlers (e.g. in the sample) can assign Id
    // from a FunctionCallContent.CallId when projecting M.E.AI content into a block.
    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public BlockLifecycleState LifecycleState
    {
        get => _lifecycleState;
        internal set => SetProperty(ref _lifecycleState, value);
    }

    public ChatRole? Role
    {
        get => _role;
        internal set => SetProperty(ref _role, value);
    }

    public string? AuthorName
    {
        get => _authorName;
        internal set => SetProperty(ref _authorName, value);
    }

    /// <summary>Gets when the source message/update was created, when supplied by the provider.</summary>
    public DateTimeOffset? CreatedAt
    {
        get => _createdAt;
        internal set => SetProperty(ref _createdAt, value);
    }

    /// <summary>
    /// Gets the turn that owns the block, or <see langword="null"/> before the block is added to a turn.
    /// </summary>
    /// <remarks>
    /// Set when the block is added to a turn's <see cref="ConversationTurn.RequestBlocks"/> or
    /// <see cref="ConversationTurn.ResponseBlocks"/>, and on the
    /// <see cref="InteractiveFunctionBlock.InnerBlock"/> of an approval or a UI action in that turn.
    /// </remarks>
    public ConversationTurn? Turn
    {
        get => _turn;
        internal set
        {
            if (ReferenceEquals(_turn, value))
                return;

            _turn = value;
            RaisePropertyChanged(s_turnChanged);
            OnTurnSet(value);
        }
    }

    /// <summary>
    /// Ends whatever the block still waits for because its turn ended without finishing it: the turn completed
    /// without a result for it, was stopped or failed, or was restored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine calls this method on every block of a turn when the turn ends, and on the
    /// <see cref="InteractiveFunctionBlock.InnerBlock"/> of an approval or a UI action, once the turn's outcome is
    /// set and before its changes are raised. When this method returns <see langword="true"/>, the engine raises
    /// <see cref="NotifyChanged"/> for the block once the turn's outcome is reported; during a restore it runs
    /// before the turn is shown.
    /// </para>
    /// <para>
    /// Override this method on a block of your own that waits for a result, such as a block that folds several
    /// tool calls into rows, to mark the rows that still have no result.
    /// </para>
    /// </remarks>
    /// <returns>Whether the block changed because its turn ended.</returns>
    protected virtual bool Abandon() => false;

    /// <summary>
    /// Reports that every property of the block may have changed: raises <see cref="PropertyChanged"/> with
    /// an empty property name. The engine reports it for a block of your own when the block's handler updates
    /// or completes it, and when the engine sets the result of a call block or abandons it; call it after
    /// changing the block yourself.
    /// </summary>
    /// <remarks>
    /// The engine's own block types raise the names of the properties that changed instead, so for them it
    /// raises nothing.
    /// </remarks>
    protected void NotifyChanged()
    {
        if (_reportsAllPropertiesOnChange)
            RaisePropertyChanged(s_allPropertiesChanged);
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for a property of the block.</summary>
    /// <param name="propertyName">
    /// The property that changed, by default the calling property; empty when every property may have changed.
    /// </param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => RaisePropertyChanged(new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Sets a property's backing field and raises <see cref="PropertyChanged"/> when the value changed.
    /// </summary>
    /// <typeparam name="T">The type of the property.</typeparam>
    /// <param name="field">The backing field.</param>
    /// <param name="value">The new value.</param>
    /// <param name="propertyName">The property, by default the calling property.</param>
    /// <returns>Whether the value changed.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    internal void RaisePropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    internal void InvokeNotifyChanged() => NotifyChanged();

    internal bool InvokeAbandon() => Abandon();

    internal virtual void OnTurnSet(ConversationTurn? turn)
    {
    }
}
