using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Css;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// DevFlow agent backed by the platform's own view hierarchy — <c>Android.Views.View</c>,
/// <c>UIView</c> or <c>NSView</c> — with no dependency on .NET MAUI.
/// </summary>
/// <remarks>
/// Everything framework-neutral (HTTP, routing, logs, network capture, profiler, extensions,
/// invoke/actions, WebView CDP registry) is inherited from <see cref="DevFlowAgentService"/>.
/// This type only implements the UI seam.
/// </remarks>
public class NativeDevFlowAgentService : DevFlowAgentService
{
    private readonly NativeElementRegistry _registry = new();
    private volatile bool _bound;

    /// <summary>
    /// Creates a native agent service.
    /// </summary>
    public NativeDevFlowAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    // ── Framework identity ────────────────────────────────────────────────

    /// <inheritdoc />
    protected override string FrameworkName => "native";

    /// <inheritdoc />
    protected override string FrameworkDisplayName => ".NET";

    /// <inheritdoc />
    protected override string UiFrameworkName => NativeUi.UiFrameworkName;

    /// <inheritdoc />
    protected override string PlatformName => NativeUi.PlatformName;

    /// <inheritdoc />
    protected override string DeviceTypeName => NativeUi.DeviceTypeName;

    /// <inheritdoc />
    protected override bool IsUiSupported => true;

    /// <inheritdoc />
    protected override bool IsScreenshotSupported => true;

    /// <inheritdoc />
    public override bool IsAppBound => _bound;

    /// <inheritdoc />
    protected override string AppDisplayName => NativeUi.AppName ?? base.AppDisplayName;

    /// <inheritdoc />
    protected override string AppPackageId => NativeUi.AppPackageId ?? base.AppPackageId;

    /// <inheritdoc />
    protected override string AppVersionString => NativeUi.AppVersion ?? base.AppVersionString;

    /// <inheritdoc />
    protected override string AppBuildString => NativeUi.AppBuild ?? base.AppBuildString;

    /// <inheritdoc />
    protected override int WindowCount => NativeUi.GetRoots().Count;

    /// <inheritdoc />
    protected override (double Width, double Height, double Density) GetWindowMetrics(int? windowIndex)
    {
        var (width, height) = NativeUi.GetWindowSize();
        return (width, height, NativeUi.DisplayDensity);
    }

    /// <summary>
    /// Starts the HTTP server and binds to the platform UI. Called by <see cref="DevFlowAgent"/>.
    /// </summary>
    public void Start()
    {
        _bound = true;
        StartServerOnly(NativeUi.CreateDispatcher());
    }

    // ── Tree, query, hit test ─────────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleTree(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        int maxDepth = 0;
        if (request.QueryParams.TryGetValue("depth", out var depthStr))
            int.TryParse(depthStr, out maxDepth);

