using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.DevFlow.Skills;

namespace Microsoft.Maui.Cli.Ai;

internal sealed record AiAssetRequest(string Command, AiAssetKind? Kind,
	Dictionary<AiAssetKind, string[]> Selectors, AgentEnvironmentKind[] Environments,
	string? Repo, string? Branch, bool Force, bool DryRun);

internal sealed class AiAssetService(HttpClient http, string currentDirectory, List<string> messages)
{
	private const string DefaultRepo = "dotnet/maui-labs";
	private const string DefaultBranch = "main";
	internal static readonly string[] BundledNames = ["maui-devflow-onboard", "maui-devflow-debug", "maui-devflow-session-review"];
	private static readonly HashSet<string> RecommendedIdentities = new(StringComparer.OrdinalIgnoreCase)
	{
		"Skill:maui-devflow-onboard", "Skill:maui-devflow-debug", "Skill:maui-devflow-session-review",
		"Skill:maui-current-apis", "Skill:maui-project-structure", "Skill:maui-app-architecture",
		"Skill:maui-ui-patterns", "Skill:maui-unit-testing", "Skill:maui-accessibility",
		"Agent:expert-reviewer", "Mcp:maui-devflow"
	};
	private readonly string projectRoot = AgentEnvironmentDetector.ResolveProjectRoot(currentDirectory);
	private readonly Dictionary<(string Repo, string Branch), List<(string Path, string Type)>> trees = [];
	internal static bool IsRecommended(AiAssetKind kind, string name) => RecommendedIdentities.Contains($"{kind}:{name}");

