using System.Collections.Specialized;

namespace Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

/// <summary>Records every <see cref="ChatConversationChange"/> a conversation publishes, in order.</summary>
internal sealed class ChangeRecorder : IDisposable
{
    private readonly IDisposable _subscription;

    public ChangeRecorder(ChatConversation conversation) =>
        _subscription = conversation.Subscribe(Changes.Add);

    public List<ChatConversationChange> Changes { get; } = [];

    public IReadOnlyList<ChatConversationChangeKind> Kinds =>
        [.. Changes.Select(static change => change.Kind)];

    public void Clear() => Changes.Clear();

    public void Dispose() => _subscription.Dispose();
}

/// <summary>Records collection change notifications so tests can assert what the list actually did.</summary>
internal sealed class CollectionRecorder : IDisposable
{
    private readonly INotifyCollectionChanged _source;

    public CollectionRecorder(INotifyCollectionChanged source)
    {
        _source = source;
        _source.CollectionChanged += OnCollectionChanged;
    }

    public List<NotifyCollectionChangedEventArgs> Events { get; } = [];

    public IReadOnlyList<NotifyCollectionChangedAction> Actions =>
        [.. Events.Select(static e => e.Action)];

    public void Clear() => Events.Clear();

    public void Dispose() => _source.CollectionChanged -= OnCollectionChanged;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Events.Add(e);
}

/// <summary>Records property change notifications raised by a bindable object.</summary>
internal sealed class PropertyRecorder : IDisposable
{
    private readonly BindableObject _source;

    public PropertyRecorder(BindableObject source)
    {
        _source = source;
        _source.PropertyChanged += OnPropertyChanged;
    }

    public List<string> Names { get; } = [];

    public int CountOf(string name) => Names.Count(n => n == name);

    public void Clear() => Names.Clear();

    public void Dispose() => _source.PropertyChanged -= OnPropertyChanged;

    private void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name)
            Names.Add(name);
    }
}
