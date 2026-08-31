using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core.Properties;

internal static class MauiPropertyDiagnostics
{
    private const int IsDynamicResourceFlag = 1 << 2;
    private static readonly Lazy<RuntimeAccessor?> Runtime = new(CreateRuntimeAccessor);

    public static BindableProperty? FindBindableProperty(
        object element,
        PropertyInfo property,
        bool allowReflection)
    {
        if (TryGetKnownBindableProperty(element, property.Name, out var known))
            return known;

        if (!allowReflection)
            return null;

        var type = element.GetType();
        var fieldName = $"{property.Name}Property";
        while (type is not null)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                ?? type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(candidate => candidate.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if (field?.GetValue(null) is BindableProperty bindableProperty
                && bindableProperty.PropertyName.Equals(property.Name, StringComparison.OrdinalIgnoreCase))
            {
                return bindableProperty;
            }

            type = type.BaseType;
        }

        return null;
    }

    public static PropertyDiagnosticSnapshot Inspect(BindableObject bindable, BindableProperty property)
    {
        var runtime = Runtime.Value;
        if (runtime is null)
        {
            return bindable.IsSet(property)
                ? PropertyDiagnosticSnapshot.Unknown()
                : PropertyDiagnosticSnapshot.Safe(
                    PropertyValueSources.Default,
                    PropertyDiagnosticConfidenceKinds.Heuristic);
        }

        try
        {
            if (bindable is Element element
                && runtime.DynamicResourcesField?.GetValue(element) is IDictionary dynamicResources
                && dynamicResources.Contains(property))
            {
                return new PropertyDiagnosticSnapshot(
                    PropertyValueSources.DynamicResource,
                    PropertyDiagnosticConfidenceKinds.Runtime,
                    PropertyMutationSafetyKinds.DynamicResourceWouldBeReplaced,
                    "Changing this property with SetValue would remove its dynamic-resource registration for the current app session.");
            }

            var context = runtime.GetContext.Invoke(bindable, new object[] { property });
            if (context is null)
            {
                return PropertyDiagnosticSnapshot.Safe(
                    PropertyValueSources.Default,
                    PropertyDiagnosticConfidenceKinds.Runtime);
            }

            var bindings = runtime.BindingsField.GetValue(context);
            var bindingCount = bindings is null
                ? 0
                : Convert.ToInt32(runtime.BindingsCount.GetValue(bindings), CultureInfo.InvariantCulture);
            if (bindingCount > 0)
            {
                return new PropertyDiagnosticSnapshot(
                    PropertyValueSources.Binding,
                    PropertyDiagnosticConfidenceKinds.Runtime,
                    PropertyMutationSafetyKinds.BindingWouldBeReplaced,
                    "Changing this property with SetValue would remove its one-way binding for the current app session.");
            }

            var attributes = Convert.ToInt32(
                runtime.AttributesField.GetValue(context),
                CultureInfo.InvariantCulture);
            if ((attributes & IsDynamicResourceFlag) != 0)
            {
                return new PropertyDiagnosticSnapshot(
                    PropertyValueSources.DynamicResource,
                    PropertyDiagnosticConfidenceKinds.Runtime,
                    PropertyMutationSafetyKinds.DynamicResourceWouldBeReplaced,
                    "Changing this property with SetValue would remove its dynamic-resource registration for the current app session.");
            }

            var valueSource = runtime.GetValueSource.Invoke(null, new object[] { bindable, property });
            if (valueSource is null)
                return PropertyDiagnosticSnapshot.Unknown();

            var baseSource = runtime.BaseValueSource.GetValue(valueSource)?.ToString();
            var isExpression = runtime.IsExpression.GetValue(valueSource) as bool? == true;
            var isCurrent = runtime.IsCurrent.GetValue(valueSource) as bool? == true;

            if (isExpression)
                return PropertyDiagnosticSnapshot.Unknown("The runtime reports an expression-backed value whose exact source is unavailable.");
            if (isCurrent)
                return PropertyDiagnosticSnapshot.Safe(
                    PropertyValueSources.Handler,
                    PropertyDiagnosticConfidenceKinds.Runtime);

            return baseSource switch
            {
                "Default" => PropertyDiagnosticSnapshot.Safe(
                    PropertyValueSources.Default,
                    PropertyDiagnosticConfidenceKinds.Runtime),
                "Local" => PropertyDiagnosticSnapshot.Safe(
                    PropertyValueSources.Local,
                    PropertyDiagnosticConfidenceKinds.Runtime),
                "StyleTrigger" or "DefaultStyleTrigger" or "TemplateTrigger" or "ParentTemplateTrigger"
                    => PropertyDiagnosticSnapshot.Safe(
                        PropertyValueSources.Trigger,
                        PropertyDiagnosticConfidenceKinds.Runtime),
                "Style" or "DefaultStyle" or "ImplicitStyleReference" or "ParentTemplate"
                    => PropertyDiagnosticSnapshot.Safe(
                        PropertyValueSources.Style,
                        PropertyDiagnosticConfidenceKinds.Runtime),
                _ => PropertyDiagnosticSnapshot.Unknown()
            };
        }
        catch
        {
            return PropertyDiagnosticSnapshot.Unknown();
        }
    }

