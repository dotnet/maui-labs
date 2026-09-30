// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Security;
using System.Text.Json;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ProfileBuildWorkspace
{
	internal const string RootDirectoryName = "maui-startup-profiling";
	internal const string OwnershipFileName = ".maui-profile-owner.json";
	internal const string DirectoryBuildPropsFileName = "MauiProfileBuildIsolation.Directory.Build.props";
	internal const string DirectoryBuildTargetsFileName = "MauiProfileBuildIsolation.Directory.Build.targets";
	internal const string IsolationPropsFileName = "MauiProfileBuildIsolation.props";
	internal const string OwnershipKind = "Microsoft.Maui.Cli.ProfileBuildWorkspace";
	internal const int OwnershipVersion = 1;
	internal static readonly TimeSpan StaleWorkspaceMinimumAge = TimeSpan.FromDays(1);

	readonly FileStream _ownershipLease;
	bool _cleanupAttempted;

	ProfileBuildWorkspace(string path, string sessionId, FileStream ownershipLease)
	{
		Path = path;
		SessionId = sessionId;
		_ownershipLease = ownershipLease;
	}

	internal string Path { get; }
	internal string SessionId { get; }
	internal string DirectoryBuildPropsPath => System.IO.Path.Combine(Path, DirectoryBuildPropsFileName);
	internal string DirectoryBuildTargetsPath => System.IO.Path.Combine(Path, DirectoryBuildTargetsFileName);
	internal string IsolationPropsPath => System.IO.Path.Combine(Path, IsolationPropsFileName);

	internal static ProfileBuildWorkspace Create(
		string projectDirectory,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose)
	{
		var rootPath = GetRootPath(projectDirectory);
		Directory.CreateDirectory(rootPath);
		RecoverStaleWorkspaces(projectDirectory, formatter, useJson, verbose);

		var sessionId = Guid.NewGuid().ToString("N")[..16];
		var workspacePath = System.IO.Path.Combine(rootPath, sessionId);
		Directory.CreateDirectory(workspacePath);

		FileStream? ownershipLease = null;
		try
		{
			var ownershipPath = System.IO.Path.Combine(workspacePath, OwnershipFileName);
			ownershipLease = new FileStream(
				ownershipPath,
				FileMode.CreateNew,
				FileAccess.ReadWrite,
				FileShare.Read);

			using var process = Process.GetCurrentProcess();
			var ownership = new ProfileBuildWorkspaceOwnership
			{
				Kind = OwnershipKind,
				Version = OwnershipVersion,
				SessionId = sessionId,
				WorkspacePath = System.IO.Path.GetFullPath(workspacePath),
				ProjectDirectory = System.IO.Path.GetFullPath(projectDirectory),
				ProcessId = Environment.ProcessId,
				ProcessStartTimeUtcTicks = TryGetProcessStartTimeUtcTicks(process),
				CreatedAtUtc = DateTimeOffset.UtcNow
			};

			JsonSerializer.Serialize(ownershipLease, ownership);
			ownershipLease.Flush(flushToDisk: true);
			ownershipLease.Position = 0;

			var workspace = new ProfileBuildWorkspace(workspacePath, sessionId, ownershipLease);
			workspace.WriteIsolationProps();
			return workspace;
		}
		catch
		{
			ownershipLease?.Dispose();
			TryDeleteDirectory(workspacePath);
			throw;
		}
	}

	internal void ConfigureBuildTargets(string? profilingInjectionTargetsPath)
	{
		var injectionImport = string.IsNullOrWhiteSpace(profilingInjectionTargetsPath)
			? string.Empty
			: $"""
			  <Import Project="{SecurityElement.Escape(profilingInjectionTargetsPath)}" />
			""";

		File.WriteAllText(
			DirectoryBuildTargetsPath,
			$"""
			<Project InitialTargets="ValidateMauiProfileBuildIsolation">
			  <PropertyGroup>
			    <_MauiProfileOriginalDirectoryBuildTargetsBasePath>$([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildProjectDirectory)', 'Directory.Build.targets'))</_MauiProfileOriginalDirectoryBuildTargetsBasePath>
			    <_MauiProfileOriginalDirectoryBuildTargetsPath Condition="'$(_MauiProfileOriginalDirectoryBuildTargetsBasePath)' != ''">$([System.IO.Path]::Combine('$(_MauiProfileOriginalDirectoryBuildTargetsBasePath)', 'Directory.Build.targets'))</_MauiProfileOriginalDirectoryBuildTargetsPath>
			  </PropertyGroup>
			  <Import Project="$(_MauiProfileOriginalDirectoryBuildTargetsPath)"
			          Condition="'$(_MauiProfileOriginalDirectoryBuildTargetsPath)' != '' and Exists('$(_MauiProfileOriginalDirectoryBuildTargetsPath)')" />
			  <Target Name="ValidateMauiProfileBuildIsolation" BeforeTargets="PrepareForBuild">
			    <PropertyGroup>
			      <_MauiProfileArtifactsRoot>$([MSBuild]::NormalizeDirectory('$(ArtifactsPath)'))</_MauiProfileArtifactsRoot>
			      <_MauiProfileBaseOutputPath>$([MSBuild]::NormalizeDirectory('$(BaseOutputPath)'))</_MauiProfileBaseOutputPath>
			      <_MauiProfileOutputPath>$([MSBuild]::NormalizeDirectory('$(OutputPath)'))</_MauiProfileOutputPath>
			      <_MauiProfileOutDir>$([MSBuild]::NormalizeDirectory('$(OutDir)'))</_MauiProfileOutDir>
			      <_MauiProfileTargetDir>$([MSBuild]::NormalizeDirectory('$(TargetDir)'))</_MauiProfileTargetDir>
			      <_MauiProfileBaseIntermediateOutputPath>$([MSBuild]::NormalizeDirectory('$(BaseIntermediateOutputPath)'))</_MauiProfileBaseIntermediateOutputPath>
			      <_MauiProfileIntermediateOutputPath>$([MSBuild]::NormalizeDirectory('$(IntermediateOutputPath)'))</_MauiProfileIntermediateOutputPath>
			      <_MauiProfileProjectExtensionsPath>$([MSBuild]::NormalizeDirectory('$(MSBuildProjectExtensionsPath)'))</_MauiProfileProjectExtensionsPath>
			      <_MauiProfileRestoreOutputPath>$([MSBuild]::NormalizeDirectory('$(RestoreOutputPath)'))</_MauiProfileRestoreOutputPath>
			    </PropertyGroup>
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileBaseOutputPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate BaseOutputPath for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileOutputPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate OutputPath for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileOutDir)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate OutDir for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileTargetDir)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate TargetDir for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileBaseIntermediateOutputPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate BaseIntermediateOutputPath for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileIntermediateOutputPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate IntermediateOutputPath for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileProjectExtensionsPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate MSBuildProjectExtensionsPath for '$(MSBuildProjectFullPath)'." />
			    <Error Condition="$([System.String]::Copy('$(_MauiProfileRestoreOutputPath)').StartsWith('$(_MauiProfileArtifactsRoot)')) != 'True'"
			           Text="The profiling build could not isolate RestoreOutputPath for '$(MSBuildProjectFullPath)'." />
			  </Target>
			{injectionImport}
			</Project>
			""");
	}

	internal void Cleanup(IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (_cleanupAttempted)
			return;

		_cleanupAttempted = true;
		_ownershipLease.Dispose();

		try
		{
			if (Directory.Exists(Path))
				Directory.Delete(Path, recursive: true);

			var rootPath = System.IO.Path.GetDirectoryName(Path);
			if (!string.IsNullOrWhiteSpace(rootPath)
				&& Directory.Exists(rootPath)
				&& !Directory.EnumerateFileSystemEntries(rootPath).Any())
			{
				Directory.Delete(rootPath);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			var message =
				$"Could not delete profile build workspace '{Path}'. " +
				$"A later profile invocation will retry after confirming the workspace is stale: {ex.Message}";
			if (useJson)
			{
				ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, message);
			}
			else
			{
				formatter.WriteWarning(message);
			}
		}
	}

	internal static int RecoverStaleWorkspaces(
		string projectDirectory,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		DateTimeOffset? now = null)
	{
		var rootPath = GetRootPath(projectDirectory);
		if (!Directory.Exists(rootPath))
			return 0;

		var recoveredCount = 0;
		IEnumerable<string> candidates;
		try
		{
			candidates = Directory.EnumerateDirectories(rootPath).ToArray();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Could not inspect stale profile build workspaces under '{rootPath}': {ex.Message}");
			return 0;
		}

		foreach (var candidate in candidates)
		{
			if (TryRecoverStaleWorkspace(candidate, projectDirectory, now ?? DateTimeOffset.UtcNow))
				recoveredCount++;
		}

		if (recoveredCount > 0)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Removed {recoveredCount} stale profile build workspace(s) from '{rootPath}'.");
		}

		return recoveredCount;
	}

	static bool TryRecoverStaleWorkspace(string workspacePath, string projectDirectory, DateTimeOffset now)
	{
		var ownershipPath = System.IO.Path.Combine(workspacePath, OwnershipFileName);
		if (!File.Exists(ownershipPath))
			return false;

		ProfileBuildWorkspaceOwnership? ownership;
		try
		{
			using var ownershipLease = new FileStream(
				ownershipPath,
				FileMode.Open,
				FileAccess.ReadWrite,
				FileShare.None);
			ownership = JsonSerializer.Deserialize<ProfileBuildWorkspaceOwnership>(ownershipLease);
		}
		catch (IOException)
		{
			return false;
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or JsonException)
		{
			return false;
		}

		if (ownership is null || !IsOwnedWorkspace(ownership, workspacePath, projectDirectory))
			return false;

		if (ownership.CreatedAtUtc > now || now - ownership.CreatedAtUtc < StaleWorkspaceMinimumAge)
			return false;

		var ownerIsRunning = IsOwnerProcessRunning(ownership);
		if (ownerIsRunning is not false)
			return false;

		try
		{
			Directory.Delete(workspacePath, recursive: true);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	static bool IsOwnedWorkspace(
		ProfileBuildWorkspaceOwnership? ownership,
		string workspacePath,
		string projectDirectory)
	{
		if (ownership is null
			|| ownership.Kind != OwnershipKind
			|| ownership.Version != OwnershipVersion
			|| string.IsNullOrWhiteSpace(ownership.SessionId)
			|| ownership.ProcessId <= 0)
		{
			return false;
		}

		try
		{
			var expectedWorkspacePath = System.IO.Path.GetFullPath(workspacePath);
			var expectedProjectDirectory = System.IO.Path.GetFullPath(projectDirectory);
			return string.Equals(ownership.SessionId, System.IO.Path.GetFileName(expectedWorkspacePath), PathComparison)
				&& string.Equals(System.IO.Path.GetFullPath(ownership.WorkspacePath), expectedWorkspacePath, PathComparison)
				&& string.Equals(System.IO.Path.GetFullPath(ownership.ProjectDirectory), expectedProjectDirectory, PathComparison)
				&& string.Equals(
					System.IO.Path.GetDirectoryName(expectedWorkspacePath),
					GetRootPath(expectedProjectDirectory),
					PathComparison);
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			return false;
		}
	}

	static bool? IsOwnerProcessRunning(ProfileBuildWorkspaceOwnership ownership)
	{
		try
		{
			using var process = Process.GetProcessById(ownership.ProcessId);
			if (process.HasExited)
				return false;

			if (ownership.ProcessStartTimeUtcTicks is not { } expectedStartTimeUtcTicks)
				return null;

			return TryGetProcessStartTimeUtcTicks(process) == expectedStartTimeUtcTicks;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch
		{
			return null;
		}
	}

	static long? TryGetProcessStartTimeUtcTicks(Process process)
	{
		try
		{
			return process.StartTime.ToUniversalTime().Ticks;
		}
		catch
		{
			return null;
		}
	}

	static string GetRootPath(string projectDirectory)
		=> System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDirectory, "obj", RootDirectoryName));

	void WriteIsolationProps()
	{
		File.WriteAllText(
			IsolationPropsPath,
			$"""
			<Project>
			  <PropertyGroup>
			    <UseArtifactsOutput>true</UseArtifactsOutput>
			    <UseArtifactsIntermediateOutput>true</UseArtifactsIntermediateOutput>
			    <IncludeProjectNameInArtifactsPaths>true</IncludeProjectNameInArtifactsPaths>
			    <_MauiProfileProjectIdentityPath>$(MSBuildProjectFullPath)</_MauiProfileProjectIdentityPath>
			    <_MauiProfileProjectIdentityPath Condition="$([MSBuild]::IsOSPlatform('osx')) and $([System.String]::Copy('$(_MauiProfileProjectIdentityPath)').StartsWith('/private/'))">$(_MauiProfileProjectIdentityPath.Substring(8))</_MauiProfileProjectIdentityPath>
			    <_MauiProfileProjectIdentityPath Condition="$([MSBuild]::IsOSPlatform('windows'))">$(_MauiProfileProjectIdentityPath.ToUpperInvariant())</_MauiProfileProjectIdentityPath>
			    <ArtifactsProjectName>$(MSBuildProjectName)-$([MSBuild]::StableStringHash($(_MauiProfileProjectIdentityPath)).ToString("X8"))</ArtifactsProjectName>
			    <BaseOutputPath>$([MSBuild]::NormalizeDirectory('$(ArtifactsPath)', 'bin', '$(ArtifactsProjectName)'))</BaseOutputPath>
			    <BaseIntermediateOutputPath>$([MSBuild]::NormalizeDirectory('$(ArtifactsPath)', 'obj', '$(ArtifactsProjectName)'))</BaseIntermediateOutputPath>
			    <MSBuildProjectExtensionsPath>$(BaseIntermediateOutputPath)</MSBuildProjectExtensionsPath>
			    <RestoreOutputPath>$(MSBuildProjectExtensionsPath)</RestoreOutputPath>
			    <OutputPath />
			    <IntermediateOutputPath />
			  </PropertyGroup>
			</Project>
			""");

		File.WriteAllText(
			DirectoryBuildPropsPath,
			$"""
			<Project>
			  <PropertyGroup>
			    <_MauiProfileOriginalDirectoryBuildPropsBasePath>$([MSBuild]::GetDirectoryNameOfFileAbove('$(MSBuildProjectDirectory)', 'Directory.Build.props'))</_MauiProfileOriginalDirectoryBuildPropsBasePath>
			    <_MauiProfileOriginalDirectoryBuildPropsPath Condition="'$(_MauiProfileOriginalDirectoryBuildPropsBasePath)' != ''">$([System.IO.Path]::Combine('$(_MauiProfileOriginalDirectoryBuildPropsBasePath)', 'Directory.Build.props'))</_MauiProfileOriginalDirectoryBuildPropsPath>
			  </PropertyGroup>
			  <Import Project="$(_MauiProfileOriginalDirectoryBuildPropsPath)"
			          Condition="'$(_MauiProfileOriginalDirectoryBuildPropsPath)' != '' and Exists('$(_MauiProfileOriginalDirectoryBuildPropsPath)')" />
			  <Import Project="{SecurityElement.Escape(IsolationPropsPath)}" />
			</Project>
			""");
	}

	static StringComparison PathComparison =>
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: true);
		}
		catch
		{
			// A later invocation will only remove the directory if it has valid ownership metadata.
		}
	}
}

internal sealed record ProfileBuildWorkspaceOwnership
{
	public required string Kind { get; init; }
	public required int Version { get; init; }
	public required string SessionId { get; init; }
	public required string WorkspacePath { get; init; }
	public required string ProjectDirectory { get; init; }
	public required int ProcessId { get; init; }
	public long? ProcessStartTimeUtcTicks { get; init; }
	public required DateTimeOffset CreatedAtUtc { get; init; }
}
