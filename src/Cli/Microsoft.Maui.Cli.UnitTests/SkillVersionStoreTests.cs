// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class SkillVersionStoreTests : IDisposable
{
	private readonly string _tempDir;

	public SkillVersionStoreTests()
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
	public async Task WriteAsync_CreatesSkillVersionFile()
	{
		var skillDir = Path.Combine(_tempDir, "test-skill");
		var version = new InstalledSkillVersion
		{
			Name = "test-skill",
			Commit = "abc123",
			Branch = "main",
			UpdatedAt = "2025-01-01T00:00:00Z",
			Source = "dotnet/maui-labs",
			PluginPath = ".github/plugin/maui"
		};

		await SkillVersionStore.WriteAsync(skillDir, version);

		Assert.True(File.Exists(Path.Combine(skillDir, ".skill-version")));
	}

	[Fact]
	public async Task WriteAsync_CreatesDirectoryIfNotExists()
	{
		var skillDir = Path.Combine(_tempDir, "nested", "dir", "test-skill");
		var version = new InstalledSkillVersion { Name = "test-skill", Commit = "abc123" };

		await SkillVersionStore.WriteAsync(skillDir, version);

		Assert.True(Directory.Exists(skillDir));
		Assert.True(File.Exists(Path.Combine(skillDir, ".skill-version")));
	}

	[Fact]
	public async Task ReadAsync_NonExistentDirectory_ReturnsNull()
	{
		var nonExistent = Path.Combine(_tempDir, "does-not-exist");

		var result = await SkillVersionStore.ReadAsync(nonExistent);

		Assert.Null(result);
	}

	[Fact]
	public async Task ReadAsync_NoVersionFile_ReturnsNull()
	{
		// Directory exists but no .skill-version file
		var result = await SkillVersionStore.ReadAsync(_tempDir);

		Assert.Null(result);
	}

	[Fact]
	public async Task RoundTrip_WriteAndRead_ReturnsSameValues()
	{
		var skillDir = Path.Combine(_tempDir, "roundtrip-skill");
		var version = new InstalledSkillVersion
		{
			Name = "my-skill",
			Commit = "def456",
			Branch = "develop",
			UpdatedAt = "2025-06-15T12:30:00Z",
			Source = "dotnet/maui-labs",
			PluginPath = ".github/plugin/maui"
		};

		await SkillVersionStore.WriteAsync(skillDir, version);
		var result = await SkillVersionStore.ReadAsync(skillDir);

		Assert.NotNull(result);
		Assert.Equal("my-skill", result.Name);
		Assert.Equal("def456", result.Commit);
		Assert.Equal("develop", result.Branch);
		Assert.Equal("2025-06-15T12:30:00Z", result.UpdatedAt);
		Assert.Equal("dotnet/maui-labs", result.Source);
		Assert.Equal(".github/plugin/maui", result.PluginPath);
	}

	[Fact]
	public async Task ReadAsync_LegacyFormat_ReturnsPartialData()
	{
		// Legacy format only has commit, updatedAt, branch (no name/source/pluginPath)
		var skillDir = Path.Combine(_tempDir, "legacy-skill");
		Directory.CreateDirectory(skillDir);
		var legacyJson = """
			{
			  "commit": "old123",
			  "updatedAt": "2024-01-01T00:00:00Z",
			  "branch": "main"
			}
			""";
		await File.WriteAllTextAsync(Path.Combine(skillDir, ".skill-version"), legacyJson);

		var result = await SkillVersionStore.ReadAsync(skillDir);

		Assert.NotNull(result);
		Assert.Equal("old123", result.Commit);
		Assert.Equal("2024-01-01T00:00:00Z", result.UpdatedAt);
		Assert.Equal("main", result.Branch);
		Assert.Null(result.Name);
		Assert.Null(result.Source);
		Assert.Null(result.PluginPath);
	}

	[Fact]
	public async Task ReadAsync_CorruptedJson_ReturnsNull()
	{
		var skillDir = Path.Combine(_tempDir, "corrupted-skill");
		Directory.CreateDirectory(skillDir);
		await File.WriteAllTextAsync(
			Path.Combine(skillDir, ".skill-version"),
			"this is not valid json {{{}}}");

		var result = await SkillVersionStore.ReadAsync(skillDir);

		Assert.Null(result);
	}

	[Fact]
	public async Task ReadAsync_EmptyFile_ReturnsNull()
	{
		var skillDir = Path.Combine(_tempDir, "empty-skill");
		Directory.CreateDirectory(skillDir);
		await File.WriteAllTextAsync(Path.Combine(skillDir, ".skill-version"), "");

		var result = await SkillVersionStore.ReadAsync(skillDir);

		Assert.Null(result);
	}

	[Fact]
	public async Task WriteAsync_ProducesIndentedJson()
	{
		var skillDir = Path.Combine(_tempDir, "indented-skill");
		var version = new InstalledSkillVersion
		{
			Name = "test-skill",
			Commit = "abc123"
		};

		await SkillVersionStore.WriteAsync(skillDir, version);
		var content = await File.ReadAllTextAsync(Path.Combine(skillDir, ".skill-version"));

		// Indented JSON contains newlines and spaces
		Assert.Contains("\n", content);
		Assert.Contains("  ", content);
	}

	[Fact]
	public async Task WriteAsync_RemovesTemporaryWriteDirectory()
	{
		var skillDir = Path.Combine(_tempDir, "atomic-skill");
		var version = new InstalledSkillVersion
		{
			Name = "test-skill",
			Commit = "abc123"
		};

		await SkillVersionStore.WriteAsync(skillDir, version);

		Assert.Empty(Directory.GetDirectories(skillDir, ".maui-ai-write.*.tmp"));
		Assert.True(File.Exists(Path.Combine(skillDir, ".skill-version")));
	}

	[Fact]
	public async Task WriteAsync_NullProperties_AreOmitted()
	{
		var skillDir = Path.Combine(_tempDir, "nulls-skill");
		var version = new InstalledSkillVersion
		{
			Name = "test-skill",
			Commit = "abc123"
			// Branch, UpdatedAt, Source, PluginPath are null
		};

		await SkillVersionStore.WriteAsync(skillDir, version);
		var content = await File.ReadAllTextAsync(Path.Combine(skillDir, ".skill-version"));

		Assert.Contains("name", content);
		Assert.Contains("commit", content);
		Assert.DoesNotContain("branch", content);
		Assert.DoesNotContain("source", content);
		Assert.DoesNotContain("pluginPath", content);
	}
}
