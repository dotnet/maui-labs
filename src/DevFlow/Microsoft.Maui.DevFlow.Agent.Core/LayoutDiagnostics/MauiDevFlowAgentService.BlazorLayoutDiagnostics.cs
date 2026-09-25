using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class MauiDevFlowAgentService
{
    private const string BlazorLayoutExpression = """
        (() => {
          const allElements = Array.from(document.querySelectorAll('body *'));
          const elements = allElements.slice(0, 500);
          const elementIndex = new Map(elements.map((element, index) => [element, index]));
          const gridSize = __GRID_SIZE__;
          const includeRawText = __INCLUDE_RAW_TEXT__;
          const collectOcclusion = __COLLECT_OCCLUSION__;
          const collectAllOcclusion = __COLLECT_ALL_OCCLUSION__;
          const isOpaque = value => {
            const match = /rgba?\(([^)]+)\)/.exec(value || '');
            if (!match) return false;
            const parts = match[1].split(',').map(part => Number(part.trim()));
            return parts.length < 4 ? true : parts[3] >= 0.99;
          };
          const opacityCache = new WeakMap();
          const effectiveOpacity = element => {
            if (!element) return 1;
            if (opacityCache.has(element)) return opacityCache.get(element);
            const own = Number(getComputedStyle(element).opacity || 1);
            const value = own * effectiveOpacity(element.parentElement);
            opacityCache.set(element, value);
            return value;
          };
          const ancestorClipsFor = element => {
            const clips = [];
            for (let ancestor = element.parentElement; ancestor; ancestor = ancestor.parentElement) {
              const style = getComputedStyle(ancestor);
              const clipsX = style.overflowX !== 'visible';
              const clipsY = style.overflowY !== 'visible';
              if (!clipsX && !clipsY) continue;
              const rect = ancestor.getBoundingClientRect();
              const left = rect.left + ancestor.clientLeft;
              const top = rect.top + ancestor.clientTop;
              clips.push({
                clipperIndex: elementIndex.has(ancestor) ? elementIndex.get(ancestor) : -1,
                kind: style.overflowX === 'auto' || style.overflowX === 'scroll'
                  || style.overflowY === 'auto' || style.overflowY === 'scroll'
                    ? 'scroll-viewport'
                    : 'ancestor-layout-clip',
                rect: {
                  x: clipsX ? left : 0,
                  y: clipsY ? top : 0,
                  width: clipsX ? ancestor.clientWidth : window.innerWidth,
                  height: clipsY ? ancestor.clientHeight : window.innerHeight
                }
              });
            }
            return clips;
          };
          const measureDirectText = element => {
            const textNodes = Array.from(element.childNodes)
              .filter(node => node.nodeType === Node.TEXT_NODE && /\S/.test(node.nodeValue || ''));
            if (!textNodes.length) return null;

            const text = textNodes.map(node => node.nodeValue || '').join('');
            const rects = [];
            for (const textNode of textNodes) {
              const range = document.createRange();
              range.selectNodeContents(textNode);
              for (const rect of range.getClientRects()) {
                if (rect.width > 0 && rect.height > 0) {
                  rects.push({
                    left: rect.left,
                    top: rect.top,
                    right: rect.right,
                    bottom: rect.bottom,
                    width: rect.width,
                    height: rect.height
                  });
                }
              }
              range.detach?.();
            }
            if (!rects.length) {
              return {
                kind: null,
                truncated: false,
                length: text.length,
                text: includeRawText ? text : null,
                renderedLineCount: 0,
                contentWidth: 0,
                contentHeight: 0,
                availableWidth: 0,
                availableHeight: 0
              };
            }

            const bounds = {
              left: Math.min(...rects.map(rect => rect.left)),
              top: Math.min(...rects.map(rect => rect.top)),
              right: Math.max(...rects.map(rect => rect.right)),
              bottom: Math.max(...rects.map(rect => rect.bottom))
            };
            const lineTops = [];
            for (const rect of rects) {
              if (!lineTops.some(top => Math.abs(top - rect.top) <= 1))
                lineTops.push(rect.top);
            }

            let clipping = null;
            for (let clipper = element; clipper; clipper = clipper.parentElement) {
              const clipperStyle = getComputedStyle(clipper);
              const clipperRect = clipper.getBoundingClientRect();
              const clipLeft = clipperRect.left + clipper.clientLeft;
              const clipTop = clipperRect.top + clipper.clientTop;
              const clipRight = clipLeft + clipper.clientWidth;
              const clipBottom = clipTop + clipper.clientHeight;
              const lineClamp = clipperStyle.webkitLineClamp || clipperStyle.lineClamp || 'none';
              const clampsLines = lineClamp !== 'none' && lineClamp !== '0';
              const clipsX = clipperStyle.overflowX === 'hidden'
                || clipperStyle.overflowX === 'clip'
                || clipperStyle.textOverflow === 'ellipsis';
              const clipsY = clipperStyle.overflowY === 'hidden'
                || clipperStyle.overflowY === 'clip'
                || clampsLines;
              let outsideX = clipsX && rects.some(rect =>
                rect.left < clipLeft - 1 || rect.right > clipRight + 1);
              let outsideY = clipsY && rects.some(rect =>
                rect.top < clipTop - 1 || rect.bottom > clipBottom + 1);
              if (clipper === element && element.children.length === 0) {
                outsideX ||= clipsX && element.scrollWidth > element.clientWidth + 1;
                outsideY ||= clipsY && element.scrollHeight > element.clientHeight + 1;
              }
              if (!outsideX && !outsideY) continue;

              clipping = {
                kind: clipperStyle.textOverflow === 'ellipsis' || clampsLines
                  ? 'ellipsis'
                  : outsideY ? 'vertical-hard-clip' : 'horizontal-hard-clip',
                availableWidth: clipper.clientWidth,
                availableHeight: clipper.clientHeight
              };
              break;
            }

            return {
              kind: clipping?.kind || null,
              truncated: clipping !== null,
              length: text.length,
              text: includeRawText ? text : null,
              renderedLineCount: lineTops.length,
              contentWidth: bounds.right - bounds.left,
              contentHeight: bounds.bottom - bounds.top,
              availableWidth: clipping?.availableWidth
                ?? (element.clientWidth > 0 ? element.clientWidth : bounds.right - bounds.left),
              availableHeight: clipping?.availableHeight
                ?? (element.clientHeight > 0 ? element.clientHeight : bounds.bottom - bounds.top)
            };
          };
          const nodes = elements.map((element, index) => {
            const rect = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            const renderedOpacity = effectiveOpacity(element);
            const role = element.getAttribute('role');
            const tag = element.tagName.toLowerCase();
            const interactive = ['button','a','input','textarea','select','summary'].includes(tag)
              || ['button','link','textbox','checkbox','radio','switch','menuitem','option'].includes(role || '')
              || element.tabIndex >= 0;
            let blockedSamples = 0;
            let sampleCount = 0;
            const blockerCounts = new Map();
            if (collectOcclusion && (collectAllOcclusion || interactive) && rect.width > 0 && rect.height > 0) {
              for (let row = 0; row < gridSize; row++) {
                for (let column = 0; column < gridSize; column++) {
                  const x = rect.left + rect.width * (column + 0.5) / gridSize;
                  const y = rect.top + rect.height * (row + 0.5) / gridSize;
                  const top = document.elementFromPoint(x, y);
                  sampleCount++;
                  if (!top || top === element || element.contains(top)) continue;
                  const blocker = top.closest('body *');
                  if (!blocker) continue;
                  blockedSamples++;
                  if (!elementIndex.has(blocker)) continue;
                  const blockerIndex = elementIndex.get(blocker);
                  blockerCounts.set(blockerIndex, (blockerCounts.get(blockerIndex) || 0) + 1);
                }
              }
            }
            let blockedByIndex = -1;
            let largestBlockerCount = 0;
            for (const [blockerIndex, count] of blockerCounts) {
              if (count > largestBlockerCount) {
                blockedByIndex = blockerIndex;
                largestBlockerCount = count;
              }
            }
            const widthOverflow = element.scrollWidth > element.clientWidth + 1;
            const heightOverflow = element.scrollHeight > element.clientHeight + 1;
            const directText = measureDirectText(element);
            const ancestorClips = ancestorClipsFor(element);
            return {
              index,
              parentIndex: elementIndex.has(element.parentElement) ? elementIndex.get(element.parentElement) : -1,
              tag,
              id: element.id || element.getAttribute('data-testid') || null,
              role,
              interactive,
              visible: style.display !== 'none' && style.visibility !== 'hidden' && renderedOpacity > 0.01,
              opaque: isOpaque(style.backgroundColor) && renderedOpacity >= 0.99,
              opacity: Number(style.opacity || 1),
              zIndex: Number.isFinite(Number(style.zIndex)) ? Number(style.zIndex) : 0,
              rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
              clientWidth: element.clientWidth,
              clientHeight: element.clientHeight,
              scrollWidth: element.scrollWidth,
              scrollHeight: element.scrollHeight,
              overflowX: style.overflowX,
              overflowY: style.overflowY,
              ancestorClips,
              directText,
              coverageOpaque: ['iframe','canvas','video','object','embed'].includes(tag)
                || !!element.shadowRoot
                || tag.includes('-'),
              widthOverflow,
              heightOverflow,
              blockedByIndex,
              blockedSamples,
              sampleCount
            };
          });
          let crossOriginFrames = 0;
          for (const frame of document.querySelectorAll('iframe')) {
            try {
              if (!frame.contentDocument) crossOriginFrames++;
              else void frame.contentDocument.documentElement;
            }
            catch { crossOriginFrames++; }
          }
          return {
            viewport: {
              width: window.innerWidth,
              height: window.innerHeight,
              devicePixelRatio: window.devicePixelRatio || 1,
              visualScale: window.visualViewport?.scale || 1
            },
            totalElementCount: allElements.length,
            crossOriginFrames,
            nodes
          };
        })()
        """;

    private async Task EnrichLayoutCaptureWithBlazorAsync(
        LayoutCaptureSnapshot capture,
        LayoutInspectionRequest request,
        Dictionary<int, Task<string>> pendingCaptures,
        HashSet<int> unavailableCaptures,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        if (!request.Scope.IncludeBlazorElements)
            return;

        var blazorHosts = capture.Nodes
            .Where(node => node.Element.Type.Contains(
                "BlazorWebView",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (blazorHosts.Count == 0)
            return;

        var webViews = GetCdpWebViewsSnapshot();
        if (webViews.Length == 0)
        {
            MarkBlazorHostsUnavailable(
                capture,
                blazorHosts,
                "No Blazor CDP bridge is registered.");
            return;
        }

        var readyWebViews = webViews
            .Where(webView => webView.IsReady)
            .ToArray();
        if (readyWebViews.Length == 0)
        {
            MarkBlazorHostsUnavailable(
                capture,
                blazorHosts,
                "The registered Blazor CDP bridge is not ready.");
            return;
        }
        if (readyWebViews.Length < webViews.Length)
        {
            var unavailableHostFound = false;
            foreach (var unreadyWebView in webViews.Where(
                webView => !webView.IsReady))
            {
                unavailableHostFound |= MarkBlazorHostOpaque(
                    capture,
                    unreadyWebView);
            }
            if (unavailableHostFound)
            {
                capture.MarkIncomplete(
                    "One or more visible Blazor WebViews have a registered CDP bridge that is not ready.");
            }
        }

        foreach (var webView in readyWebViews)
        {
            if (unavailableCaptures.Contains(webView.Index))
            {
                capture.MarkIncomplete(
                    $"Blazor WebView {webView.Index} layout capture was unavailable after an earlier timeout in this scan.");
                MarkBlazorHostOpaque(capture, webView);
                continue;
            }

            try
            {
                var gridSize = GetOcclusionGridSize(request.Occlusion.MaxSamplesPerElement);
                var expression = BlazorLayoutExpression.Replace(
                    "__GRID_SIZE__",
                    gridSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                    .Replace(
                        "__INCLUDE_RAW_TEXT__",
                        request.Privacy.Text.Equals("raw", StringComparison.OrdinalIgnoreCase)
                            ? "true"
                            : "false",
                        StringComparison.Ordinal)
                    .Replace(
                        "__COLLECT_OCCLUSION__",
                        request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase)
                            ? "false"
                            : "true",
                        StringComparison.Ordinal)
                    .Replace(
                        "__COLLECT_ALL_OCCLUSION__",
                        request.Occlusion.Mode.Equals("all", StringComparison.OrdinalIgnoreCase)
                            ? "true"
                            : "false",
                        StringComparison.Ordinal);
                var command = JsonSerializer.Serialize(new
                {
                    id = 73001 + webView.Index,
                    method = "Runtime.evaluate",
                    @params = new
                    {
                        expression = $"JSON.stringify({expression})",
                        returnByValue = true,
                        awaitPromise = true
                    }
                });
                if (!pendingCaptures.TryGetValue(webView.Index, out var commandTask))
                {
                    var probeGate = _blazorLayoutProbeGates.GetOrAdd(
                        webView.Index,
                        static _ => new SemaphoreSlim(1, 1));
                    if (!await probeGate.WaitAsync(0, cancellationToken))
                    {
                        capture.MarkIncomplete(
                            $"A previous Blazor WebView {webView.Index} layout probe is still running.");
                        MarkBlazorHostOpaque(capture, webView);
                        continue;
                    }
                    try
                    {
                        commandTask = webView.CommandHandler(command);
                    }
                    catch
                    {
                        probeGate.Release();
                        throw;
                    }
                    _ = commandTask.ContinueWith(
                        task =>
                        {
                            _ = task.Exception;
                            probeGate.Release();
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    pendingCaptures[webView.Index] = commandTask;
                }
                var winner = await Task.WhenAny(
                    commandTask,
                    Task.Delay(
                        GetRemainingProbeTimeoutMs(deadline),
                        cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (winner != commandTask)
                {
                    capture.MarkIncomplete(
                        $"Blazor WebView {webView.Index} layout capture did not finish before the current scan deadline.");
                    pendingCaptures.Remove(webView.Index);
                    unavailableCaptures.Add(webView.Index);
                    MarkBlazorHostOpaque(capture, webView);
                    continue;
                }
                pendingCaptures.Remove(webView.Index);
                var response = await commandTask;
                using var document = JsonDocument.Parse(response);
                if (!TryGetLayoutCdpValue(document.RootElement, out var value))
                {
                    capture.MarkIncomplete($"Blazor WebView {webView.Index} did not return layout data.");
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String)
                {
                    var serializedLayout = value.GetString();
                    if (string.IsNullOrWhiteSpace(serializedLayout))
                    {
                        capture.MarkIncomplete($"Blazor WebView {webView.Index} returned empty layout data.");
                        continue;
                    }
                    using var layoutDocument = JsonDocument.Parse(serializedLayout);
                    AppendBlazorLayoutNodes(
                        capture,
                        webView,
                        layoutDocument.RootElement,
                        request);
                }
                else
                {
                    AppendBlazorLayoutNodes(capture, webView, value, request);
                }
            }
            catch (Exception ex)
            {
                capture.MarkIncomplete($"Blazor WebView {webView.Index} layout capture failed: {ex.GetType().Name}");
                MarkBlazorHostOpaque(capture, webView);
            }
        }
    }

    private static void MarkBlazorHostsUnavailable(
        LayoutCaptureSnapshot capture,
        IReadOnlyList<LayoutNodeSnapshot> hosts,
        string reason)
    {
        foreach (var host in hosts)
            host.IsCoverageOpaque = true;
        capture.MarkIncomplete(reason);
    }

    private static bool MarkBlazorHostOpaque(
        LayoutCaptureSnapshot capture,
        CdpWebViewInfo webView)
    {
        var host = capture.Nodes.FirstOrDefault(node =>
            webView.ElementId is not null
            && node.Element.Id.Equals(
                webView.ElementId,
                StringComparison.OrdinalIgnoreCase));
        host ??= capture.Nodes.FirstOrDefault(node =>
            webView.AutomationId is not null
            && (node.Element.Id.Equals(
                    webView.AutomationId,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    node.Element.AutomationId,
                    webView.AutomationId,
                    StringComparison.OrdinalIgnoreCase)));
        if (host is null)
            return false;
        host.IsCoverageOpaque = true;
        return true;
    }

    private static bool TryGetLayoutCdpValue(JsonElement root, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty("result", out var result)
            && result.TryGetProperty("result", out var innerResult)
            && innerResult.TryGetProperty("value", out value);
    }

    internal static void AppendBlazorLayoutNodes(
        LayoutCaptureSnapshot capture,
        CdpWebViewInfo webView,
        JsonElement value,
        LayoutInspectionRequest request)
    {
        if (!value.TryGetProperty("viewport", out var viewport)
            || !value.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            capture.MarkIncomplete($"Blazor WebView {webView.Index} returned an invalid layout snapshot.");
            return;
        }

        var host = capture.Nodes.FirstOrDefault(node =>
            webView.ElementId is not null
                && node.Element.Id.Equals(
                    webView.ElementId,
                    StringComparison.OrdinalIgnoreCase));
        host ??= capture.Nodes.FirstOrDefault(node =>
            webView.AutomationId is not null
            && (node.Element.Id.Equals(
                    webView.AutomationId,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    node.Element.AutomationId,
                    webView.AutomationId,
                    StringComparison.OrdinalIgnoreCase)));
        if (host is null)
        {
            capture.MarkIncomplete($"Blazor WebView {webView.Index} could not be mapped to a native DevFlow element.");
            return;
        }
        host.IsCoverageOpaque = false;

        var viewportWidth = ReadDouble(viewport, "width");
        var viewportHeight = ReadDouble(viewport, "height");
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            capture.MarkIncomplete($"Blazor WebView {webView.Index} reported an empty CSS viewport.");
            return;
        }

        var scaleX = host.FullRegion.Bounds.Width / viewportWidth;
        var scaleY = host.FullRegion.Bounds.Height / viewportHeight;
        var capturedElementCount = nodes.GetArrayLength();
        var syntheticByIndex = new Dictionary<int, LayoutNodeSnapshot>();
        var pendingOccluders = new Dictionary<LayoutNodeSnapshot, int>();
        var treeOrder = capture.Nodes.Count == 0 ? 0 : capture.Nodes.Max(node => node.TreeOrder) + 1;

        foreach (var nodeJson in nodes.EnumerateArray())
        {
            if (!nodeJson.TryGetProperty("rect", out var rect))
                continue;
            var visible = ReadBool(nodeJson, "visible");
            var width = ReadDouble(rect, "width");
            var height = ReadDouble(rect, "height");
            var widthOverflow = ReadBool(nodeJson, "widthOverflow");
            var heightOverflow = ReadBool(nodeJson, "heightOverflow");
            if (!visible)
                continue;
            if ((width <= 0 || height <= 0)
                && !widthOverflow
                && !heightOverflow)
                continue;

            var index = ReadInt(nodeJson, "index");
            var parentIndex = ReadInt(nodeJson, "parentIndex");
            var id = $"web-{webView.Index}-{index}";
            var parentId = syntheticByIndex.TryGetValue(parentIndex, out var parent)
                ? parent.Element.Id
                : host.Element.Id;
            var x = host.FullRegion.Bounds.X + ReadDouble(rect, "x") * scaleX;
            var y = host.FullRegion.Bounds.Y + ReadDouble(rect, "y") * scaleY;
            var fullRegion = LayoutRegionMath.FromRect(x, y, width * scaleX, height * scaleY);
            var visibleRegion = LayoutRegionMath.Intersect(fullRegion, host.VisibleRegion);
            var clipChain = new List<LayoutClipContribution>();
            if (visibleRegion.Area + 0.000001 < fullRegion.Area)
            {
                clipChain.Add(new LayoutClipContribution
                {
                    ClipperElementId = host.Element.Id,
                    Kind = "ancestor-layout-clip",
                    Precision = host.VisibleRegion.Precision,
                    AreaBefore = fullRegion.Area,
                    AreaAfter = visibleRegion.Area,
                    LostAreaRatio = fullRegion.Area > 0
                        ? 1 - visibleRegion.Area / fullRegion.Area
                        : 0,
                    Region = host.VisibleRegion
                });
            }
            if (nodeJson.TryGetProperty("ancestorClips", out var ancestorClips)
                && ancestorClips.ValueKind == JsonValueKind.Array)
            {
                foreach (var ancestorClip in ancestorClips.EnumerateArray())
                {
                    if (!ancestorClip.TryGetProperty("rect", out var clipRect))
                        continue;
                    var clipRegion = LayoutRegionMath.FromRect(
                        host.FullRegion.Bounds.X
                            + ReadDouble(clipRect, "x") * scaleX,
                        host.FullRegion.Bounds.Y
                            + ReadDouble(clipRect, "y") * scaleY,
                        ReadDouble(clipRect, "width") * scaleX,
                        ReadDouble(clipRect, "height") * scaleY,
                        "exactRect");
                    var areaBefore = visibleRegion.Area;
                    var clipped = LayoutRegionMath.Intersect(
                        visibleRegion,
                        clipRegion);
                    if (clipped.Area + 0.000001 >= areaBefore)
                        continue;
                    var clipperIndex = ReadInt(
                        ancestorClip,
                        "clipperIndex");
                    clipChain.Add(new LayoutClipContribution
                    {
                        ClipperElementId = syntheticByIndex.TryGetValue(
                            clipperIndex,
                            out var clipper)
                                ? clipper.Element.Id
                                : host.Element.Id,
                        Kind = ReadString(ancestorClip, "kind")
                            ?? "ancestor-layout-clip",
                        Precision = clipRegion.Precision,
                        AreaBefore = areaBefore,
                        AreaAfter = clipped.Area,
                        LostAreaRatio = areaBefore > 0
                            ? 1 - clipped.Area / areaBefore
                            : 0,
                        Region = clipRegion
                    });
                    visibleRegion = clipped;
                }
            }
            var scrollWidth = ReadDouble(nodeJson, "scrollWidth") * scaleX;
            var scrollHeight = ReadDouble(nodeJson, "scrollHeight") * scaleY;
            var clientWidth = ReadDouble(nodeJson, "clientWidth") * scaleX;
            var clientHeight = ReadDouble(nodeJson, "clientHeight") * scaleY;
            var overflowX = ReadString(nodeJson, "overflowX");
            var overflowY = ReadString(nodeJson, "overflowY");
            LayoutTextEvidence? directTextEvidence = null;
            if (nodeJson.TryGetProperty("directText", out var directText)
                && directText.ValueKind == JsonValueKind.Object)
            {
                var directTextLength = ReadInt(directText, "length", 0);
                var renderedLineCount = ReadInt(
                    directText,
                    "renderedLineCount",
                    -1);
                directTextEvidence = new LayoutTextEvidence
                {
                    Kind = ReadString(directText, "kind"),
                    IsTruncated = ReadBool(directText, "truncated"),
                    TextLength = request.Privacy.Text is "length" or "raw"
                        ? directTextLength
                        : null,
                    Text = request.Privacy.Text == "raw"
                        ? ReadString(directText, "text")
                        : null,
                    RenderedLineCount = renderedLineCount >= 0
                        ? renderedLineCount
                        : null,
                    ContentWidth = ReadDouble(directText, "contentWidth") * scaleX,
                    ContentHeight = ReadDouble(directText, "contentHeight") * scaleY,
                    AvailableWidth = ReadDouble(directText, "availableWidth") * scaleX,
                    AvailableHeight = ReadDouble(directText, "availableHeight") * scaleY,
                    MeasurementSource = "browser-range-direct-text"
                };
            }
            var element = new ElementInfo
            {
                Id = id,
                ParentId = parentId,
                Type = ReadString(nodeJson, "tag") ?? "element",
                FullType = "Blazor.DOM.Element",
                Framework = "blazor",
                AutomationId = ReadString(nodeJson, "id"),
                Role = ReadString(nodeJson, "role"),
                IsVisible = visible,
                IsEnabled = true,
                Opacity = ReadDouble(nodeJson, "opacity", 1),
                Bounds = new BoundsInfo
                {
                    X = x,
                    Y = y,
                    Width = width * scaleX,
                    Height = height * scaleY
                },
                WindowBounds = new BoundsInfo
                {
                    X = x,
                    Y = y,
                    Width = width * scaleX,
                    Height = height * scaleY
                },
                Traits = ReadBool(nodeJson, "interactive") ? ["interactive"] : null
            };
            var node = new LayoutNodeSnapshot
            {
                Element = element,
                LayoutRegion = fullRegion,
                FullRegion = fullRegion,
                VisibleRegion = visibleRegion,
                ClipChain = clipChain,
                ContentRegion = LayoutRegionMath.FromRect(
                    x,
                    y,
                    Math.Max(clientWidth, scrollWidth),
                    Math.Max(clientHeight, scrollHeight),
                    "exactRect"),
                WindowId = host.WindowId,
                WindowScale = host.WindowScale,
                TreeOrder = treeOrder++,
                ZIndex = ReadInt(nodeJson, "zIndex"),
                IsInteractive = ReadBool(nodeJson, "interactive"),
                IsRendered = visible,
                IsHitTestVisible = visible,
                IsOpaque = ReadBool(nodeJson, "opaque"),
                IsCoverageOpaque = ReadBool(
                    nodeJson,
                    "coverageOpaque"),
                IsScrollable = overflowX is "auto" or "scroll" || overflowY is "auto" or "scroll",
                IsInsideScrollableViewport = true,
                Text = directTextEvidence
            };

            var blockedByIndex = ReadInt(nodeJson, "blockedByIndex");
            var blockedSamples = ReadInt(nodeJson, "blockedSamples", 0);
            var sampleCount = ReadInt(nodeJson, "sampleCount", 0);
            if (blockedSamples > 0 && sampleCount > 0)
            {
                var (lower, upper) = EstimateBlockedInterval(
                    blockedSamples,
                    sampleCount,
                    request.Occlusion.CoverageError);
                node.InteractionBlockedLowerBound = lower;
                node.InteractionBlockedUpperBound = upper;
                node.InteractionSampleCount = sampleCount;
                if (blockedByIndex >= 0)
                {
                    pendingOccluders[node] = blockedByIndex;
                }
                else
                {
                    node.InteractionOccluderId = "blazor-unmapped";
                    node.Limitations.Add("One or more interaction blockers were outside the captured DOM node limit.");
                }
            }
            syntheticByIndex[index] = node;
            capture.Nodes.Add(node);
        }

        foreach (var pending in pendingOccluders)
        {
            if (!syntheticByIndex.TryGetValue(pending.Value, out var occluder))
                continue;
            pending.Key.InteractionOccluderId = occluder.Element.Id;
        }

        var crossOriginFrames = ReadInt(value, "crossOriginFrames");
        if (crossOriginFrames > 0)
            capture.MarkIncomplete($"{crossOriginFrames} cross-origin iframe(s) could not be inspected in Blazor WebView {webView.Index}.");
        var totalElementCount = ReadInt(value, "totalElementCount", capturedElementCount);
        if (totalElementCount > capturedElementCount)
        {
            capture.MarkIncomplete(
                $"Blazor WebView {webView.Index} contains {totalElementCount} DOM elements; layout capture was limited to the first 500.");
        }

        var devicePixelRatio = ReadDouble(viewport, "devicePixelRatio", 1);
        var visualScale = ReadDouble(viewport, "visualScale", 1);
        if (Math.Abs(scaleX - scaleY) > 0.01)
            capture.Limitations.Add($"Blazor WebView {webView.Index} uses non-uniform native-to-CSS scaling.");
        if (devicePixelRatio <= 0 || visualScale <= 0)
            capture.Limitations.Add($"Blazor WebView {webView.Index} reported invalid browser scaling metadata.");
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double ReadDouble(JsonElement element, string name, double fallback = 0)
        => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : fallback;

    private static int ReadInt(JsonElement element, string name, int fallback = -1)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : fallback;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

    private static int GetOcclusionGridSize(int maxSamples)
    {
        var gridSize = Math.Max(1, (int)Math.Floor(Math.Sqrt(Math.Max(1, maxSamples))));
        if (gridSize > 1 && gridSize % 2 == 0)
            gridSize--;
        return gridSize;
    }

    private static (double Lower, double Upper) EstimateBlockedInterval(
        int blocked,
        int total,
        double errorProbability)
    {
        if (total <= 0)
            return (0, 1);
        var ratio = (double)blocked / total;
        var probability = Math.Clamp(errorProbability, 0.001, 0.5);
        var margin = Math.Sqrt(Math.Log(2 / probability) / (2 * total));
        return (Math.Max(0, ratio - margin), Math.Min(1, ratio + margin));
    }
}
