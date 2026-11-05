using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

// Keeps the native presentation in sync without instantiating inactive page templates.
internal sealed class ShellTabBarCoordinator : IDisposable
{
    readonly Shell _shell;
    readonly Action _changed;
    readonly List<ShellSection> _observedSections = new();
    readonly HashSet<ShellContent> _observedContents = new();
    ShellSection[] _sections = Array.Empty<ShellSection>();
    ShellItem? _item;
    Page? _page;

    public ShellTabBarCoordinator(Shell shell, Action changed)
    {
        _shell = shell;
        _changed = changed;
        _shell.PropertyChanged += OnShellPropertyChanged;
        ObserveCurrentItem();
    }

    public IReadOnlyList<ShellSection> Sections => _sections;
    public int SelectedIndex => Array.IndexOf(_sections, _item?.CurrentItem);
    public bool IsVisible => Sections.Count > 0 &&
        (_item is TabBar || Sections.Count > 1) &&
        (_page == null || Shell.GetTabBarIsVisible(_page));

    public void SetPage(Page? page)
    {
        if (ReferenceEquals(_page, page))
            return;

        if (_page != null)
            _page.PropertyChanged -= OnPagePropertyChanged;
        _page = page;
        if (_page != null)
            _page.PropertyChanged += OnPagePropertyChanged;
        _changed();
    }

    public bool Select(int index)
    {
        if (_item == null || index < 0 || index >= Sections.Count || !Sections[index].IsEnabled)
            return false;

        _item.CurrentItem = Sections[index];
        return true;
    }

    public void Dispose()
    {
        _shell.PropertyChanged -= OnShellPropertyChanged;
        UnobserveItem();
        if (_page != null)
            _page.PropertyChanged -= OnPagePropertyChanged;
        _page = null;
    }

    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Shell.CurrentItem))
        {
            ObserveCurrentItem();
            _changed();
        }
    }

    void ObserveCurrentItem()
    {
        UnobserveItem();
        _item = _shell.CurrentItem;
        if (_item != null)
        {
            _item.PropertyChanged += OnItemPropertyChanged;
            ((INotifyCollectionChanged)_item.Items).CollectionChanged += OnSectionsChanged;
        }
        ObserveSections();
    }

    void UnobserveItem()
    {
        if (_item != null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
            ((INotifyCollectionChanged)_item.Items).CollectionChanged -= OnSectionsChanged;
        }
        foreach (var section in _observedSections)
            section.PropertyChanged -= OnSectionPropertyChanged;
        _observedSections.Clear();
        foreach (var content in _observedContents)
            content.PropertyChanged -= OnContentPropertyChanged;
        _observedContents.Clear();
    }

    void ObserveSections()
    {
        foreach (var section in _observedSections)
            section.PropertyChanged -= OnSectionPropertyChanged;
        _observedSections.Clear();
        if (_item != null)
        {
            foreach (var section in _item.Items)
            {
                section.PropertyChanged += OnSectionPropertyChanged;
                _observedSections.Add(section);
            }
        }
        _sections = _observedSections.Where(section => section.IsVisible).ToArray();
        ObserveContents();
    }

    void ObserveContents()
    {
        foreach (var content in _observedContents)
            content.PropertyChanged -= OnContentPropertyChanged;
        _observedContents.Clear();
        foreach (var section in _observedSections)
        {
            if (section.CurrentItem is ShellContent content && _observedContents.Add(content))
                content.PropertyChanged += OnContentPropertyChanged;
        }
    }

    void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveSections();
        _changed();
    }

    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellItem.CurrentItem))
            _changed();
    }

    void OnSectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellSection.IsVisible))
            _sections = _observedSections.Where(section => section.IsVisible).ToArray();
        if (e.PropertyName == nameof(ShellSection.CurrentItem))
            ObserveContents();
        if (e.PropertyName is nameof(ShellSection.IsVisible) or nameof(ShellSection.IsEnabled)
            or nameof(ShellSection.Title) or nameof(ShellSection.CurrentItem))
            _changed();
    }

    void OnContentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellContent.Title))
            _changed();
    }

    void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == Shell.TabBarIsVisibleProperty.PropertyName)
            _changed();
    }
}
