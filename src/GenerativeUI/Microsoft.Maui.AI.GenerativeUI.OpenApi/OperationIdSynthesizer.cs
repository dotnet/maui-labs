using System.Text;

namespace Microsoft.Maui.AI.GenerativeUI.OpenApi;

/// <summary>
/// Produces a stable, legible <c>operationId</c> for an operation that lacks one. The synthesized id
/// is <c>{method}_{path}</c> with path parameters folded in, e.g. <c>GET /products/{sku}</c> becomes
/// <c>get_products_by_sku</c>.
/// </summary>
public static class OperationIdSynthesizer
{
    /// <summary>
    /// Returns <paramref name="authoredId"/> when it is non-empty; otherwise synthesizes an id from
    /// the HTTP method and route template.
    /// </summary>
    public static string Resolve(string? authoredId, string method, string path)
        => string.IsNullOrWhiteSpace(authoredId) ? Synthesize(method, path) : authoredId!;

    /// <summary>Synthesizes <c>{method}_{path}</c>, folding <c>{param}</c> segments in as <c>by_param</c>.</summary>
    public static string Synthesize(string method, string path)
    {
        var sb = new StringBuilder();
        sb.Append(method.ToLowerInvariant());

        foreach (var raw in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string segment = raw;
            bool isParam = segment.Length >= 2 && segment[0] == '{' && segment[^1] == '}';
            if (isParam)
                segment = "by_" + segment[1..^1];

            sb.Append('_');
            AppendSnake(sb, segment);
        }

        return sb.ToString();
    }

    private static void AppendSnake(StringBuilder sb, string segment)
    {
        bool prevUnderscore = sb.Length > 0 && sb[^1] == '_';
        foreach (var c in segment)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                prevUnderscore = false;
            }
            else if (!prevUnderscore)
            {
                sb.Append('_');
                prevUnderscore = true;
            }
        }
    }
}
