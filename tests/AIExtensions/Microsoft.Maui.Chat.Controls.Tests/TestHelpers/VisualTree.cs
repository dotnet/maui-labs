namespace Microsoft.Maui.Chat.Controls.Tests.TestHelpers;

/// <summary>Walks a control's visual tree so tests can assert on what a view actually built.</summary>
internal static class VisualTree
{
    public static IEnumerable<T> Descendants<T>(Element root)
        where T : Element
    {
        foreach (var child in Children(root))
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    public static T Single<T>(Element root, Func<T, bool>? predicate = null)
        where T : Element
    {
        var matches = Descendants<T>(root).Where(predicate ?? (static _ => true)).ToList();
        Assert.Single(matches);
        return matches[0];
    }

    public static IReadOnlyList<T> All<T>(Element root)
        where T : Element => [.. Descendants<T>(root)];

    private static IEnumerable<Element> Children(Element element)
    {
        switch (element)
        {
            case ContentView contentView when contentView.Content is not null:
                yield return contentView.Content;
                break;

            case Border border when border.Content is Element content:
                yield return content;
                break;

            case ContentPresenter presenter when presenter.Content is not null:
                yield return presenter.Content;
                break;

            case Layout layout:
                foreach (var child in layout.Children)
                {
                    if (child is Element childElement)
                        yield return childElement;
                }

                break;
        }
    }
}
