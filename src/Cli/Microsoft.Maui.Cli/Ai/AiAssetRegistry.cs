using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

internal static class AiAssetRegistry
{
	internal static string RegistryPath(string root) => Path.Combine(root, ".maui", "ai-assets.json");

	internal static void Guard(string path, string root)
	{
		if (!FileSystemPathGuard.IsPathWithinRoot(path, root) || FileSystemPathGuard.IsReparsePoint(path))
			throw new IOException($"Unsafe AI asset path: '{path}'.");
	}

	internal static JsonObject Read(string root)
	{
		var path = RegistryPath(root);
		Guard(path, root);
		if (Directory.Exists(path)) throw new IOException("AI asset registry path is a directory.");
		if (!File.Exists(path)) return new() { ["schemaVersion"] = 1, ["installations"] = new JsonArray() };
		var value = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
			?? throw new InvalidOperationException("Invalid AI asset registry.");
		if (value["schemaVersion"]?.GetValue<int>() != 1 || value["installations"] is not JsonArray)
			throw new InvalidOperationException("Unsupported AI asset registry schema.");
		foreach (var node in (JsonArray)value["installations"]!)
			if (node is not JsonObject entry || entry["scope"]?.GetValue<string>() is not ("project" or "user"))
				throw new InvalidOperationException("Invalid AI registry installation scope.");
		return value;
	}

	internal static List<AiAsset> Inventory(string root, string scope)
	{
		if (scope is not ("project" or "user"))
			throw new InvalidOperationException("Invalid requested AI registry scope.");
		var results = new List<AiAsset>();
		foreach (var node in (JsonArray)Read(root)["installations"]!)
		{
			if (node is not JsonObject entry ||
				!Enum.TryParse<AiAssetKind>(entry["kind"]?.GetValue<string>(), true, out var kind) ||
				!Enum.IsDefined(kind) || kind == AiAssetKind.Skill)
				throw new InvalidOperationException("Invalid AI registry installation.");
			var storedScope = entry["scope"]!.GetValue<string>();
			if (storedScope == "user" && kind != AiAssetKind.Mcp)
				throw new InvalidOperationException("The user scope may only own user-wide MCP registrations.");
			var relativePath = entry["path"]?.GetValue<string>() ?? "";
			if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
				throw new InvalidOperationException("Registry destination must be scope-root relative.");
			var path = Path.GetFullPath(Path.Combine(root, relativePath));
			Guard(path, root);
			if (storedScope != scope) continue;
			var item = new AiAsset
			{
				Kind = kind, Name = entry["name"]?.GetValue<string>() ?? "",
				Scope = storedScope, Path = path, EntryKey = entry["entryKey"]?.GetValue<string>(),
				Owner = entry["owner"]?.GetValue<string>() ?? "unknown",
				Origin = AiAssetOrigin.FromJson(entry["origin"] as JsonObject),
				AppliedHash = entry["contentHash"]?.GetValue<string>(), Managed = true
			};
			if (entry["environments"] is JsonArray environments)
				foreach (var environment in environments)
					if (Enum.TryParse<AgentEnvironmentKind>(environment?.GetValue<string>(), out var parsed) && Enum.IsDefined(parsed))
						item.Environments.Add(parsed);
			results.Add(item);
		}
		return results;
	}

	// The lock covers both the content merge and the registry read-modify-write; downloads happen before it.
	internal static async Task ApplyAsync(AiAsset asset, string root, Func<Task> apply, CancellationToken ct)
	{
		var path = RegistryPath(root);
		Guard(path, root);
		var directory = Path.GetDirectoryName(path)!;
		Directory.CreateDirectory(directory);
		Guard(directory, root);
		var lockPath = path + ".lock";
		Guard(lockPath, root);
		using var stateLock = await AcquireAsync(lockPath, ct);
		var state = Read(root);
		var entries = (JsonArray)state["installations"]!;
		var relativePath = Path.GetRelativePath(root, asset.Path).Replace('\\', '/');
		var previous = entries.OfType<JsonObject>().FirstOrDefault(e =>
			string.Equals(e["kind"]?.GetValue<string>(), asset.Kind.ToString(), StringComparison.OrdinalIgnoreCase) &&
			e["scope"]?.GetValue<string>() == asset.Scope &&
			string.Equals(e["name"]?.GetValue<string>(), asset.Name, StringComparison.OrdinalIgnoreCase) && e["path"]?.GetValue<string>() == relativePath &&
			e["entryKey"]?.GetValue<string>() == asset.EntryKey);
		if (asset.Managed != (previous is not null) ||
			previous is not null && previous["contentHash"]?.GetValue<string>() != asset.AppliedHash)
			throw new IOException("Asset ownership changed after planning; retry the command.");
		await apply();
		try
		{
			var environments = asset.Environments.Select(e => e.ToString()).ToHashSet(StringComparer.Ordinal);
			if (previous?["environments"] is JsonArray oldEnvironments)
				foreach (var environment in oldEnvironments)
					if (environment?.GetValue<string>() is { } name) environments.Add(name);
			if (previous is not null) entries.Remove(previous);
			entries.Add(new JsonObject
			{
				["kind"] = asset.Kind.ToString().ToLowerInvariant(), ["name"] = asset.Name,
				["scope"] = asset.Scope, ["path"] = relativePath, ["entryKey"] = asset.EntryKey,
				["environments"] = new JsonArray(environments.Order().Select(e => (JsonNode?)JsonValue.Create(e)).ToArray()),
				["owner"] = asset.Owner, ["origin"] = asset.Origin?.ToJson(), ["contentHash"] = asset.AppliedHash
			});
			if (!await FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(path, root,
				Encoding.UTF8.GetBytes(state.ToJsonString(new JsonSerializerOptions { WriteIndented = true })), ct))
				throw new IOException("Registry write failed.");
		}
		catch (Exception ex)
		{
			throw new IOException("Asset content was applied, but ownership registry could not be saved. Inspect the content before retrying.", ex);
		}
	}

	private static async Task<FileStream> AcquireAsync(string path, CancellationToken ct)
	{
		var started = DateTime.UtcNow;
		while (true)
		{
			ct.ThrowIfCancellationRequested();
			try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
			catch (IOException) when (DateTime.UtcNow - started < TimeSpan.FromSeconds(10)) { await Task.Delay(50, ct); }
		}
	}
}
