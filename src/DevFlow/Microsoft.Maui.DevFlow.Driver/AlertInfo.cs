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
public record AlertInfo(string? Title, IReadOnlyList<AlertButton> Buttons);

/// <summary>Platform alert detection and dismissal used by CLI and Inspector hosts.</summary>
public interface IAlertDriver : IDisposable
{
    Task<AlertInfo?> DetectAlertAsync();
    Task DismissAlertAsync(string? buttonLabel = null);
    Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null);
}