        var windowIndex = ParseWindowIndex(request);
        var tree = await DispatchAsync(() => BuildTree(maxDepth, windowIndex));
        return HttpResponse.Json(tree);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleElement(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        if (!request.QueryParams.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
            return HttpResponse.Error("id is required");

        var element = await DispatchAsync(() =>
        {
            BuildTree(0, null);
            var view = _registry.Resolve(id);
            return view == null ? null : Describe(view, id, _registry.ParentOf(id), includeChildren: true, maxDepth: 0, depth: 0);
        });

        return element == null
            ? HttpResponse.NotFound($"Element '{id}' not found")
            : HttpResponse.Json(element);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleQuery(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        if (request.QueryParams.TryGetValue("selector", out var selector) && !string.IsNullOrWhiteSpace(selector))
        {
            try
            {
                var matches = await DispatchAsync(() => CssSelectorEngine.Query(BuildTree(0, null), selector));
                return HttpResponse.Json(Flatten(matches));
            }
            catch (FormatException ex)
            {
                return HttpResponse.Error($"Invalid CSS selector: {ex.Message}");
            }
        }

        request.QueryParams.TryGetValue("type", out var type);
        request.QueryParams.TryGetValue("automationId", out var automationId);
        request.QueryParams.TryGetValue("text", out var text);

        if (type == null && automationId == null && text == null)
            return HttpResponse.Error("At least one query parameter required: type, automationId, text, or selector");

        var results = await DispatchAsync(() =>
        {
            var matches = new List<ElementInfo>();
            Visit(BuildTree(0, null), element =>
            {
                if (type != null && !string.Equals(element.Type, type, StringComparison.OrdinalIgnoreCase)) return;
                if (automationId != null && !string.Equals(element.AutomationId, automationId, StringComparison.Ordinal)) return;
                if (text != null && element.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) != true) return;
                matches.Add(Detach(element));
            });
            return matches;
        });

        return HttpResponse.Json(results);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleHitTest(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        if (!TryParseDouble(request, "x", out var x) || !TryParseDouble(request, "y", out var y))
            return HttpResponse.Error("x and y query parameters are required");

        var hits = await DispatchAsync(() =>
        {
            var matches = new List<ElementInfo>();
            Visit(BuildTree(0, null), element =>
            {
                var bounds = element.Bounds;
                if (bounds == null || !element.IsVisible) return;
                if (x < bounds.X || x > bounds.X + bounds.Width) return;
                if (y < bounds.Y || y > bounds.Y + bounds.Height) return;
                matches.Add(Detach(element));
            });

            // Deepest-first: the last element added while walking depth-first is the topmost hit.
            matches.Reverse();
            return matches;
        });

        return HttpResponse.Json(hits);
    }

    // ── Properties ────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleProperty(HttpRequest request)
    {
        if (!TryGetElementId(request, out var id, out var error)) return error!;
        if (!request.QueryParams.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return HttpResponse.Error("name is required");

        var result = await DispatchAsync(() =>
        {
            var view = ResolveView(id);
            if (view == null) return (object?)null;

            var properties = NativeUi.GetProperties(view);
            return properties.TryGetValue(name, out var value)
                ? new { elementId = id, name, value }
                : null;
        });

        return result == null
            ? HttpResponse.NotFound($"Property '{name}' not found on element '{id}'")
            : HttpResponse.Json(result);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleSetProperty(HttpRequest request)
    {
        if (!TryGetElementId(request, out var id, out var error)) return error!;
        if (!request.QueryParams.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return HttpResponse.Error("name is required");

        var body = request.BodyAs<SetPropertyRequest>();
        var result = await DispatchAsync(() =>
        {
            var view = ResolveView(id);
            if (view == null) return $"Element '{id}' not found";

            return NativeUi.TrySetProperty(view, name, body?.Value, out var failure) ? "ok" : failure ?? "Set failed";
        });

        return result == "ok" ? HttpResponse.Ok($"Set {name}") : HttpResponse.Error(result!);
    }

    // ── Interactions ──────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override Task<HttpResponse> HandleTap(HttpRequest request)
        => ActOnElement(request, "action.tap", (view, _) => NativeUi.TryTap(view, null, null) ? "ok" : "Tap not handled", "Tapped");

    /// <inheritdoc />
    protected override Task<HttpResponse> HandleFill(HttpRequest request)
    {
        var body = request.BodyAs<FillRequest>();
        return ActOnElement(
            request,
            "action.fill",
            (view, _) => NativeUi.TrySetText(view, body?.Text ?? string.Empty) ? "ok" : "Element is not editable",
            "Filled",
            body?.ElementId);
    }

    /// <inheritdoc />
    protected override Task<HttpResponse> HandleClear(HttpRequest request)
        => ActOnElement(request, "action.clear", (view, _) => NativeUi.TrySetText(view, string.Empty) ? "ok" : "Element is not editable", "Cleared");

    /// <inheritdoc />
    protected override Task<HttpResponse> HandleFocus(HttpRequest request)
        => ActOnElement(request, "action.focus", (view, _) => NativeUi.TryFocus(view) ? "ok" : "Element cannot take focus", "Focused");

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleBack(HttpRequest request)
    {
        var went = await DispatchAsync(NativeUi.TryGoBack);
        return went ? HttpResponse.Ok("Navigated back") : HttpResponse.Error("Nothing to navigate back to");
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleScroll(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<ScrollRequest>();
        if (body?.ElementId == null) return HttpResponse.Error("elementId is required");

        var result = await DispatchAsync(() =>
        {
            var view = ResolveView(body.ElementId);
            if (view == null) return $"Element '{body.ElementId}' not found";

            if (body.DeltaX == 0 && body.DeltaY == 0)
                return NativeUi.TryScrollIntoView(view) ? "ok" : "Element is not inside a scrollable container";

            return NativeUi.TryScrollBy(view, body.DeltaX, body.DeltaY) ? "ok" : "Element is not scrollable";
        });

        return result == "ok" ? HttpResponse.Ok("Scrolled") : HttpResponse.Error(result!);
    }

    // ── Screenshot ────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleScreenshot(HttpRequest request)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        int? maxWidth = null;
        if (request.QueryParams.TryGetValue("maxWidth", out var maxWidthText)
            && int.TryParse(maxWidthText, out var parsed) && parsed > 0)
        {
            maxWidth = parsed;
        }

        var autoScale = true;
        if (request.QueryParams.TryGetValue("scale", out var scale))
        {
            autoScale = !scale.Equals("native", StringComparison.OrdinalIgnoreCase)
                     && !scale.Equals("full", StringComparison.OrdinalIgnoreCase);
        }

        var hasElementId = request.QueryParams.TryGetValue("id", out var elementId)
            || request.QueryParams.TryGetValue("elementId", out elementId);

        try
        {
            var png = await DispatchAsync(() =>
            {
                if (hasElementId && !string.IsNullOrEmpty(elementId))
                {
                    var view = ResolveView(elementId);
                    return view == null ? null : NativeUi.CaptureView(view);
                }

                return NativeUi.CaptureScreen();
            });

            if (png == null)
            {
                return hasElementId
                    ? HttpResponse.NotFound($"Element '{elementId}' not found or has no size")
                    : HttpResponse.Error("Screen capture returned no data");
            }

            return HttpResponse.Png(ResizePngIfNeeded(png, maxWidth, NativeUi.DisplayDensity, autoScale));
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Screenshot failed: {ex.Message}");
        }
    }

    // ── Tree construction ─────────────────────────────────────────────────

    /// <summary>
    /// Walks the live native view hierarchy. Must be called on the UI thread.
    /// </summary>
    private List<ElementInfo> BuildTree(int maxDepth, int? windowIndex)
    {
        _registry.BeginWalk();

        var roots = NativeUi.GetRoots();
        var tree = new List<ElementInfo>();

        for (int i = 0; i < roots.Count; i++)
        {
            if (windowIndex.HasValue && windowIndex.Value != i) continue;

            var id = _registry.Register(roots[i], parentId: null);
            tree.Add(Describe(roots[i], id, null, includeChildren: true, maxDepth, depth: 0));
        }

        return tree;
    }

    private ElementInfo Describe(object view, string id, string? parentId, bool includeChildren, int maxDepth, int depth)
    {
        var descriptor = NativeUi.Describe(view);

        var element = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = descriptor.Type,
            FullType = descriptor.FullType,
            NativeType = descriptor.FullType,
            Framework = "native",
            AutomationId = descriptor.AutomationId,
            Text = descriptor.Text ?? descriptor.AccessibilityLabel,
            Value = descriptor.Value,
            IsVisible = descriptor.IsVisible,
            IsEnabled = descriptor.IsEnabled,
            IsFocused = descriptor.IsFocused,
            IsSelected = descriptor.IsSelected,
            Opacity = descriptor.Opacity,
            Bounds = new BoundsInfo
            {
                X = descriptor.X,
                Y = descriptor.Y,
                Width = descriptor.Width,
                Height = descriptor.Height,
            },
            NativeProperties = descriptor.Properties,
            Gestures = descriptor.IsTappable ? ["tap"] : null,
        };

        if (descriptor.IsTextInput || descriptor.IsScrollable || descriptor.IsTappable)
        {
            element.Traits = BuildNativeTraits(descriptor);
        }

        if (!includeChildren || (maxDepth > 0 && depth >= maxDepth))
            return element;

        var children = NativeUi.GetChildren(view);
        if (children.Count == 0) return element;

        element.Children = new List<ElementInfo>(children.Count);
        foreach (var child in children)
        {
            var childId = _registry.Register(child, id);
            element.Children.Add(Describe(child, childId, id, includeChildren: true, maxDepth, depth + 1));
        }

        return element;
    }

    private static List<string> BuildNativeTraits(NativeViewDescriptor descriptor)
    {
        var traits = new List<string>();
        if (descriptor.IsTappable || descriptor.IsTextInput) traits.Add("interactive");
        if (descriptor.IsTappable || descriptor.IsTextInput) traits.Add("focusable");
        if (descriptor.IsScrollable) traits.Add("scrollable");
        return traits;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private object? ResolveView(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var view = _registry.Resolve(id);
        if (view != null) return view;

        // The client may be holding an id from a tree it fetched before a navigation.
        // Re-walk once so stale-but-still-live ids resolve instead of hard-failing.
        BuildTree(0, null);
        return _registry.Resolve(id);
    }

    private async Task<HttpResponse> ActOnElement(
        HttpRequest request,
        string span,
        Func<object, string, string> action,
        string successMessage,
        string? elementIdOverride = null)
    {
        if (!_bound) return HttpResponse.Error("Agent not bound to app");

        var elementId = elementIdOverride ?? request.BodyAs<ActionRequest>()?.ElementId;
        if (string.IsNullOrEmpty(elementId)) return HttpResponse.Error("elementId is required");

        var startedAtUtc = DateTime.UtcNow;
        var result = await DispatchAsync(() =>
        {
            var view = ResolveView(elementId);
            return view == null ? $"Element '{elementId}' not found" : action(view, elementId);
        });

        PublishUiOperationSpan(span, startedAtUtc, result == "ok", result == "ok" ? null : result, elementId);

        return result == "ok" ? HttpResponse.Ok(successMessage) : HttpResponse.Error(result!);
    }

    private bool TryGetElementId(HttpRequest request, out string? id, out HttpResponse? error)
    {
        id = null;
        error = null;

        if (!_bound)
        {
            error = HttpResponse.Error("Agent not bound to app");
            return false;
        }

        if (request.QueryParams.TryGetValue("id", out var value) && !string.IsNullOrEmpty(value))
        {
            id = value;
            return true;
        }

        if (request.QueryParams.TryGetValue("elementId", out value) && !string.IsNullOrEmpty(value))
        {
            id = value;
            return true;
        }

        error = HttpResponse.Error("id is required");
        return false;
    }

    private static bool TryParseDouble(HttpRequest request, string key, out double value)
        => double.TryParse(
            request.QueryParams.TryGetValue(key, out var text) ? text : null,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    private static void Visit(List<ElementInfo> tree, Action<ElementInfo> visitor)
    {
        foreach (var element in tree)
        {
            visitor(element);
            if (element.Children is { Count: > 0 } children)
                Visit(children, visitor);
        }
    }

    private static List<ElementInfo> Flatten(List<ElementInfo> matches)
        => matches.Select(Detach).ToList();

    /// <summary>Returns a copy without children so query results stay flat.</summary>
    private static ElementInfo Detach(ElementInfo element)
    {
        if (element.Children == null) return element;

        return new ElementInfo
        {
            Id = element.Id,
            ParentId = element.ParentId,
            Type = element.Type,
            FullType = element.FullType,
            NativeType = element.NativeType,
            Framework = element.Framework,
            AutomationId = element.AutomationId,
            Text = element.Text,
            Value = element.Value,
            IsVisible = element.IsVisible,
            IsEnabled = element.IsEnabled,
            IsFocused = element.IsFocused,
            IsSelected = element.IsSelected,
            Opacity = element.Opacity,
            Bounds = element.Bounds,
            NativeProperties = element.NativeProperties,
            Gestures = element.Gestures,
        };
    }
}
