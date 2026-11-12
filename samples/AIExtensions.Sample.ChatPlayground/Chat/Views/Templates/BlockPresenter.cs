using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Shows one block with the template that a selector picks for it. The approval card uses it to show the call it
/// wraps with that call's own card.
/// </summary>
public sealed class BlockPresenter : ContentView
{
    public static readonly BindableProperty BlockProperty = BindableProperty.Create(
        nameof(Block), typeof(ContentBlock), typeof(BlockPresenter), propertyChanged: OnPresentationChanged);

    public static readonly BindableProperty TemplateSelectorProperty = BindableProperty.Create(
        nameof(TemplateSelector), typeof(DataTemplateSelector), typeof(BlockPresenter),
        propertyChanged: OnPresentationChanged);

    /// <summary>Gets or sets the block to show.</summary>
    public ContentBlock? Block
    {
        get => (ContentBlock?)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    /// <summary>Gets or sets the selector that picks the block's template.</summary>
    public DataTemplateSelector? TemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(TemplateSelectorProperty);
        set => SetValue(TemplateSelectorProperty, value);
    }

    private static void OnPresentationChanged(BindableObject bindable, object? oldValue, object? newValue) =>
        ((BlockPresenter)bindable).Present();

    private void Present()
    {
        if (Block is not { } block || TemplateSelector?.SelectTemplate(block, this)?.CreateContent() is not View view)
        {
            Content = null;
            return;
        }

        view.BindingContext = block;
        Content = view;
    }
}
