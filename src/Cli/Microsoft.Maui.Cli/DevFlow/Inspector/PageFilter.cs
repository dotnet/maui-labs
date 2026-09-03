using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// MAUI Shell keeps EVERY tab/flyout page mounted and marked <c>IsVisible=true</c>, so a single
/// visual-tree pull contains all pages at once — the inactive ones with their content collapsed to
/// the origin (0,0) but still page-sized. Rendering that verbatim makes the inspector's visual tree
/// and overlay "stick" on a stale page after navigation and stacks elements from pages that aren't
/// on screen (both SubscriptionsPage and InsightsPage overlapping, for example).
///
/// This computes the set of element ids to DROP — inactive pages plus their exclusive Shell wrapper
/// chain — so the inspector reflects only what's displayed. Active-page detection, most reliable
/// first: (1) the selected Tab/FlyoutItem whose id encodes the target page type; (2) otherwise the
/// uniquely most-populated page (real content laid out in the viewport body, not collapsed to the
/// origin). If it is genuinely ambiguous, nothing is dropped — fail-safe, never hide the page the
/// user is actually looking at.
///
/// Matches the Canvas host's <c>inactivePageIds</c> filtering. Set the
/// <c>DEVFLOW_NO_PAGE_FILTER</c> environment variable to disable (renders every page verbatim).
/// </summary>
internal static class PageFilter
{
    public static HashSet<string> InactivePageIds(List<ElementInfo>? roots)
    {
        var drop = new HashSet<string>(StringComparer.Ordinal);
        if (roots is null || roots.Count == 0) return drop;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVFLOW_NO_PAGE_FILTER"))) return drop;

        var flat = new List<ElementInfo>();
        var parent = new Dictionary<string, string?>(StringComparer.Ordinal);
        var byId = new Dictionary<string, ElementInfo>(StringComparer.Ordinal);
        var kids = new Dictionary<string, List<ElementInfo>>(StringComparer.Ordinal);

        void Visit(ElementInfo? el, string? pid)
        {
            if (el?.Id is not { Length: > 0 } id) return;
            flat.Add(el);
            parent[id] = pid;
            byId[id] = el;
            if (pid is not null)
            {
                if (!kids.TryGetValue(pid, out var list)) kids[pid] = list = new();
                list.Add(el);
            }
            if (el.Children is not null)
                foreach (var c in el.Children) Visit(c, id);
        }
        foreach (var r in roots) Visit(r, null);

        var pages = flat.Where(e => (e.Type ?? "").EndsWith("Page", StringComparison.Ordinal) && e.IsVisible).ToList();
        if (pages.Count <= 1) return drop;

        static BoundsInfo? Abs(ElementInfo e) => e.WindowBounds ?? e.Bounds;

        // Viewport body band: below the nav bar, above the tab bar (fall back to window fractions).
        double navBottom = 0, tabTop = double.PositiveInfinity, maxY = 0;
        foreach (var e in flat)
        {
            var b = Abs(e);
            if (b is null) continue;
            var t = e.Type ?? "";
            maxY = Math.Max(maxY, b.Y + b.Height);
            if ((t.StartsWith("NavBar", StringComparison.Ordinal) || t.StartsWith("Toolbar", StringComparison.Ordinal)) && b.Height > 0)
                navBottom = Math.Max(navBottom, b.Y + b.Height);
            if (t == "Tab" && b.Height > 0) tabTop = Math.Min(tabTop, b.Y);
        }
        if (double.IsPositiveInfinity(tabTop)) tabTop = maxY > 0 ? maxY * 0.9 : double.PositiveInfinity;
        if (navBottom <= 0) navBottom = maxY > 0 ? maxY * 0.1 : 0;

        // score(page) = visible, positive-size descendants actually laid out in the viewport body
        // (not collapsed to the origin). Collapsed inactive pages score ~0.
        int Score(ElementInfo page)
        {
            int n = 0;
            var stack = new Stack<ElementInfo>();
            if (kids.TryGetValue(page.Id, out var pk)) foreach (var c in pk) stack.Push(c);
            while (stack.Count > 0)
            {
                var el = stack.Pop();
                if (kids.TryGetValue(el.Id, out var ek)) foreach (var c in ek) stack.Push(c);
                if (!el.IsVisible) continue;
                var b = Abs(el);
                if (b is null || !(b.Width > 1) || !(b.Height > 1)) continue;
                bool atOrigin = Math.Abs(b.X) < 2 && Math.Abs(b.Y) < 2;
                double cy = b.Y + b.Height / 2;
                if (!atOrigin && cy > navBottom && cy < tabTop) n++;
            }
            return n;
        }
        var scoreById = pages.ToDictionary(p => p.Id, Score, StringComparer.Ordinal);

        // Selected nav items (Tab / FlyoutItem) whose id encodes the target page type.
        static string Norm(string? s) => new(((s ?? "").ToLowerInvariant()).Where(char.IsLetterOrDigit).ToArray());
        var selectedNav = flat.Where(e =>
            (e.IsSelected || e.IsFocused)
            && ((e.Type ?? "").IndexOf("Tab", StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Type ?? "").IndexOf("Flyout", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        int TabMatchLen(ElementInfo page)
        {
            var pt = Norm(page.Type);
            if (pt.Length == 0) return 0;
            int best = 0;
            foreach (var nav in selectedNav)
                if (Norm(nav.Id).Contains(pt, StringComparison.Ordinal)) best = Math.Max(best, pt.Length);
            return best;
        }

        // Pick the active page: an authoritative selected Tab first, else a strict geometry winner.
        ElementInfo? active = null;
        bool confident = false;
        var tabbed = pages
            .Select(p => (p, len: TabMatchLen(p), s: scoreById[p.Id]))
            .Where(x => x.len > 0)
            .OrderByDescending(x => x.len).ThenByDescending(x => x.s)
            .ToList();
        if (tabbed.Count > 0 && tabbed[0].s > 0)
        {
            active = tabbed[0].p;
            confident = true;
        }
        else
        {
            var sorted = pages.OrderByDescending(p => scoreById[p.Id]).ToList();
            int top = scoreById[sorted[0].Id];
            int second = sorted.Count > 1 ? scoreById[sorted[1].Id] : 0;
            if (top > 0 && top > second) active = sorted[0]; // require a strict, unambiguous winner
        }
        if (active is null) return drop;

        int activeScore = scoreById[active.Id];
        bool IsDescendantOf(string id, string ancestorId)
        {
            var cur = parent.GetValueOrDefault(id);
            while (cur is not null)
            {
                if (cur == ancestorId) return true;
                cur = parent.GetValueOrDefault(cur);
            }
            return false;
        }

        // Drop every OTHER page plus that page's exclusive Shell wrapper chain (so no empty container
        // box is left behind). When we only GUESSED the active page, keep the safety net: never hide a
        // page more populated than our guess. When a selected Tab named it (confident), drop the others
        // unconditionally — a just-left page can retain more laid-out content until MAUI tears it down.
        foreach (var p in pages)
        {
            if (p.Id == active.Id) continue;
            if (!confident && scoreById[p.Id] > activeScore) continue;
            drop.Add(p.Id);
            var cur = parent.GetValueOrDefault(p.Id);
            while (cur is not null)
            {
                if (!byId.TryGetValue(cur, out var e)) break;
                var t = e.Type ?? "";
                if (t != "ShellContent" && t != "ShellSection") break;
                bool hostsKeptPage = pages.Any(x => !drop.Contains(x.Id) && IsDescendantOf(x.Id, cur));
                if (hostsKeptPage) break;
                drop.Add(cur);
                cur = parent.GetValueOrDefault(cur);
            }
        }
        return drop;
    }
}
