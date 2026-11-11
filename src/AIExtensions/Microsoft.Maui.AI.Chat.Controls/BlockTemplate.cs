using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// Chooses how <see cref="AIChatView"/> renders a <see cref="ContentBlock"/>: a tool call, an approval, reasoning,
/// assistant text, or any block type of your own.
/// </summary>
/// <remarks>
/// <para>
/// Add templates to <see cref="AIChatView.ContentTemplates"/>. A template matches a block when every filter
/// that is set matches (<see cref="BlockType"/>, <see cref="ToolName"/>, <see cref="Role"/>) and
/// <see cref="When"/> returns <see langword="true"/>. Your templates always win over the built-in ones. Among
/// your matching templates the highest <see cref="Priority"/> wins, then the most specific (a tool name beats a
/// block type, a derived block type beats its base type, and a block type beats a role), then the one declared
/// last.
/// </para>
/// <para>
/// The view is created from <see cref="ViewType"/> (through dependency injection when the app's service
/// provider is available, otherwise its public parameterless constructor) or from the inline
/// <see cref="Template"/>. Its <see cref="BindableObject.BindingContext"/> is the block itself and is refreshed
/// whenever the block changes, so bindings such as <c>{Binding Result.Result}</c> stay live while a response
/// streams. Derive the view from <see cref="BlockView"/> to also get the session and a change callback.
/// </para>
/// <para>
/// Request blocks (what the user sent) render as the built-in user message unless a template whose
/// <see cref="Role"/> is <c>"user"</c> (or that otherwise matches them) claims them.
/// </para>
/// </remarks>
[ContentProperty(nameof(Template))]
public class BlockTemplate : BindableObject
{
    /// <summary>Backing property for <see cref="BlockType"/>.</summary>
    public static readonly BindableProperty BlockTypeProperty =
        BindableProperty.Create(nameof(BlockType), typeof(Type), typeof(BlockTemplate));

    /// <summary>Backing property for <see cref="ToolName"/>.</summary>
    public static readonly BindableProperty ToolNameProperty =
        BindableProperty.Create(nameof(ToolName), typeof(string), typeof(BlockTemplate));

    /// <summary>Backing property for <see cref="Role"/>.</summary>
    public static readonly BindableProperty RoleProperty =
        BindableProperty.Create(nameof(Role), typeof(string), typeof(BlockTemplate));

    /// <summary>Backing property for <see cref="ViewType"/>.</summary>
    public static readonly BindableProperty ViewTypeProperty =
        BindableProperty.Create(
            nameof(ViewType),
            typeof(Type),
            typeof(BlockTemplate),
            propertyChanged: static (bindable, _, _) => ((BlockTemplate)bindable)._dataTemplate = null);

    /// <summary>Backing property for <see cref="Template"/>.</summary>
    public static readonly BindableProperty TemplateProperty =
        BindableProperty.Create(
            nameof(Template),
            typeof(DataTemplate),
            typeof(BlockTemplate),
            propertyChanged: static (bindable, _, _) => ((BlockTemplate)bindable)._dataTemplate = null);

    /// <summary>Backing property for <see cref="Priority"/>.</summary>
    public static readonly BindableProperty PriorityProperty =
        BindableProperty.Create(nameof(Priority), typeof(int), typeof(BlockTemplate), 0);

    private DataTemplate? _dataTemplate;

    /// <summary>Initializes a new instance of the <see cref="BlockTemplate"/> class.</summary>
    public BlockTemplate()
    {
        PropertyChanged += static (sender, e) =>
        {
            if (e.PropertyName != BindingContextProperty.PropertyName)
                ((BlockTemplate)sender!).Version++;
        };
    }

