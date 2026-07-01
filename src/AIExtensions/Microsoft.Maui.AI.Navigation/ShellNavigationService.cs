using System.Reflection;
using System.Text;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.AI.Navigation;

/// <summary>
/// Metadata about a discovered Shell route.
/// </summary>
/// <param name="Route">The route segment name (e.g. "product").</param>
/// <param name="FullPath">The absolute Shell path (e.g. "//main/products").</param>
/// <param name="Parameters">Query parameters the page accepts.</param>
public record RouteInfo(
    string Route,
    string FullPath,
    IReadOnlyList<QueryParameterInfo> Parameters);

/// <summary>
/// A query parameter accepted by a route's page or view model.
/// </summary>
/// <param name="QueryName">The URL query key (e.g. "sku").</param>
/// <param name="PropertyName">The CLR property name on the page/VM (e.g. "Sku").</param>
/// <param name="PropertyType">The simple type name (e.g. "String").</param>
public record QueryParameterInfo(
    string QueryName,
    string PropertyName,
    string PropertyType);

/// <summary>
/// Discovers Shell routes at runtime and provides template-aware navigation.
/// <para>
/// The AI writes clean URIs like <c>//main/products/product/seed-tomato/review</c>.
/// This service matches path segments against known routes, extracts inline parameter
/// values, and issues the correct sequence of <c>Shell.GoToAsync</c> calls so each
/// page receives its parameters.
/// </para>
/// </summary>
public class ShellNavigationService
{
    private List<RouteInfo>? _cachedRoutes;

    /// <summary>
    /// Lists all available navigation routes by walking the Shell hierarchy
    /// and reflecting on <c>Routing.RegisterRoute</c> entries.
    /// </summary>
    public virtual IReadOnlyList<RouteInfo> GetRoutes()
    {
        if (_cachedRoutes is not null)
            return _cachedRoutes;

        var routes = new List<RouteInfo>();

        if (Shell.Current is { } shell)
        {
            foreach (var item in shell.Items)
            {
                var itemRoute = Routing.GetRoute(item);
                if (IsGenerated(itemRoute))
                    continue;

                foreach (var section in item.Items)
                {
                    foreach (var content in section.Items)
                    {
                        var contentRoute = Routing.GetRoute(content);
                        if (IsGenerated(contentRoute))
                            continue;

                        var fullPath = $"//{itemRoute}/{contentRoute}";
                        routes.Add(new RouteInfo(contentRoute, fullPath, []));
                    }
                }
            }
        }

        try
        {
            var field = typeof(Routing).GetField("s_routes",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is System.Collections.IDictionary routeDict)
            {
                foreach (System.Collections.DictionaryEntry entry in routeDict)
                {
                    var routeName = entry.Key?.ToString();
                    if (string.IsNullOrWhiteSpace(routeName) || IsGenerated(routeName))
                        continue;

                    var pageType = GetTypeFromFactory(entry.Value);
                    var queryParams = DiscoverQueryParameters(pageType);

                    routes.Add(new RouteInfo(routeName, routeName, queryParams));
                }
            }
        }
        catch
        {
            // Reflection on internal Routing dictionary failed
        }

        _cachedRoutes = routes;
        return routes;
    }

    /// <summary>
    /// Returns the current Shell navigation location as a URI string.
    /// </summary>
    public virtual string GetCurrentRoute()
    {
        return Shell.Current?.CurrentState?.Location?.OriginalString ?? "unknown";
    }

