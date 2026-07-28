namespace AIExtensions.Sample.Garden;

/// <summary>
/// Attached property that auto-resolves a ViewModel from DI and sets it as BindingContext.
/// Usage: <c>&lt;views:ChatView local:ViewModelBinder.Type="{x:Type vm:ChatViewModel}" /&gt;</c>
/// </summary>
public static class ViewModelBinder
{
    public static readonly BindableProperty TypeProperty =
        BindableProperty.CreateAttached("Type", typeof(Type), typeof(ViewModelBinder), null,
            propertyChanged: OnTypeChanged);

    public static Type? GetType(BindableObject obj) => (Type?)obj.GetValue(TypeProperty);
    public static void SetType(BindableObject obj, Type? value) => obj.SetValue(TypeProperty, value);

    private static void OnTypeChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not Element element || newValue is not Type vmType)
            return;

        element.HandlerChanged += (_, _) => TryResolve(element, vmType);
        TryResolve(element, vmType);
    }

    private static void TryResolve(Element element, Type vmType)
    {
        if (element.IsSet(BindableObject.BindingContextProperty))
            return;

        if (element.Handler?.MauiContext?.Services?.GetService(vmType) is { } vm)
            element.BindingContext = vm;
    }
}
