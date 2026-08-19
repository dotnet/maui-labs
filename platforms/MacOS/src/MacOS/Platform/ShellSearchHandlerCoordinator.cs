using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

internal static class ShellSearchHandlerCoordinator
{
    public static bool IsVisible(SearchHandler? searchHandler)
        => searchHandler is not null
            && searchHandler.SearchBoxVisibility != SearchBoxVisibility.Hidden;

    public static bool IsCollapsible(SearchHandler? searchHandler)
        => searchHandler?.SearchBoxVisibility == SearchBoxVisibility.Collapsible;

    public static void ConfirmQuery(SearchHandler searchHandler, string query)
    {
        searchHandler.Query = query;
        ((ISearchHandlerController)searchHandler).QueryConfirmed();
    }
}
