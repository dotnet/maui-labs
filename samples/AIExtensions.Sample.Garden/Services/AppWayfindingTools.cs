using System.ComponentModel;
using System.Text;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Navigation;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// AI-facing bridge for semantic app discovery, current-screen context,
/// destination explanation, and navigation.
/// </summary>
public sealed class AppWayfindingTools(
    ApplicationMapService applicationMap,
    ShellNavigationService navigation)
{
    [ExportAIFunction("search_app_ui")]
    [Description(
        "Search the app's real indexed screens for a feature, control, or task. " +
        "MUST be used first for every where/how/back question and before opening a destination, " +
        "even when a similar search was run in an earlier turn. Returns destination IDs, route " +
        "requirements, the page path, and matching controls. For explanations, this result is " +
        "incomplete until get_app_destination is called.")]
    public string SearchAppUi(
        [Description(
            "Natural-language feature or task to find, such as 'write a review', " +
            "'past orders', or 'product catalog'.")]
        string query)
    {
        var matches = applicationMap.Search(query);
        if (matches.Count == 0)
            return $"No navigable app screens matched '{query}'.";

        var builder = new StringBuilder();
        builder.AppendLine($"Found {matches.Count} app destination(s):");

        foreach (var match in matches)
        {
            var destination = match.Destination;
            builder.AppendLine();
            builder.AppendLine($"## {destination.PageName}");
            builder.AppendLine($"Destination ID: {destination.DestinationId}");
            builder.AppendLine($"Route: {destination.RouteTemplate}");
            builder.AppendLine($"Page path: {string.Join(" -> ", destination.PagePath)}");
            if (destination.RequiredParameters.Count > 0)
            {
                builder.AppendLine(
                    $"Required parameters: {string.Join(", ", destination.RequiredParameters.Select(parameter => parameter.QueryName))}");
            }

            if (match.RelevantLines.Count > 0)
            {
                builder.AppendLine("Matching controls:");
                foreach (var line in match.RelevantLines)
                    builder.AppendLine($"- {line}");
            }
        }

        builder.AppendLine();
        builder.AppendLine(
            "Use get_app_destination with a Destination ID to read its full indexed UI. " +
            "Use navigate_to_app_destination only when the user asked to move.");
        return builder.ToString();
    }

    [ExportAIFunction("get_app_destination")]
    [Description(
        "Read the compile-time indexed UI for every page from home through one destination " +
        "without moving the user. MUST be called after search_app_ui before answering " +
        "where/how/back questions. Does not inspect or describe the current app state.")]
    public string GetAppDestination(
        [Description("Destination ID returned by search_app_ui, for example 'ProductReviewPage'.")]
        string destinationId)
    {
        var destinations = applicationMap.GetDestinations();
        var exact = destinations.Where(destination =>
            string.Equals(
                destination.DestinationId,
                destinationId,
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                destination.PageTypeName,
                destinationId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (exact.Length == 0)
        {
            var semanticMatches = applicationMap.Search(destinationId, 5);
            if (semanticMatches.Count == 0)
                return $"No app destination matched '{destinationId}'.";
            if (semanticMatches.Count > 1)
            {
                return $"'{destinationId}' is ambiguous. Use one of these Destination IDs: " +
                    string.Join(", ", semanticMatches.Select(match => match.Destination.DestinationId));
            }

            exact = [semanticMatches[0].Destination];
        }

        if (exact.Length > 1)
        {
            return $"'{destinationId}' maps to multiple routes: " +
                string.Join(", ", exact.Select(destination => destination.RouteTemplate));
        }

        var destination = exact[0];
        var builder = new StringBuilder();
        builder.AppendLine($"Destination ID: {destination.DestinationId}");
        builder.AppendLine($"Route: {destination.RouteTemplate}");
        builder.AppendLine($"Destination path: {string.Join(" -> ", destination.PagePath)}");
        builder.AppendLine();
        builder.AppendLine("Verified destination path UI:");

        foreach (var pageIdentity in destination.PagePath)
        {
            var page = applicationMap.GetIndexedPage(pageIdentity);
            if (page is null)
                return $"The indexed page path could not resolve '{pageIdentity}'.";

            builder.AppendLine();
            builder.AppendLine($"## {page.Name}");
            builder.AppendLine(page.Markdown);
        }

        return builder.ToString();
    }

    [ExportAIFunction("get_current_app_state")]
    [Description(
        "Return the live Shell navigation URI, optionally with the currently visible page's UI. " +
        "MUST be called in the current turn before giving directions because the user may have " +
        "navigated manually since the last message. Private input text is omitted.")]
    public async Task<string> GetCurrentAppStateAsync(
        [Description(
            "Set true when exact controls or from-here directions are needed. " +
            "Set false to return only the current navigation URI.")]
        bool includePageUi = false)
    {
        var route = navigation.GetCurrentRoute();
        if (!includePageUi)
            return route;

        var snapshot = await applicationMap.CaptureCurrentPageAsync();
        return snapshot is null
            ? $"Current navigation URI: {route}\nNo currently presented app page is available."
            : $"Current navigation URI: {route}\n" +
              $"Current page: {snapshot.PageName}\n\n{snapshot.Markdown}\n\n" +
              $"DIRECTION CONSTRAINT: The first direction step must use a control from " +
              $"{snapshot.PageName} above. Do not begin at home or another page unless " +
              $"{snapshot.PageName} is home.";
    }

    [ExportAIFunction("navigate_to_app_destination")]
    [Description(
        "Navigate to a Destination ID returned by search_app_ui. Use only when the user " +
        "explicitly asks to open, show, or be taken to that screen. Supply every required " +
        "parameter shown by search_app_ui.")]
    public async Task<string> NavigateToAppDestinationAsync(
        [Description("Destination ID returned by search_app_ui.")]
        string destinationId,
        [Description(
            "Route parameter values keyed by the required parameter names from search_app_ui, " +
            "for example { \"sku\": \"seed-basil\" }. Omit for destinations without parameters.")]
        Dictionary<string, string>? parameters = null)
    {
        var resolution = await applicationMap.NavigateAsync(destinationId, parameters);
        return resolution.Status switch
        {
            DestinationResolutionStatus.Success =>
                $"Opened {resolution.Destination!.PageName} using {resolution.Route}.",
            DestinationResolutionStatus.NotFound =>
                $"No app destination matched '{destinationId}'. Use search_app_ui first.",
            DestinationResolutionStatus.Ambiguous =>
                $"'{destinationId}' is ambiguous. Use one of these Destination IDs: " +
                string.Join(", ", resolution.Candidates.Select(candidate => candidate.DestinationId)),
            DestinationResolutionStatus.MissingParameters =>
                $"Cannot open {resolution.Destination!.PageName} until these parameters are supplied: " +
                string.Join(", ", resolution.MissingParameters),
            _ => throw new InvalidOperationException(
                $"Unsupported destination status '{resolution.Status}'."),
        };
    }

}
