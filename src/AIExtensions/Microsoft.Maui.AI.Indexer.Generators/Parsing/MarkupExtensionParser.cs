using System.Text.RegularExpressions;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Parsing;

/// <summary>Parses XAML markup extensions like {Binding Path=X, Mode=Y}.</summary>
internal static class MarkupExtensionParser
{
    private static readonly Regex BindingRegex = new(
        @"^\{(?:Binding)\s*(.*)\}$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TemplateBindingRegex = new(
        @"^\{TemplateBinding\s+(.*)\}$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex StaticResourceRegex = new(
        @"^\{StaticResource\s+(\w+)\}$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Try to parse a value string as a binding. Returns null if it's not a binding.
    /// </summary>
    public static BindingInfo? TryParseBinding(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value!.Trim();

        var match = BindingRegex.Match(value);
        if (match.Success)
        {
            return ParseBindingContent(match.Groups[1].Value.Trim());
        }

        match = TemplateBindingRegex.Match(value);
        if (match.Success)
        {
            return new BindingInfo { Path = match.Groups[1].Value.Trim() };
        }

        return null;
    }

    /// <summary>
    /// Checks if a value is a markup extension (starts with {).
    /// </summary>
    public static bool IsMarkupExtension(string? value)
    {
        return value != null && value.TrimStart().StartsWith("{") && !value.TrimStart().StartsWith("{}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks if a value uses a StaticResource.
    /// </summary>
    public static bool IsStaticResource(string? value)
    {
        return value != null && StaticResourceRegex.IsMatch(value.Trim());
    }

    /// <summary>
    /// Extracts the binding path from a value, or returns the literal value.
    /// </summary>
    public static string GetDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var binding = TryParseBinding(value);
        if (binding != null)
        {
            return binding.ToDisplayString();
        }

        // For static resources and other extensions, show raw
        if (IsMarkupExtension(value))
            return value!.Trim();

        return value!.Trim();
    }

    private static BindingInfo ParseBindingContent(string content)
    {
        var info = new BindingInfo();

        if (string.IsNullOrWhiteSpace(content))
        {
            info.Path = ".";
            return info;
        }

        // Simple path: {Binding UserName} or {Binding Path=UserName}
        var parts = SplitBindingParts(content);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
            {
                info.Path = trimmed.Substring(5).Trim();
            }
            else if (trimmed.StartsWith("Mode=", StringComparison.OrdinalIgnoreCase))
            {
                info.Mode = trimmed.Substring(5).Trim();
            }
            else if (trimmed.StartsWith("Converter=", StringComparison.OrdinalIgnoreCase))
            {
                info.Converter = trimmed.Substring(10).Trim();
                // Strip {StaticResource ...} wrapper
                if (info.Converter.StartsWith("{StaticResource ", StringComparison.Ordinal))
                    info.Converter = info.Converter.Substring(16).TrimEnd('}').Trim();
            }
            else if (trimmed.StartsWith("StringFormat=", StringComparison.OrdinalIgnoreCase))
            {
                info.StringFormat = trimmed.Substring(13).Trim().Trim('\'');
            }
            else if (trimmed.StartsWith("Source=", StringComparison.OrdinalIgnoreCase))
            {
                // Skip RelativeSource etc.
            }
            else if (info.Path == null && !trimmed.Contains("="))
            {
                // First unnamed parameter is the Path
                info.Path = trimmed;
            }
        }

        info.Path ??= ".";
        return info;
    }

    private static List<string> SplitBindingParts(string content)
    {
        var parts = new List<string>();
        var depth = 0;
        var current = new System.Text.StringBuilder();

        foreach (var ch in content)
        {
            if (ch == '{') depth++;
            else if (ch == '}') depth--;
            else if (ch == ',' && depth == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }
}
