using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Preserves the property metadata used by DevFlow inspection in trimmed applications.
/// Register custom controls (and custom objects reached through property paths) at startup.
/// </summary>
public static class MauiAgentInspection
{
    private const DynamicallyAccessedMemberTypes Members =
        DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields;
    private static readonly ConcurrentDictionary<Type, Registration> Types = new();

    static MauiAgentInspection()
    {
        RegisterType<Element>();
        RegisterType<Window>();
        RegisterType<VisualElement>();
        RegisterType<View>();
        RegisterType<Page>();
        RegisterType<ContentPage>();
        RegisterType<ContentView>();
        RegisterType<NavigationPage>();
        RegisterType<Shell>();
        RegisterType<ShellItem>();
        RegisterType<ShellSection>();
        RegisterType<ShellContent>();
        RegisterType<ToolbarItem>();
        RegisterType<MenuItem>();
        RegisterType<Label>();
        RegisterType<Button>();
        RegisterType<ImageButton>();
        RegisterType<Entry>();
        RegisterType<Editor>();
        RegisterType<SearchBar>();
        RegisterType<CheckBox>();
        RegisterType<RadioButton>();
        RegisterType<Switch>();
        RegisterType<Slider>();
        RegisterType<Stepper>();
        RegisterType<ProgressBar>();
        RegisterType<ActivityIndicator>();
        RegisterType<Picker>();
        RegisterType<DatePicker>();
        RegisterType<TimePicker>();
        RegisterType<Image>();
        RegisterType<WebView>();
        RegisterType<Border>();
        RegisterType<BoxView>();
        RegisterType<ScrollView>();
        RegisterType<Grid>();
        RegisterType<StackLayout>();
        RegisterType<VerticalStackLayout>();
        RegisterType<HorizontalStackLayout>();
        RegisterType<FlexLayout>();
        RegisterType<AbsoluteLayout>();
        RegisterType<CollectionView>();
        RegisterType<CarouselView>();
        RegisterType<RefreshView>();
        RegisterType<Shadow>();
        RegisterType<SolidColorBrush>();
        RegisterType<LinearGradientBrush>();
        RegisterType<RadialGradientBrush>();
        RegisterType<Microsoft.Maui.Controls.Shapes.RoundRectangle>();
        RegisterType<Microsoft.Maui.Graphics.Color>();
        RegisterType<Thickness>();
        RegisterType<CornerRadius>();
        RegisterType<ItemsViewScrolledEventArgs>();
        RegisterType<ShellNavigatingEventArgs>();
        RegisterType<ShellNavigatedEventArgs>();
        RegisterType<ShellNavigationState>();
    }

    /// <summary>
    /// Keeps public property accessors and bindable-property fields for a custom control.
    /// This roots the requested type's inspection contract, not its assembly.
    /// </summary>
    public static void RegisterType<[DynamicallyAccessedMembers(Members)] T>()
        => Types.TryAdd(typeof(T), new Registration(typeof(T)));

    internal static PropertyInfo? GetProperty(object instance, string name)
    {
        var runtimeType = instance.GetType();
        // Prefer the runtime property so hidden/overridden custom members keep the existing
        // behavior in non-trimmed hosts. Known controls and registered custom types are rooted.
        if (runtimeType.GetProperty(name,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } runtimeProperty)
            return runtimeProperty;
        for (var type = runtimeType; type != null; type = type.BaseType)
        {
            if (Types.TryGetValue(type, out var registration)
                && registration.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } property)
                return property;
        }
        return null;
    }

    internal static FieldInfo? GetBindableField(Type type, string name)
    {
        if (Types.TryGetValue(type, out var registration))
            return registration.Type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .FirstOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return type.GetField(name,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.IgnoreCase);
    }

    private sealed class Registration([DynamicallyAccessedMembers(Members)] Type type)
    {
        [DynamicallyAccessedMembers(Members)]
        public Type Type { get; } = type;
    }
}
