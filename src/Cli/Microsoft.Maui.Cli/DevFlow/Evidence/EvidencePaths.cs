using System.Globalization;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>Outcome of validating a requested bundle destination.</summary>
internal sealed record EvidencePathResult(string? Path, string? Error)
{
    public bool Ok => Error is null && Path is not null;
}

/// <summary>
/// Filesystem policy for evidence bundles: where a capture goes by default, which destinations are
/// accepted, and how generated HTML reports are stored and aged out.
/// </summary>
internal static class EvidencePaths
{
    /// <summary>Directory holding generated (regenerated, never bundled) HTML reports.</summary>
    internal static string ReportDirectory { get; set; } =
        Path.Combine(Path.GetTempPath(), "maui-devflow-evidence");

    private const string ReportPrefix = "evidence-report-";
    private const string ReportSuffix = ".html";
    private static readonly TimeSpan ReportTtl = TimeSpan.FromHours(24);
    private const int MaxRetainedReports = 20;

    /// <summary>
    /// Resolves the project root used for project-relative source paths and the default
    /// <c>maui-traces</c> output folder. Falls back to walking up from the working directory
    /// looking for a project file, then returns null.
    /// </summary>
    public static string? FindProjectRoot(string? projectHint, string? startDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(projectHint))
        {
            try
            {
                if (Path.IsPathFullyQualified(projectHint))
                {
                    var full = Path.GetFullPath(projectHint!);
                    if (Directory.Exists(full)) return full;
                    if (File.Exists(full)) return Path.GetDirectoryName(full);
                }
            }
            catch
            {
                // Unusable hint — fall through to directory probing.
            }
        }

        try
        {
            var directory = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
            for (var depth = 0; directory is not null && depth < 12; depth++)
            {
                if (directory.EnumerateFiles("*.csproj").Any() ||
                    directory.EnumerateFiles("*.fsproj").Any() ||
                    directory.EnumerateFiles("*.slnx").Any() ||
                    directory.EnumerateFiles("*.sln").Any())
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
        }
        catch
        {
            // Probing is best-effort; a missing project root only affects defaults.
        }

        return null;
    }

    /// <summary>Timestamped, filesystem-safe bundle name (e.g. <c>MyApp-20260729-114233.mauitrace</c>).</summary>
    public static string BuildDefaultFileName(string? appName, DateTime utcNow)
    {
        var prefix = SanitizeFileNameComponent(appName);
        if (prefix.Length == 0) prefix = "devflow";
        var stamp = utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return $"{prefix}-{stamp}{EvidenceFormat.FileExtension}";
    }

    /// <summary>
    /// Default destination: <c>&lt;projectRoot&gt;/maui-traces/&lt;name&gt;.mauitrace</c> when the
    /// project root is known, otherwise the same file name in the current directory.
    /// </summary>
    public static string ResolveDefaultOutputPath(string? projectRoot, string? appName, DateTime utcNow)
    {
        var fileName = BuildDefaultFileName(appName, utcNow);
        var directory = string.IsNullOrWhiteSpace(projectRoot)
            ? Directory.GetCurrentDirectory()
            : Path.Combine(projectRoot!, EvidenceFormat.DefaultFolderName);
        return Path.GetFullPath(Path.Combine(directory, fileName));
    }

    /// <summary>
    /// Validates an explicit <c>--output</c> value. Accepts a full path, a relative path, or an
    /// existing directory (in which case the default file name is used inside it). Rejects control
    /// characters, invalid file names, and any extension other than <c>.mauitrace</c>.
    /// </summary>
    public static EvidencePathResult ValidateOutputPath(
        string? requested,
        string? projectRoot,
        string? appName,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return new EvidencePathResult(ResolveDefaultOutputPath(projectRoot, appName, utcNow), null);

        var value = requested!.Trim();
        if (value.Contains('\0') || value.Any(char.IsControl))
            return new EvidencePathResult(null, "Output path contains invalid characters.");
        if (value.Length > 1024)
            return new EvidencePathResult(null, "Output path is too long.");

        string full;
        try
        {
            full = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new EvidencePathResult(null, "Output path is not a valid file path.");
        }

        var endsWithSeparator = value.EndsWith('/') || value.EndsWith('\\') ||
            value.EndsWith(Path.DirectorySeparatorChar) || value.EndsWith(Path.AltDirectorySeparatorChar);
        if (Directory.Exists(full) || endsWithSeparator)
            full = Path.Combine(full, BuildDefaultFileName(appName, utcNow));

        var fileName = Path.GetFileName(full);
        if (string.IsNullOrEmpty(fileName))
            return new EvidencePathResult(null, "Output path must include a file name.");
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return new EvidencePathResult(null, "Output file name contains invalid characters.");
        if (!fileName.EndsWith(EvidenceFormat.FileExtension, StringComparison.OrdinalIgnoreCase))
            return new EvidencePathResult(null, $"Evidence bundles must use the {EvidenceFormat.FileExtension} extension.");

        return new EvidencePathResult(full, null);
    }

