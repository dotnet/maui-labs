using System;

using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Layouts;

namespace ControlGallery.Common
{

    public static class AbsoluteLayoutExtensions
    {
        public static void Add(this AbsoluteLayout layout, IView child, Rect bounds, AbsoluteLayoutFlags flags)
        {
            layout.Add(child);

            layout.SetLayoutBounds(child, bounds);
            layout.SetLayoutFlags(child, flags);
        }
    }
}