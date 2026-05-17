using System.Globalization;

namespace Microsoft.Maui.AI.Controls.Chat;

[ContentProperty(nameof(Templates))]
public class ContentTemplateSelector : DataTemplateSelector
{
    private static readonly DataTemplate FallbackTemplate =
        new(() =>
        {
            var label = new Label
            {
                FontSize = 12,
                Padding = new Thickness(4),
                TextColor = Colors.Gray,
            };

            label.SetBinding(
                Label.TextProperty,
                new Binding(path: ".", converter: MissingTemplateTextConverter.Instance));

            return label;
        });

    public IList<ContentTemplate> Templates { get; } = new List<ContentTemplate>();

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ContentContext context)
            return FallbackTemplate;

        ContentTemplate? selectedTemplate = null;
        var highestPriority = int.MinValue;

        foreach (var template in Templates)
        {
            if (!template.When(context))
                continue;

            var priority = template.GetPriority(context);
            if (selectedTemplate is null || priority > highestPriority)
            {
                selectedTemplate = template;
                highestPriority = priority;
            }
        }

        return selectedTemplate?.GetTemplate() ?? FallbackTemplate;
    }

    private sealed class MissingTemplateTextConverter : IValueConverter
    {
        public static MissingTemplateTextConverter Instance { get; } = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is ContentContext context
                ? $"No content template registered for {context.Content.GetType().Name}."
                : "No content template registered.";

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