    /// <summary>Validates a bundle path supplied for reading. The file must exist and be a regular file.</summary>
    public static EvidencePathResult ValidateInputPath(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return new EvidencePathResult(null, "A bundle path is required.");

        var value = requested!.Trim();
        if (value.Contains('\0') || value.Any(char.IsControl))
            return new EvidencePathResult(null, "Bundle path contains invalid characters.");

        string full;
        try
        {
            full = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new EvidencePathResult(null, "Bundle path is not a valid file path.");
        }

        if (Directory.Exists(full))
            return new EvidencePathResult(null, "Bundle path is a directory.");
        if (!File.Exists(full))
            return new EvidencePathResult(null, $"Bundle not found: {full}");

        return new EvidencePathResult(full, null);
    }

    public static EvidencePathResult ValidateReportPath(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return new EvidencePathResult(null, "A report path is required.");

        var value = requested.Trim();
        if (value.Contains('\0') || value.Any(char.IsControl))
            return new EvidencePathResult(null, "Report path contains invalid characters.");
        if (value.Length > 1024)
            return new EvidencePathResult(null, "Report path is too long.");

        string full;
        try
        {
            full = Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new EvidencePathResult(null, "Report path is not a valid file path.");
        }

        if (Directory.Exists(full))
            return new EvidencePathResult(null, "Report path is a directory.");

        var fileName = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(fileName))
            return new EvidencePathResult(null, "Report path must include a file name.");
        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            (OperatingSystem.IsWindows() && fileName.Contains(':')))
        {
            return new EvidencePathResult(null, "Report file name contains invalid characters.");
        }
        if (OperatingSystem.IsWindows() && IsReservedWindowsFileName(fileName))
            return new EvidencePathResult(null, "Report file name is reserved by Windows.");
        if (!fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return new EvidencePathResult(null, "Evidence reports must use the .html or .htm extension.");
        }

        return new EvidencePathResult(full, null);
    }

    public static string SanitizeFileNameComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim()
            .Where(c => !char.IsControl(c) && Array.IndexOf(invalid, c) < 0 && c != ' ')
            .Take(48)
            .ToArray();
        return new string(chars).Trim('.', '-');
    }

    private static bool IsReservedWindowsFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName)
            .TrimEnd(' ', '.')
            .ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL")
            return true;
        return stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.Ordinal) ||
             stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            stem[3] is >= '1' and <= '9';
    }

    // ── Generated report files ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Allocates a path for a freshly generated report and ages out old ones. Only files this
    /// subsystem created (matching the report name pattern) are ever deleted — the directory
    /// itself is never removed recursively.
    /// </summary>
    public static string CreateReportPath(DateTime utcNow)
    {
        Directory.CreateDirectory(ReportDirectory);
        CleanupReports(utcNow);
        var stamp = utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var unique = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(ReportDirectory, $"{ReportPrefix}{stamp}-{unique}{ReportSuffix}");
    }

    /// <summary>Deletes generated reports older than the TTL, then trims to the newest N.</summary>
    public static void CleanupReports(DateTime utcNow)
    {
        try
        {
            if (!Directory.Exists(ReportDirectory)) return;

            var reports = new DirectoryInfo(ReportDirectory)
                .GetFiles(ReportPrefix + "*" + ReportSuffix, SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            for (var i = 0; i < reports.Count; i++)
            {
                var expired = utcNow - reports[i].LastWriteTimeUtc > ReportTtl;
                if (expired || i >= MaxRetainedReports)
                {
                    try { reports[i].Delete(); }
                    catch { /* a report open in a browser stays until the next sweep */ }
                }
            }
        }
        catch
        {
            // Cleanup is best-effort and must never fail a view.
        }
    }
}
