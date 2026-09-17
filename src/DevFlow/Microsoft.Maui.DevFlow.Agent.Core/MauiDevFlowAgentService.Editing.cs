using System.Reflection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.DevFlow.Agent.Core.Editing;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;

namespace Microsoft.Maui.DevFlow.Agent.Core;

// Design-time editing: structural changes to the live tree, clearing properties, XAML reload and
// the in-app selection overlay. Every edit targets the running app only; persisting a change to
// source is the client's job.
public partial class MauiDevFlowAgentService
{
    private SelectionOverlay? _selection;

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleClearProperty(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");
        if (!request.RouteParams.TryGetValue("name", out var propName))
            return HttpResponse.Error("Property name required");
        if (IsNativeElementId(id))
        {
            return HttpResponse.Error(
                "Generic property mutation is not supported for native elements.",
                statusCode: 400,
                reason: "native-property-not-supported");
        }

        if (await PrepareUiMutationAsync(request, ReadCaptureBinding(request), id) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        var (status, error) = await DispatchAsync(() =>
        {
            var el = ResolveCapturedElement(reservedCapture, id, elementId => _treeWalker.GetElementById(elementId, _app));
            if (el == null) return (404, $"Element '{id}' not found");
            CaptureMutationTarget(request, el);

            var type = el.GetType();
            var prop = type.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null)
                return (404, $"Property '{propName}' not found on element '{id}'");

            if (el is not BindableObject bindable || FindBindableProperty(type, prop) is not { } bp)
                return (400, $"Property '{prop.Name}' is not a bindable property and has no default to restore");
            if (bp.IsReadOnly)
                return (400, $"Property '{prop.Name}' is read-only");

            bindable.ClearValue(bp);
            return (200, (string?)null);
        });

        PublishUiOperationSpan("action.clear-property", startedAtUtc, status == 200, error, id, new { property = propName });

        if (status != 200)
            return HttpResponse.Error(error!, status, status == 404 ? "not-found" : "invalid-property");

        PublishTreeChange("modified", id, "property", parentId: null);
        var value = await DispatchAsync(() => ReadFormattedPropertyValue(id, propName));
        return HttpResponse.Json(new { id, property = propName, value });
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleAddElement(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var parentId))
            return HttpResponse.Error("Parent element ID required");

