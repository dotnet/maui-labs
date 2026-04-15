#nullable enable
using System;
using Microsoft.Maui.Maps;

namespace ControlGallery.Common;

public static class MyExtensions
{
    /// <summary>
    /// Sets the <see cref="IImageSourcePart.Source" /> property.
    /// </summary>
    /// <typeparam name="TBindable"><see cref="BindableObject"/> : <see cref="IImageSourcePart"/></typeparam>
    /// <param name="bindable">The <see cref="BindableObject"/> on which to set the <see cref="IImageSourcePart.Source"/> Property</param>
    /// <param name="imageSource">The <see cref="Microsoft.Maui.Controls.ImageSource"/> value</param>
    /// <returns>The bindable object for fluent chaining.</returns>
    public static TBindable ImageSource<TBindable>(this TBindable bindable, ImageSource? imageSource) where TBindable : BindableObject, IImageSourcePart
    {
        if (bindable is Button)
            bindable.SetValue(Button.ImageSourceProperty, imageSource);
        else if (bindable is ImageButton)
            bindable.SetValue(ImageButton.SourceProperty, imageSource);
        else if (bindable is Image)
            bindable.SetValue(Image.SourceProperty, imageSource);

        return bindable;
    }

    public static TBindable Style<TBindable>(this TBindable bindable, string? styleKey) where TBindable : BindableObject
    {
        bindable.SetValue(VisualElement.StyleProperty, styleKey);
        return bindable;
    }

    // public static TBindable IsRunning(this TBindable activityIndicator, bool isRunning) where TBindable : BindableObject, IActivityIndicator
    // {
    //     activityIndicator.IsRunning = isRunning;
    //     return activityIndicator;
    // }
}

