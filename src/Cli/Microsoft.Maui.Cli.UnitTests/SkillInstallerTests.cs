// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class SkillInstallerTests : IDisposable
{
	private readonly string _tempDir;

	public SkillInstallerTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	[Fact]
	public async Task InstallSkillAsync_InvalidName_PathTraversal_ReturnsNegativeOne()
	{
		var skill = new SkillInfo { Name = "../escape" };
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
	}

	[Fact]
	public async Task InstallSkillAsync_InvalidName_PathSeparator_ReturnsNegativeOne()
	{
		var separator = Path.DirectorySeparatorChar.ToString();
		var skill = new SkillInfo { Name = $"bad{separator}name" };
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
	}

	[Theory]
	[InlineData(".")]
	[InlineData("bad/name")]
	[InlineData("bad\\name")]
	public async Task InstallSkillAsync_InvalidName_EdgeCase_ReturnsNegativeOne(string skillName)
	{
		var skill = new SkillInfo { Name = skillName };
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
	}

	[Fact]
	public async Task InstallSkillAsync_SymlinkedSkillsDirectoryOutsideProject_ReturnsNegativeOne()
	{
		var projectRoot = Path.Combine(_tempDir, "project");
		var outsideRoot = Path.Combine(_tempDir, "outside");
		Directory.CreateDirectory(projectRoot);
		Directory.CreateDirectory(outsideRoot);

		if (!TryCreateDirectorySymlink(Path.Combine(projectRoot, ".claude"), outsideRoot))
			return;

		var skill = new SkillInfo
		{
			Name = "safe-name",
			RemotePath = ".github/skills/safe-name",
			Files = [".github/skills/safe-name/SKILL.md"]
		};
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(projectRoot, ".claude", "skills")
		};
		using var http = new HttpClient(new SuccessfulInstallHandler());

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, projectRoot, "owner/repo", "main", force: true);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
		Assert.False(Directory.Exists(Path.Combine(outsideRoot, "skills")));
	}

	[Fact]
	public async Task InstallSkillAsync_ValidName_DoesNotReturnNegativeOne()
	{
		var skill = new SkillInfo
		{
			Name = "valid-skill",
			RemotePath = ".github/plugins/maui/skills/valid-skill",
			Files = ["file1.md"]
		};
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		// Use a mock handler that returns 404 for all requests so no real
		// network calls are made. The install should pass name validation
		// and return 0 or -2 (no files downloaded), but never -1 (invalid name).
		var handler = new NotFoundHandler();
		using var http = new HttpClient(handler);

		var (filesInstalled, _) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.NotEqual(-1, filesInstalled);
	}

	[Fact]
	public async Task InstallSkillAsync_PartialDownload_RollsBackAndReturnsNegativeTwo()
	{
		var skill = new SkillInfo
		{
			Name = "partial-skill",
			RemotePath = ".github/skills/partial-skill",
			Files =
			[
				".github/skills/partial-skill/SKILL.md",
				".github/skills/partial-skill/references/setup.md"
			]
		};
		var skillsDir = Path.Combine(_tempDir, "skills");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir
		};
		var handler = new SelectiveNotFoundHandler("SKILL.md");
		using var http = new HttpClient(handler);

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-2, filesInstalled);
		Assert.Equal(string.Empty, installPath);
		Assert.Empty(Directory.EnumerateFileSystemEntries(skillsDir));
	}

	[Fact]
	public async Task InstallSkillAsync_DownloadThrows_RemovesTempInstallDirectory()
	{
		var skill = new SkillInfo
		{
			Name = "throwing-skill",
			RemotePath = ".github/skills/throwing-skill",
			Files = [".github/skills/throwing-skill/SKILL.md"]
		};
		var skillsDir = Path.Combine(_tempDir, "skills");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir
		};
		using var http = new HttpClient(new ThrowingHandler());

		await Assert.ThrowsAsync<InvalidOperationException>(() => SkillInstaller.InstallSkillAsync(
			http, skill, env, _tempDir, "owner/repo", "main", force: false));

		Assert.Empty(Directory.EnumerateFileSystemEntries(skillsDir));
	}

	[Fact]
	public async Task InstallSkillAsync_Force_ReplacesExistingDirectory()
	{
		var skill = new SkillInfo
		{
			Name = "replaced-skill",
			RemotePath = ".github/skills/replaced-skill",
			Files = [".github/skills/replaced-skill/SKILL.md"]
		};
		var skillsDir = Path.Combine(_tempDir, "skills");
		var installPath = Path.Combine(skillsDir, skill.Name);
		Directory.CreateDirectory(Path.Combine(installPath, "references"));
		await File.WriteAllTextAsync(Path.Combine(installPath, "SKILL.md"), "old content");
		await File.WriteAllTextAsync(Path.Combine(installPath, "references", "old-guide.md"), "stale content");
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = skillsDir
		};
		using var http = new HttpClient(new SuccessfulInstallHandler());

		var (filesInstalled, resultPath) = await SkillInstaller.InstallSkillAsync(
			http, skill, env, _tempDir, "owner/repo", "main", force: true);

		Assert.Equal(1, filesInstalled);
		Assert.Equal(installPath, resultPath);
		Assert.Equal("new content", await File.ReadAllTextAsync(Path.Combine(installPath, "SKILL.md")));
		Assert.True(File.Exists(Path.Combine(installPath, ".skill-version")));
		Assert.False(File.Exists(Path.Combine(installPath, "references", "old-guide.md")));
		Assert.Empty(Directory.EnumerateDirectories(skillsDir, "*.bak"));
		Assert.DoesNotContain(Directory.EnumerateDirectories(skillsDir), path => Path.GetFileName(path).StartsWith($".{skill.Name}.", StringComparison.Ordinal));
	}

	private sealed class NotFoundHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
		}
	}

	private sealed class SelectiveNotFoundHandler(string successfulFileName) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri?.AbsolutePath.EndsWith(successfulFileName, StringComparison.Ordinal) == true)
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
				{
					Content = new ByteArrayContent("content"u8.ToArray())
				});
			}

			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
		}
	}

	private sealed class ThrowingHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("download failed");
		}
	}

	private sealed class SuccessfulInstallHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.RequestUri?.Host == "api.github.com")
			{
				return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
				{
					Content = new StringContent("""[{ "sha": "abc123" }]""")
				});
			}

			return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent("new content"u8.ToArray())
			});
		}
	}

	static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
	{
		try
		{
			Directory.CreateSymbolicLink(linkPath, targetPath);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
			return false;
		}
	}
}