        var body = request.BodyAs<AddElementRequest>();
        if (string.IsNullOrWhiteSpace(body?.Xaml))
            return HttpResponse.Error("xaml is required", reason: "invalid-xaml");
        if (await PrepareUiMutationAsync(request, body, parentId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        var outcome = await DispatchAsync(() => RunEdit(() =>
        {
            var parent = ResolveCapturedElement(reservedCapture, parentId, id => _treeWalker.GetElementById(id, _app)) as Element
                ?? throw new EditNotFoundException($"Parent element '{parentId}' not found");

            var view = LiveTreeEditor.InflateView(body.Xaml);
            LiveTreeEditor.Insert(parent, view, body.Index);
            return BuildEditResult(view);
        }));

        PublishUiOperationSpan("action.add-element", startedAtUtc, outcome.Response is null, outcome.Error, parentId);
        if (outcome.Response is { } failure)
            return failure;

        PublishTreeChange("added", outcome.Element!.Id, outcome.Element.Type, parentId);
        return HttpResponse.Json(new { success = true, element = outcome.Element, parentId, index = outcome.Index }, 201);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleRemoveElement(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");

        if (await PrepareUiMutationAsync(request, ReadCaptureBinding(request), id) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        string? parentId = null;
        string? elementType = null;
        var outcome = await DispatchAsync(() => RunEdit(() =>
        {
            var view = ResolveEditableView(reservedCapture, id);
            CaptureMutationTarget(request, view);
            parentId = view.Parent is IVisualTreeElement p ? IdOf(p) : null;
            elementType = view.GetType().Name;
            LiveTreeEditor.Detach(view);
            return default(EditResult);
        }));

        PublishUiOperationSpan("action.remove-element", startedAtUtc, outcome.Response is null, outcome.Error, id);
        if (outcome.Response is { } failure)
            return failure;

        await DispatchAsync(() => { _selection?.ClearIfDetached(_app); return true; });
        PublishTreeChange("removed", id, elementType!, parentId);
        return HttpResponse.Json(new { success = true, id, parentId });
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleMoveElement(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");
        if (!request.RouteParams.TryGetValue("id", out var id))
            return HttpResponse.Error("Element ID required");

        var body = request.BodyAs<MoveElementRequest>();
        if (string.IsNullOrWhiteSpace(body?.ParentId))
            return HttpResponse.Error("parentId is required", reason: "invalid-target");
        if (await PrepareUiMutationAsync(request, body, id, body.ParentId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        string? oldParentId = null;
        var outcome = await DispatchAsync(() => RunEdit(() =>
        {
            var view = ResolveEditableView(reservedCapture, id);
            var parent = ResolveCapturedElement(reservedCapture, body.ParentId, elementId => _treeWalker.GetElementById(elementId, _app)) as Element
                ?? throw new EditNotFoundException($"Target parent '{body.ParentId}' not found");
            CaptureMutationTarget(request, view);

            oldParentId = view.Parent is IVisualTreeElement p ? IdOf(p) : null;
            LiveTreeEditor.Move(view, parent, body.Index);
            return BuildEditResult(view);
        }));

        PublishUiOperationSpan("action.move-element", startedAtUtc, outcome.Response is null, outcome.Error, id);
        if (outcome.Response is { } failure)
            return failure;

        PublishTreeChange("removed", id, outcome.Element!.Type, oldParentId);
        PublishTreeChange("added", outcome.Element.Id, outcome.Element.Type, body.ParentId);
        return HttpResponse.Json(new { success = true, element = outcome.Element, parentId = body.ParentId, index = outcome.Index });
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleReloadXaml(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<XamlReloadRequest>();
        if (string.IsNullOrWhiteSpace(body?.Xaml))
            return HttpResponse.Error("xaml is required", reason: "invalid-xaml");
        if (await PrepareUiMutationAsync(request, body, body.ElementId) is { } staleCapture)
            return staleCapture;

        var startedAtUtc = DateTime.UtcNow;
        var reservedCapture = GetReservedCapture(request);
        var result = await DispatchAsync(() => ReloadXaml(body, reservedCapture));

        PublishUiOperationSpan("action.reload-xaml", startedAtUtc, result.Response is null, null, result.ClassName);
        if (result.Response is { } failure)
            return failure;

        await DispatchAsync(() => { _selection?.ClearIfDetached(_app); return true; });
        foreach (var reloadedId in result.Ids)
            PublishTreeChange("modified", reloadedId, result.ClassName ?? "xaml", parentId: null);

        return HttpResponse.Json(new
        {
            success = true,
            className = result.ClassName,
            reloaded = result.Ids.Length,
            elementIds = result.Ids,
            sourceHash = result.Hash
        });
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandleHighlight(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<HighlightRequest>() ?? new HighlightRequest();
        var status = await DispatchAsync(() =>
        {
            var selection = GetSelectionOverlay();
            if (string.IsNullOrWhiteSpace(body.ElementId))
            {
                selection.Highlight(_app, null);
                return 200;
            }

            if (IsNativeElementId(body.ElementId)
                || _treeWalker.GetElementById(body.ElementId, _app) is not IVisualTreeElement element)
            {
                return 404;
            }

            return selection.Highlight(_app, element) ? 200 : 501;
        });

        return status switch
        {
            200 => HttpResponse.Json(new { success = true, elementId = body.ElementId }),
            404 => HttpResponse.Error($"Element '{body.ElementId}' not found", 404, "not-found"),
            _ => NotSupported("ui.highlight", "The element's window has no visual diagnostics overlay.")
        };
    }

    /// <inheritdoc />
    protected override async Task<HttpResponse> HandlePickMode(HttpRequest request)
    {
        if (_app == null) return HttpResponse.Error("Agent not bound to app");

        var body = request.BodyAs<PickModeRequest>() ?? new PickModeRequest();
        var installed = await DispatchAsync(() => GetSelectionOverlay().SetPickMode(_app, body.Enabled));

        // Turning pick mode on without an overlay would report success while taps kept going to the
        // app, leaving the client waiting for a pick that can never arrive.
        if (!installed && body.Enabled)
            return NotSupported("ui.highlight", "The app's windows have no visual diagnostics overlay.");

        return HttpResponse.Json(new { success = true, enabled = body.Enabled });
    }

    /// <summary>The overlay behind highlight and pick mode. Internal so tests can simulate a pick tap.</summary>
    internal SelectionOverlay GetSelectionOverlay()
    {
        if (_selection is { } existing)
            return existing;

        var selection = new SelectionOverlay();
        selection.Picked += picked =>
        {
            if (_app is null)
                return;

            _treeWalker.WalkTree(_app, 0, null);
            var pickedId = IdOfWalked(picked);
            if (pickedId is null)
                return;

            PublishUiEvent("elementPicked", new
            {
                elementId = pickedId,
                elementType = picked.GetType().Name,
                timestamp = DateTimeOffset.UtcNow.ToString("O")
            });
        };
        _selection = selection;
        return selection;
    }

    private readonly record struct ReloadOutcome(HttpResponse? Response, string[] Ids, string? ClassName, string? Hash)
    {
        public static ReloadOutcome Failed(HttpResponse response, string? className = null)
            => new(response, [], className, null);
    }

    private ReloadOutcome ReloadXaml(XamlReloadRequest body, UiCaptureContext capture)
    {
        string? className;
        try
        {
            className = body.ClassName ?? XamlReloader.GetClassName(body.Xaml!);
        }
        catch (LiveTreeEditException ex)
        {
            return ReloadOutcome.Failed(HttpResponse.Error(ex.Message, 400, ex.Reason));
        }

        List<Element> targets;
        if (!string.IsNullOrWhiteSpace(body.ElementId))
        {
            if (ResolveCapturedElement(capture, body.ElementId, id => _treeWalker.GetElementById(id, _app)) is not Element target)
                return ReloadOutcome.Failed(HttpResponse.Error($"Element '{body.ElementId}' not found", 404, "not-found"), className);
            if (className != null && target.GetType().FullName != className)
            {
                return ReloadOutcome.Failed(HttpResponse.Error(
                    $"Element '{body.ElementId}' is a {target.GetType().FullName}, not {className}",
                    400,
                    "class-mismatch"), className);
            }
            targets = [target];
        }
        else if (className is null)
        {
            return ReloadOutcome.Failed(HttpResponse.Error("XAML has no x:Class and no elementId was given", 400, "invalid-xaml"));
        }
        else
        {
            targets = XamlReloader.FindLiveInstances(_app!, className).ToList();
        }

        try
        {
            // Reload restores a target it could not inflate, so invalid XAML fails on the first one
            // and leaves the rest untouched.
            foreach (var target in targets)
                XamlReloader.Reload(target, body.Xaml!);
        }
        catch (XamlParseException ex)
        {
            return ReloadOutcome.Failed(HttpResponse.Error(
                $"Invalid XAML: {ex.Message}",
                400,
                "invalid-xaml",
                new { line = ex.XmlInfo?.LineNumber, column = ex.XmlInfo?.LinePosition }), className);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException or System.Xml.XmlException)
        {
            return ReloadOutcome.Failed(HttpResponse.Error($"XAML reload failed: {ex.Message}", 400, "reload-failed"), className);
        }

        // The build-time source map describes the old text. Replace it so element -> line mapping
        // and sourceHash follow what is now running.
        string? hash = null;
        var typeName = className ?? targets.FirstOrDefault()?.GetType().FullName;
        if (typeName != null)
        {
            var file = XamlSourceMapRegistry.Instance.GetMap(typeName)?.File ?? body.SourceFile;
            if (file != null && XamlSourceMap.Parse(body.Xaml!, file) is { } map)
            {
                XamlSourceMapRegistry.Override(typeName, map);
                hash = map.ContentHash;
            }
        }

        _treeWalker.WalkTree(_app!, 0, null);
        var ids = targets
            .OfType<IVisualTreeElement>()
            .Select(IdOfWalked)
            .OfType<string>()
            .ToArray();
        return new ReloadOutcome(null, ids, typeName, hash);
    }

    private readonly record struct EditResult(ElementInfo? Element, int? Index);

    private readonly record struct EditOutcome(HttpResponse? Response, string? Error, ElementInfo? Element, int? Index);

    private sealed class EditNotFoundException(string message) : Exception(message);

    private static EditOutcome RunEdit(Func<EditResult> edit)
    {
        try
        {
            var result = edit();
            return new EditOutcome(null, null, result.Element, result.Index);
        }
        catch (EditNotFoundException ex)
        {
            return new EditOutcome(HttpResponse.Error(ex.Message, 404, "not-found"), ex.Message, null, null);
        }
        catch (LiveTreeEditException ex)
        {
            return new EditOutcome(HttpResponse.Error(ex.Message, 400, ex.Reason), ex.Message, null, null);
        }
    }

    private View ResolveEditableView(UiCaptureContext capture, string id)
    {
        if (IsNativeElementId(id))
            throw new LiveTreeEditException("Native elements cannot be edited structurally", "native-element-not-supported");

        return ResolveCapturedElement(capture, id, elementId => _treeWalker.GetElementById(elementId, _app)) switch
        {
            View view => view,
            null => throw new EditNotFoundException($"Element '{id}' not found"),
            var other => throw new LiveTreeEditException($"{other.GetType().Name} is not a view and cannot be moved or removed", "not-a-view")
        };
    }

    private EditResult BuildEditResult(View view)
    {
        var tree = _treeWalker.WalkTree(_app!, 0, null);
        var id = IdOfWalked(view);
        var info = id is null
            ? null
            : VisualTreeWalker.FlattenElementInfos(tree).FirstOrDefault(element => element.Id == id);
        return new EditResult(info, LiveTreeEditor.IndexInParent(view));
    }

    /// <summary>Id of an element as of a fresh tree walk.</summary>
    private string? IdOf(IVisualTreeElement element)
    {
        _treeWalker.WalkTree(_app!, 0, null);
        return IdOfWalked(element);
    }

    /// <summary>Id of an element from the most recent tree walk.</summary>
    private string? IdOfWalked(IVisualTreeElement element) => _treeWalker.GetIdForElement(element);

    private void PublishTreeChange(string changeType, string elementId, string elementType, string? parentId)
        => PublishUiEvent("treeChange", new
        {
            changeType,
            elementId,
            elementType,
            parentId,
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        });

    // DELETE requests usually carry no body, so capture metadata may also arrive as query parameters.
    private static CaptureBoundRequest ReadCaptureBinding(HttpRequest request)
    {
        var binding = request.BodyAs<CaptureBoundRequest>() ?? new CaptureBoundRequest();
        binding.CaptureEpoch ??= ParseLongQueryParameter(request, "captureEpoch");
        binding.RegistryGeneration ??= ParseLongQueryParameter(request, "registryGeneration");
        return binding;
    }
}
