using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal static class VisualTreeRevision
{
    public static string ComputeTree(IEnumerable<ElementInfo> roots)
        => ComputeFlat(Flatten(roots));

    public static string ComputeFlat(IEnumerable<ElementInfo> elements)
    {
        var builder = new StringBuilder();
        foreach (var element in elements)
        {
            if (element.Framework.Equals("blazor", StringComparison.OrdinalIgnoreCase))
                continue;
            var bounds = element.WindowBounds ?? element.Bounds;
            if (bounds is null)
                continue;
            builder.Append(element.Id).Append('|')
                .Append(element.ParentId).Append('|')
                .Append(element.Type).Append('|')
                .Append(element.Framework).Append('|')
                .Append(element.AutomationId).Append('|')
                .Append(element.Role).Append('|')
                .Append(element.IsVisible).Append('|')
                .Append(element.IsEnabled).Append('|')
                .Append(element.Opacity.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(string.Join(",", element.Traits ?? [])).Append('|')
                .Append(string.Join(",", element.Gestures ?? [])).Append('|')
                .Append(bounds.X.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Y.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Width.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(bounds.Height.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            if (root.Children is not null)
            {
                foreach (var child in Flatten(root.Children))
                    yield return child;
            }
        }
    }
}
