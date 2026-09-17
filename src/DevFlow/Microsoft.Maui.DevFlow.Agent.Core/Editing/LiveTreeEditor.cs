using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;

namespace Microsoft.Maui.DevFlow.Agent.Core.Editing;

/// <summary>
/// A structural edit that cannot be applied to the live tree. <see cref="Reason"/> is the
/// machine-readable error code returned to the client.
/// </summary>
internal sealed class LiveTreeEditException(string message, string reason) : InvalidOperationException(message)
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Adds, removes and moves views in the running MAUI visual tree. Children are placed through
/// <see cref="Layout.Children"/> or the parent's <see cref="ContentPropertyAttribute"/>, so any
/// container works without a type-specific table.
/// </summary>
internal static class LiveTreeEditor
{
    internal const string MauiNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";
    internal const string XamlNamespace = "http://schemas.microsoft.com/winfx/2009/xaml";

    /// <summary>
    /// Inflates a single view from a XAML snippet such as <c>&lt;Label Text="Hi" /&gt;</c>. The
    /// default MAUI and <c>x:</c> namespaces are supplied when the snippet does not declare them.
    /// </summary>
    public static View InflateView(string xaml)
    {
        var snippet = StripXmlDeclaration(xaml);
        if (string.IsNullOrWhiteSpace(snippet))
            throw new LiveTreeEditException("xaml is required", "invalid-xaml");

        var wrapped = $"<ContentView xmlns=\"{MauiNamespace}\" xmlns:x=\"{XamlNamespace}\">{snippet}</ContentView>";

        XElement root;
        try
        {
            root = XDocument.Parse(wrapped, LoadOptions.SetLineInfo).Root!;
        }
        catch (XmlException ex)
        {
            throw new LiveTreeEditException($"Invalid XAML: {ex.Message}", "invalid-xaml");
        }

        var elements = root.Elements().ToList();
        if (elements.Count != 1 || root.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
            throw new LiveTreeEditException("XAML must contain exactly one root view", "invalid-xaml");

        if (elements[0].Attribute(XName.Get("Class", XamlNamespace)) is not null)
            throw new LiveTreeEditException("A snippet cannot declare x:Class; use XAML reload for pages and views", "invalid-xaml");

        var host = new ContentView();
        try
        {
            host.LoadFromXaml(wrapped);
        }
        catch (XamlParseException ex)
        {
            throw new LiveTreeEditException($"Invalid XAML: {ex.Message}", "invalid-xaml");
        }

        if (host.Content is not { } view)
            throw new LiveTreeEditException("XAML did not produce a view", "invalid-xaml");

        host.Content = null;
        return view;
    }

    /// <summary>Inserts <paramref name="view"/> into <paramref name="parent"/>.</summary>
    public static void Insert(Element parent, View view, int? index)
    {
        if (parent is Layout layout)
        {
            var position = index is { } i && i >= 0 && i <= layout.Children.Count ? i : layout.Children.Count;
            layout.Children.Insert(position, view);
            return;
        }

        var property = GetContentProperty(parent)
            ?? throw new LiveTreeEditException($"{parent.GetType().Name} cannot hold child views", "not-a-container");

        if (property.GetValue(parent) is { } existing && !ReferenceEquals(existing, view))
        {
            throw new LiveTreeEditException(
                $"{parent.GetType().Name}.{property.Name} already has content; remove it first or add into it",
                "content-occupied");
        }

        property.SetValue(parent, view);
    }

    /// <summary>Removes <paramref name="view"/> from its parent and returns that parent.</summary>
    public static Element Detach(View view)
    {
        var parent = view.Parent
            ?? throw new LiveTreeEditException($"{view.GetType().Name} is not attached to a parent", "no-parent");

        if (parent is Layout layout)
        {
            if (!layout.Children.Remove(view))
                throw new LiveTreeEditException($"{view.GetType().Name} is not a child of {parent.GetType().Name}", "no-parent");
            return parent;
        }

        if (GetContentProperty(parent) is { } property && ReferenceEquals(property.GetValue(parent), view))
        {
            property.SetValue(parent, null);
            return parent;
        }

        throw new LiveTreeEditException($"Cannot remove an element from {parent.GetType().Name}", "not-removable");
    }

    /// <summary>
    /// Moves <paramref name="view"/> to <paramref name="newParent"/> at <paramref name="index"/>.
    /// Within the same layout the index is the element's final position.
    /// </summary>
    public static void Move(View view, Element newParent, int? index)
    {
        for (Element? ancestor = newParent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, view))
                throw new LiveTreeEditException("Cannot move an element into itself or its descendants", "invalid-target");
        }

        if (newParent is not Layout && GetContentProperty(newParent) is null)
            throw new LiveTreeEditException($"{newParent.GetType().Name} cannot hold child views", "not-a-container");

        // remember where it was so a failed insert restores the original order, not just the parent
        var oldIndex = IndexInParent(view);
        var oldParent = Detach(view);
        try
        {
            Insert(newParent, view, index);
        }
        catch
        {
            // put it back rather than leave the element orphaned
            Insert(oldParent, view, oldIndex);
            throw;
        }
    }

    /// <summary>Position of <paramref name="view"/> among its parent's children, or null for single content.</summary>
    public static int? IndexInParent(View view)
        => view.Parent is Layout layout ? layout.Children.IndexOf(view) : null;

    /// <summary>The parent's <see cref="ContentPropertyAttribute"/> property when it holds a single view.</summary>
    internal static PropertyInfo? GetContentProperty(Element parent)
    {
        for (var type = parent.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetCustomAttribute<ContentPropertyAttribute>(inherit: false) is not { } attribute)
                continue;

            var property = FindMostDerivedProperty(parent.GetType(), attribute.Name);
            return property is { CanWrite: true } && property.PropertyType.IsAssignableFrom(typeof(View))
                ? property
                : null;
        }
        return null;
    }

    // GetProperty(name) throws AmbiguousMatchException when a subclass hides the property with `new`.
    static PropertyInfo? FindMostDerivedProperty(Type type, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property is not null)
                return property;
        }
        return null;
    }

    static string StripXmlDeclaration(string xaml)
    {
        var text = xaml.Trim().TrimStart('\uFEFF');
        if (text.StartsWith("<?xml", StringComparison.Ordinal))
        {
            var end = text.IndexOf("?>", StringComparison.Ordinal);
            if (end >= 0)
                text = text[(end + 2)..].TrimStart();
        }
        return text;
    }
}
