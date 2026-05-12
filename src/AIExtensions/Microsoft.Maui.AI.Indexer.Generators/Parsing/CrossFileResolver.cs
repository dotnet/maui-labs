using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>
/// Resolves cross-file user control references by matching element type names
/// to known x:Class names from other parsed XAML files. Caches resolved results
/// so repeated references (e.g., CartView used in both CartPage and CartPane)
/// are parsed once and inlined everywhere.
/// </summary>
internal sealed class CrossFileResolver
{
    private readonly Dictionary<string, PageModel> _pagesByClassName;
    private readonly Dictionary<string, List<UiElement>> _resolvedCache = new(StringComparer.OrdinalIgnoreCase);

    public CrossFileResolver(IEnumerable<PageModel> allPages)
    {
        _pagesByClassName = new Dictionary<string, PageModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in allPages)
        {
            // Index by simple class name (e.g., "CartView")
            _pagesByClassName[page.ClassName] = page;
        }
    }

    /// <summary>
    /// Resolve all user control references in every page's element tree.
    /// Modifies pages in-place by replacing unresolved user control placeholders
    /// with their inlined semantic content.
    /// </summary>
    public void ResolveAll(List<PageModel> pages)
    {
        foreach (var page in pages)
        {
            page.Elements = ResolveElements(page.Elements, page.ClassName);
        }
    }

    private List<UiElement> ResolveElements(List<UiElement> elements, string ownerClassName)
    {
        var resolved = new List<UiElement>(elements.Count);

        foreach (var el in elements)
        {
            if (el.IsUserControlReference)
            {
                var inlined = ResolveUserControl(el.TypeName, ownerClassName);
                if (inlined != null)
                {
                    // Create a wrapper element showing the user control name
                    var wrapper = new UiElement
                    {
                        TypeName = el.TypeName,
                        IsUserControlReference = true,
                        Semantics = el.Semantics,
                        Condition = el.Condition,
                        Children = inlined,
                    };
                    resolved.Add(wrapper);
                }
                // If not resolved, skip (the control's XAML isn't available)
            }
            else
            {
                // Recursively resolve children
                if (el.Children.Count > 0)
                    el.Children = ResolveElements(el.Children, ownerClassName);

                // Also resolve inside templates
                if (el.ItemTemplate != null)
                    el.ItemTemplate = ResolveElements(el.ItemTemplate, ownerClassName);
                if (el.HeaderTemplate != null)
                    el.HeaderTemplate = ResolveElements(el.HeaderTemplate, ownerClassName);
                if (el.FooterTemplate != null)
                    el.FooterTemplate = ResolveElements(el.FooterTemplate, ownerClassName);
                if (el.GroupHeaderTemplate != null)
                    el.GroupHeaderTemplate = ResolveElements(el.GroupHeaderTemplate, ownerClassName);
                if (el.GroupFooterTemplate != null)
                    el.GroupFooterTemplate = ResolveElements(el.GroupFooterTemplate, ownerClassName);
                if (el.EmptyView != null)
                    el.EmptyView = ResolveElements(el.EmptyView, ownerClassName);
                if (el.BindableLayoutItemTemplate != null)
                    el.BindableLayoutItemTemplate = ResolveElements(el.BindableLayoutItemTemplate, ownerClassName);

                resolved.Add(el);
            }
        }

        return resolved;
    }

    private List<UiElement>? ResolveUserControl(string typeName, string ownerClassName)
    {
        // Prevent self-referencing loops
        if (string.Equals(typeName, ownerClassName, StringComparison.OrdinalIgnoreCase))
            return null;

        // Check cache first
        if (_resolvedCache.TryGetValue(typeName, out var cached))
            return DeepCloneElements(cached);

        // Find the page model for this control
        if (!_pagesByClassName.TryGetValue(typeName, out var controlPage))
            return null;

        // Cache a deep clone of the elements before resolving (to prevent infinite recursion)
        _resolvedCache[typeName] = controlPage.Elements;

        // Recursively resolve the control's own references
        var resolvedElements = ResolveElements(controlPage.Elements, typeName);

        // Update cache with fully resolved version
        _resolvedCache[typeName] = resolvedElements;

        return DeepCloneElements(resolvedElements);
    }

    /// <summary>
    /// Deep clone element list to prevent shared mutation between pages
    /// that both reference the same user control.
    /// </summary>
    private static List<UiElement> DeepCloneElements(List<UiElement> elements)
    {
        var cloned = new List<UiElement>(elements.Count);
        foreach (var el in elements)
        {
            cloned.Add(CloneElement(el));
        }
        return cloned;
    }

    private static UiElement CloneElement(UiElement el)
    {
        return new UiElement
        {
            TypeName = el.TypeName,
            Text = el.Text,
            Placeholder = el.Placeholder,
            Source = el.Source,
            TextBinding = el.TextBinding,
            CommandName = el.CommandName,
            CommandParameter = el.CommandParameter,
            Semantics = el.Semantics,
            Condition = el.Condition,
            ItemsSourceBinding = el.ItemsSourceBinding,
            IsGrouped = el.IsGrouped,
            IsBindableLayout = el.IsBindableLayout,
            BindableLayoutItemsSource = el.BindableLayoutItemsSource,
            IsUserControlReference = el.IsUserControlReference,
            Minimum = el.Minimum,
            Maximum = el.Maximum,
            ValueBinding = el.ValueBinding,
            Title = el.Title,
            SelectedItemBinding = el.SelectedItemBinding,
            Children = DeepCloneElements(el.Children),
            HeaderTemplate = el.HeaderTemplate != null ? DeepCloneElements(el.HeaderTemplate) : null,
            FooterTemplate = el.FooterTemplate != null ? DeepCloneElements(el.FooterTemplate) : null,
            ItemTemplate = el.ItemTemplate != null ? DeepCloneElements(el.ItemTemplate) : null,
            GroupHeaderTemplate = el.GroupHeaderTemplate != null ? DeepCloneElements(el.GroupHeaderTemplate) : null,
            GroupFooterTemplate = el.GroupFooterTemplate != null ? DeepCloneElements(el.GroupFooterTemplate) : null,
            EmptyView = el.EmptyView != null ? DeepCloneElements(el.EmptyView) : null,
            BindableLayoutItemTemplate = el.BindableLayoutItemTemplate != null ? DeepCloneElements(el.BindableLayoutItemTemplate) : null,
            TemplateVariants = el.TemplateVariants,
        };
    }
}
