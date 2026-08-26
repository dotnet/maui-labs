using System;
using System.Collections.Generic;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Canonical DevFlow platform identity.
/// </summary>
/// <remarks>
/// <para>
/// DevFlow agents report a platform as a free-form string (for example <c>"iOS"</c>,
/// <c>"MacCatalyst"</c>, <c>"WinUI"</c> or <c>"Tizen"</c>) on
/// <c>GET /api/v1/agent/status</c> and in their broker registration. This type is the single
/// place that maps those agent-reported spellings onto the canonical lowercase identifiers used
/// by the protocol schema, the CLI and tooling.
/// </para>
/// <para>
/// The mapping is intentionally forgiving and forward compatible: an identifier that DevFlow
/// does not recognize is lowercased and returned unchanged rather than rejected or coerced, so a
/// newer or out-of-tree agent can introduce a platform without an older client refusing to talk
/// to it. Nothing here changes the values placed on the wire — agents keep sending the spelling
/// they always sent.
/// </para>
/// </remarks>
public static class DevFlowPlatform
{
    /// <summary>Canonical identifier for Android agents.</summary>
    public const string Android = "android";

    /// <summary>Canonical identifier for iOS agents.</summary>
    public const string iOS = "ios";

    /// <summary>Canonical identifier for Mac Catalyst agents.</summary>
    public const string MacCatalyst = "maccatalyst";

    /// <summary>Canonical identifier for macOS (AppKit) agents.</summary>
    public const string MacOS = "macos";

    /// <summary>Canonical identifier for Windows agents (WinUI and WPF).</summary>
    public const string Windows = "windows";

    /// <summary>Canonical identifier for Linux (GTK) agents.</summary>
    public const string Linux = "linux";

    /// <summary>
    /// Canonical identifier for Tizen agents. Tizen agents live outside this repository — see
    /// <see href="https://github.com/Redth/Maui.Tizen">Maui.Tizen</see> — and reuse the shared
    /// DevFlow agent abstractions, so DevFlow only needs to recognize the identity.
    /// </summary>
    public const string Tizen = "tizen";

    /// <summary>Canonical identifier used when the platform could not be determined.</summary>
    public const string Unknown = "unknown";

    private static readonly string[] s_knownIds =
    [
        Android,
        iOS,
        MacCatalyst,
        Windows,
        Linux,
        MacOS,
        Tizen,
    ];

    // Exact (case-insensitive) spellings agents are known to report, plus the aliases callers
    // pass on the command line. Matched before the substring pass below so that, for example,
    // "macos" is not swallowed by the "mac" token used for Mac Catalyst.
    private static readonly Dictionary<string, string> s_aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["android"] = Android,

        ["ios"] = iOS,
        ["iphone"] = iOS,
        ["ipad"] = iOS,
        ["iossimulator"] = iOS,
        ["ios-simulator"] = iOS,

        ["maccatalyst"] = MacCatalyst,
        ["mac catalyst"] = MacCatalyst,
        ["catalyst"] = MacCatalyst,
        ["mac"] = MacCatalyst,

        ["macos"] = MacOS,
        ["mac os"] = MacOS,
        ["osx"] = MacOS,
        ["appkit"] = MacOS,

        ["windows"] = Windows,
        ["win"] = Windows,
        ["winui"] = Windows,
        ["wpf"] = Windows,

        ["linux"] = Linux,
        ["gtk"] = Linux,
        ["gtk4"] = Linux,

        ["tizen"] = Tizen,
        ["tizen-nui"] = Tizen,
        ["tizennui"] = Tizen,
        ["nui"] = Tizen,

        ["unknown"] = Unknown,
    };

    // Ordered substring probes for agent strings that carry extra decoration, e.g. "Tizen 8.0"
    // or "net10.0-windows10.0.19041.0". Tizen is probed before Linux because Tizen is a Linux
    // distribution and its agents can legitimately describe themselves with both words.
    private static readonly KeyValuePair<string, string>[] s_tokens =
    [
        new("android", Android),
        new("maccatalyst", MacCatalyst),
        new("mac catalyst", MacCatalyst),
        new("tizen", Tizen),
        new("winui", Windows),
        new("windows", Windows),
        new("macos", MacOS),
        new("appkit", MacOS),
        new("gtk", Linux),
        new("linux", Linux),
        new("ios", iOS),
    ];

    private static readonly Dictionary<string, string> s_displayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [Android] = "Android",
        [iOS] = "iOS",
        [MacCatalyst] = "Mac Catalyst",
        [MacOS] = "macOS",
        [Windows] = "Windows",
        [Linux] = "Linux",
        [Tizen] = "Tizen",
        [Unknown] = "Unknown",
    };

    /// <summary>
    /// The canonical platform identifiers DevFlow knows about, in the order they should be listed
    /// to users. <see cref="Unknown"/> is deliberately excluded: it is a fallback, not a platform.
    /// </summary>
    public static IReadOnlyList<string> KnownIds => s_knownIds;

    /// <summary>
    /// Maps an agent-reported platform string onto its canonical identifier.
    /// </summary>
    /// <param name="platform">The platform string reported by an agent, or supplied by a user.</param>
    /// <returns>
    /// The canonical identifier when <paramref name="platform"/> is recognized;
    /// otherwise the trimmed, lowercased input so unknown platforms round-trip intact.
    /// Returns <see cref="Unknown"/> for null, empty or whitespace input.
    /// </returns>
    public static string Normalize(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return Unknown;

        var trimmed = platform!.Trim();

        if (s_aliases.TryGetValue(trimmed, out var alias))
            return alias;

        var lowered = trimmed.ToLowerInvariant();

        foreach (var token in s_tokens)
        {
            if (lowered.IndexOf(token.Key, StringComparison.Ordinal) >= 0)
                return token.Value;
        }

        return lowered;
    }

    /// <summary>
    /// Whether <paramref name="platform"/> resolves to a platform DevFlow ships support for.
    /// Unrecognized identifiers return <see langword="false"/> but remain usable — see
    /// <see cref="Normalize"/>.
    /// </summary>
    public static bool IsKnown(string? platform)
    {
        var normalized = Normalize(platform);

        foreach (var id in s_knownIds)
        {
            if (string.Equals(id, normalized, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable name for display in CLI output and logs.
    /// </summary>
    /// <returns>
    /// The display name for known platforms; otherwise the original trimmed value, so an
    /// unrecognized agent still shows what it actually reported.
    /// </returns>
    public static string GetDisplayName(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return s_displayNames[Unknown];

        var normalized = Normalize(platform);

        return s_displayNames.TryGetValue(normalized, out var displayName)
            ? displayName
            : platform!.Trim();
    }

    /// <summary>
    /// Whether an agent-reported <paramref name="platform"/> satisfies a user-supplied
    /// <paramref name="filter"/>, comparing canonical identities so <c>--platform tizen</c>
    /// matches an agent that registered itself as <c>"Tizen"</c>.
    /// </summary>
    /// <param name="platform">The platform reported by the agent.</param>
    /// <param name="filter">The requested platform. A null or whitespace filter matches everything.</param>
    public static bool Matches(string? platform, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        if (string.Equals(Normalize(platform), Normalize(filter), StringComparison.Ordinal))
            return true;

        // Preserve the historical substring behavior so partial filters such as "--platform mac"
        // or a raw TFM fragment keep matching the agents they always matched.
        return platform is not null
            && platform.IndexOf(filter!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
