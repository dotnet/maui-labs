using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.Platforms.MacOS.Tests;

public class ShellTabBarCoordinatorTests
{
    [Fact]
    public void ContentTitle_RefreshesFallbackLabelAndUnsubscribesReplacedContent()
    {
        var item = CreateItem(true, 1);
        var section = item.Items[0];
        section.Title = null;
        var original = section.CurrentItem;
        var shell = new Shell { Items = { item } };
        var changes = 0;
        using var coordinator = new ShellTabBarCoordinator(shell, () => changes++);
        original.Title = "Changed";
        Assert.True(changes > 0);

        var replacement = new ShellContent { Title = "Replacement" };
        section.Items.Add(replacement);
        section.CurrentItem = replacement;
        changes = 0;
        original.Title = "Detached";
        Assert.Equal(0, changes);
        replacement.Title = "New label";
        Assert.True(changes > 0);
    }

    [Fact]
    public void HidingSelectedSection_PreservesMauiSelectedVisibleSection()
    {
        var item = CreateItem(true, 3);
        var shell = new Shell { Items = { item } };
        using var coordinator = new ShellTabBarCoordinator(shell, () => { });
        item.CurrentItem = item.Items[2];
        item.CurrentItem.IsVisible = false;
        Assert.Contains(item.CurrentItem, coordinator.Sections);
        Assert.InRange(coordinator.SelectedIndex, 0, 1);
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, 6, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, 6, true)]
    public void CurrentItem_ExposesVisibleTabsWithoutCreatingPages(bool tabBar, int count, bool visible)
    {
        var created = 0;
        var item = CreateItem(tabBar, count, () => created++);
        var shell = new Shell { Items = { item } };
        using var coordinator = new ShellTabBarCoordinator(shell, () => { });

        Assert.Equal(count, coordinator.Sections.Count);
        Assert.Equal(visible, coordinator.IsVisible);
        Assert.Equal(0, coordinator.SelectedIndex);
        Assert.Equal(0, created);
    }

    [Fact]
    public void Select_UsesVisibleSectionIndicesAndRejectsDisabledTabs()
    {
        var item = CreateItem(true, 3);
        item.Items[1].IsVisible = false;
        var shell = new Shell { Items = { item } };
        using var coordinator = new ShellTabBarCoordinator(shell, () => { });

        Assert.True(coordinator.Select(1));
        Assert.Same(item.Items[2], item.CurrentItem);
        Assert.Equal(1, coordinator.SelectedIndex);
        item.Items[0].IsEnabled = false;
        Assert.False(coordinator.Select(0));
        Assert.False(coordinator.Select(-1));
        Assert.False(coordinator.Select(2));
        Assert.Same(item.Items[2], item.CurrentItem);
    }

    [Fact]
    public void SectionChanges_RefreshPresentationAndKeepSelection()
    {
        var item = CreateItem(true, 3);
        var shell = new Shell { Items = { item } };
        var changes = 0;
        using var coordinator = new ShellTabBarCoordinator(shell, () => changes++);

        item.CurrentItem = item.Items[2];
        Assert.Equal(2, coordinator.SelectedIndex);
        Assert.True(changes > 0);
        changes = 0;
        item.Items[0].Title = "Renamed";
        Assert.True(changes > 0);
        item.Items[0].IsVisible = false;
        Assert.Equal(2, coordinator.Sections.Count);
        Assert.Equal(1, coordinator.SelectedIndex);
        item.Items.RemoveAt(0);
        Assert.Equal(2, coordinator.Sections.Count);
        item.Items.Add(CreateItem(true, 1).Items[0]);
        Assert.Equal(3, coordinator.Sections.Count);
    }

    [Fact]
    public void ReplacingItem_UnsubscribesOldSections()
    {
        var oldItem = CreateItem(true, 2);
        var newItem = CreateItem(false, 1);
        var shell = new Shell { Items = { oldItem, newItem } };
        var changes = 0;
        using var coordinator = new ShellTabBarCoordinator(shell, () => changes++);

        shell.CurrentItem = newItem;
        Assert.Single(coordinator.Sections);
        Assert.False(coordinator.IsVisible);
        changes = 0;
        oldItem.Items[0].Title = "Detached";
        Assert.Equal(0, changes);
        shell.Items.Remove(oldItem);
        newItem.Items.Clear();
        Assert.Empty(coordinator.Sections);
        Assert.False(coordinator.IsVisible);
    }

    [Fact]
    public void PageVisibilityAndDisposal_UpdateOnlyWhileSubscribed()
    {
        var item = CreateItem(true, 2);
        var shell = new Shell { Items = { item } };
        var changes = 0;
        var coordinator = new ShellTabBarCoordinator(shell, () => changes++);
        var first = new ContentPage();
        var second = new ContentPage();
        coordinator.SetPage(first);
        Shell.SetTabBarIsVisible(first, false);
        Assert.False(coordinator.IsVisible);
        coordinator.SetPage(second);
        Assert.True(coordinator.IsVisible);
        changes = 0;
        Shell.SetTabBarIsVisible(first, true);
        Assert.Equal(0, changes);
        coordinator.Dispose();
        item.Items[0].Title = "Disconnected";
        Shell.SetTabBarIsVisible(second, false);
        Assert.Equal(0, changes);
    }

    static ShellItem CreateItem(bool tabBar, int count, Action? created = null)
    {
        ShellItem item = tabBar ? new TabBar() : new FlyoutItem();
        for (var i = 0; i < count; i++)
        {
            item.Items.Add(new Tab
            {
                Title = $"Tab {i}",
                Items =
                {
                    new ShellContent
                    {
                        Route = $"page{i}",
                        ContentTemplate = new DataTemplate(() =>
                        {
                            created?.Invoke();
                            return new ContentPage();
                        }),
                    },
                },
            });
        }
        return item;
    }
}
