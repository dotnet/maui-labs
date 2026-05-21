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

	internal static async Task<bool> WriteFileAtomicallyWithinRootAsync(
		string destinationPath,
		string root,
		byte[] content,
		CancellationToken ct)
	{
		var fullDestinationPath = Path.GetFullPath(destinationPath);
		var destinationDirectory = Path.GetDirectoryName(fullDestinationPath);
		if (destinationDirectory is null)
			return false;

		if (!IsPathWithinRoot(destinationDirectory, root))
			return false;

		Directory.CreateDirectory(destinationDirectory);
		if (!IsSafeDestination(fullDestinationPath, destinationDirectory, root))
			return false;

		var tempDirectory = Path.Combine(root, $".maui-ai-write.{Guid.NewGuid():N}.tmp");
		var tempPath = Path.Combine(tempDirectory, Path.GetFileName(fullDestinationPath));

		try
		{
			CreatePrivateDirectory(tempDirectory);
			if (IsReparsePoint(tempDirectory) || !IsPathWithinRoot(tempDirectory, root))
				return false;

			await File.WriteAllBytesAsync(tempPath, content, ct).ConfigureAwait(false);
			if (!IsPathWithinRoot(tempPath, root))
			{
				return false;
			}

			// Re-evaluate the destination immediately before the replace so a symlink
			// swap between directory creation and the final move cannot redirect writes.
			if (!IsSafeDestination(fullDestinationPath, destinationDirectory, root))
				return false;

			File.Move(tempPath, fullDestinationPath, overwrite: true);
			return true;
		}
		finally
		{
			if (Directory.Exists(tempDirectory))
				Directory.Delete(tempDirectory, recursive: true);
		}
	}

	internal static bool IsReparsePoint(string path)
	{
		try
		{
			return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
		}
		catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
		{
			return false;
		}
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

	static bool IsSafeDestination(string destinationPath, string destinationDirectory, string root)
		=> IsPathWithinRoot(destinationDirectory, root) &&
			IsPathWithinRoot(destinationPath, root) &&
			!IsReparsePoint(destinationDirectory) &&
			!IsReparsePoint(destinationPath);

	static void CreatePrivateDirectory(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			Directory.CreateDirectory(path);
			return;
		}

		Directory.CreateDirectory(
			path,
			UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}
}
