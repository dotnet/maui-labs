using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ControlGallery.Models;
using Microsoft.Maui.Controls;

namespace CollectionViewDemos.ViewModels
{
    public class CollectionViewPageViewModel
    {
        public ObservableCollection<GroupedNavItems> Groups { get; } = new();

        public ICommand NavigateCommand { get; }

        public CollectionViewPageViewModel()
        {
            NavigateCommand = new Command<NavItem>(
                async (NavItem item) =>
                {
                    if (item == null)
                        return;

                    var route = !string.IsNullOrEmpty(item.Route) ? item.Route : item.Destination?.Name;
                    if (string.IsNullOrEmpty(route))
                        return;

                    if (!route.StartsWith("/"))
                        await Shell.Current.GoToAsync(route);
                    else
                        await Shell.Current.GoToAsync(route);
                });

            PopulateGroups();
        }

        void PopulateGroups()
        {
            Groups.Add(new GroupedNavItems("Layout")
            {
                new NavItem { Title = "Vertical list", Description = "Text", Route = "VerticalListTextPage" },
                new NavItem { Title = "Horizontal list", Description = "Text", Route = "HorizontalListTextPage" },
                new NavItem { Title = "Vertical grid", Description = "Text", Route = "VerticalGridTextPage" },
                new NavItem { Title = "Horizontal grid", Description = "Text", Route = "HorizontalGridTextPage" },
                new NavItem { Title = "Vertical list", Description = "DataTemplate", Route = "VerticalListPage" },
                new NavItem { Title = "Horizontal list", Description = "DataTemplate", Route = "HorizontalListPage" },
                new NavItem { Title = "Vertical grid", Description = "DataTemplate", Route = "VerticalGridPage" },
                new NavItem { Title = "Horizontal grid", Description = "DataTemplate", Route = "HorizontalGridPage" },
                new NavItem { Title = "Vertical list", Description = "Data template selector", Route = "VerticalListDataTemplateSelectorPage" },
                new NavItem { Title = "Vertical list", Description = "RTL FlowDirection", Route = "VerticalListRTLPage" }
            });

            Groups.Add(new GroupedNavItems("Spacing")
            {
                new NavItem { Title = "Vertical list", Description = "ItemSpacing", Route = "VerticalListSpacingPage" },
                new NavItem { Title = "Horizontal list", Description = "ItemSpacing", Route = "HorizontalListSpacingPage" },
                new NavItem { Title = "Vertical grid", Description = "HorizontalItemSpacing and VerticalItemSpacing", Route = "VerticalGridSpacingPage" },
                new NavItem { Title = "Horizontal grid", Description = "HorizontalItemSpacing and VerticalItemSpacing", Route = "HorizontalGridSpacingPage" }
            });

            Groups.Add(new GroupedNavItems("Sizing")
            {
                new NavItem { Title = "Variable size items", Description = "Vertical list", Route = "VerticalListVariableSizeItemsPage" },
                new NavItem { Title = "Dynamic item sizing", Description = "Vertical list", Route = "VerticalListDynamicSizeItemsPage" }
            });

            Groups.Add(new GroupedNavItems("Selection")
            {
                new NavItem { Title = "Single", Description = "Vertical list", Route = "VerticalListSingleSelectionPage" },
                new NavItem { Title = "Multiple", Description = "Vertical list", Route = "VerticalListMultipleSelectionPage" },
                new NavItem { Title = "Single pre-selection", Description = "Vertical list", Route = "VerticalListSinglePreSelectionPage" },
                new NavItem { Title = "Multiple pre-selection", Description = "Vertical list", Route = "VerticalListMultiplePreSelectionPage" },
                new NavItem { Title = "Selection color", Description = "Vertical list", Route = "VerticalListSelectionColorPage" }
            });

            Groups.Add(new GroupedNavItems("EmptyView")
            {
                new NavItem { Title = "EmptyView string", Description = "ItemsSource Null", Route = "EmptyViewNullPage" },
                new NavItem { Title = "EmptyView string", Description = "Load simulation", Route = "EmptyViewLoadSimulationPage" },
                new NavItem { Title = "EmptyView string", Description = "Filtering", Route = "EmptyViewFilteredPage" },
                new NavItem { Title = "EmptyView with multiple views", Description = "Filtering", Route = "EmptyViewWithViewsFilteredPage" },
                new NavItem { Title = "EmptyView with EmptyViewTemplate", Description = "Filtering", Route = "EmptyViewTemplatePage" },
                new NavItem { Title = "EmptyView with EmptyViewTemplate", Description = "Data template selector", Route = "EmptyViewDataTemplateSelectorPage" },
                new NavItem { Title = "EmptyView swap", Description = "Filtering", Route = "EmptyViewSwapPage" }
            });

            Groups.Add(new GroupedNavItems("Grouping")
            {
                new NavItem { Title = "Grouping", Description = "Vertical list with DataTemplates", Route = "VerticalListGroupingPage" },
                new NavItem { Title = "Grouping", Description = "Vertical list without DataTemplates", Route = "VerticalListTextGroupingPage" },
                new NavItem { Title = "Grouping including empty groups", Description = "Vertical list with DataTemplates", Route = "VerticalListEmptyGroupsPage" },
                new NavItem { Title = "Grouping with variable sized items", Description = "Vertical list with DataTemplates", Route = "VerticalListGroupingVariableSizeItemsPage" }
            });

            Groups.Add(new GroupedNavItems("Pull to Refresh")
            {
                new NavItem { Title = "Pull to refresh using RefreshView", Description = "Vertical list", Route = "VerticalListPullToRefreshPage" },
                new NavItem { Title = "Pull to refresh using RefreshView", Description = "Horizontal grid", Route = "HorizontalGridPullToRefreshPage" }
            });

            Groups.Add(new GroupedNavItems("Headers and Footers")
            {
                new NavItem { Title = "String header and footer", Description = "Vertical list", Route = "VerticalListHeaderFooterStringPage" },
                new NavItem { Title = "DataTemplate header and footer", Description = "Vertical list", Route = "VerticalListHeaderFooterDataTemplatePage" },
                new NavItem { Title = "View header and footer", Description = "Vertical list", Route = "VerticalListHeaderFooterViewPage" },
                new NavItem { Title = "View header and footer", Description = "Horizontal grid", Route = "HorizontalGridHeaderFooterViewPage" }
            });

            Groups.Add(new GroupedNavItems("Scrolling")
            {
                new NavItem { Title = "Scroll by index", Description = "ScrollTo", Route = "ScrollToByIndexPage" },
                new NavItem { Title = "Scroll by object", Description = "ScrollTo", Route = "ScrollToByObjectPage" },
                new NavItem { Title = "Scroll by index", Description = "Grouped data", Route = "ScrollToByIndexWithGroupingPage" },
                new NavItem { Title = "Scroll by object", Description = "Grouped data", Route = "ScrollToByObjectWithGroupingPage" },
                new NavItem { Title = "Snap points", Description = "Type and alignment", Route = "VerticalListSnapPointsPage" },
                new NavItem { Title = "Scroll mode when adding items", Description = "ItemsUpdatingScrollMode", Route = "ItemsUpdatingScrollModePage" },
                new NavItem { Title = "Incremental loading on scroll", Description = "RemainingItemsThreshold", Route = "IncrementalLoadingPage" }
            });

            Groups.Add(new GroupedNavItems("Swipe")
            {
                new NavItem { Title = "Context menu items", Description = "Vertical list", Route = "VerticalListSwipeContextItemsPage" }
            });
        }
    }
}
