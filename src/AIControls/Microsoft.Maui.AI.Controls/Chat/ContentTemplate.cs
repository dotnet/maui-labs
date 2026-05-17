using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Controls.Chat;

/// <summary>
/// Maps a <see cref="ContentContext"/> to a view type via the <see cref="When"/> predicate.
/// Declare in XAML inside <c>ChatPanelControl.ContentTemplates</c>.
/// </summary>
public abstract class ContentTemplate : BindableObject
{
    public static readonly BindableProperty ViewTypeProperty =
        BindableProperty.Create(nameof(ViewType), typeof(Type), typeof(ContentTemplate));

    public static readonly BindableProperty PriorityProperty =
        BindableProperty.Create(nameof(Priority), typeof(int), typeof(ContentTemplate), 0);

    public Type? ViewType
    {
        get => (Type?)GetValue(ViewTypeProperty);
        set => SetValue(ViewTypeProperty, value);
    }

    /// <summary>
    /// Gets the selection priority for this template. Higher priorities win over lower priorities.
    /// Templates with the same priority preserve declaration order.
    /// </summary>
    public int Priority
    {
        get => (int)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    /// <summary>Return true if this template should handle the given content.</summary>
    public abstract bool When(ContentContext context);

    private DataTemplate? _cachedTemplate;

    /// <summary>
    /// Returns a cached DataTemplate for the ViewType. MAUI requires the same
    /// instance on repeated calls to avoid memory leaks and broken virtualization.
    /// Subclasses can override to customize template creation (e.g. to compose wrapper + inner content).
    /// </summary>
    internal virtual DataTemplate GetTemplate()
    {
        var type = ViewType ?? throw new InvalidOperationException($"{GetType().Name} has no ViewType set.");
        return _cachedTemplate ??= new DataTemplate(() => PrepareDataTemplateView(CreateView(type)));
    }

    internal virtual int GetPriority(ContentContext context) => Priority;

    internal static View CreateView(Type type, IServiceProvider? services = null)
    {
        if (!typeof(View).IsAssignableFrom(type))
            throw new InvalidOperationException($"{type.Name} must derive from {nameof(View)}.");

        services ??= Application.Current?.Handler?.MauiContext?.Services;

        if (services is not null)
        {
            try
            {
                return (View)ActivatorUtilities.CreateInstance(services, type);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Could not create '{type.Name}'. Register the view and any constructor dependencies in DI, or provide a public parameterless constructor.",
                    ex);
            }
        }

        try
        {
            return (View)Activator.CreateInstance(type)!;
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Could not create '{type.Name}' because the MAUI service provider is unavailable and the view has no public parameterless constructor.",
                ex);
        }
    }

    internal static T PrepareDataTemplateView<T>(T view)
        where T : View
    {
        if (view is ContentContextView contextView)
            contextView.SetBinding(ContentContextView.ContentContextProperty, new Binding("."));

        return view;
    }
}
