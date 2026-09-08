namespace Microsoft.Maui.DevFlow.Agent.Core;

internal readonly record struct ResolvedFileStoragePath(
    string BasePath,
    string FullPath,
    string RelativePath);

internal static class FileStoragePathResolver
{
    public static ResolvedFileStoragePath Resolve(string basePath, string? relativePath, bool allowRoot = false)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new InvalidOperationException("App data directory is unavailable");

        var fullBasePath = Path.GetFullPath(basePath);
        var requestedPath = relativePath ?? string.Empty;

        if (requestedPath.IndexOf('\0') >= 0)
            throw new InvalidOperationException("Invalid file path");

        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            if (!allowRoot)
                throw new InvalidOperationException("file path is required");

            return new ResolvedFileStoragePath(fullBasePath, fullBasePath, string.Empty);
        }

        if (IsRootedOrDriveQualified(requestedPath))
            throw new InvalidOperationException("Rooted or absolute paths are not allowed");

        var normalizedForSegments = requestedPath.Replace('\\', '/');
        var segments = normalizedForSegments.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == ".."))
            throw new InvalidOperationException("Directory traversal is not allowed");

        var normalizedPath = string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        var fullPath = Path.GetFullPath(Path.Combine(fullBasePath, normalizedPath));

        EnsureContained(fullBasePath, fullPath);

        var relative = Path.GetRelativePath(fullBasePath, fullPath);
        if (relative == ".")
            relative = string.Empty;

        relative = relative.Replace(Path.DirectorySeparatorChar, '/');
        if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');

        return new ResolvedFileStoragePath(fullBasePath, fullPath, relative);
    }

    public static void EnsureNoReparsePointTraversal(string basePath, string targetPath, bool includeTarget)
    {
        var relative = Path.GetRelativePath(basePath, targetPath);
        if (relative == ".")
            return;

        var parts = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var path = Path.GetFullPath(basePath);
        var count = includeTarget ? parts.Length : Math.Max(0, parts.Length - 1);
        for (var i = 0; i < count; i++)
        {
            path = Path.Combine(path, parts[i]);
            if (!File.Exists(path) && !Directory.Exists(path))
                continue;

            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symbolic links are not allowed in file storage paths");
        }
    }

    private static bool IsRootedOrDriveQualified(string path)
        => Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':');

    private static void EnsureContained(string basePath, string fullPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var baseWithSeparator = basePath.EndsWith(Path.DirectorySeparatorChar)
            || basePath.EndsWith(Path.AltDirectorySeparatorChar)
            ? basePath
            : basePath + Path.DirectorySeparatorChar;

        if (!string.Equals(fullPath, basePath, comparison)
            && !fullPath.StartsWith(baseWithSeparator, comparison))
        {
            throw new InvalidOperationException("Directory traversal is not allowed");
        }
    }
}
