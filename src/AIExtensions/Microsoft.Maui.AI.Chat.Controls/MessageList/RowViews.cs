namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>Base for message list item views: tracks the row it shows and re-renders on coalesced changes.</summary>
internal abstract class RowView<TRow> : ContentView
    where TRow : ChatRow
{
    private readonly bool _themed = ChatElements.EnsureTheme();
    private TRow? _row;

    protected RowView() => SetDynamicResource(StyleProperty, AIChatThemeKeys.RowStyle);

    internal TRow? Row => _row;

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        var row = BindingContext as TRow;
        if (ReferenceEquals(row, _row))
            return;

        if (_row is not null)
            _row.Changed -= OnRowChanged;

        _row = row;
        if (row is null)
            return;

        row.Changed += OnRowChanged;
        Render(row, isNewRow: true);
    }

    /// <summary>Renders <paramref name="row"/>; <paramref name="isNewRow"/> is false for in-place updates.</summary>
    protected abstract void Render(TRow row, bool isNewRow);

    private void OnRowChanged(object? sender, EventArgs e)
    {
        if (_row is { } row)
            Render(row, isNewRow: false);
    }
}

/// <summary>Hosts the view a <see cref="BlockTemplate"/>'s template creates and keeps it bound to the current block.</summary>
internal sealed class BlockHost(DataTemplate template) : RowView<BlockRow>
{
    private View? _view;
    private DataTemplate? _selected;

    internal View? HostedView => _view;

    protected override void Render(BlockRow row, bool isNewRow)
    {
        EnsureView(row);
        ChatBlockView.Bind(_view!, row.Block, row.Owner, row.Session, row.Turn);
    }

    private void EnsureView(BlockRow row)
    {
        // A selector may pick another template as the block changes (for example once a tool call completes).
        var selected = template is DataTemplateSelector selector ? selector.SelectTemplate(row.Block, this) : template;
        if (_view is not null && ReferenceEquals(selected, _selected))
            return;

        _selected = selected;
        _view = BuiltInBlockTemplates.CreateView(selected);
        Content = _view;
    }
}

/// <summary>Hosts <see cref="AIChatView.ResponseFooterTemplate"/> with the turn as its binding context.</summary>
internal sealed class TurnFooterHost : RowView<TurnFooterRow>
{
    private View? _view;

    protected override void Render(TurnFooterRow row, bool isNewRow)
    {
        if (_view is null)
        {
            _view = row.Template.CreateContent() as View;
            Content = _view;
        }

        if (_view is not null)
        {
            _view.BindingContext = null;
            _view.BindingContext = row.Turn;
        }
    }
}
