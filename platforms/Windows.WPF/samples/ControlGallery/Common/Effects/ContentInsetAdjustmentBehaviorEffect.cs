using System.ComponentModel;

namespace ControlGallery.Common.Effects;

public static class ContentInsetAdjustmentBehavior
{
    public static readonly BindableProperty ContentInsetProperty =
        BindableProperty.CreateAttached("ContentInset", typeof(Thickness), typeof(ContentInsetAdjustmentBehavior), new Thickness(0));

    public static Thickness GetContentInset(BindableObject view)
    {
        return (Thickness)view.GetValue(ContentInsetProperty);
    }

    public static void SetContentInset(BindableObject view, bool value)
    {
        view.SetValue(ContentInsetProperty, value);
    }
}

public class ContentInsetAdjustmentBehaviorRoutingEffect : RoutingEffect
{
}
