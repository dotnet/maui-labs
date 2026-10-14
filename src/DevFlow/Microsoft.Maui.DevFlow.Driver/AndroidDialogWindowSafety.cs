using System.Globalization;
using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class AndroidDialogWindowSafety
{
    internal static string? GetInstanceId(string windowState, string? hierarchyPackage)
    {
        var windows = ParseWindows(windowState);
        var focusedToken = GetFocusedToken(windowState);
        var focused = windows.FirstOrDefault(window => window.Token == focusedToken);
        return focused != null && focused.Package == hierarchyPackage ? focused.Token : null;
    }

    internal static bool IsUnobscuredDialog(
        string windowState, AlertInfo dialog, string expectedPackage, int expectedUid)
    {
        if (string.IsNullOrWhiteSpace(dialog.InstanceId) || dialog.Buttons.Count == 0
            || dialog.SourceProcessName != expectedPackage
            || GetFocusedToken(windowState) != dialog.InstanceId)
        {
            return false;
        }

        var windows = ParseWindows(windowState);
        var targetIndex = windows.FindIndex(window => window.Token == dialog.InstanceId);
        if (targetIndex < 0)
            return false;
        var target = windows[targetIndex];
        if (target.Package != expectedPackage || target.OwnerUid != expectedUid
            || target.DisplayId != 0 || target.IsOnScreen != true || target.Frame == null)
        {
            return false;
        }

        foreach (var button in dialog.Buttons)
        {
            if (!double.IsFinite(button.X) || !double.IsFinite(button.Y)
                || !double.IsFinite(button.Width) || !double.IsFinite(button.Height)
                || button.Width <= 0 || button.Height <= 0
                || !target.Frame.Contains(button))
            {
                return false;
            }

            // WindowManager dumps windows front-to-back, including non-focusable overlays.
            foreach (var above in windows.Take(targetIndex))
            {
                if (above.IsOnScreen == false)
                    continue;
                if (above.IsOnScreen == null || above.DisplayId == null)
                    return false;
                if (above.DisplayId != target.DisplayId)
                    continue;
                if (above.Frame == null || above.Frame.Intersects(button))
                    return false;
            }
        }

        return true;
    }

    internal static int? GetPackageUid(string output, string package)
    {
        var matches = Regex.Matches(output, @"(?m)^package:(?<package>\S+)\s+uid:(?<uid>\d+)\s*$")
            .Where(match => match.Groups["package"].Value == package)
            .ToArray();
        return matches.Length == 1
            && int.TryParse(matches[0].Groups["uid"].Value, CultureInfo.InvariantCulture, out var uid)
            ? uid
            : null;
    }

    private static string? GetFocusedToken(string state)
    {
        var matches = Regex.Matches(state, @"mCurrentFocus=Window\{(?<token>[a-fA-F0-9]+)\s");
        return matches.Count == 1 ? matches[0].Groups["token"].Value : null;
    }

    private static List<WindowSnapshot> ParseWindows(string state)
    {
        var headers = Regex.Matches(state, @"(?m)^\s*Window #(?<index>\d+) Window\{(?<token>[a-fA-F0-9]+)\s[^\r\n]*\}:\s*$");
        var windows = new List<WindowSnapshot>();
        for (var index = 0; index < headers.Count; index++)
        {
            var header = headers[index];
            if (!int.TryParse(header.Groups["index"].Value, out var windowIndex) || windowIndex != index)
                return [];
            if (windows.Any(window => window.Token == header.Groups["token"].Value))
                return [];
            var start = header.Index + header.Length;
            var end = index + 1 < headers.Count ? headers[index + 1].Index : state.Length;
            var body = state[start..end];
            var frame = Regex.Match(body, @"\b(?:frame|mFrame)=\[(?<left>-?\d+),(?<top>-?\d+)\]\[(?<right>-?\d+),(?<bottom>-?\d+)\]");
            var visible = Regex.Match(body, @"\bisOnScreen=(true|false)\b");
            windows.Add(new(
                header.Groups["token"].Value,
                Regex.Match(body, @"\bpackage=(?<package>\S+)").Groups["package"].Value,
                ReadInt(body, "mOwnerUid"),
                ReadInt(body, "mDisplayId"),
                visible.Success ? visible.Groups[1].Value == "true" : null,
                frame.Success ? new WindowFrame(
                    ReadCoordinate(frame, "left"), ReadCoordinate(frame, "top"),
                    ReadCoordinate(frame, "right"), ReadCoordinate(frame, "bottom")) : null));
        }

        return windows;
    }

    private static int? ReadInt(string body, string field)
    {
        var match = Regex.Match(body, $@"\b{field}=(\d+)\b");
        return match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var value)
            ? value : null;
    }

    private static double ReadCoordinate(Match frame, string field)
        => double.Parse(frame.Groups[field].Value, CultureInfo.InvariantCulture);

    private sealed record WindowSnapshot(
        string Token, string Package, int? OwnerUid, int? DisplayId, bool? IsOnScreen, WindowFrame? Frame);

    private sealed record WindowFrame(double Left, double Top, double Right, double Bottom)
    {
        internal bool Contains(AlertButton button)
            => button.X >= Left && button.Y >= Top
                && button.X + button.Width <= Right && button.Y + button.Height <= Bottom;

        internal bool Intersects(AlertButton button)
            => Left < button.X + button.Width && Right > button.X
                && Top < button.Y + button.Height && Bottom > button.Y;
    }
}