    private static RuntimeAccessor? CreateRuntimeAccessor()
    {
        try
        {
            var assembly = typeof(BindableObject).Assembly;
            var diagnosticsType = assembly.GetType(
                "Microsoft.Maui.Controls.Xaml.Diagnostics.BindablePropertyDiagnostics",
                throwOnError: false);
            var getValueSource = diagnosticsType?.GetMethod(
                "GetValueSource",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(BindableObject), typeof(BindableProperty) },
                modifiers: null);
            var getContext = typeof(BindableObject).GetMethod(
                "GetContext",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(BindableProperty) },
                modifiers: null);
            var contextType = getContext?.ReturnType;
            var attributesField = contextType?.GetField(
                "Attributes",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var bindingsField = contextType?.GetField(
                "Bindings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var bindingsCount = bindingsField?.FieldType.GetProperty(
                "Count",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var valueSourceType = getValueSource?.ReturnType;
            var baseValueSource = valueSourceType?.GetProperty("BaseValueSource");
            var isExpression = valueSourceType?.GetProperty("IsExpression");
            var isCurrent = valueSourceType?.GetProperty("IsCurrent");
            var dynamicResourcesField = typeof(Element).GetField(
                "_dynamicResources",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (getValueSource is null
                || getContext is null
                || attributesField is null
                || bindingsField is null
                || bindingsCount is null
                || baseValueSource is null
                || isExpression is null
                || isCurrent is null)
            {
                return null;
            }

            return new RuntimeAccessor(
                getValueSource,
                getContext,
                attributesField,
                bindingsField,
                bindingsCount,
                baseValueSource,
                isExpression,
                isCurrent,
                dynamicResourcesField);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetKnownBindableProperty(
        object element,
        string propertyName,
        out BindableProperty property)
    {
        property = propertyName switch
        {
            nameof(VisualElement.IsVisible) when element is VisualElement => VisualElement.IsVisibleProperty,
            nameof(VisualElement.IsEnabled) when element is VisualElement => VisualElement.IsEnabledProperty,
            nameof(VisualElement.Opacity) when element is VisualElement => VisualElement.OpacityProperty,
            nameof(VisualElement.BackgroundColor) when element is VisualElement => VisualElement.BackgroundColorProperty,
            nameof(Label.Text) when element is Label => Label.TextProperty,
            nameof(Label.TextColor) when element is Label => Label.TextColorProperty,
            nameof(Label.FontSize) when element is Label => Label.FontSizeProperty,
            nameof(Label.FontAttributes) when element is Label => Label.FontAttributesProperty,
            nameof(Label.HorizontalTextAlignment) when element is Label => Label.HorizontalTextAlignmentProperty,
            nameof(Label.LineBreakMode) when element is Label => Label.LineBreakModeProperty,
            nameof(Button.Text) when element is Button => Button.TextProperty,
            nameof(Button.TextColor) when element is Button => Button.TextColorProperty,
            nameof(Button.FontSize) when element is Button => Button.FontSizeProperty,
            nameof(InputView.Text) when element is Entry or Editor => InputView.TextProperty,
            nameof(Entry.Placeholder) when element is Entry => Entry.PlaceholderProperty,
            nameof(Editor.Placeholder) when element is Editor => Editor.PlaceholderProperty,
            nameof(InputView.TextColor) when element is Entry or Editor => InputView.TextColorProperty,
            nameof(SearchBar.Text) when element is SearchBar => SearchBar.TextProperty,
            nameof(SearchBar.Placeholder) when element is SearchBar => SearchBar.PlaceholderProperty,
            nameof(CheckBox.IsChecked) when element is CheckBox => CheckBox.IsCheckedProperty,
            nameof(CheckBox.Color) when element is CheckBox => CheckBox.ColorProperty,
            nameof(Switch.IsToggled) when element is Switch => Switch.IsToggledProperty,
            nameof(Switch.OnColor) when element is Switch => Switch.OnColorProperty,
            nameof(StackLayout.Spacing) when element is StackLayout => StackLayout.SpacingProperty,
            nameof(VerticalStackLayout.Spacing) when element is VerticalStackLayout => VerticalStackLayout.SpacingProperty,
            nameof(HorizontalStackLayout.Spacing) when element is HorizontalStackLayout => HorizontalStackLayout.SpacingProperty,
            _ => null!
        };

        return property is not null;
    }

    private sealed record RuntimeAccessor(
        MethodInfo GetValueSource,
        MethodInfo GetContext,
        FieldInfo AttributesField,
        FieldInfo BindingsField,
        PropertyInfo BindingsCount,
        PropertyInfo BaseValueSource,
        PropertyInfo IsExpression,
        PropertyInfo IsCurrent,
        FieldInfo? DynamicResourcesField);
}