	private async Task<List<(string Path, string Type)>> TreeAsync(string repo, string branch, CancellationToken ct)
	{
		if (trees.TryGetValue((repo, branch), out var cached)) return cached;
		var tree = await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct)
			?? throw new InvalidOperationException($"Could not read source catalog from {repo}@{branch}.");
		trees.Add((repo, branch), tree);
		return tree;
	}

	internal async Task<List<AiAsset>> PlanAsync(AiAssetRequest request, CancellationToken ct)
	{
		var environments = ResolveEnvironments(request);
		var kinds = request.Selectors.Count > 0 ? request.Selectors.Keys.ToArray() :
			request.Kind is { } kind ? [kind] : Enum.GetValues<AiAssetKind>();
		ValidateSupport(request, environments, kinds);
		if (request.Command == "list")
		{
			var catalog = await CatalogAsync(request, kinds, ct);
			foreach (var asset in catalog)
			{
				asset.Environments.AddRange(environments.Where(e => AgentEnvironmentDetector.Supports(e.Kind, asset.Kind)).Select(e => e.Kind));
				asset.Outcome = "skipped";
			}
			return catalog.Where(a => a.Environments.Count > 0).ToList();
		}
		var inventory = await InventoryAsync(environments, kinds, ct);
		if (request.Command == "status")
		{
			foreach (var item in inventory) { item.Outcome = "skipped"; item.ReasonCode = item.State; }
			return inventory;
		}
		if (request.Command == "update")
		{
			var selected = request.Selectors.Count == 0 ? inventory :
				inventory.Where(a => request.Selectors.TryGetValue(a.Kind, out var names) && names.Contains(a.Name, StringComparer.OrdinalIgnoreCase)).ToList();
			foreach (var (selectedKind, names) in request.Selectors)
				foreach (var name in names)
					if (!selected.Any(a => a.Kind == selectedKind && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && a.Managed && a.State != "missing"))
						throw new InvalidOperationException($"No existing managed {selectedKind.ToString().ToLowerInvariant()} installation eligible for update: '{name}'.");
			foreach (var item in selected)
			{
				if (!item.Managed) { Skip(item, "unmanaged", "Unmanaged assets are never adopted by update, even with --force."); continue; }
				if (item.State == "missing") { Skip(item, "skipped-missing", "Tracked installation is missing; update never restores missing assets."); continue; }
				await PlanExistingAsync(item, request, ct);
			}
			return selected;
		}
		var available = await CatalogAsync(request, kinds, ct);
		foreach (var (selectedKind, names) in request.Selectors)
			foreach (var name in names)
				if (!available.Any(a => a.Kind == selectedKind && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException($"Unknown {selectedKind.ToString().ToLowerInvariant()} '{name}'.");
		if (request.Selectors.Count > 0)
			available = available.Where(a => request.Selectors.TryGetValue(a.Kind, out var names) && names.Contains(a.Name, StringComparer.OrdinalIgnoreCase)).ToList();
		else if (request.Command == "init")
			available = available.Where(a => a.Recommended).ToList();
		var plans = new List<AiAsset>();
		foreach (var source in available)
		{
			foreach (var environment in environments.Where(e => AgentEnvironmentDetector.Supports(e.Kind, source.Kind)))
			{
				var path = Destination(source, environment);
				var scope = source.Kind == AiAssetKind.Mcp && environment.Kind == AgentEnvironmentKind.CopilotCli ? "user" : "project";
				var existingPlan = plans.FirstOrDefault(a => a.Kind == source.Kind && a.Name == source.Name && SamePath(a.Path, path));
				if (existingPlan is not null) { existingPlan.Environments.Add(environment.Kind); continue; }
				var installed = inventory.FirstOrDefault(a => a.Kind == source.Kind && a.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase) && a.Scope == scope &&
					SamePath(a.Path, path) && a.EntryKey == source.EntryKey);
				var item = new AiAsset
				{
					Kind = source.Kind, Name = source.Name, Path = path, EntryKey = source.EntryKey,
					Scope = scope,
					Environment = environment, Owner = source.Owner, Origin = source.Origin, Skill = source.Skill,
					Managed = installed?.Managed ?? false, AppliedHash = installed?.AppliedHash,
					CurrentHash = installed?.CurrentHash, State = installed?.State ?? "missing", SkillVersion = installed?.SkillVersion
				};
				item.Environments.Add(environment.Kind);
				AiAssetRegistry.Guard(path, ScopeRoot(item));
				if (item.Kind == AiAssetKind.Skill ? File.Exists(path) : Directory.Exists(path))
				{
					Block(item, "invalid-destination", "An incompatible file or directory occupies the asset destination.", requiresForce: false);
					plans.Add(item);
					continue;
				}
				if (item.Kind == AiAssetKind.Skill && item.Owner == "devflow")
					await PlanBundledAsync(item, request, ct);
				else
				{
					await PrepareSourceAsync(item, request, ct);
					Classify(item, request);
				}
				plans.Add(item);
			}
		}
		return plans;
	}

	private List<DetectedEnvironment> ResolveEnvironments(AiAssetRequest request)
	{
		var detected = AgentEnvironmentDetector.Detect(currentDirectory);
		if (request.Environments.Length > 0)
			return request.Environments.Select(kind => detected.FirstOrDefault(e => e.Kind == kind) ?? AgentEnvironmentDetector.Canonical(kind, projectRoot)).ToList();
		foreach (var (root, scope) in new[] { (projectRoot, "project"), (AgentEnvironmentDetector.UserHome, "user") })
		{
			if (!File.Exists(AiAssetRegistry.RegistryPath(root))) continue;
			foreach (var tracked in AiAssetRegistry.Inventory(root, scope))
				foreach (var kind in tracked.Environments)
				{
					if (detected.Any(e => e.Kind == kind)) continue;
					var environment = AgentEnvironmentDetector.Canonical(kind, projectRoot);
					if (tracked.Kind == AiAssetKind.Mcp) environment.McpConfigPath = tracked.Path;
					detected.Add(environment);
				}
		}
		if (detected.Count == 0)
			throw new InvalidOperationException("No agent environments detected. Use --env to select an environment explicitly.");
		return detected;
	}

	private void ValidateSupport(AiAssetRequest request, List<DetectedEnvironment> environments, AiAssetKind[] kinds)
	{
		var explicitAssets = request.Kind is not null || request.Selectors.Count > 0;
		foreach (var kind in kinds)
		{
			foreach (var environment in environments.Where(e => !AgentEnvironmentDetector.Supports(e.Kind, kind)))
			{
				if (explicitAssets && request.Environments.Length > 0)
					throw new InvalidOperationException($"{environment.Kind} does not support {kind.ToString().ToLowerInvariant()} assets.");
				messages.Add($"Excluded {kind.ToString().ToLowerInvariant()} for {environment.Kind}: unsupported asset kind.");
			}
			if (explicitAssets && !environments.Any(e => AgentEnvironmentDetector.Supports(e.Kind, kind)))
				throw new InvalidOperationException($"No supported detected environment for {kind.ToString().ToLowerInvariant()}; use --env.");
		}
	}

	private async Task<List<AiAsset>> CatalogAsync(AiAssetRequest request, AiAssetKind[] kinds, CancellationToken ct)
	{
		var repo = request.Repo ?? DefaultRepo;
		var branch = request.Branch ?? DefaultBranch;
		var result = new List<AiAsset>();
		if (kinds.Contains(AiAssetKind.Skill))
			result.AddRange(BundledNames.Select(name => new AiAsset { Kind = AiAssetKind.Skill, Name = name, Owner = "devflow" }));
		if (kinds.Contains(AiAssetKind.Mcp))
			result.Add(new AiAsset { Kind = AiAssetKind.Mcp, Name = "maui-devflow", Owner = "maui-cli", EntryKey = "maui-devflow" });
		var needSkills = kinds.Contains(AiAssetKind.Skill) &&
			(!request.Selectors.TryGetValue(AiAssetKind.Skill, out var skillNames) || skillNames.Any(n => !BundledNames.Contains(n, StringComparer.OrdinalIgnoreCase)));
		var needAgents = kinds.Contains(AiAssetKind.Agent);
		if (!needSkills && !needAgents) return result;
		var tree = await TreeAsync(repo, branch, ct);
		if (needSkills)
		{
			var skills = new List<SkillInfo>();
			var marketplace = await MarketplaceClient.GetMarketplaceAsync(http, repo, branch, ct);
			if (marketplace is not null)
				foreach (var pluginEntry in marketplace.Plugins)
				{
					var plugin = await MarketplaceClient.GetPluginAsync(http, repo, branch, pluginEntry.Source, ct);
					if (plugin is not null)
						skills.AddRange(await MarketplaceClient.GetSkillsAsync(http, repo, branch, plugin, pluginEntry.Source, tree, ct));
				}
			skills.AddRange(await MarketplaceClient.GetSkillsFromDirectoryAsync(http, repo, branch, ".github/skills", "dotnet-maui-repo", tree, ct));
			foreach (var skill in skills.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
				if (!Commands.AiCommands.IsDevFlowManagedSkillName(skill.Name))
					result.Add(new AiAsset { Kind = AiAssetKind.Skill, Name = skill.Name, Origin = new(repo, branch, skill.RemotePath), Skill = skill });
		}
		if (needAgents)
			foreach (var agent in (await RepositoryAssetInstaller.GetCopilotAgentsAsync(http, repo, branch, tree, ct))
				.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
				result.Add(new AiAsset { Kind = AiAssetKind.Agent, Name = agent.Name, Origin = new(repo, branch, agent.RemotePath) });
		return result;
	}

	private string Destination(AiAsset source, DetectedEnvironment environment) => source.Kind switch
	{
		AiAssetKind.Skill => Path.Combine(environment.SkillsDirectory, ValidSkillName(source.Name)),
		AiAssetKind.Agent => Path.Combine(projectRoot, ".github", "agents", Path.GetFileName(source.Origin!.Path)),
		_ => environment.McpConfigPath
	};

	private static string ValidSkillName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Contains("..") || name.Contains('/') || name.Contains('\\') ||
			name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || (OperatingSystem.IsWindows() && SkillInstaller.IsWindowsReservedName(name)))
			throw new InvalidOperationException("Invalid skill name.");
		return name;
	}

	private string ScopeRoot(AiAsset item) => item.Scope == "user" ? AgentEnvironmentDetector.UserHome : projectRoot;
	private static bool SamePath(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	private (string Target, string? CustomPath) BundledTarget(DetectedEnvironment environment)
	{
		var relative = Path.GetRelativePath(projectRoot, environment.SkillsDirectory);
		if (SamePath(relative, Path.Combine(".claude", "skills"))) return ("claude", null);
		if (SamePath(relative, Path.Combine(".github", "skills"))) return ("github", null);
		return ("auto", relative);
	}

	private async Task<List<AiAsset>> InventoryAsync(List<DetectedEnvironment> environments, AiAssetKind[] kinds, CancellationToken ct)
	{
		var items = new List<AiAsset>();
		var projectRegistry = kinds.Any(k => k != AiAssetKind.Skill) ? AiAssetRegistry.Inventory(projectRoot, "project") : [];
		var userRegistry = kinds.Contains(AiAssetKind.Mcp) && environments.Any(e => e.Kind == AgentEnvironmentKind.CopilotCli)
			? AiAssetRegistry.Inventory(AgentEnvironmentDetector.UserHome, "user") : [];
		foreach (var tracked in projectRegistry.Concat(userRegistry).Where(a => kinds.Contains(a.Kind)))
		{
			var matching = environments.Where(e => AgentEnvironmentDetector.Supports(e.Kind, tracked.Kind) &&
				(tracked.Kind == AiAssetKind.Agent
					? tracked.Scope == "project" && SamePath(Path.GetDirectoryName(tracked.Path)!, Path.Combine(projectRoot, ".github", "agents"))
					: tracked.Environments.Contains(e.Kind) && tracked.Scope == (e.Kind == AgentEnvironmentKind.CopilotCli ? "user" : "project"))).ToList();
			if (matching.Count == 0) continue;
			tracked.Environments.Clear(); tracked.Environments.AddRange(matching.Select(e => e.Kind));
			tracked.Environment = matching[0];
			AiAssetRegistry.Guard(tracked.Path, ScopeRoot(tracked));
			if (tracked.Kind == AiAssetKind.Agent)
				tracked.CurrentHash = File.Exists(tracked.Path) ? AiContentHash.Bytes(await File.ReadAllBytesAsync(tracked.Path, ct)) : null;
			else
			{
				tracked.Environment = new() { Kind = matching[0].Kind, SkillsDirectory = matching[0].SkillsDirectory, McpConfigPath = tracked.Path };
				tracked.CurrentHash = McpConfigurator.InspectOwnedHash(tracked.Environment, ScopeRoot(tracked), tracked.EntryKey ?? tracked.Name);
			}
			tracked.State = CurrentState(tracked);
			if (tracked.Kind == AiAssetKind.Mcp && tracked.State == "configured" && tracked.Name == "maui-devflow" &&
				tracked.CurrentHash != McpConfigurator.OwnedHash(McpConfigurator.OwnedDefinition(tracked.Environment!.Kind)))
				tracked.State = "update-available";
			items.Add(tracked);
		}
		if (kinds.Contains(AiAssetKind.Skill))
			foreach (var group in environments.GroupBy(e => e.SkillsDirectory, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
			{
				var environment = group.First();
				AiAssetRegistry.Guard(environment.SkillsDirectory, projectRoot);
				if (Directory.Exists(environment.SkillsDirectory))
					foreach (var directory in Directory.EnumerateDirectories(environment.SkillsDirectory).Order(StringComparer.Ordinal))
					{
						AiAssetRegistry.Guard(directory, projectRoot);
						var name = Path.GetFileName(directory);
						var currentHash = AiContentHash.DirectoryHash(directory);
						if (BundledNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
						AiAssetRegistry.Guard(Path.Combine(directory, ".skill-version"), projectRoot);
						var version = await SkillVersionStore.ReadAsync(directory, ct);
						var item = new AiAsset
						{
							Kind = AiAssetKind.Skill, Name = name, Path = directory, Environment = environment,
							Managed = version is not null, AppliedHash = version?.ContentHash,
							Origin = version is null ? null : new(version.Source ?? "", version.Branch ?? "", version.PluginPath ?? ""),
							SkillVersion = version, CurrentHash = currentHash
						};
						item.Environments.AddRange(group.Select(e => e.Kind));
						item.State = CurrentState(item); items.Add(item);
					}
				var (target, customPath) = BundledTarget(environment);
				var bundled = await DevFlowSkillManager.CheckAsync("project", target, customPath, false, ct, recordCheck: false);
				foreach (var row in ((JsonArray)bundled["skills"]!).OfType<JsonObject>().Where(r => BundledNames.Contains(r["skillId"]?.GetValue<string>() ?? "")))
				{
					var name = row["skillId"]!.GetValue<string>();
					var managed = row["contentHash"] is not null;
					var path = Path.Combine(environment.SkillsDirectory, name);
					if (!managed && !Directory.Exists(path)) continue;
					var item = new AiAsset
					{
						Kind = AiAssetKind.Skill, Name = name, Path = path, Owner = "devflow", Environment = environment,
						Managed = managed, AppliedHash = row["contentHash"]?.GetValue<string>(),
						State = row["status"]!.GetValue<string>()
					};
					item.Environments.AddRange(group.Select(e => e.Kind)); items.Add(item);
				}
			}
		if (kinds.Contains(AiAssetKind.Agent) && environments.Any(e => AgentEnvironmentDetector.Supports(e.Kind, AiAssetKind.Agent)))
		{
			var directory = Path.Combine(projectRoot, ".github", "agents");
			AiAssetRegistry.Guard(directory, projectRoot);
			if (Directory.Exists(directory))
				foreach (var path in Directory.EnumerateFiles(directory, "*.agent.md"))
				{
					AiAssetRegistry.Guard(path, projectRoot);
					if (items.Any(a => a.Kind == AiAssetKind.Agent && SamePath(a.Path, path))) continue;
					var (name, _) = MarketplaceClient.ParseFrontmatter(await File.ReadAllTextAsync(path, ct));
					var item = new AiAsset { Kind = AiAssetKind.Agent, Name = name ?? Path.GetFileName(path)[..^9], Path = path, State = "unmanaged", CurrentHash = AiContentHash.Bytes(await File.ReadAllBytesAsync(path, ct)) };
					item.Environments.AddRange(environments.Where(e => AgentEnvironmentDetector.Supports(e.Kind, AiAssetKind.Agent)).Select(e => e.Kind));
					item.Environment = environments.First(e => AgentEnvironmentDetector.Supports(e.Kind, AiAssetKind.Agent));
					items.Add(item);
				}
		}
		if (kinds.Contains(AiAssetKind.Mcp))
			foreach (var environment in environments)
			{
				var root = environment.Kind == AgentEnvironmentKind.CopilotCli ? AgentEnvironmentDetector.UserHome : projectRoot;
				foreach (var name in McpConfigurator.ConfiguredNames(environment, root))
				{
					if (items.Any(a => a.Kind == AiAssetKind.Mcp && SamePath(a.Path, environment.McpConfigPath) && a.EntryKey == name)) continue;
					var item = new AiAsset
					{
						Kind = AiAssetKind.Mcp, Name = name, EntryKey = name, Owner = "maui-cli", Path = environment.McpConfigPath,
						Scope = environment.Kind == AgentEnvironmentKind.CopilotCli ? "user" : "project", Environment = environment,
						State = "configured-unmanaged", CurrentHash = McpConfigurator.InspectOwnedHash(environment, root, name)
					};
					item.Environments.Add(environment.Kind); items.Add(item);
				}
			}
		return items;
	}

	private static string CurrentState(AiAsset item) => item.CurrentHash is null ? "missing" :
		!item.Managed ? "unmanaged" : item.AppliedHash is null ? "uncheckable" :
		item.CurrentHash != item.AppliedHash ? "customized" : item.Kind == AiAssetKind.Mcp ? "configured" : "installed";

	private async Task PlanExistingAsync(AiAsset item, AiAssetRequest request, CancellationToken ct)
	{
		if (item.Owner == "devflow") { await PlanBundledAsync(item, request, ct); return; }
		if (item.Kind != AiAssetKind.Mcp && item.Origin?.IsComplete != true)
		{
			Block(item, "unknown-origin", "Required recorded source repository, branch or path is missing.", requiresForce: false);
			return;
		}
		if (item.Kind == AiAssetKind.Mcp && (item.Name != "maui-devflow" || item.EntryKey != "maui-devflow" || item.Owner != "maui-cli"))
		{
			Block(item, "unknown-origin", "No known managed MCP definition for this installation.", requiresForce: false); return;
		}
		if (item.Origin is { } origin)
			item.Origin = origin with { Repo = request.Repo ?? origin.Repo, Branch = request.Branch ?? origin.Branch };
		await PrepareSourceAsync(item, request, ct);
		Classify(item, request);
	}

	private async Task PrepareSourceAsync(AiAsset item, AiAssetRequest request, CancellationToken ct)
	{
		if (item.Kind == AiAssetKind.Mcp) return;
		var origin = item.Origin!;
		if (item.Kind == AiAssetKind.Agent)
		{
			item.Content = await MarketplaceClient.FetchRawBytesAsync(http, origin.Repo, origin.Branch, origin.Path, ct)
				?? throw new InvalidOperationException($"Could not download agent '{item.Name}' from its recorded source.");
			return;
		}
		if (item.Skill is null)
		{
			var tree = await TreeAsync(origin.Repo, origin.Branch, ct);
			var prefix = MarketplaceClient.NormalizePath(origin.Path) + "/";
			item.Skill = new SkillInfo { Name = item.Name, RemotePath = origin.Path,
				Files = tree.Where(e => e.Type == "blob" && e.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e.Path).ToList() };
			if (!item.Skill.Files.Contains(prefix + "SKILL.md"))
				throw new InvalidOperationException($"Recorded skill source '{origin.Path}' is unavailable.");
		}
		var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		var remotePrefix = MarketplaceClient.NormalizePath(origin.Path) + "/";
		foreach (var file in item.Skill.Files)
		{
			var normalized = MarketplaceClient.NormalizePath(file);
			if (!normalized.StartsWith(remotePrefix, StringComparison.Ordinal))
				throw new InvalidOperationException("Source skill file is outside its recorded path.");
			var relative = normalized[remotePrefix.Length..];
			if (relative == ".skill-version") continue;
			files.Add(relative, await MarketplaceClient.FetchRawBytesAsync(http, origin.Repo, origin.Branch, normalized, ct)
				?? throw new InvalidOperationException($"Could not download skill '{item.Name}' completely."));
		}
		if (!files.ContainsKey("SKILL.md")) throw new InvalidOperationException($"Skill '{item.Name}' has no SKILL.md.");
		item.DesiredHash = AiContentHash.FilesHash(files);
		item.PreparedFiles = files;
		item.PreparedCommit = await MarketplaceClient.GetRemoteCommitShaAsync(http, origin.Repo, origin.Branch, origin.Path, ct);
	}

	private void Classify(AiAsset item, AiAssetRequest request)
	{
		string? desired = item.Kind switch
		{
			AiAssetKind.Agent => AiContentHash.Bytes(item.Content!),
			AiAssetKind.Mcp => McpConfigurator.OwnedHash(McpConfigurator.OwnedDefinition(item.Environment!.Kind)),
			_ => item.DesiredHash
		};
		var adoptMatchingContent = request.Force && !item.Managed &&
			request.Command is "init" or "add";
		if (item.CurrentHash is not null && desired == item.CurrentHash && !adoptMatchingContent)
		{
			Skip(item, item.Managed ? "already-current" : item.Kind == AiAssetKind.Mcp ? "already-configured-unmanaged" : "already-current-unmanaged",
				item.Managed ? "Already current." : "Matching unmanaged content; ownership was not adopted."); return;
		}
		if (item.CurrentHash is not null && (!item.Managed || item.AppliedHash is null || item.CurrentHash != item.AppliedHash))
		{
			item.RequiresForce = true;
			if (!request.Force)
			{
				Block(item, item.AppliedHash is null && item.Managed ? "uncheckable" : "content-conflict",
					"Existing content is unmanaged, customized, or lacks a recorded content hash; use --force to replace it."); return;
			}
		}
		item.Action = item.CurrentHash is null ? "create" : item.Managed ? "replace" : "adopt";
		item.ReasonCode = item.Action;
		item.Message = item.Kind == AiAssetKind.Mcp ? "Merge owned launch fields only; executable installation and approval are not performed." : "Apply selected asset.";
	}

	private async Task PlanBundledAsync(AiAsset item, AiAssetRequest request, CancellationToken ct)
	{
		var (target, customPath) = BundledTarget(item.Environment!);
		var status = await DevFlowSkillManager.CheckAsync("project", target, customPath, false, ct, recordCheck: false);
		var row = ((JsonArray)status["skills"]!).OfType<JsonObject>().Single(r => r["skillId"]?.GetValue<string>() == item.Name);
		item.State = row["status"]!.GetValue<string>();
		item.Managed = row["contentHash"] is not null;
		item.AppliedHash = row["contentHash"]?.GetValue<string>();
		if (item.State == "up-to-date") { Skip(item, "already-current", "Bundled skill is current."); return; }
		if (item.State == "unknown-or-unmanaged" && !request.Force &&
			await DevFlowSkillManager.MatchesBundledSkillAsync(item.Name, item.Path, ct))
		{
			Skip(item, "already-current-unmanaged", "Matching unmanaged bundled content; ownership was not adopted."); return;
		}
		if (item.State is "dirty" or "unknown-or-unmanaged" or "installed-from-newer-cli")
		{
			item.RequiresForce = true;
			if (!request.Force) { Block(item, "content-conflict", "Bundled skill content requires --force to replace."); return; }
		}
		item.Action = item.State == "missing" ? "create" : item.Managed ? "replace" : "adopt";
		item.ReasonCode = item.Action;
	}

	internal async Task ExecuteAsync(AiAssetRequest request, List<AiAsset> results, CancellationToken ct)
	{
		var failed = false;
		foreach (var item in results)
		{
			if (failed) { item.Outcome = "not-executed"; item.ReasonCode = "previous-action-failed"; continue; }
			if (item.Action == "skip" || item.Outcome != "planned")
			{
				if (item.Outcome == "planned") item.Outcome = "skipped";
				continue;
			}
			try
			{
				AiAssetRegistry.Guard(item.Path, ScopeRoot(item));
				if (item.Kind == AiAssetKind.Skill && item.Owner == "devflow")
				{
					var (target, customPath) = BundledTarget(item.Environment!);
					var result = await DevFlowSkillManager.InstallSkillAsync(item.Name, "project", target, customPath, request.Force, ct);
					if (result["results"] is not JsonArray rows || rows.OfType<JsonObject>().Any(r => r["action"]?.GetValue<string>() == "skipped"))
						throw new IOException("Bundled skill apply was blocked by its owner.");
				}
				else if (item.Kind == AiAssetKind.Skill)
				{
					var currentHash = Directory.Exists(item.Path) ? AiContentHash.DirectoryHash(item.Path) : null;
					if (currentHash != item.CurrentHash) throw new IOException("Skill changed after planning; retry.");
					var result = await SkillInstaller.InstallSkillAsync(http, item.Skill!, item.Environment!, projectRoot, item.Origin!.Repo, item.Origin.Branch, force: true, ct, item.PreparedFiles, item.PreparedCommit);
					if (result.FilesInstalled <= 0) throw new IOException($"Skill installation failed ({result.FilesInstalled}).");
				}
				else
				{
					await AiAssetRegistry.ApplyAsync(item, ScopeRoot(item), async () =>
					{
						if (item.Kind == AiAssetKind.Agent)
						{
							var currentHash = File.Exists(item.Path) ? AiContentHash.Bytes(await File.ReadAllBytesAsync(item.Path, ct)) : null;
							if (currentHash != item.CurrentHash) throw new IOException("Agent changed after planning; retry.");
							if (!await FileSystemPathGuard.WriteFileAtomicallyWithinRootAsync(item.Path, projectRoot, item.Content!, ct))
								throw new IOException("Could not apply agent definition.");
							item.AppliedHash = AiContentHash.Bytes(item.Content!);
						}
						else
						{
							await McpConfigurator.MergeOwnedAsync(item.Environment!, ScopeRoot(item), item.CurrentHash, ct);
							item.AppliedHash = McpConfigurator.OwnedHash(McpConfigurator.OwnedDefinition(item.Environment!.Kind));
						}
					}, ct);
				}
				item.Managed = true; item.Outcome = "succeeded"; item.State = item.Kind == AiAssetKind.Mcp ? "configured" : "installed";
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				item.Outcome = "failed"; item.ReasonCode = "apply-failed"; item.Error = ex.Message; item.Message = ex.Message;
				failed = true;
			}
		}
	}

	private static void Skip(AiAsset item, string reason, string message)
	{
		item.Action = "skip"; item.Outcome = "skipped"; item.ReasonCode = reason; item.Message = message;
	}
	private static void Block(AiAsset item, string reason, string message, bool requiresForce = true)
	{
		item.Action = "skip"; item.Outcome = "blocked"; item.ReasonCode = reason; item.Message = message; item.RequiresForce = requiresForce;
	}
}
