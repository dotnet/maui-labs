using System.ComponentModel;
using System.Text;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Maui.AI.Navigation;

namespace AIExtensions.Sample.Garden.Services;

/// <summary>
/// AI-facing bridge for semantic app discovery, current-screen context,
/// destination explanation, and navigation.
/// </summary>
public sealed class AppWayfindingTools(ApplicationMapService applicationMap)
{
    [ExportAIFunction("find_in_app")]
    [Description(
        "Search the app's real indexed screens for a feature, control, or task. " +
        "Use this first for where/how questions and before opening a destination. " +
        "Returns destination IDs, route requirements, the page path, and matching controls.")]
    public string FindInApp(
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
            "Use describe_app_destination with a Destination ID to read its full indexed UI. " +
            "Use open_app_destination only when the user asked to move.");
        return builder.ToString();
    }

    [ExportAIFunction("describe_app_destination")]
    [Description(
        "Read the complete indexed UI and route path for one app destination without moving " +
        "the user. Use this to explain where a feature is and to inspect every page in the " +
        "page path returned by find_in_app.")]
    public string DescribeAppDestination(
        [Description("Destination ID returned by find_in_app, for example 'ProductReviewPage'.")]
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
        return
            $"Destination ID: {destination.DestinationId}\n" +
            $"Route: {destination.RouteTemplate}\n" +
            $"Page path: {string.Join(" -> ", destination.PagePath)}\n\n" +
            destination.Markdown;
    }

    [ExportAIFunction("describe_current_screen")]
    [Description(
        "Read the currently visible page and live controls. ALWAYS use this for questions " +
        "containing 'this', 'here', 'current screen', or referring to a visible field, button, " +
        "or value. Private input text is omitted.")]
    public async Task<string> DescribeCurrentScreenAsync()
    {
        var snapshot = await applicationMap.CaptureCurrentPageAsync();
        return snapshot is null
            ? "No currently presented app page is available."
            : $"Current page: {snapshot.PageName}\n\n{snapshot.Markdown}";
    }

    [ExportAIFunction("open_app_destination")]
    [Description(
        "Navigate to a Destination ID returned by find_in_app. Use only when the user " +
        "explicitly asks to open, show, or be taken to that screen. Supply every required " +
        "parameter shown by find_in_app.")]
    public async Task<string> OpenAppDestinationAsync(
        [Description("Destination ID returned by find_in_app.")]
        string destinationId,
        [Description(
            "Route parameter values keyed by the required parameter names from find_in_app, " +
            "for example { \"sku\": \"seed-basil\" }. Omit for destinations without parameters.")]
        Dictionary<string, string>? parameters = null)
    {
        var resolution = await applicationMap.NavigateAsync(destinationId, parameters);
        return resolution.Status switch
        {
            DestinationResolutionStatus.Success =>
                $"Opened {resolution.Destination!.PageName} using {resolution.Route}.",
            DestinationResolutionStatus.NotFound =>
                $"No app destination matched '{destinationId}'. Use find_in_app first.",
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