    /// <summary>
    /// Changes whenever a property that affects matching or the created view changes, so rows recreate their
    /// views for an edited template even when it still wins (or was edited while the chat view was unloaded).
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>
    /// Gets or sets the block type to match, including derived types — for example
    /// <c>{x:Type chat:FunctionInvocationContentBlock}</c> or a type generated with <c>[ToolBlock]</c>.
    /// </summary>
    public Type? BlockType
    {
        get => (Type?)GetValue(BlockTypeProperty);
        set => SetValue(BlockTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the tool name to match (case-insensitive) on tool calls, approvals, and UI actions.
    /// </summary>
    public string? ToolName
    {
        get => (string?)GetValue(ToolNameProperty);
        set => SetValue(ToolNameProperty, value);
    }

    /// <summary>Gets or sets the role to match (case-insensitive), such as <c>"user"</c> or <c>"assistant"</c>.</summary>
    public string? Role
    {
        get => (string?)GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>Gets or sets the type of <see cref="View"/> to create for a matching block.</summary>
    public Type? ViewType
    {
        get => (Type?)GetValue(ViewTypeProperty);
        set => SetValue(ViewTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets an inline template for a matching block. Used when <see cref="ViewType"/> is not set.
    /// A <see cref="DataTemplateSelector"/> is evaluated against the block.
    /// </summary>
    public DataTemplate? Template
    {
        get => (DataTemplate?)GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>Gets or sets the priority among matching templates. Higher wins. Defaults to 0.</summary>
    public int Priority
    {
        get => (int)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }

    /// <summary>Creates the view for built-in templates.</summary>
    internal Func<View>? Factory { get; init; }

    /// <summary>Gets whether this template can produce a view.</summary>
    internal bool CanRender => Factory is not null || ViewType is not null || Template is not null;

    /// <summary>Gets whether a block matches this template.</summary>
    /// <param name="block">The block to test.</param>
    /// <returns>
    /// <see langword="true"/> when every filter that is set matches. Override to add conditions, and call the
    /// base implementation to keep the filters.
    /// </returns>
    public virtual bool When(ContentBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (BlockType is { } blockType && !blockType.IsInstanceOfType(block))
            return false;

        if (!string.IsNullOrEmpty(ToolName) &&
            !string.Equals(GetToolName(block), ToolName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Role) &&
            !string.Equals(block.Role?.Value, Role, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>Compares how specific two matching templates are: tool name, then type depth, then role.</summary>
    internal (int ToolName, int TypeDepth, int Role) Specificity =>
        (string.IsNullOrEmpty(ToolName) ? 0 : 1, GetTypeDepth(BlockType), string.IsNullOrEmpty(Role) ? 0 : 1);

    /// <summary>Gets the shared row template. Its views host one block at a time.</summary>
    internal DataTemplate GetDataTemplate() =>
        _dataTemplate ??= new DataTemplate(() => new BlockHost(this));

    /// <summary>Creates the inner view for a block.</summary>
    internal View CreateView(ContentBlock block, BindableObject container, IServiceProvider? services)
    {
        if (Factory is { } factory)
            return factory();

        if (ViewType is { } viewType)
            return CreateView(viewType, services);

        var template = Template
            ?? throw new InvalidOperationException($"{nameof(BlockTemplate)} has neither a {nameof(ViewType)} nor a {nameof(Template)}.");
        if (template is DataTemplateSelector selector)
            template = selector.SelectTemplate(block, container);

        return template.CreateContent() switch
        {
            View view => view,
            var other => throw new InvalidOperationException(
                $"The {nameof(Template)} of a {nameof(BlockTemplate)} must create a View, not '{other?.GetType().Name ?? "null"}'."),
        };
    }

    /// <summary>
    /// Creates a view, resolving constructor dependencies from <paramref name="services"/> or the application's
    /// service provider when one is available.
    /// </summary>
    internal static View CreateView(Type viewType, IServiceProvider? services = null)
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

    internal static string? GetToolName(ContentBlock block) =>
        block switch
        {
            FunctionInvocationContentBlock function => function.ToolName,
            InteractiveFunctionBlock interactive => interactive.ToolName,
            _ => null,
        };

    private static int GetTypeDepth(Type? type)
    {
        var depth = 0;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            depth++;
        return depth;
    }
}