    /// <summary>
    /// Navigates using a clean template-style URI. Unknown path segments that
    /// follow a parameterized route are treated as inline parameter values.
    /// The method automatically issues the correct sequence of
    /// <c>GoToAsync</c> calls so each page receives its parameters.
    /// Returns a JSON array of the navigation steps that were executed.
    /// </summary>
    public virtual async Task<string> NavigateAsync(string route)
    {
        var steps = ParseRoute(route);

        if (steps.Count == 0)
        {
            await GoToAsyncOnMainThread(route);
            return System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new { route, location = GetCurrentRoute() }
            });
        }

        var results = new List<object>();
        foreach (var step in steps)
        {
            await GoToAsyncOnMainThread(step.route);
            results.Add(new { route = step.route, location = GetCurrentRoute() });
        }

        return System.Text.Json.JsonSerializer.Serialize(results);
    }

    /// <summary>
    /// Invalidates the cached route list so the next <see cref="GetRoutes"/>
    /// call rediscovers routes.
    /// </summary>
    public void InvalidateCache() => _cachedRoutes = null;

    /// <summary>
    /// Builds a multi-segment Shell route where shared query parameters are
    /// applied to intermediate pages using Shell's dot-prefix convention.
    /// </summary>
    public string BuildRoute(
        string basePath,
        IReadOnlyList<string> segments,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (segments.Count == 0)
            return basePath;

        var routes = GetRoutes();
        var sb = new StringBuilder(basePath.TrimEnd('/'));

        foreach (var segment in segments)
        {
            sb.Append('/');
            sb.Append(segment);
        }

        if (parameters is null or { Count: 0 })
            return sb.ToString();

        var queryParts = new List<string>();
        var lastSegment = segments[^1];

        var lastRouteInfo = routes.FirstOrDefault(r =>
            string.Equals(r.Route, lastSegment, StringComparison.OrdinalIgnoreCase));
        if (lastRouteInfo is not null)
        {
            foreach (var (key, value) in parameters)
            {
                if (lastRouteInfo.Parameters.Any(p =>
                    string.Equals(p.QueryName, key, StringComparison.OrdinalIgnoreCase)))
                {
                    queryParts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
                }
            }
        }

        for (int i = 0; i < segments.Count - 1; i++)
        {
            var segment = segments[i];
            var routeInfo = routes.FirstOrDefault(r =>
                string.Equals(r.Route, segment, StringComparison.OrdinalIgnoreCase));
            if (routeInfo is null)
                continue;

            foreach (var (key, value) in parameters)
            {
                if (routeInfo.Parameters.Any(p =>
                    string.Equals(p.QueryName, key, StringComparison.OrdinalIgnoreCase)))
                {
                    queryParts.Add($"{Uri.EscapeDataString(segment)}.{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
                }
            }
        }

        if (queryParts.Count > 0)
        {
            sb.Append('?');
            sb.Append(string.Join('&', queryParts));
        }

        return sb.ToString();
    }

    // ─── Route parsing ──────────────────────────────────────────────

    /// <summary>
    /// Parses a template-style URI into navigation steps.
    /// <para>
    /// The algorithm walks each path segment and classifies it as:
    /// <list type="bullet">
    ///   <item>A Shell hierarchy segment (its route has FullPath starting with //) — accumulated into the base path</item>
    ///   <item>A registered (pushed) route — becomes a navigation step</item>
    ///   <item>An unknown segment after a parameterized route — treated as an inline parameter value</item>
    /// </list>
    /// If the URI already contains a query string (<c>?</c>), it is preserved as-is
    /// and no template parsing is applied.
    /// </para>
    /// </summary>
    internal List<(string route, Dictionary<string, string> extractedParams)> ParseRoute(string uri)
    {
        // Empty / trivial
        if (string.IsNullOrEmpty(uri))
            return [(uri, new())];

        // If the URI already has a query string, the caller is using explicit params — pass through
        if (uri.Contains('?'))
            return [(uri, new())];

        // Relative single-segment routes (like "..", "cart", "review") — pass through
        if (!uri.Contains('/'))
            return [(uri, new())];

        // Relative back navigation ("..","../..","../../..") — pass through
        if (uri.StartsWith(".."))
            return [(uri, new())];

        var routes = GetRoutes();

        // Build lookup sets
        var hierarchyRoutes = new HashSet<string>(
            routes.Where(r => r.FullPath.StartsWith("//")).Select(r => r.Route),
            StringComparer.OrdinalIgnoreCase);

        var registeredRoutes = new Dictionary<string, RouteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in routes.Where(r => !r.FullPath.StartsWith("//")))
            registeredRoutes.TryAdd(r.Route, r);

        var allKnown = new HashSet<string>(
            routes.Select(r => r.Route), StringComparer.OrdinalIgnoreCase);

        // Split into segments
        var segments = uri.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Phase 1: separate hierarchy prefix from pushed segments
        var baseParts = new List<string>();
        int pushedStart = segments.Length; // index where pushed routes begin

        for (int i = 0; i < segments.Length; i++)
        {
            if (hierarchyRoutes.Contains(segments[i]) ||
                // "main" or other TabBar-level route names that aren't in hierarchy
                // but appear before any registered route
                (!registeredRoutes.ContainsKey(segments[i]) && !allKnown.Contains(segments[i])))
            {
                baseParts.Add(segments[i]);
            }
            else
            {
                pushedStart = i;
                break;
            }
        }

        // No pushed segments — this is purely a hierarchy route
        if (pushedStart >= segments.Length)
        {
            var hierarchyRoute = baseParts.Count > 0
                ? "//" + string.Join("/", baseParts)
                : uri;
            return [(hierarchyRoute, new())];
        }

        // Phase 2: walk pushed segments, extract inline parameter values
        var steps = new List<(string route, Dictionary<string, string> extractedParams)>();
        var basePath = "//" + string.Join("/", baseParts);
        var collectedParams = new Dictionary<string, string>(); // params inherited down the stack

        for (int i = pushedStart; i < segments.Length; i++)
        {
            var seg = segments[i];

            if (registeredRoutes.TryGetValue(seg, out var routeInfo))
            {
                var stepParams = new Dictionary<string, string>();

                // Check if the next segment is an inline value (not a known route)
                if (routeInfo.Parameters.Count > 0 &&
                    i + 1 < segments.Length &&
                    !allKnown.Contains(segments[i + 1]))
                {
                    i++; // consume the value segment
                    var value = Uri.UnescapeDataString(segments[i]);
                    var paramName = routeInfo.Parameters[0].QueryName;
                    stepParams[paramName] = value;
                    collectedParams[paramName] = value;
                }

                // Build the step route string
                var sb = new StringBuilder();
                if (steps.Count == 0)
                {
                    // First pushed route: use absolute path
                    sb.Append(basePath);
                    sb.Append('/');
                }
                sb.Append(seg);

                // Add query string for this step's params
                // Include the step's own extracted params + any inherited params this route accepts
                var queryParams = new Dictionary<string, string>(stepParams);
                foreach (var (key, val) in collectedParams)
                {
                    if (!queryParams.ContainsKey(key) &&
                        routeInfo.Parameters.Any(p =>
                            string.Equals(p.QueryName, key, StringComparison.OrdinalIgnoreCase)))
                    {
                        queryParams[key] = val;
                    }
                }

                if (queryParams.Count > 0)
                {
                    sb.Append('?');
                    sb.Append(string.Join('&', queryParams.Select(
                        kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")));
                }

                steps.Add((sb.ToString(), stepParams));
            }
            else
            {
                // Unknown segment not following a param route — just append to last step or pass through
            }
        }

        return steps.Count > 0 ? steps : [(uri, new Dictionary<string, string>())];
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private async Task GoToAsyncOnMainThread(string route)
    {
        var tcs = new TaskCompletionSource();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Shell.Current.GoToAsync(route);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
    }

    private static bool IsGenerated(string route) =>
        route.StartsWith("IMPL_", StringComparison.Ordinal) ||
        route.StartsWith("D_FAULT_", StringComparison.Ordinal);

    private static Type? GetTypeFromFactory(object? factory)
    {
        if (factory is null)
            return null;

        var typeField = factory.GetType().GetField("_type",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return typeField?.GetValue(factory) as Type;
    }

    /// <summary>
    /// Discovers <see cref="QueryPropertyAttribute"/> on the page type
    /// and on the VM type (inferred from the page's first constructor parameter).
    /// </summary>
    public static List<QueryParameterInfo> DiscoverQueryParameters(Type? pageType)
    {
        var result = new List<QueryParameterInfo>();
        if (pageType is null)
            return result;

        AddQueryProperties(pageType, result);

        var ctor = pageType.GetConstructors().FirstOrDefault();
        if (ctor is not null)
        {
            var vmParam = ctor.GetParameters().FirstOrDefault();
            if (vmParam is not null && vmParam.ParameterType != typeof(string) && !vmParam.ParameterType.IsPrimitive)
                AddQueryProperties(vmParam.ParameterType, result);
        }

        return result;
    }

    private static void AddQueryProperties(Type type, List<QueryParameterInfo> result)
    {
        var attrs = type.GetCustomAttributes(typeof(QueryPropertyAttribute), false);
        foreach (QueryPropertyAttribute attr in attrs)
        {
            if (result.Any(r => r.QueryName == attr.QueryId))
                continue;

            var prop = type.GetProperty(attr.Name);
            var typeName = prop?.PropertyType.Name ?? "string";
            result.Add(new QueryParameterInfo(attr.QueryId, attr.Name, typeName));
        }
    }
}
