using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Shared, fail-closed policy used by reviewed XAML and C# AutomationId proposals. It validates
/// source identity, a static identifier, and project/live uniqueness; language-specific parsers
/// must add their own declaration-safety checks.
/// </summary>
public static class MauiAutomationIdProposalPolicy
{
    public const int MaximumLength = 128;

    private static readonly string[] UserDerivedTokens =
    [
        "binding", "user", "customer", "email", "mail", "phone", "address", "password",
        "token", "secret", "displayname", "firstname", "lastname",
    ];

    public static bool TryValidate(string? value, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            reason = "AutomationId must be a nonempty static literal.";
            return false;
        }
        if (value.Length > MaximumLength)
        {
            reason = $"AutomationId must be {MaximumLength} characters or fewer.";
            return false;
        }
        if (IsPotentiallyLocalizedOrUserDerived(value))
        {
            reason = "AutomationId must be a static nonlocalized test identifier and cannot be user-derived.";
            return false;
        }
        if (!IsAsciiLetter(value[0]))
        {
            reason = "AutomationId must begin with an ASCII letter.";
            return false;
        }

        var previousSeparator = false;
        foreach (var character in value)
        {
            if (IsAsciiLetter(character) || character is >= '0' and <= '9')
            {
                previousSeparator = false;
                continue;
            }
            if (character is '_' or '-' or '.')
            {
                if (previousSeparator)
                {
                    reason = "AutomationId separators cannot be adjacent.";
                    return false;
                }
                previousSeparator = true;
                continue;
            }

            reason = "AutomationId must use only ASCII letters, digits, '.', '-', or '_'; localized and user-derived values are not allowed.";
            return false;
        }
        if (previousSeparator)
        {
            reason = "AutomationId cannot end with a separator.";
            return false;
        }
        return true;
    }

    public static bool IsPotentiallyLocalizedOrUserDerived(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value.Any(static character => character > 0x7f) ||
            value.IndexOfAny(['{', '}', '$', '%']) >= 0)
        {
            return true;
        }
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return UserDerivedTokens.Any(token => normalized.Contains(token, StringComparison.Ordinal));
    }

    /// <summary>Matches the short UTF-8 source hash emitted by DevFlow source maps.</summary>
    public static string ComputeSourceHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();

    /// <summary>Computes a full content digest used to bind a proposal to one exact document.</summary>
    public static string ComputeContentDigest(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool IsAsciiLetter(char character)
        => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}

/// <summary>Language-neutral facts checked before an XAML or C# parser considers a declaration.</summary>
public sealed class MauiSourceProposalCommonEligibilityInput
{
    public string? SourceText { get; init; }
    public string? FileRelativePath { get; init; }
    public string? ExpectedSourceHash { get; init; }
    public string? RequiredFileExtension { get; init; }
    public string? WrongLanguageCode { get; init; }
    public bool HasMappedSource { get; init; }
    public bool IsProjectContained { get; init; }
    public bool IsRegisteredProjectFile { get; init; }
    public bool IsGenerated { get; init; }
    public bool IsLinked { get; init; }
    public bool HasReparsePoint { get; init; }
    public bool IsNativeOrWebViewSynthetic { get; init; }
    public bool IsVirtualizedOrTemplated { get; init; }
    public string? ProposedAutomationId { get; init; }
    public IReadOnlyList<string>? ProjectAutomationIds { get; init; }
    public IReadOnlyList<string>? LiveAutomationIds { get; init; }
    public bool LiveUniquenessAvailable { get; init; }
    public bool RequireLiveUniqueness { get; init; } = true;
}

