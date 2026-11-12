// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// The view of a wrapping block: the wrapper's chrome as its <see cref="TemplatedView.ControlTemplate"/>, and the view
/// of the wrapped block as its <see cref="ContentView.Content"/>, which the chrome's <see cref="ContentPresenter"/>
/// shows. Without chrome, it shows the wrapped block's view alone.
/// </summary>
internal sealed class WrappedBlockView : ContentView
{
    private readonly BlockWrapper _wrapper;

    public WrappedBlockView(BlockWrapper wrapper, ControlTemplate? chrome, DataTemplate wrappedTemplate)
    {
        _wrapper = wrapper;
        if (wrappedTemplate.CreateContent() is View wrappedView)
        {
            // The wrapped block's view binds to the wrapped block, never to the wrapping block it would inherit.
            wrappedView.BindingContext = null;
            Content = wrappedView;
        }

        if (chrome is not null)
            ControlTemplate = chrome;
    }

    protected override void OnBindingContextChanged()
    {
        if (Content is { } wrappedView)
            wrappedView.BindingContext = BindingContext is ContentBlock block ? _wrapper.GetWrappedBlock(block) : null;

        base.OnBindingContextChanged();
    }
}
