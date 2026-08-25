using System.Text;
using Microsoft.Maui.AI.Indexer;

namespace Microsoft.Maui.AI.Navigation;

/// <summary>
/// Composes the generated semantic UI catalog, current-page runtime context,
/// and Shell navigation metadata into one application wayfinding map.
/// </summary>
public sealed class ApplicationMapService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "can", "do", "for", "how", "i", "in", "is",
        "it", "me", "my", "of", "on", "the", "this", "to", "what", "where",
    };

    private readonly IndexedPageCatalog _catalog;
    private readonly ShellNavigationService _navigation;
    private readonly ICurrentPageContextProvider _currentPageContext;

    public ApplicationMapService(
        IndexedPageCatalog catalog,
        ShellNavigationService navigation,
        ICurrentPageContextProvider currentPageContext)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _currentPageContext = currentPageContext
            ?? throw new ArgumentNullException(nameof(currentPageContext));
    }

    /// <summary>Returns every indexed page that has a reliable Shell destination.</summary>
    public IReadOnlyList<ApplicationDestination> GetDestinations()
    {
        var routes = _navigation.GetRoutes();
        var destinations = new List<ApplicationDestination>();

        foreach (var page in _catalog.Pages)
        {
            foreach (var route in page.Routes)
            {
                destinations.Add(CreateDestination(page, route, routes));
            }

            foreach (var route in routes.Where(candidate =>
                MatchesPageIdentity(page, candidate.TargetPageName)))
            {
                destinations.Add(CreateDestination(page, route.DestinationPath, routes));
            }
        }

        var uniqueDestinations = destinations
            .GroupBy(
                destination => (destination.PageTypeName, destination.Route),
                StringTupleComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var destinationIdCounts = uniqueDestinations
            .GroupBy(destination => destination.PageName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);

        return uniqueDestinations
            .Select(destination => destination with
            {
                DestinationId = destinationIdCounts[destination.PageName] == 1
                    ? destination.PageName
                    : $"{destination.PageTypeName}@{destination.Route}",
            })
            .OrderBy(destination => destination.PageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Searches the navigable UI index using a natural-language query.</summary>
    public IReadOnlyList<ApplicationSearchResult> Search(
        string query,
        int maxResults = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (maxResults <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxResults));

        var terms = ExtractTerms(query);
        if (terms.Length == 0)
            return [];

        var phrase = string.Join(' ', terms);
        var matches = new List<ApplicationSearchResult>();

        foreach (var destination in GetDestinations())
        {
            var normalizedName = Normalize(destination.PageName);
            var normalizedMarkdown = Normalize(destination.Markdown);
            var score = 0;

            if (normalizedMarkdown.Contains(phrase, StringComparison.Ordinal)
                || normalizedName.Contains(phrase, StringComparison.Ordinal))
            {
                score += 20;
            }

            foreach (var term in terms)
            {
                if (normalizedName.Contains(term, StringComparison.Ordinal))
                    score += 8;
                if (normalizedMarkdown.Contains(term, StringComparison.Ordinal))
                    score += 3;
            }

            if (score == 0)
                continue;

            var relevantLines = destination.Markdown
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => terms.Any(term =>
                    Normalize(line).Contains(term, StringComparison.Ordinal)))
                .Take(6)
                .ToArray();

            matches.Add(new ApplicationSearchResult(destination, score, relevantLines));
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Destination.PageName, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    /// <summary>
    /// Resolves a page name, route, or semantic query to one destination and validates
    /// all parameters required by its route chain.
    /// </summary>
    public DestinationResolution ResolveDestination(
        string destination,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);

        var allDestinations = GetDestinations();
        var exactMatches = allDestinations.Where(candidate =>
                string.Equals(candidate.DestinationId, destination, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.PageName, destination, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.PageTypeName, destination, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Route, destination, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.RouteTemplate, destination, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        ApplicationDestination? resolved;
        if (exactMatches.Length == 1)
        {
            resolved = exactMatches[0];
        }
        else if (exactMatches.Length > 1)
        {
            return Ambiguous(exactMatches);
        }
        else
        {
            var semanticMatches = Search(destination, 10)
                .Select(match => match.Destination)
                .ToArray();
            if (semanticMatches.Length == 0)
            {
                return new DestinationResolution(
                    DestinationResolutionStatus.NotFound,
                    null,
                    null,
                    [],
                    []);
            }

            if (semanticMatches.Length > 1)
                return Ambiguous(semanticMatches);

            resolved = semanticMatches[0];
        }

        var normalizedParameters = NormalizeParameters(parameters);
        var missingParameters = GetMissingParameters(
            resolved,
            normalizedParameters);

        if (missingParameters.Count > 0)
        {
            return new DestinationResolution(
                DestinationResolutionStatus.MissingParameters,
                resolved,
                null,
                [resolved],
                missingParameters);
        }

        var route = BuildRoute(resolved, normalizedParameters);
        return new DestinationResolution(
            DestinationResolutionStatus.Success,
            resolved,
            route,
            [resolved],
            []);
    }

    /// <summary>
    /// Resolves and navigates to a destination. Ambiguous, unknown, or incomplete
    /// requests are returned without changing the current screen.
    /// </summary>
    public async Task<DestinationResolution> NavigateAsync(
        string destination,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var resolution = ResolveDestination(destination, parameters);
        if (resolution.Status != DestinationResolutionStatus.Success)
            return resolution;

        await _navigation.NavigateAsync(resolution.Route!);
        return resolution;
    }

    /// <summary>Captures the currently visible page and its live semantic state.</summary>
    public Task<CurrentPageSnapshot?> CaptureCurrentPageAsync(
        CurrentPageSnapshotOptions? options = null)
        => _currentPageContext.CaptureAsync(options);

    private ApplicationDestination CreateDestination(
        IndexedPage page,
        string routePath,
        IReadOnlyList<RouteInfo> routes)
    {
        var routeChain = GetRegisteredRouteChain(routePath, routes);
        var requiredParameters = routeChain
            .SelectMany(route => route.Parameters)
            .GroupBy(parameter => parameter.QueryName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return new ApplicationDestination(
            page.Name,
            page.Name,
            page.TypeName,
            routePath,
            BuildRouteTemplate(routePath, routeChain),
            requiredParameters,
            BuildPagePath(page, routePath, routes),
            page.Markdown,
            page.FilePath);
    }

    private IReadOnlyList<string> BuildPagePath(
        IndexedPage destinationPage,
        string routePath,
        IReadOnlyList<RouteInfo> routes)
    {
        var pagePath = new List<string>();
        var pageTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicatePageNames = _catalog.Pages
            .GroupBy(page => page.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AddPage(_catalog.Home);

        foreach (var page in _catalog.Pages
            .Where(page => page.Routes.Any(route =>
                IsRoutePrefix(route, routePath)))
            .OrderBy(page => page.Routes
                .Where(route => IsRoutePrefix(route, routePath))
                .Min(route => route.Length)))
        {
            AddPage(page);
        }

        foreach (var route in GetRegisteredRouteChain(routePath, routes))
        {
            if (route.TargetPageName is null)
                continue;
            AddPage(FindCatalogPage(route.TargetPageName));
        }

        AddPage(destinationPage);
        return pagePath;

        void AddPage(IndexedPage? page)
        {
            if (page is not null
                && pageTypes.Add(page.TypeName))
            {
                pagePath.Add(duplicatePageNames.Contains(page.Name)
                    ? page.TypeName
                    : page.Name);
            }
        }
    }

    private IndexedPage? FindCatalogPage(string typeIdentity)
        => _catalog.Pages.FirstOrDefault(page =>
            MatchesPageIdentity(page, typeIdentity));

    private string BuildRoute(
        ApplicationDestination destination,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (destination.RequiredParameters.Count == 0)
            return destination.Route;

        var routes = _navigation.GetRoutes();
        var hierarchyRoute = routes
            .Where(route => route.FullPath.StartsWith("//", StringComparison.Ordinal)
                && IsRoutePrefix(route.FullPath, destination.Route))
            .OrderByDescending(route => route.FullPath.Length)
            .FirstOrDefault();

        var basePath = hierarchyRoute?.FullPath ?? "";
        var remainingPath = basePath.Length == 0
            ? destination.Route.Trim('/')
            : destination.Route[basePath.Length..].Trim('/');
        var segments = remainingPath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        return _navigation.BuildRoute(basePath, segments, parameters);
    }

    private IReadOnlyList<string> GetMissingParameters(
        ApplicationDestination destination,
        IReadOnlyDictionary<string, string> parameters)
    {
        var routeChain = GetRegisteredRouteChain(
            destination.Route,
            _navigation.GetRoutes());
        var missing = new List<string>();

        foreach (var parameterGroup in routeChain
            .SelectMany(route => route.Parameters.Select(parameter =>
                (Route: route.Route, Parameter: parameter)))
            .GroupBy(
                item => item.Parameter.QueryName,
                StringComparer.OrdinalIgnoreCase))
        {
            if (parameters.ContainsKey(parameterGroup.Key))
                continue;

            var qualifiedKeys = parameterGroup
                .Select(item => $"{item.Route}.{item.Parameter.QueryName}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var missingQualifiedKeys = qualifiedKeys
                .Where(key => !parameters.ContainsKey(key))
                .ToArray();

            if (missingQualifiedKeys.Length == qualifiedKeys.Length)
                missing.Add(parameterGroup.Key);
            else
                missing.AddRange(missingQualifiedKeys);
        }

        return missing;
    }

    private static IReadOnlyList<RouteInfo> GetRegisteredRouteChain(
        string routePath,
        IReadOnlyList<RouteInfo> routes)
    {
        var registeredRoutes = routes
            .Where(route => !route.FullPath.StartsWith("//", StringComparison.Ordinal))
            .GroupBy(route => route.Route, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return routePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(registeredRoutes.ContainsKey)
            .Select(segment => registeredRoutes[segment])
            .ToArray();
    }

    private static string BuildRouteTemplate(
        string routePath,
        IReadOnlyList<RouteInfo> routeChain)
    {
        var routeByName = routeChain.ToDictionary(
            route => route.Route,
            StringComparer.OrdinalIgnoreCase);
        var insertedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(routePath.StartsWith("//", StringComparison.Ordinal)
            ? "//"
            : "");

        foreach (var segment in routePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0 && builder[^1] != '/')
                builder.Append('/');
            builder.Append(segment);

            if (!routeByName.TryGetValue(segment, out var route))
                continue;

            foreach (var parameter in route.Parameters)
            {
                if (insertedParameters.Add(parameter.QueryName))
                    builder.Append($"/<{parameter.QueryName}>");
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, string> NormalizeParameters(
        IReadOnlyDictionary<string, string>? parameters)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
            return normalized;

        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                normalized[key] = value;
        }

        return normalized;
    }

    private static DestinationResolution Ambiguous(
        IReadOnlyList<ApplicationDestination> candidates)
        => new(
            DestinationResolutionStatus.Ambiguous,
            null,
            null,
            candidates,
            []);

    private static string[] ExtractTerms(string query)
        => Normalize(query)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length > 1 && !StopWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsRoutePrefix(string? prefix, string route)
        => prefix is not null
            && (string.Equals(prefix, route, StringComparison.OrdinalIgnoreCase)
                || route.StartsWith(
                    $"{prefix.TrimEnd('/')}/",
                    StringComparison.OrdinalIgnoreCase));

    private static bool MatchesPageIdentity(
        IndexedPage page,
        string? typeIdentity)
    {
        if (typeIdentity is null)
            return false;

        if (typeIdentity.Contains('.')
            && page.TypeName.Contains('.'))
        {
            return string.Equals(
                page.TypeName,
                typeIdentity,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            page.Name,
            typeIdentity,
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                page.TypeName,
                typeIdentity,
                StringComparison.OrdinalIgnoreCase)
            || typeIdentity.EndsWith(
                $".{page.Name}",
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StringTupleComparer
        : IEqualityComparer<(string PageTypeName, string Route)>
    {
        public static StringTupleComparer OrdinalIgnoreCase { get; } = new();

        public bool Equals(
            (string PageTypeName, string Route) x,
            (string PageTypeName, string Route) y)
            => string.Equals(x.PageTypeName, y.PageTypeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Route, y.Route, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string PageTypeName, string Route) value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.PageTypeName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Route));
    }
}
