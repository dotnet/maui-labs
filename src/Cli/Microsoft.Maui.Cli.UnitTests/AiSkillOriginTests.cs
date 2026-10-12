using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class AiSkillOriginTests
{
	[Theory]
	[InlineData(null, null, "custom/skills", "release")]
	[InlineData("override/skills", null, "override/skills", "release")]
	[InlineData(null, "preview", "custom/skills", "preview")]
	[InlineData("dotnet/maui-labs", "main", "dotnet/maui-labs", "main")]
	public async Task Update_PreservesRecordedOriginUnlessExplicitlyOverridden(string? repo, string? branch, string expectedRepo, string expectedBranch)
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "skill", "custom-skill", "--repo", "custom/skills", "--branch", "release", "--env", "Claude")).Exit);
		test.Handler.Revision = "updated";
		var arguments = new List<string> { "update", "skill", "--skill", "custom-skill", "--env", "Claude" };
		if (repo is not null) arguments.AddRange(["--repo", repo]);
		if (branch is not null) arguments.AddRange(["--branch", branch]);
		Assert.Equal(0, (await test.Invoke(arguments.ToArray())).Exit);
		var version = await SkillVersionStore.ReadAsync(Path.Combine(test.Root, ".claude", "skills", "custom-skill"));
		Assert.Equal(expectedRepo, version!.Source);
		Assert.Equal(expectedBranch, version.Branch);
		Assert.Equal(".github/skills/custom-skill", version.PluginPath);
		Assert.NotNull(version.ContentHash);
		Assert.Equal("new-commit", version.Commit);
	}

	[Fact]
	public async Task ManagedSkill_LocalEditRequiresForce_ThenBecomesCurrent()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "skill", "custom-skill", "--env", "Claude")).Exit);
		var directory = Path.Combine(test.Root, ".claude", "skills", "custom-skill");
		var path = Path.Combine(directory, "SKILL.md");
		File.WriteAllText(path, "my changes");
		Assert.Equal(1, (await test.Invoke("update", "skill", "--env", "Claude", "--yes")).Exit);
		Assert.Equal("my changes", File.ReadAllText(path));
		Assert.Equal(0, (await test.Invoke("update", "skill", "--env", "Claude", "--force")).Exit);
		Assert.Equal(AiContentHash.DirectoryHash(directory), (await SkillVersionStore.ReadAsync(directory))!.ContentHash);
		var result = await test.Invoke("update", "skill", "--env", "Claude");
		Assert.Equal("already-current", Assert.Single(result.Result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task Update_IncompleteMetadataDoesNotGuessSource(bool missingSource)
	{
		using var test = new AiCommandFixture();
		var directory = Path.Combine(test.Root, ".claude", "skills", "custom-skill");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "SKILL.md"), "original");
		await SkillVersionStore.WriteAsync(directory, new InstalledSkillVersion
		{
			Name = "custom-skill", Source = missingSource ? null : "custom/skills", Branch = "release",
			PluginPath = missingSource ? ".github/skills/custom-skill" : null
		});
		var before = test.Snapshot();
		var (exit, result) = await test.Invoke("update", "skill", "--env", "Claude", "--force");
		Assert.Equal(1, exit);
		Assert.Equal("unknown-origin", Assert.Single(result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}

	[Fact]
	public async Task Update_OlderMetadataWithoutHashIsConservative()
	{
		using var test = new AiCommandFixture();
		var directory = Path.Combine(test.Root, ".claude", "skills", "custom-skill");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "SKILL.md"), "original");
		await SkillVersionStore.WriteAsync(directory, new InstalledSkillVersion { Name = "custom-skill", Source = "custom/skills", Branch = "release", PluginPath = ".github/skills/custom-skill" });
		var (exit, result) = await test.Invoke("update", "skill", "--env", "Claude");
		Assert.Equal(1, exit);
		Assert.Equal("uncheckable", Assert.Single(result["results"]!.AsArray())!["reasonCode"]!.GetValue<string>());
		Assert.Equal("original", File.ReadAllText(Path.Combine(directory, "SKILL.md")));
	}

	[Fact]
	public async Task Update_UnmanagedIsNeverAdoptedEvenWithForce()
	{
		using var test = new AiCommandFixture();
		var directory = Path.Combine(test.Root, ".claude", "skills", "custom-skill");
		Directory.CreateDirectory(directory);
		File.WriteAllText(Path.Combine(directory, "SKILL.md"), "unmanaged");
		Assert.Equal(0, (await test.Invoke("update", "skill", "--env", "Claude", "--force")).Exit);
		Assert.False(File.Exists(Path.Combine(directory, ".skill-version")));
		Assert.Equal("unmanaged", File.ReadAllText(Path.Combine(directory, "SKILL.md")));
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(1, (await test.Invoke("update", "skill", "--skill", "custom-skill", "--env", "Claude", "--force")).Exit);
	}

	[Fact]
	public async Task Status_IsLocalReadOnlyInventory()
	{
		using var test = new AiCommandFixture();
		Assert.Equal(0, (await test.Invoke("add", "skill", "custom-skill", "--env", "Claude")).Exit);
		test.Handler.Requests.Clear();
		var before = test.Snapshot();
		Assert.Equal(0, (await test.Invoke("status", "skill", "--env", "Claude")).Exit);
		Assert.Empty(test.Handler.Requests);
		Assert.Equal(before, test.Snapshot());
	}
}
