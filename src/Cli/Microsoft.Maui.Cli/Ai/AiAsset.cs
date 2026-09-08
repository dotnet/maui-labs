using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

internal enum AiAssetKind { Skill, Agent, Mcp }

internal sealed record AiAssetOrigin(string Repo, string Branch, string Path)
{
	internal JsonObject ToJson() => new() { ["repo"] = Repo, ["branch"] = Branch, ["path"] = Path };
	internal static AiAssetOrigin? FromJson(JsonObject? value) => value is null ? null :
		new(value["repo"]?.GetValue<string>() ?? "", value["branch"]?.GetValue<string>() ?? "", value["path"]?.GetValue<string>() ?? "");
	internal bool IsComplete => !string.IsNullOrWhiteSpace(Repo) && !string.IsNullOrWhiteSpace(Branch) && !string.IsNullOrWhiteSpace(Path);
}

internal sealed class AiAsset
{
	internal required AiAssetKind Kind { get; init; }
	internal required string Name { get; init; }
	internal List<AgentEnvironmentKind> Environments { get; } = [];
	internal string Scope { get; set; } = "project";
	internal string Path { get; set; } = "";
	internal string? EntryKey { get; set; }
	internal string Owner { get; set; } = "repository";
	internal AiAssetOrigin? Origin { get; set; }
	internal bool Managed { get; set; }
	internal bool Recommended => AiAssetService.IsRecommended(Kind, Name);
	internal string State { get; set; } = "available";
	internal string Action { get; set; } = "skip";
	internal string Outcome { get; set; } = "planned";
	internal string ReasonCode { get; set; } = "available";
	internal string Message { get; set; } = "";
	internal bool RequiresForce { get; set; }
	internal string? Error { get; set; }
	internal string? AppliedHash { get; set; }
	internal string? CurrentHash { get; set; }
	internal string? DesiredHash { get; set; }
	internal SkillInfo? Skill { get; set; }
	internal byte[]? Content { get; set; }
	internal DetectedEnvironment? Environment { get; set; }
	internal InstalledSkillVersion? SkillVersion { get; set; }
	internal IReadOnlyDictionary<string, byte[]>? PreparedFiles { get; set; }
	internal string? PreparedCommit { get; set; }
	internal string Identity => $"{Kind}:{Name}:{Scope}:{Path}:{EntryKey}";

	internal JsonObject ToJson() => new()
	{
		["kind"] = Kind.ToString().ToLowerInvariant(), ["name"] = Name,
		["environments"] = new JsonArray(Environments.Select(e => (JsonNode?)JsonValue.Create(e.ToString())).ToArray()),
		["scope"] = Scope, ["path"] = Path, ["entryKey"] = EntryKey, ["owner"] = Owner,
		["origin"] = Origin?.ToJson(), ["managed"] = Managed, ["recommended"] = Recommended, ["state"] = State, ["action"] = Action,
		["outcome"] = Outcome, ["reasonCode"] = ReasonCode, ["message"] = Message,
		["requiresForce"] = RequiresForce, ["error"] = Error
	};
}

internal static class AiContentHash
{
	internal static string Bytes(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
	internal static string DirectoryHash(string directory)
	{
		var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		Visit(directory);
		return FilesHash(files);

		void Visit(string path)
		{
			if (FileSystemPathGuard.IsReparsePoint(path))
				throw new IOException("Cannot hash symbolic links in an AI asset.");
			foreach (var child in Directory.EnumerateFileSystemEntries(path).OrderBy(p => p, StringComparer.Ordinal))
			{
				if (FileSystemPathGuard.IsReparsePoint(child))
					throw new IOException("Cannot hash symbolic links in an AI asset.");
				if (Directory.Exists(child)) { Visit(child); continue; }
				if (System.IO.Path.GetFileName(child) == ".skill-version") continue;
				files.Add(System.IO.Path.GetRelativePath(directory, child).Replace('\\', '/'), File.ReadAllBytes(child));
			}
		}
	}

	internal static string FilesHash(IReadOnlyDictionary<string, byte[]> files)
	{
		using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		foreach (var (name, contents) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
		{
			hash.AppendData(Encoding.UTF8.GetBytes(name + "\0"));
			hash.AppendData(contents);
			hash.AppendData([0]);
		}
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}
}
