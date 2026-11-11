namespace Microsoft.Maui.AI.Chat.Controls;

// The built-in parts of AIChatView are made of these types rather than the standard controls, so the app's implicit
// styles (a Button or Label style in App.xaml, as in the default MAUI template) leave them alone: an implicit style
// applies to its target type and, only when it sets ApplyToDerivedTypes, to derived types. The theme gives each of
// these types an empty implicit style of its own, which takes precedence over styles for their base types (see
// ChatElements.Types). Content the app supplies keeps the app's styles.
//
// Each type also makes sure the application has the theme before it is constructed (see ChatElements.EnsureTheme).

internal sealed class ChatActivityIndicator : ActivityIndicator
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatBorder : Border
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatBoxView : BoxView
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatButton : Button
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatContentView : ContentView
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatEditor : Editor
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatFlexLayout : FlexLayout
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatGrid : Grid
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatHorizontalStackLayout : HorizontalStackLayout
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatLabel : Label
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatScrollView : ScrollView
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatSpan : Span
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal sealed class ChatVerticalStackLayout : VerticalStackLayout
{
    private readonly bool _themed = ChatElements.EnsureTheme();
}

internal static class ChatElements
{
    /// <summary>
    /// Gets every element type the built-in parts are made of. The theme adds an empty implicit style for each: an
    /// element takes the implicit style of its own type before any style for a base type, so the app's styles with
    /// <see cref="Style.ApplyToDerivedTypes"/> don't reach the built-in parts either.
    /// </summary>
    public static IReadOnlyList<Type> Types { get; } =
    [
        typeof(ChatActivityIndicator),
        typeof(ChatBorder),
        typeof(ChatBoxView),
        typeof(ChatButton),
        typeof(ChatContentView),
        typeof(ChatEditor),
        typeof(ChatFlexLayout),
        typeof(ChatGrid),
        typeof(ChatHorizontalStackLayout),
        typeof(ChatImage),
        typeof(ChatLabel),
        typeof(ChatScrollView),
        typeof(ChatSpan),
        typeof(ChatVerticalStackLayout),
        typeof(ChatMessageList),
        typeof(BlockHost),
        typeof(TurnFooterHost),
        typeof(UserMessageView),
        typeof(ThinkingView),
        typeof(TurnStoppedView),
        typeof(TurnNotSentView),
        typeof(TurnErrorView),
        typeof(TextBlockView),
        typeof(RichTextBlockView),
        typeof(ToolCallView),
        typeof(ToolApprovalView),
        typeof(UIActionView),
        typeof(ReasoningView),
        typeof(MediaBlockView),
        typeof(LinkedImageView),
        typeof(MarkdownView),
        typeof(MarkdownRenderer.LineView),
    ];

    /// <summary>
    /// Merges the theme into the application resources, if there is an application and it lacks the theme. Every
    /// built-in element type calls this from a field initializer, which runs before the base constructors look up the
    /// element's implicit styles: the element then finds its own empty style there and never takes an app style for a
    /// base type, even when the view was created before the application or the app replaced its resources. (MAUI
    /// leaves the visual-state setters of an implicit style applied at construction behind when it is replaced.)
    /// </summary>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool EnsureTheme()
    {
        AIChatTheme.EnsureLoaded();
        return true;
    }

    /// <summary>Adds the empty implicit style of every built-in element type to <paramref name="resources"/>.</summary>
    public static void AddImplicitStyles(ResourceDictionary resources)
    {
        foreach (var type in Types)
            resources.Add(new Style(type));
    }
}
