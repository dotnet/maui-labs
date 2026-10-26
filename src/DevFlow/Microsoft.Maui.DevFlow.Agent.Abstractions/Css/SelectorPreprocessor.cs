using System.Text.RegularExpressions;

namespace Microsoft.Maui.DevFlow.Agent.Core.Css;

/// <summary>
/// Transforms MAUI-specific pseudo-classes into synthetic attribute selectors
/// that Fizzler's CSS Level 3 parser can handle.
/// </summary>
public static partial class SelectorPreprocessor
{
    static readonly (string PseudoClass, string Attribute)[] Mappings =
    [
        (":visible", "[__maui-visible]"),
        (":hidden", "[__maui-hidden]"),
        (":enabled", "[__maui-enabled]"),
        (":disabled", "[__maui-disabled]"),
        (":focused", "[__maui-focused]"),
    ];

    // Matches MAUI pseudo-classes: :visible, :hidden, :enabled, :disabled, :focused
    // Our keywords don't appear in any standard CSS pseudo-class name,
    // so a simple match is safe without lookbehind.
    [GeneratedRegex(@":(visible|hidden|enabled|disabled|focused)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MauiPseudoClassPattern();

    /// <summary>
    /// Replaces MAUI-specific pseudo-classes with synthetic attribute selectors.
    /// E.g., "Button:visible" becomes "Button[__maui-visible]"
    /// </summary>
    public static string Preprocess(string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return selector;

        return MauiPseudoClassPattern().Replace(selector, match =>
        {
            var name = match.Groups[1].Value.ToLowerInvariant();
            return $"[__maui-{name}]";
        });
    }
}
