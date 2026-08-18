using System.Text.Json;

using Microsoft.Maui.Controls;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// MAUI implementations of the framework-neutral mutation-recording and WebView-resolution seams.
/// </summary>
public partial class MauiDevFlowAgentService
{
    /// <inheritdoc />
    protected override async Task<HashSet<string>> GetActiveWebViewAutomationIdsAsync()
    {
        if (_app is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return await DispatchAsync(() =>
            {
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var window = _app.Windows.FirstOrDefault();

                static Page? ActivePage(Page? page)
                {
                    while (page is not null)
                    {
                        page = page switch
                        {
                            Shell => Shell.Current?.CurrentPage,
                            NavigationPage navigationPage => navigationPage.CurrentPage,
                            TabbedPage tabbedPage => tabbedPage.CurrentPage,
                            FlyoutPage flyoutPage => flyoutPage.Detail,
                            _ => page
                        };

                        if (page is not Shell and
                            not NavigationPage and
                            not TabbedPage and
                            not FlyoutPage)
                        {
                            return page;
                        }
                    }

                    return null;
                }

                var root = window?.Navigation?.ModalStack.LastOrDefault()
                    ?? ActivePage(window?.Page);
                if (root is not IVisualTreeElement rootElement)
                    return ids;

                static bool IsBlazorWebView(IVisualTreeElement element)
                {
                    for (var type = element.GetType(); type is not null; type = type.BaseType)
                    {
                        if (type.FullName is
                            "Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView" or
                            "Microsoft.Maui.Platforms.MacOS.Controls.MacOSBlazorWebView")
                        {
                            return true;
                        }
                    }

                    return false;
                }

                void Visit(IVisualTreeElement element)
                {
                    if (element is VisualElement visualElement &&
                        IsBlazorWebView(element) &&
                        !string.IsNullOrWhiteSpace(visualElement.AutomationId))
                    {
                        ids.Add(visualElement.AutomationId);
                    }

                    foreach (var child in element.GetVisualChildren())
                        Visit(child);
                }

                Visit(rootElement);
                return ids;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Microsoft.Maui.DevFlow] Failed to resolve active WebView: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    internal override async Task<MutationObservation?> CreateMutationObservationAsync(HttpRequest request)
    {
        string? action = null;
        string? targetId = null;
        string? value = null;
        string? name = null;
        double? dx = null;
        double? dy = null;
        int? itemIndex = null;
        string? position = null;

        JsonElement body = default;
        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            try
            {
                using var document = JsonDocument.Parse(request.Body);
                body = document.RootElement.Clone();
            }
            catch { }
        }

        switch (request.Path)
        {
            case "/api/v1/ui/actions/tap":
                action = "tap";
                targetId = ReadJsonString(body, "elementId");
                break;
            case "/api/v1/ui/actions/fill":
                action = "fill";
                targetId = ReadJsonString(body, "elementId");
                value = ReadJsonString(body, "text") ?? "";
                break;
            case "/api/v1/ui/actions/scroll":
                action = "scroll";
                targetId = ReadJsonString(body, "elementId");
                dx = ReadJsonDouble(body, "deltaX");
                dy = ReadJsonDouble(body, "deltaY");
                itemIndex = ReadJsonInt(body, "itemIndex");
                position = ReadJsonString(body, "scrollToPosition");
                break;
            case "/api/v1/ui/actions/navigate":
                action = "navigate";
                value = ReadJsonString(body, "route");
                break;
            case "/api/v1/ui/actions/back":
                action = "back";
                break;
            case "/api/v1/device/app/theme":
                action = "setTheme";
                value = ReadJsonString(body, "theme");
                break;
            default:
                if (request.Method.Equals("PUT", StringComparison.OrdinalIgnoreCase) &&
                    request.Path.StartsWith("/api/v1/ui/elements/", StringComparison.OrdinalIgnoreCase) &&
                    request.Path.Contains("/properties/", StringComparison.OrdinalIgnoreCase))
                {
                    action = "setProperty";
                    request.RouteParams.TryGetValue("id", out targetId);
                    request.RouteParams.TryGetValue("name", out name);
                    value = ReadJsonString(body, "value") ?? "";
                }
                break;
        }

        if (action is null)
            return null;

        ElementInfo? target = null;
        if (!string.IsNullOrWhiteSpace(targetId) && _app is not null)
        {
            target = await DispatchAsync(() =>
            {
                var tree = _treeWalker.WalkTree(_app, _options.MaxTreeDepth);
                return VisualTreeWalker.FlattenElementInfos(tree)
                    .FirstOrDefault(element => string.Equals(element.Id, targetId, StringComparison.Ordinal));
            });
        }

        var observedProperty = action == "fill"
            ? "Text"
            : (action == "setProperty" ? name : null);
        if (!string.IsNullOrWhiteSpace(targetId) && !string.IsNullOrWhiteSpace(observedProperty) && _app is not null)
        {
            var runtimeValue = await DispatchAsync(() => ReadFormattedPropertyValue(targetId, observedProperty));
            if (runtimeValue is not null)
                value = runtimeValue;
        }

        var avoidText = action == "fill" ||
            (action == "setProperty" && string.Equals(name, "Text", StringComparison.OrdinalIgnoreCase));
        var automationId = request.MutationTargetAutomationId ?? target?.AutomationId;
        var route = await DispatchAsync(() => GetCurrentRouteLocation());
        return new MutationObservation
        {
            Action = action,
            AutomationId = automationId,
            Text = !avoidText && string.IsNullOrWhiteSpace(automationId) ? target?.Text : null,
            Type = null,
            Id = string.IsNullOrWhiteSpace(automationId) && (avoidText || string.IsNullOrWhiteSpace(target?.Text))
                ? targetId
                : null,
            Value = value,
            Name = name,
            Dx = dx,
            Dy = dy,
            ItemIndex = itemIndex,
            Position = position,
            Page = route
        };
    }
}
