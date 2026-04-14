#nullable enable
using System;
using Microsoft.Maui.Maps;

namespace ControlGallery.Common;

public static class MyExtensions
{
    /// <summary>
    /// Sets the <see cref="IImageSourcePart.Source" /> Property/>
    /// Sets the <see cref="IImageSourcePart.Source" /> Property/>
    /// </summary>
    /// <typeparam name="TBindable"><see cref="BindableObject"/> : <see cref="IImageSourcePart"/></typeparam>
    /// <typeparam name="TBindable"><see cref="BindableObject"/> : <see cref="IImageSourcePart"/></typeparam>
    /// <param name="bindable">The <see cref="BindableObject"/> on which to set the <see cref="IImageSourcePart.Source"/> Property</param>
    /// <param name="bindable">The <see cref="BindableObject"/> on which to set the <see cref="IImageSourcePart.Source"/> Property</param>
    /// <param name="imageSource">The <see cref="Microsoft.Maui.Controls.ImageSource"/> value</param>
    /// <param name="imageSource">The <see cref="Microsoft.Maui.Controls.ImageSource"/> value</param>
    /// <returns></returns>
    public static TBindable ImageSource<TBindable>(this TBindable bindable, ImageSource? imageSource) where TBindable : BindableObject, IImageSourcePart
    {
        bindable.SetValue(Button.ImageSourceProperty, imageSource);
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

