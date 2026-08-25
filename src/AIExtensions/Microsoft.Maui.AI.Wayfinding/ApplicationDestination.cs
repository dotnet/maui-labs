using Microsoft.Maui.AI.Navigation;

namespace Microsoft.Maui.AI.Wayfinding;

/// <summary>
/// A semantically indexed page that can be reached through Shell navigation.
/// </summary>
public sealed record ApplicationDestination(
    string DestinationId,
    string PageName,
    string PageTypeName,
    string Route,
    string RouteTemplate,
    IReadOnlyList<QueryParameterInfo> RequiredParameters,
    IReadOnlyList<string> PagePath,
    string Markdown,
    string? FilePath);

/// <summary>A ranked semantic match within the application's navigable UI.</summary>
public sealed record ApplicationSearchResult(
    ApplicationDestination Destination,
    int Score,
    IReadOnlyList<string> RelevantLines);

/// <summary>The outcome of resolving a destination and its route parameters.</summary>
public enum DestinationResolutionStatus
{
    Success,
    NotFound,
    Ambiguous,
    MissingParameters,
}

/// <summary>
/// A destination resolution result. Navigation occurs only for
/// <see cref="DestinationResolutionStatus.Success"/>.
/// </summary>
public sealed record DestinationResolution(
    DestinationResolutionStatus Status,
    ApplicationDestination? Destination,
    string? Route,
    IReadOnlyList<ApplicationDestination> Candidates,
    IReadOnlyList<string> MissingParameters);
