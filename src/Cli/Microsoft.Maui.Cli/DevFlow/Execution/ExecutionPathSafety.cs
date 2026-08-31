namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal static class ExecutionPathSafety
{
    public static string PrepareNewOrEmptyDirectory(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw FlowExecutionException.Invalid(
                "execution-output-invalid",
                "The execution output path is invalid.");
        }

        RejectReparsePoints(
            fullPath,
            "execution-output-reparse-point",
            "The execution output path and its existing ancestors cannot be symbolic links or reparse points.");
        if (File.Exists(fullPath))
        {
            throw FlowExecutionException.Invalid(
                "execution-output-invalid",
                "The execution output path is an existing file.");
        }
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            throw FlowExecutionException.Invalid(
                "execution-output-not-empty",
                "The execution output directory must be new or empty because first-attempt artifacts are immutable.");
        }

        try
        {
            Directory.CreateDirectory(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FlowExecutionException.Invalid(
                "execution-output-create-failed",
                "The execution output directory could not be created.");
        }

        RejectReparsePoints(
            fullPath,
            "execution-output-reparse-point",
            "The execution output path and its existing ancestors cannot be symbolic links or reparse points.");
        if (!Directory.Exists(fullPath) ||
            Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            throw FlowExecutionException.Invalid(
                "execution-output-not-empty",
                "The execution output directory changed while it was being prepared.");
        }
        return fullPath;
    }

    public static void ValidateOutputDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        RejectReparsePoints(
            fullPath,
            "execution-output-reparse-point",
            "The execution output path and its existing ancestors cannot be symbolic links or reparse points.");
        if (!Directory.Exists(fullPath))
        {
            throw FlowExecutionException.Infrastructure(
                "execution-output-missing",
                "The prepared execution output directory is no longer available.");
        }
    }

    public static string PrepareParentForNewFile(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw FlowExecutionException.Invalid(
                "execution-output-invalid",
                "The output path is invalid.");
        }

        if (EntryExists(fullPath))
        {
            throw FlowExecutionException.Invalid(
                "execution-output-exists",
                "The output path already exists.");
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw FlowExecutionException.Invalid(
                "execution-output-invalid",
                "The output path has no parent directory.");
        }

        RejectReparsePoints(
            directory,
            "execution-output-reparse-point",
            "The output path and its existing ancestors cannot be symbolic links or reparse points.");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FlowExecutionException.Invalid(
                "execution-output-create-failed",
                "The output parent directory could not be created.");
        }
        RejectReparsePoints(
            directory,
            "execution-output-reparse-point",
            "The output path and its existing ancestors cannot be symbolic links or reparse points.");
        if (EntryExists(fullPath))
        {
            throw FlowExecutionException.Invalid(
                "execution-output-exists",
                "The output path already exists.");
        }
        return fullPath;
    }

    public static void ValidateConfinedArtifactPath(string ownedRoot, string artifactPath)
    {
        var root = Path.GetFullPath(ownedRoot);
        var candidate = Path.GetFullPath(artifactPath);
        if (!IsWithinRoot(root, candidate))
        {
            throw FlowExecutionException.Infrastructure(
                "artifact-path-outside-build-root",
                "MSBuild returned an app artifact outside the invocation-owned build root.");
        }

        RejectReparsePoints(
            root,
            "artifact-build-root-reparse-point",
            "The invocation-owned build root cannot contain or traverse a symbolic link or reparse point.");
        RejectReparsePoints(
            candidate,
            "artifact-reparse-point",
            "The resolved app artifact cannot contain or traverse a symbolic link or reparse point.");
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            throw FlowExecutionException.Infrastructure(
                "artifact-file-missing",
                "The resolved app artifact no longer exists after the project build.");
        }
    }

    public static void RejectReparsePoints(string path, string code, string message)
    {
        var current = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (current.Length == 0)
            current = Path.GetPathRoot(Path.GetFullPath(path)) ?? Path.GetFullPath(path);

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (TryGetAttributes(current, out var attributes) &&
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw FlowExecutionException.Invalid(code, message);
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, current, PathComparison))
            {
                break;
            }
            current = parent;
        }
    }

    public static bool IsWithinRoot(string root, string candidate)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullRoot, fullCandidate, PathComparison))
            return false;
        return fullCandidate.StartsWith(
            fullRoot + Path.DirectorySeparatorChar,
            PathComparison);
    }

    public static bool EntryExists(string path)
        => TryGetAttributes(path, out _);

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }

        attributes = default;
        return false;
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
