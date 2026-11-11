// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for every renderable unit in a conversation. Owns an <see cref="Id"/>, a
/// <see cref="LifecycleState"/>, a <see cref="Role"/>, and the change notifications used to stream
/// incremental updates to the UI.
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
/// </remarks>
public abstract class ContentBlock : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs s_allPropertiesChanged = new(string.Empty);

    private readonly List<Action> _callbacks = new();
    private readonly bool _reportsAllPropertiesOnChange;
    private string _id = string.Empty;
    private BlockLifecycleState _lifecycleState;
    private ChatRole? _role;
    private string? _authorName;
    private DateTimeOffset? _createdAt;

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

    /// <summary>Registers a callback that runs whenever the block changes.</summary>
    /// <remarks>
    /// Runs once for each change the block reports, after its <see cref="PropertyChanged"/> events, so a
    /// view can refresh once for several property changes.
    /// </remarks>
    public ContentBlockChangedSubscription OnChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
        return new ContentBlockChangedSubscription(this, callback);
    }

    /// <summary>
    /// Reports that the block changed: runs the <see cref="OnChanged(Action)"/> callbacks, after raising
    /// <see cref="PropertyChanged"/> with an empty property name for a block whose type is not one of the
    /// engine's own.
    /// </summary>
    protected void NotifyChanged()
    {
        if (_reportsAllPropertiesOnChange)
            RaisePropertyChanged(s_allPropertiesChanged);

        // Snapshot the callbacks to allow safe removal during iteration
        var snapshot = _callbacks.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i]();
        }
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

    internal void RemoveCallback(Action callback)
    {
        _callbacks.Remove(callback);
    }
}
