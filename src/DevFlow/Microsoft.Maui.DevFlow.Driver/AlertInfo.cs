using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Represents a button found in a system or app alert dialog.
/// </summary>
public record AlertButton(string Label, double X, double Y, double Width, double Height)
{
    public string? Identifier { get; init; }
    public int CenterX => (int)(X + Width / 2);
    public int CenterY => (int)(Y + Height / 2);
}

/// <summary>
/// Information about a detected alert dialog.
/// </summary>
public record AlertInfo(string? Title, IReadOnlyList<AlertButton> Buttons)
{
    /// <summary>
    /// Stable fingerprint for this exact visible prompt. Use it when responding so a stale
    /// automation step cannot act on a different dialog that appeared later.
    /// </summary>
    public string? PromptId { get; init; }

    public int? SourceProcessId { get; init; }
    public string? SourceProcessName { get; init; }
    public IReadOnlyList<string> Text { get; init; } = Array.Empty<string>();
    public bool IsSystemDialog { get; init; }
    public bool RequiresUserConfirmation { get; init; }
    public bool CanRespond { get; init; }
}

public record AlertActionResult(
    bool Success,
    bool UserActionRequired,
    string Message,
    AlertInfo? Dialog = null);

internal static class NativeDialogSafety
{
    internal static string CreateFingerprint(
        int sourceProcessId,
        string sourceProcessName,
        AlertInfo dialog)
    {
        var identity = new StringBuilder()
            .Append(sourceProcessId).Append('\n')
            .Append(sourceProcessName).Append('\n')
            .Append(dialog.Title).Append('\n');

        foreach (var text in dialog.Text)
            identity.Append(text).Append('\n');

        foreach (var button in dialog.Buttons)
            identity.Append(button.Label).Append(':').Append(button.Identifier).Append('\n');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())))
            [..16]
            .ToLowerInvariant();
    }

    internal static bool IsSystemDialogForTarget(
        AlertInfo dialog,
        params string?[] targetNames)
    {
        var promptText = string.Join(
            "\n",
            dialog.Text.Prepend(dialog.Title ?? string.Empty));
        if (string.IsNullOrWhiteSpace(promptText))
            return false;

        foreach (var targetName in targetNames)
        {
            if (ContainsWholeName(promptText, targetName))
                return true;
        }

        return false;
    }

    private static bool ContainsWholeName(string promptText, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        var name = targetName.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? targetName[..^4]
            : targetName;
        if (name.Length < 3)
            return false;

        var searchIndex = 0;
        while (searchIndex < promptText.Length)
        {
            var matchIndex = promptText.IndexOf(
                name,
                searchIndex,
                StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                return false;

            var beforeIsWord = matchIndex > 0 && char.IsLetterOrDigit(promptText[matchIndex - 1]);
            var afterIndex = matchIndex + name.Length;
            var afterIsWord = afterIndex < promptText.Length && char.IsLetterOrDigit(promptText[afterIndex]);
            if (!beforeIsWord && !afterIsWord)
                return true;

            searchIndex = matchIndex + 1;
        }

        return false;
    }
}
