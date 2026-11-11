namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>Creates chat views attached to a window, so they load like they would in an app.</summary>
internal static class ChatHost
{
    public static AIChatView Create(Action<AIChatView>? configure = null)
    {
        var view = new AIChatView();
        configure?.Invoke(view);
        Load(view);
        return view;
    }

    public static AIChatView Create(AgentContext session, Action<AIChatView>? configure = null) =>
        Create(view =>
        {
            view.Session = session;
            configure?.Invoke(view);
        });

    public static Window Load(View view) => new(new ContentPage { Content = view });

    public static void Unload(View view)
    {
        if (view.Window is { } window)
            window.Page = null;
    }

    public static AgentContext Session(ScriptedChatClient client, Action<UIAgentOptions>? configure = null) =>
        new(new UIAgent(client, configure));

    public static IReadOnlyList<ChatRow> Rows(this AIChatView view) => view.MessageList.Rows;

    public static T ByAutomationId<T>(this Element root, string automationId)
        where T : VisualElement =>
        root.Descendants<T>().FirstOrDefault(element => element.AutomationId == automationId)
            ?? throw new InvalidOperationException($"No {typeof(T).Name} with AutomationId '{automationId}'.");

    /// <summary>Creates the item view the message list would create for a row.</summary>
    public static View Realize(this AIChatView view, ChatRow row)
    {
        var template = view.MessageList.ItemTemplate is DataTemplateSelector selector
            ? selector.SelectTemplate(row, view.MessageList)
            : view.MessageList.ItemTemplate!;
        var item = (View)template.CreateContent();

        // Like a realized cell: inside the list, so theme resources resolve.
        item.Parent = view.MessageList;
        item.BindingContext = row;
        return item;
    }

    public static IEnumerable<T> Descendants<T>(this Element element) =>
        element.GetVisualTreeDescendants().OfType<T>();
}
