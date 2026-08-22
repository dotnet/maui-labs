using System.Text;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record ComponentLayoutValidationError(
    string Code,
    string Path,
    string Message,
    bool IsWarning = false);

public sealed record ComponentLayoutValidationResult(
    IReadOnlyList<ComponentLayoutValidationError> Issues)
{
    public bool IsValid => Issues.All(issue => issue.IsWarning);

    public IReadOnlyList<ComponentLayoutValidationError> Errors
        => Issues.Where(issue => !issue.IsWarning).ToArray();

    public IReadOnlyList<ComponentLayoutValidationError> Warnings
        => Issues.Where(issue => issue.IsWarning).ToArray();
}

public static class ComponentLayoutValidationErrorFormatter
{
    public static string Format(ComponentLayoutValidationResult result)
    {
        var issues = result.Issues
            .OrderBy(issue => issue.Path, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"error\": \"invalid_component_layout\",");
        builder.AppendLine("  \"issues\": [");
        for (var index = 0; index < issues.Length; index++)
        {
            var issue = issues[index];
            builder.AppendLine("    {");
            builder.AppendLine($"      \"code\": \"{Escape(issue.Code)}\",");
            builder.AppendLine($"      \"path\": \"{Escape(issue.Path)}\",");
            builder.AppendLine($"      \"message\": \"{Escape(issue.Message)}\",");
            builder.AppendLine($"      \"severity\": \"{(issue.IsWarning ? "warning" : "error")}\"");
            builder.Append("    }");
            builder.AppendLine(index == issues.Length - 1 ? string.Empty : ",");
        }
        builder.AppendLine("  ]");
        builder.Append('}');
        return builder.ToString();
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