/// <summary>One language-neutral source-proposal gate failure.</summary>
public sealed class MauiSourceProposalCommonEligibilityReason
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>
/// Common identity, identifier, and uniqueness gate. This performs no parsing and never writes
/// source; callers append syntax- and semantic-model-specific failures before declaring eligibility.
/// </summary>
public static class MauiSourceProposalCommonEligibility
{
    public static List<MauiSourceProposalCommonEligibilityReason> Analyze(
        MauiSourceProposalCommonEligibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reasons = new List<MauiSourceProposalCommonEligibilityReason>();
        void Reject(string code, string message)
        {
            if (!reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
                reasons.Add(new MauiSourceProposalCommonEligibilityReason { Code = code, Message = message });
        }

        if (!input.HasMappedSource || string.IsNullOrWhiteSpace(input.SourceText))
        {
            Reject("source-map-unavailable",
                "A current unambiguous source document, span, and hash are required.");
        }
        if (!IsSafeRelativePath(input.FileRelativePath, input.RequiredFileExtension))
        {
            Reject(input.WrongLanguageCode ?? "source-language-invalid",
                $"Only a registered project {input.RequiredFileExtension ?? "source"} file can receive this proposal.");
        }
        if (!input.IsProjectContained)
            Reject("source-file-outside-project", "The source file is not contained by the registered project.");
        if (!input.IsRegisteredProjectFile)
            Reject("source-file-unregistered", "The source file is not registered by the current project.");
        if (input.IsGenerated)
            Reject("source-file-generated", "Generated source files are not eligible for source proposals.");
        if (input.IsLinked)
            Reject("source-file-linked", "Linked source files are not eligible for source proposals.");
        if (input.HasReparsePoint)
            Reject("source-path-reparse-point", "A source path containing a symbolic link or reparse point is not eligible.");
        if (input.IsNativeOrWebViewSynthetic)
            Reject("native-or-webview-synthetic", "Native, Shell, and WebView synthetic elements have no writable static declaration.");
        if (input.IsVirtualizedOrTemplated)
            Reject("repeater-or-virtualized", "Virtualized, templated, and repeater elements are not eligible.");

        var source = input.SourceText ?? string.Empty;
        if (!IsExpectedHash(input.ExpectedSourceHash) ||
            !string.Equals(
                MauiAutomationIdProposalPolicy.ComputeSourceHash(source),
                input.ExpectedSourceHash,
                StringComparison.OrdinalIgnoreCase))
        {
            Reject("source-hash-mismatch", "The current source text does not match the mapped source hash.");
        }

        if (MauiAutomationIdProposalPolicy.IsPotentiallyLocalizedOrUserDerived(input.ProposedAutomationId))
        {
            Reject("automation-id-localized-or-user-derived",
                "AutomationId must be static, nonlocalized, and independent of user data.");
        }
        else if (!MauiAutomationIdProposalPolicy.TryValidate(input.ProposedAutomationId, out var idReason))
        {
            Reject("automation-id-invalid", idReason!);
        }

        var proposed = input.ProposedAutomationId ?? string.Empty;
        if (CountMatches(input.ProjectAutomationIds, proposed) > 0)
        {
            Reject("automation-id-duplicate-project",
                "The proposed AutomationId already exists in the required project scope.");
        }
        if (input.RequireLiveUniqueness && !input.LiveUniquenessAvailable)
        {
            Reject("live-uniqueness-unavailable",
                "Current live uniqueness evidence is required before a source proposal can be approved.");
        }
        if (input.LiveUniquenessAvailable && CountMatches(input.LiveAutomationIds, proposed) > 0)
        {
            Reject("automation-id-duplicate-live",
                "The proposed AutomationId already exists in the current live scope.");
        }

        return reasons;
    }

    private static bool IsExpectedHash(string? value)
        => value is { Length: 16 } && value.All(Uri.IsHexDigit);

    private static bool IsSafeRelativePath(string? value, string? extension)
        => !string.IsNullOrWhiteSpace(value) &&
           !Path.IsPathRooted(value) &&
           !value.Contains("..", StringComparison.Ordinal) &&
           !string.IsNullOrWhiteSpace(extension) &&
           value.EndsWith(extension, StringComparison.OrdinalIgnoreCase);

    private static int CountMatches(IReadOnlyList<string>? values, string proposed)
        => values?.Count(value => string.Equals(value, proposed, StringComparison.Ordinal)) ?? 0;
}
