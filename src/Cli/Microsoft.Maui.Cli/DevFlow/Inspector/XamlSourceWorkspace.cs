namespace Microsoft.Maui.Cli.DevFlow.Inspector;

// An unavailable workspace is distinct from an unspecified one: a later Inspector must not
// silently substitute its own working directory for the broker's rejected source-search root.
internal sealed class XamlSourceWorkspace
{
    private XamlSourceWorkspace(string? startPath) => StartPath = startPath;

    public string? StartPath { get; }

    public string? Error => StartPath is null
        ? "The XAML source workspace must be an existing local directory. Set MAUI_DEVFLOW_PROJECT_ROOT to the app workspace before restarting the broker."
        : null;

    public static XamlSourceWorkspace Capture(string? startPath = null)
    {
        try
        {
            var candidate = string.IsNullOrWhiteSpace(startPath)
                ? Directory.GetCurrentDirectory()
                : startPath;
            if (XamlSourcePropertyEditor.IsUnsafeAbsoluteSourcePath(candidate))
                return new(null);

            var fullPath = Path.GetFullPath(candidate);
            return !XamlSourcePropertyEditor.IsUnsafeAbsoluteSourcePath(fullPath) && Directory.Exists(fullPath)
                ? new(fullPath)
                : new(null);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(null);
        }
    }
}
