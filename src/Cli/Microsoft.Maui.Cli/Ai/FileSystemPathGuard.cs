// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai;

internal static class FileSystemPathGuard
{
	internal static readonly StringComparison PathComparison =
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	internal static bool IsPathWithinRoot(string path, string root)
	{
		var canonicalPath = Path.TrimEndingDirectorySeparator(ResolveCanonicalPath(path));
		var canonicalRoot = Path.TrimEndingDirectorySeparator(ResolveCanonicalPath(root));

		if (string.Equals(canonicalPath, canonicalRoot, PathComparison))
			return true;

		var rootWithSeparator = Path.EndsInDirectorySeparator(canonicalRoot)
			? canonicalRoot
			: canonicalRoot + Path.DirectorySeparatorChar;

		return canonicalPath.StartsWith(rootWithSeparator, PathComparison);
	}

	internal static string ResolveCanonicalPath(string path)
	{
		var fullPath = Path.GetFullPath(path);
		var root = Path.GetPathRoot(fullPath);
		if (string.IsNullOrEmpty(root))
			return fullPath;

		var current = root;
		var relative = Path.GetRelativePath(root, fullPath);
		if (relative == ".")
			return ResolveExistingFileSystemEntry(current);

		foreach (var segment in relative.Split(
			[Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
			StringSplitOptions.RemoveEmptyEntries))
		{
			current = ResolveExistingFileSystemEntry(Path.Combine(current, segment));
		}

		return Path.GetFullPath(current);
	}

	static string ResolveExistingFileSystemEntry(string path)
	{
		FileSystemInfo? info = Directory.Exists(path)
			? new DirectoryInfo(path)
			: File.Exists(path)
				? new FileInfo(path)
				: null;

		if (info is null)
			return path;

		return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
	}
}
