using AIExtensions.Sample.ChatPlayground.Features.Search;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Controls;

/// <summary>Search input and index picker shared by saved-chat surfaces.</summary>
public partial class ChatSearchBox : ContentView
{
    public ChatSearchBox() => InitializeComponent();

    private async void SelectModeClicked(object? sender, EventArgs e)
    {
        if (sender is not Button { BindingContext: ChatSearchDescriptor selected } ||
            BindingContext is not ChatLibraryViewModel library)
            return;

        PopupMenu.Dismiss(SearchMethodButton);
        var visitId = library.VisitId;
        try
        {
            if (selected.DataLocation == ChatSearchDataLocation.Remote)
            {
                var page = Window?.Page
                    ?? throw new InvalidOperationException("The search picker is not attached to an app window.");
                var approved = await page.DisplayAlertAsync(
                    $"Search with {selected.DisplayName}?",
                    "For this visit, this provider will receive saved user and assistant text when indexing, and search terms after you pause typing. Remote calls may incur charges. System instructions, tools, reasoning, and image bytes stay local.",
                    "Use provider", "Keep local");
                if (!approved)
                    return;
            }

            if (library.IsOpen && library.VisitId == visitId)
                await library.SelectModeAsync(selected);
        }
        catch (Exception exception)
        {
            library.HasSearchError = true;
            library.StatusMessage = $"Could not select {selected.DisplayName}: {exception.Message}";
        }
    }
}
