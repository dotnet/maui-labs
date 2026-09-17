using System.CodeDom.Compiler;
using System.Collections.Concurrent;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Internals;
using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.DevFlow.Agent.Core.Editing;

/// <summary>
/// XAML reload without an IDE: re-inflates live pages and views from new XAML text so a design
/// tool can apply a file edit to the running app.
/// </summary>
internal static class XamlReloader
{
    static readonly ConcurrentDictionary<Type, FieldInfo[]> s_generatedFields = new();

    /// <summary>Reads <c>x:Class</c> from a XAML document.</summary>
    public static string? GetClassName(string xaml)
    {
        try
        {
            return XDocument.Parse(Normalize(xaml)).Root?
                .Attribute(XName.Get("Class", LiveTreeEditor.XamlNamespace))?.Value;
        }
        catch (XmlException ex)
        {
            throw new LiveTreeEditException($"Invalid XAML: {ex.Message}", "invalid-xaml");
        }
    }

    /// <summary>Every live instance of <paramref name="className"/> across all windows.</summary>
    public static IReadOnlyList<Element> FindLiveInstances(Application app, string className)
    {
        var found = new HashSet<Element>(ReferenceEqualityComparer.Instance);

        // Pages that are not currently visible (navigation stacks, unselected tabs, modals) are not
        // in the visual tree, so walk the page containers as well as the tree.
        foreach (var window in app.Windows)
        {
            if (window.Page is { } page)
                CollectPages(page, className, found);
        }

        foreach (var descendant in ((IVisualTreeElement)app).GetVisualTreeDescendants())
        {
            if (descendant is Element element && element.GetType().FullName == className)
                found.Add(element);
        }
        return found.ToList();
    }

    /// <summary>Replaces <paramref name="root"/>'s content with a fresh inflation of <paramref name="xaml"/>.</summary>
    public static void Reload(Element root, string xaml)
    {
        xaml = Normalize(xaml);

        // Inflation appends to what is already there, so the root has to start empty - otherwise the
        // load throws "key already exists" / "name already registered", or duplicates children. The
        // undo actions put the old state back when inflation fails, so invalid XAML leaves the
        // running view as it was instead of gutted.
        var undo = new List<Action>();

        if (root is VisualElement visual)
        {
            var resources = visual.Resources;
            undo.Add(() => visual.Resources = resources);
            visual.Resources = new ResourceDictionary();

            Clear(visual.Behaviors, undo);
            Clear(visual.Triggers, undo);
        }
        if (root is View view)
            Clear(view.GestureRecognizers, undo);
        if (root is Page page)
            Clear(page.ToolbarItems, undo);
        if (root is Layout layout)
            Clear(layout.Children, undo);
        if (root is Shell shell)
            Clear(shell.Items, undo);
        if (root is MultiPage<Page> multi)
            Clear(multi.Children, undo);

        // A single-content root (ContentPage, ContentView, Border, ScrollView, ...) would otherwise
        // keep its old child when the new XAML no longer declares one.
        if (LiveTreeEditor.GetContentProperty(root) is { } contentProperty)
        {
            var content = contentProperty.GetValue(root);
            undo.Add(() => contentProperty.SetValue(root, content));
            contentProperty.SetValue(root, null);
        }

        // NameScope.SetNameScope is a no-op once a scope exists, so set the attached property directly.
        var nameScope = root.GetValue(NameScope.NameScopeProperty);
        undo.Add(() => root.SetValue(NameScope.NameScopeProperty, nameScope));
        root.SetValue(NameScope.NameScopeProperty, new NameScope());

        try
        {
            root.LoadFromXaml(xaml);
        }
        catch
        {
            for (var i = undo.Count - 1; i >= 0; i--)
                undo[i]();
            throw;
        }

        // Re-point the generated x:Name fields so code-behind keeps working against the new elements.
        // A name the new document no longer declares is cleared rather than left pointing at the
        // element that was just detached.
        foreach (var field in GetGeneratedFields(root.GetType()))
        {
            var named = root.FindByName(field.Name);
            field.SetValue(root, named is not null && field.FieldType.IsInstanceOfType(named) ? named : null);
        }
    }

    // Empties a collection and records how to put its items back, dropping anything a failed
    // inflation appended in the meantime.
    static void Clear<T>(IList<T> collection, List<Action> undo)
    {
        var saved = collection.ToArray();
        undo.Add(() =>
        {
            collection.Clear();
            foreach (var item in saved)
                collection.Add(item);
        });
        collection.Clear();
    }

    static void CollectPages(Page page, string className, HashSet<Element> found)
    {
        if (page.GetType().FullName == className)
            found.Add(page);

        switch (page)
        {
            case Shell shell:
                foreach (var item in shell.Items)
                {
                    foreach (var section in item.Items)
                    {
                        foreach (var stacked in section.Stack)
                        {
                            if (stacked is not null)
                                CollectPages(stacked, className, found);
                        }
                        foreach (var content in section.Items)
                        {
                            if (((IShellContentController)content).Page is { } contentPage)
                                CollectPages(contentPage, className, found);
                        }
                    }
                }
                break;

            case NavigationPage navigation:
                foreach (var stacked in navigation.Navigation.NavigationStack)
                    CollectPages(stacked, className, found);
                break;

            case FlyoutPage flyout:
                CollectPages(flyout.Flyout, className, found);
                CollectPages(flyout.Detail, className, found);
                break;

            case IPageContainer<Page> container when container.CurrentPage is not null:
                CollectPages(container.CurrentPage, className, found);
                break;
        }

        if (page is MultiPage<Page> multi)
        {
            foreach (var child in multi.Children)
                CollectPages(child, className, found);
        }

        foreach (var modal in page.Navigation.ModalStack)
            CollectPages(modal, className, found);
    }

    static FieldInfo[] GetGeneratedFields(Type type)
        => s_generatedFields.GetOrAdd(type, static t => t
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(f => typeof(Element).IsAssignableFrom(f.FieldType) && f.GetCustomAttribute<GeneratedCodeAttribute>() is not null)
            .ToArray());

    // Files saved by Visual Studio start with a BOM, which XmlReader rejects in a string.
    static string Normalize(string xaml) => xaml.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
}
