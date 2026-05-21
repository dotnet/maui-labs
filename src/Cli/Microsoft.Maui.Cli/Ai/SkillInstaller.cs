// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.ExceptionServices;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Orchestrates skill installation by downloading files from the marketplace,
/// creating the local directory structure, and writing version metadata.
/// </summary>
internal static class SkillInstaller
{
	/// <summary>
	/// Installs a skill into the target environment directory.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/> (caller manages lifetime).</param>
	/// <param name="skill">Skill to install.</param>
	/// <param name="env">Target agent environment.</param>
	/// <param name="projectRoot">Absolute path to the project root directory.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to install from.</param>
	/// <param name="force">When <c>true</c>, overwrite an existing installation.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>
	/// A tuple of (filesInstalled, installPath) where filesInstalled is the number
	/// of files written and installPath is the absolute path to the skill directory.
	/// Returns (0, installPath) if the skill is already installed and <paramref name="force"/> is <c>false</c>.
	/// Returns (-1, string.Empty) if the skill name contains invalid characters.
	/// Returns (-2, installPath) if the download produced zero files (network or remote failure).
	/// </returns>
	public static async Task<(int FilesInstalled, string InstallPath)> InstallSkillAsync(
		HttpClient http,
		SkillInfo skill,
		DetectedEnvironment env,
		string projectRoot,
		string repo,
		string branch,
		bool force,
		CancellationToken ct = default)
	{
		if (skill.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || skill.Name.Contains(".."))
			return (-1, string.Empty);

		// If the skills directory is not rooted, resolve it relative to the project root.
		var skillsDir = Path.IsPathRooted(env.SkillsDirectory)
			? env.SkillsDirectory
			: Path.GetFullPath(Path.Combine(projectRoot, env.SkillsDirectory));

		var installPath = Path.Combine(skillsDir, skill.Name);

		// Skip if already installed and not forcing.
		if (!force)
		{
			var existing = await SkillVersionStore.ReadAsync(installPath, ct).ConfigureAwait(false);
			if (existing is not null)
				return (0, installPath);
		}

		Directory.CreateDirectory(skillsDir);
		var tempInstallPath = Path.Combine(skillsDir, $".{skill.Name}.{Guid.NewGuid():N}.tmp");
		Directory.CreateDirectory(tempInstallPath);

		try
		{
			var filesInstalled = await MarketplaceClient.DownloadSkillFilesAsync(
				http, skill, tempInstallPath, repo, branch, ct).ConfigureAwait(false);

			if (skill.Files.Count == 0 || filesInstalled != skill.Files.Count)
				return (-2, installPath);

			// Resolve the latest commit SHA for version tracking.
			var commitSha = await MarketplaceClient.GetRemoteCommitShaAsync(
				http, repo, branch, skill.RemotePath, ct).ConfigureAwait(false);

			var version = new InstalledSkillVersion
			{
				Name = skill.Name,
				Commit = commitSha,
				Branch = branch,
				UpdatedAt = DateTime.UtcNow.ToString("o"),
				Source = repo,
				PluginPath = skill.RemotePath
			};

			await SkillVersionStore.WriteAsync(tempInstallPath, version, ct).ConfigureAwait(false);

			ReplaceDirectory(tempInstallPath, installPath);

			return (filesInstalled, installPath);
		}
		finally
		{
			TryDeleteDirectoryIfExists(tempInstallPath);
		}
	}

	static void ReplaceDirectory(string sourceDirectory, string destinationDirectory)
	{
		var backupDirectory = $"{destinationDirectory}.{Guid.NewGuid():N}.bak";
		TryDeleteDirectoryIfExists(backupDirectory);

		if (Directory.Exists(destinationDirectory))
			Directory.Move(destinationDirectory, backupDirectory);

		try
		{
			Directory.Move(sourceDirectory, destinationDirectory);
			TryDeleteDirectoryIfExists(backupDirectory);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			RestoreBackupDirectory(backupDirectory, destinationDirectory, ex);
			ExceptionDispatchInfo.Capture(ex).Throw();
			throw;
		}
	}

	static void RestoreBackupDirectory(string backupDirectory, string destinationDirectory, Exception originalException)
	{
		if (!Directory.Exists(backupDirectory) || Directory.Exists(destinationDirectory))
			return;

		try
		{
			Directory.Move(backupDirectory, destinationDirectory);
		}
		catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
		{
			throw new InvalidOperationException(
				$"Could not replace skill directory '{destinationDirectory}' and could not restore the previous installation.",
				new AggregateException(originalException, restoreException));
		}
	}

	static void DeleteDirectoryIfExists(string path)
	{
		if (!Directory.Exists(path))
			return;

		try
		{
			Directory.Delete(path, recursive: true);
		}
		catch (IOException ex)
		{
			throw new InvalidOperationException($"Could not clean up temporary skill installation directory '{path}'.", ex);
		}
		catch (UnauthorizedAccessException ex)
		{
			throw new InvalidOperationException($"Could not clean up temporary skill installation directory '{path}'.", ex);
		}
	}

	static void TryDeleteDirectoryIfExists(string path)
	{
		try
		{
			DeleteDirectoryIfExists(path);
		}
		catch (InvalidOperationException)
		{
			// Best-effort cleanup: do not hide the actual install result or root exception.
		}
	}
}
