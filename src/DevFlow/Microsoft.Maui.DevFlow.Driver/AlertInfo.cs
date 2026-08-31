using System.Security.Cryptography;
using System.Text;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Represents a button found in a system or app alert dialog.
/// </summary>
public record AlertButton(string Label, double X, double Y, double Width, double Height)
{
    public int CenterX => (int)(X + Width / 2);
    public int CenterY => (int)(Y + Height / 2);
}

/// <summary>
/// Information about a detected alert dialog.
/// </summary>
public record AlertInfo(string? Title, IReadOnlyList<AlertButton> Buttons)
{
    public string? Message { get; init; }
}

public record AlertActionResult(AlertInfo? Alert, bool MatchesExpected, bool Dismissed);

public static class AlertRevision
{
    public static string? Create(AlertInfo? alert)
    {
        if (alert is null)
            return null;

        var canonical = string.Join(
            "\n",
            new[] { alert.Title?.Trim() ?? string.Empty, alert.Message?.Trim() ?? string.Empty }
                .Concat(alert.Buttons.Select(button => button.Label.Trim())));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}

/// <summary>Platform alert detection and dismissal used by CLI and Inspector hosts.</summary>
public interface IAlertDriver : IDisposable
{
    Task<AlertInfo?> DetectAlertAsync();
    Task DismissAlertAsync(string? buttonLabel = null);
    Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null);

    async Task<AlertActionResult> HandleAlertAsync(
        string? buttonLabel = null,
        string? expectedRevision = null)
    {
        var alert = await DetectAlertAsync();
        if (alert is null)
            return new(null, MatchesExpected: true, Dismissed: false);
        if (!string.IsNullOrEmpty(expectedRevision) &&
            !string.Equals(expectedRevision, AlertRevision.Create(alert), StringComparison.Ordinal))
        {
            return new(alert, MatchesExpected: false, Dismissed: false);
        }

        if (!string.IsNullOrEmpty(expectedRevision))
        {
            var current = await DetectAlertAsync();
            if (current is null ||
                !string.Equals(expectedRevision, AlertRevision.Create(current), StringComparison.Ordinal))
            {
                return new(current, MatchesExpected: false, Dismissed: false);
            }

            await DismissAlertAsync(buttonLabel);
            return new(current, MatchesExpected: true, Dismissed: true);
        }

        var handled = await HandleAlertIfPresentAsync(buttonLabel);
        return new(handled, MatchesExpected: true, Dismissed: handled is not null);
    }
}
