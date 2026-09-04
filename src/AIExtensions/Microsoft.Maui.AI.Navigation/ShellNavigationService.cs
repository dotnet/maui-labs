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
/// values, and resolves them to a single Shell URI with route-prefixed query parameters
/// so each page receives its parameters.
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
    /// The method resolves the template to one Shell URI and issues a single
    /// <c>GoToAsync</c> call. Returns a JSON array containing the executed route.
    /// </summary>
    public virtual async Task<string> NavigateAsync(string route)
    {
        var resolvedRoute = ResolveRoute(route);
        await GoToAsyncOnMainThread(resolvedRoute);
        return System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { route = resolvedRoute, location = GetCurrentRoute() }
        });
    }

    /// <summary>
    /// Invalidates the cached route list so the next <see cref="GetRoutes"/>
    /// call rediscovers routes.
    /// </summary>
    public void InvalidateCache() => _cachedRoutes = null;

    /// <summary>
    /// Builds a multi-segment Shell route where shared query parameters are
    /// applied to intermediate pages using Shell's route-prefix convention.
    /// </summary>
    public string BuildRoute(
        string basePath,
        IReadOnlyList<string> segments,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (segments.Count == 0)
            return basePath;

        var routes = GetRoutes();
        var sb = BuildPath(basePath, segments);

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

    // ─── Route resolution ───────────────────────────────────────────

    /// <summary>
    /// Resolves a template-style URI to a Shell URI.
    /// <para>
    /// The algorithm walks each path segment and classifies it as:
    /// <list type="bullet">
    ///   <item>A Shell hierarchy segment (its route has FullPath starting with //) — accumulated into the base path</item>
    ///   <item>A registered (pushed) route — appended to the resolved path</item>
    ///   <item>An unknown segment after a parameterized route — treated as an inline parameter value</item>
    /// </list>
    /// If the URI already contains a query string (<c>?</c>), it is preserved as-is
    /// and no template parsing is applied.
    /// </para>
    /// </summary>
    internal string ResolveRoute(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return uri;

        if (uri.Contains('?'))
            return uri;

        if (!uri.Contains('/'))
            return uri;

        if (uri.StartsWith("..", StringComparison.Ordinal))
            return uri;

        var routes = GetRoutes();
        var hierarchyRoutes = new HashSet<string>(
            routes.Where(r => r.FullPath.StartsWith("//")).Select(r => r.Route),
            StringComparer.OrdinalIgnoreCase);

        var registeredRoutes = new Dictionary<string, RouteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in routes.Where(r => !r.FullPath.StartsWith("//")))
            registeredRoutes.TryAdd(r.Route, r);

        var allKnown = new HashSet<string>(
            routes.Select(r => r.Route), StringComparer.OrdinalIgnoreCase);
        var segments = uri.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var baseParts = new List<string>();
        var pushedStart = segments.Length;

        for (int i = 0; i < segments.Length; i++)
        {
            if (hierarchyRoutes.Contains(segments[i]) ||
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

        if (pushedStart >= segments.Length)
        {
            return uri.StartsWith("//", StringComparison.Ordinal)
                ? "//" + string.Join("/", baseParts)
                : string.Join("/", baseParts);
        }

        var matchedRoutes =
            new List<(RouteInfo Route, Dictionary<string, string> ExplicitParameters)>();

        for (int i = pushedStart; i < segments.Length; i++)
        {
            var seg = segments[i];
            if (!registeredRoutes.TryGetValue(seg, out var routeInfo))
            {
                return uri;
            }

            var explicitParameters =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (routeInfo.Parameters.Count > 0 &&
                i + 1 < segments.Length &&
                !allKnown.Contains(segments[i + 1]))
            {
                i++;
                explicitParameters[routeInfo.Parameters[0].QueryName] =
                    Uri.UnescapeDataString(segments[i]);
            }

            matchedRoutes.Add((routeInfo, explicitParameters));
        }

        var isAbsolute = uri.StartsWith("//", StringComparison.Ordinal);
        var basePath = string.Join("/", baseParts);
        if (isAbsolute)
            basePath = "//" + basePath;

        return BuildResolvedRoute(basePath, matchedRoutes);
    }

    // ─── Helpers ─────────────────────────────────────────────────────

    private static string BuildResolvedRoute(
        string basePath,
        IReadOnlyList<(RouteInfo Route, Dictionary<string, string> ExplicitParameters)> routes)
    {
        var sb = BuildPath(basePath, routes.Select(r => r.Route.Route));
        var inheritedParameters =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lastRouteQueryParts = new List<string>();
        var intermediateQueryParts = new List<string>();

        for (int i = 0; i < routes.Count; i++)
        {
            var (route, explicitParameters) = routes[i];
            foreach (var (key, value) in explicitParameters)
                inheritedParameters[key] = value;

            foreach (var parameter in route.Parameters)
            {
                if (!inheritedParameters.TryGetValue(parameter.QueryName, out var value))
                    continue;

                var queryName = i == routes.Count - 1
                    ? parameter.QueryName
                    : $"{route.Route}.{parameter.QueryName}";
                var queryPart =
                    $"{Uri.EscapeDataString(queryName)}={Uri.EscapeDataString(value)}";
                if (i == routes.Count - 1)
                    lastRouteQueryParts.Add(queryPart);
                else
                    intermediateQueryParts.Add(queryPart);
            }
        }

        if (lastRouteQueryParts.Count > 0 || intermediateQueryParts.Count > 0)
        {
            sb.Append('?');
            sb.Append(string.Join('&',
                lastRouteQueryParts.Concat(intermediateQueryParts)));
        }

        return sb.ToString();
    }

    private static StringBuilder BuildPath(string basePath, IEnumerable<string> segments)
    {
        var trimmedBasePath = basePath.TrimEnd('/');
        var sb = new StringBuilder(
            trimmedBasePath.Length == 0 && basePath.StartsWith("//", StringComparison.Ordinal)
                ? "//"
                : trimmedBasePath);

        foreach (var segment in segments)
        {
            if (sb.Length > 0 && sb[^1] != '/')
                sb.Append('/');
            sb.Append(segment);
        }

        return sb;
    }

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
