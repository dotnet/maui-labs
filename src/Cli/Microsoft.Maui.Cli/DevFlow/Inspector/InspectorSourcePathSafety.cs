using System.Text;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Path checks shared by the Inspector's read-only source lookups and by the recording and plan
/// sidecar writers. These are containment tests only: nothing here reads, writes, or patches a
/// project file, and this layer ships no source-apply route that could consume them for a write.
/// </summary>
internal static class InspectorSourcePathSafety
{
    /// <summary>
    /// Resolves the project directory that owns <paramref name="sourcePath"/>. An absolute
    /// <paramref name="project"/> is trusted directly; otherwise the walk up from the source file
    /// must land on a project whose derived session id matches the connected agent's, so a source
    /// map cannot redirect the Inspector at an unrelated checkout.
    /// </summary>
    internal static string? FindProjectRoot(string sourcePath, string project, string? sessionId)
    {
        if (Path.IsPathFullyQualified(project))
        {
            string projectPath;
            try { projectPath = Path.GetFullPath(project); }
            catch { return null; }
            if (Directory.Exists(projectPath))
                return projectPath;
            return File.Exists(projectPath) ? Path.GetDirectoryName(projectPath) : null;
        }

        var projectName = Path.GetFileName(project);
        var normalizedSessionId = SanitizeIdentity(sessionId);
        if (string.IsNullOrWhiteSpace(projectName) || normalizedSessionId.Length == 0)
            return null;

        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null)
        {
            var candidateProject = Path.Combine(directory.FullName, projectName);
            if (File.Exists(candidateProject) &&
                string.Equals(
                    ComputeDefaultSessionId(candidateProject),
                    normalizedSessionId,
                    StringComparison.Ordinal))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        return null;
    }

    internal static string ComputeDefaultSessionId(string projectPath)
    {
        var sanitized = SanitizeIdentity(Path.GetFullPath(projectPath));
        if (sanitized.Length > 24)
            sanitized = sanitized[^24..];
        return sanitized.Length == 0 ? string.Empty : $"dw{sanitized}";
    }

    /// <summary>
    /// True when <paramref name="path"/> stays inside <paramref name="root"/> after normalization.
    /// </summary>
    internal static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
            !string.Equals(relative, "..", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    /// <summary>
    /// True when any segment between <paramref name="root"/> and <paramref name="path"/> is a
    /// symlink or junction, which would let a containment check pass while the real target escapes
    /// the project. Any probing failure is reported as a reparse point so the caller refuses.
    /// </summary>
    internal static bool PathContainsReparsePoint(string root, string path)
    {
        try
        {
            if (IsReparsePoint(new DirectoryInfo(root)))
                return true;

            var relative = Path.GetRelativePath(root, path);
            var current = root;
            foreach (var segment in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                FileSystemInfo entry = string.Equals(current, path, PathComparison)
                    ? new FileInfo(current)
                    : new DirectoryInfo(current);
                if (IsReparsePoint(entry))
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return true;
        }
    }

    private static string SanitizeIdentity(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
                builder.Append(lower);
        }
        return builder.ToString();
    }

    private static bool IsReparsePoint(FileSystemInfo entry)
    {
        entry.Refresh();
        return (entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
