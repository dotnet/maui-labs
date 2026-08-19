using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Chat.Controls;

/// <summary>
/// Maps a <see cref="ChatContentItem"/> to the view that renders it: <see cref="When"/> decides whether
/// the template applies and <see cref="ViewType"/> says what to create.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ChatContentTemplateSelector"/> asks every registered template and picks the highest
/// priority match. The <see cref="DataTemplate"/> returned by <see cref="GetTemplate"/> is created once
/// and cached: MAUI relies on a stable template instance to recycle cells, so a new template must never
/// be produced per selection.
/// </para>
/// <para>
/// Views are created through the MAUI service provider when one is available, so a custom view can take
/// constructor dependencies; otherwise a public parameterless constructor is used.
/// </para>
/// </remarks>
public abstract class ChatContentTemplate : BindableObject
{
    /// <summary>Backing property for <see cref="ViewType"/>.</summary>
    public static readonly BindableProperty ViewTypeProperty =
        BindableProperty.Create(
            nameof(ViewType),
            typeof(Type),
            typeof(ChatContentTemplate),
            propertyChanged: static (bindable, _, _) => ((ChatContentTemplate)bindable).InvalidateTemplate());

    /// <summary>Backing property for <see cref="Priority"/>.</summary>
    public static readonly BindableProperty PriorityProperty =
        BindableProperty.Create(nameof(Priority), typeof(int), typeof(ChatContentTemplate), 0);

    private DataTemplate? _cachedTemplate;

    /// <summary>Gets or sets the <see cref="View"/> type this template creates.</summary>
    public Type? ViewType
    {
        get => (Type?)GetValue(ViewTypeProperty);
        set => SetValue(ViewTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection priority. Within a tier the highest priority wins and declaration
    /// order breaks ties. Defaults to <c>0</c>.
    /// </summary>
    public int Priority
    {
        get => (int)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    /// <summary>Gets whether this template renders <paramref name="item"/>.</summary>
    /// <param name="item">The row being rendered.</param>
    /// <returns><see langword="true"/> when this template applies.</returns>
    public abstract bool When(ChatContentItem item);

    /// <summary>Gets the effective priority for <paramref name="item"/>. Override to boost more specific matches.</summary>
    /// <param name="item">The row being rendered.</param>
    /// <returns>The priority to compare against other matching templates.</returns>
    public virtual int GetPriority(ChatContentItem item) => Priority;

    /// <summary>Gets the cached <see cref="DataTemplate"/> for this template. The same instance is returned every time.</summary>
    /// <returns>The stable data template.</returns>
    /// <exception cref="InvalidOperationException"><see cref="ViewType"/> is not set and <see cref="CreateTemplate"/> was not overridden.</exception>
    public DataTemplate GetTemplate() => _cachedTemplate ??= CreateTemplate();

    /// <summary>Creates the data template. Called at most once per <see cref="ViewType"/> change.</summary>
    /// <returns>The new data template.</returns>
    /// <exception cref="InvalidOperationException"><see cref="ViewType"/> is not set.</exception>
    protected virtual DataTemplate CreateTemplate()
    {
        var viewType = ViewType
            ?? throw new InvalidOperationException(
                $"{GetType().Name} has no {nameof(ViewType)} set. Set one, or override {nameof(CreateTemplate)}.");

        return new DataTemplate(() => PrepareView(CreateView(viewType)));
    }

    /// <summary>Drops the cached template so the next <see cref="GetTemplate"/> call rebuilds it.</summary>
    protected void InvalidateTemplate() => _cachedTemplate = null;

    /// <summary>
    /// Creates a view instance, resolving constructor dependencies from <paramref name="services"/> or the
    /// application's service provider when one is available.
    /// </summary>
    /// <param name="viewType">The view type to create. Must derive from <see cref="View"/>.</param>
    /// <param name="services">An explicit service provider, or <see langword="null"/> to use the application's.</param>
    /// <returns>The created view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="viewType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The type is not a view, or it could not be created.</exception>
    public static View CreateView(Type viewType, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(viewType);

        if (!typeof(View).IsAssignableFrom(viewType))
            throw new InvalidOperationException($"{viewType.Name} must derive from {nameof(View)}.");

        services ??= Application.Current?.Handler?.MauiContext?.Services;

        if (services is not null)
        {
            try
            {
                return (View)ActivatorUtilities.CreateInstance(services, viewType);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Could not create '{viewType.Name}'. Register the view and its constructor dependencies in DI, or give it a public parameterless constructor.",
                    ex);
            }
        }

        try
        {
            return (View)Activator.CreateInstance(viewType)!;
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Could not create '{viewType.Name}' because no service provider is available and the view has no public parameterless constructor.",
                ex);
        }
    }

    /// <summary>
    /// Binds a freshly created view to the row it renders. A <see cref="ChatContentView"/> gets its
    /// <see cref="ChatContentView.Item"/> bound to the binding context.
    /// </summary>
    /// <typeparam name="T">The view type.</typeparam>
    /// <param name="view">The view to prepare.</param>
    /// <returns>The same <paramref name="view"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is <see langword="null"/>.</exception>
    protected static T PrepareView<T>(T view)
        where T : View
    {
        ArgumentNullException.ThrowIfNull(view);

        if (view is ChatContentView contentView)
            contentView.SetBinding(ChatContentView.ItemProperty, new Binding("."));

        return view;
    }

    /// <summary>
    /// Wraps a custom message body in the standard avatar/name/alignment/metadata chrome. A
    /// <see cref="ChatBubbleView"/> already owns that chrome and is returned unchanged.
    /// </summary>
    /// <param name="body">The message body.</param>
    /// <param name="presentationOverride">
    /// An optional standard-bubble override; <see langword="null"/> uses the content's presentation.
    /// </param>
    /// <returns>The prepared message view.</returns>
    protected static View WrapInMessageChrome(
        View body,
        ChatContentPresentation? presentationOverride = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        var preparedBody = PrepareView(body);
        if (preparedBody is ChatBubbleView bubble)
        {
            bubble.PresentationOverride = presentationOverride;
            return bubble;
        }

        return PrepareView(new ChatTemplatedBubbleView(
            preparedBody,
            presentationOverride));
    }
}
