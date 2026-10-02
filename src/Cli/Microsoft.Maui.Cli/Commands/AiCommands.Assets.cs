using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	internal static IAnsiConsole? ConsoleForTests { get; set; }
	internal static bool? InputRedirectedForTests { get; set; }

	private static Command CreateAssetCommand(string operation, AiAssetKind? addKind = null)
	{
		var description = operation switch
		{
			"list" => "List available skills, agents and known MCP registrations.",
			"status" => "Show installed assets and configured MCP inventory.",
			"update" => "Refresh existing managed assets only.",
			"init" => "Install recommendations or the exact union of selected assets.",
			_ => $"Add one named {addKind!.Value.ToString().ToLowerInvariant()} asset only."
		};
		var command = new DetailedHelpCommand(addKind?.ToString().ToLowerInvariant() ?? operation, description);
		command.HelpDetails = operation switch
		{
			"list" => "List available skills, agents and known MCP registrations.",
			"status" => "Show installed assets and configured MCP inventory (not connectivity).",
			"update" => "Refresh existing managed assets only; MCP updates refresh the owned launch definition, not the executable.",
			"init" => "Install the recommended combined setup, or the exact union of --skill, --agent and --mcp selectors.",
			_ => addKind switch
			{
				AiAssetKind.Skill => "Install one named skill in the selected clients' skill directories. No implicit MCP registration, agents, or bundled sibling skills.",
				AiAssetKind.Agent => "Install one project-scoped agent definition in .github/agents for VsCode or CopilotCli. No implicit skills or MCP registration.",
				_ => "Merge the known maui-devflow MCP registration only; do not install or approve an executable. CopilotCli configuration is user-wide (~/.copilot/mcp-config.json). No implicit skills or agents."
			}
		};
		command.HelpDetails += "\n\nExamples:\n" + (operation switch
		{
			"init" => "  maui ai init --env VsCode --dry-run\n  maui ai init --skill maui-devflow-debug --env Claude --yes",
			"list" => "  maui ai list --env VsCode\n  maui ai list skill --env Claude --json",
			"status" => "  maui ai status --env VsCode\n  maui ai status mcp --env CopilotCli --json",
			"update" => "  maui ai update --env VsCode --dry-run\n  maui ai update skill --skill maui-devflow-debug --env Claude --yes",
			_ => addKind switch
			{
				AiAssetKind.Skill => "  maui ai add skill maui-devflow-debug --env Claude --yes",
				AiAssetKind.Agent => "  maui ai add agent expert-reviewer --env VsCode --dry-run",
				_ => "  maui ai add mcp maui-devflow --env CopilotCli --dry-run"
			}
		}) + "\n\nGlobal flags: --dry-run previews without writes; --json emits one result envelope; --ci disables prompts. " +
			(operation is "init" or "add" or "update" ? "--yes accepts confirmation, not conflicts. " : "") +
			"No-target automation must supply --env; configuration markers do not prove executables are installed." +
			(operation is "list" or "status" or "update" ? "\n[type] is optional: skill, agent, or mcp. Omit it to include all applicable kinds." : "");
		var type = new Argument<string?>("type")
		{
			Arity = ArgumentArity.ZeroOrOne,
			Description = "Optional singular kind: skill, agent, mcp. Omit to include all applicable kinds."
		};
		type.AcceptOnlyFromAmong("skill", "agent", "mcp");
		var name = new Argument<string>("name")
		{
			Description = addKind switch
			{
				AiAssetKind.Skill => "Exact catalog skill name, including a single canonical bundled DevFlow skill",
				AiAssetKind.Agent => "Exact catalog agent name; installs one project definition",
				_ => "Known MCP registration name: maui-devflow"
			}
		};
		if (operation == "add") command.Add(name);
		else if (operation != "init") command.Add(type);
		var environmentDescription = addKind == AiAssetKind.Agent
			? "Compatible clients: VsCode, CopilotCli (repeatable); both share project .github/agents."
			: "Clients: Claude, VsCode, CopilotCli, OpenCode (repeatable).";
		environmentDescription += operation is "init" or "add"
			? " Explicit targets may be created on apply; otherwise use detected clients."
			: " Filter existing/configured targets; never create client directories.";
		var env = NamesOption("--env", environmentDescription);
		var skills = NamesOption("--skill", "Select only these exact skill names (repeatable).");
		var agents = NamesOption("--agent", "Select only these exact agent names (repeatable).");
		var mcp = NamesOption("--mcp", "Select only these known MCP registrations (repeatable).");
		command.Add(env);
		if (operation is "init" or "update") { command.Add(skills); command.Add(agents); command.Add(mcp); }
		var repo = new Option<string?>("--repo") { Description = operation == "update"
			? "Source repository override for skills/agents (otherwise preserve recorded origin)"
			: "Source repository for skills/agents (default: dotnet/maui-labs)" };
		var branch = new Option<string?>("--branch", "-b") { Description = operation == "update"
			? "Tracked source branch/ref override for skills/agents; full 40-character commit SHA stays pinned (otherwise preserve origin)"
			: "Tracked source branch/ref for skills/agents (default: main); full 40-character commit SHA stays pinned" };
		if (operation != "status" && addKind != AiAssetKind.Mcp) { command.Add(repo); command.Add(branch); }
		var force = CreateForceOption();
		if (operation == "add" && addKind is AiAssetKind.Agent or AiAssetKind.Mcp)
			force.Description = addKind == AiAssetKind.Mcp
				? "Allow adopting or replacing the MCP registration's owned launch fields; preserve other settings and do not accept prompts"
				: "Allow adopting or replacing the project agent definition; does not accept prompts";
		if (operation == "update")
			force.Description = "Allow replacing customized managed content, including downgrading bundled DevFlow skills to this CLI's version; never adopt unmanaged or restore missing assets, and do not accept prompts";
		var yes = new Option<bool>("--yes", "-y") { Description = "Accept the apply confirmation (prompted by default); --ci/--json also run without prompts. Never authorize content conflicts" };
		if (operation is "init" or "add" or "update") { command.Add(force); command.Add(yes); }
		command.SetAction(async (ParseResult parse, CancellationToken ct) =>
		{
			var dryRun = parse.GetValue(GlobalOptions.DryRunOption);
			var json = parse.GetValue(GlobalOptions.JsonOption);
			var results = new List<AiAsset>();
			var messages = new List<string>();
			var humanPlanShown = false;
			var resolvedEnvironments = new List<DetectedEnvironment>();
			try
			{
				AiAssetKind? kind = addKind;
				var typeValue = operation is "list" or "status" or "update" ? parse.GetValue(type) : null;
				if (typeValue is not null) kind = ParseAssetKind(typeValue);
				var selectors = new Dictionary<AiAssetKind, string[]>();
				if (operation is "init" or "update")
				{
					foreach (var (selectorKind, option) in new[] { (AiAssetKind.Skill, skills), (AiAssetKind.Agent, agents), (AiAssetKind.Mcp, mcp) })
					{
						var values = parse.GetValue(option) ?? [];
						if (values.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Asset selectors cannot be empty.");
						if (values.Length == 0) continue;
						if (kind is not null && kind != selectorKind) throw new InvalidOperationException($"Type {kind} cannot be combined with --{selectorKind.ToString().ToLowerInvariant()}.");
						selectors[selectorKind] = values;
					}
				}
				if (operation == "add")
				{
					var value = parse.GetValue(name);
					if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Asset name cannot be empty.");
					selectors[addKind!.Value] = [value];
				}
				if ((kind == AiAssetKind.Mcp || selectors.Count == 1 && selectors.ContainsKey(AiAssetKind.Mcp)) &&
					(parse.GetValue(repo) is not null || parse.GetValue(branch) is not null))
					throw new InvalidOperationException("--repo and --branch apply only to skills and agents, not MCP registrations.");
				var environmentNames = parse.GetValue(env) ?? [];
				var requestedEnvironments = environmentNames.Select(value =>
					Enum.TryParse<Ai.Models.AgentEnvironmentKind>(value, true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _)
						? parsed : throw new InvalidOperationException($"Unknown environment '{value}'.")).Distinct().ToArray();
				using var http = CreateGitHubHttpClient();
				var service = new AiAssetService(http, Directory.GetCurrentDirectory(), messages);
				var request = new AiAssetRequest(operation, kind, selectors, requestedEnvironments,
					parse.GetValue(repo), parse.GetValue(branch), parse.GetValue(force), dryRun);
				resolvedEnvironments = service.ResolveEnvironments(request);
				if (resolvedEnvironments.Count == 0)
				{
					if (!ShouldPromptForEnvironments(operation, parse.GetValue(GlobalOptions.CiOption), json, dryRun,
						parse.GetValue(yes), InputRedirectedForTests ?? Console.IsInputRedirected))
						throw new AiEnvironmentSelectionException("environment-selection-required", AiAssetService.NoEnvironmentsMessage(request));
					var selected = PickEnvironments(ConsoleForTests);
					if (selected.Length == 0)
						throw new AiEnvironmentSelectionException("environment-selection-cancelled",
							$"Environment selection cancelled or empty; no files were written. Retry with:\n  {AiAssetService.EnvironmentRecoveryCommand(request)}");
					request = request with { Environments = selected };
					resolvedEnvironments = service.ResolveEnvironments(request);
					foreach (var environment in resolvedEnvironments) environment.ReasonCode = "guided-selection";
				}
				if (!json)
				{
					WriteEnvironmentSelection(resolvedEnvironments);
				}
				results = await service.PlanAsync(request, ct, resolvedEnvironments);
				var blocked = results.Any(r => r.Outcome is "blocked" or "failed");
				if (!dryRun && operation is "init" or "add" or "update")
				{
					if (!json)
					{
						WriteHumanAssetPlan(results, messages);
						humanPlanShown = true;
					}
					var actionable = results.Any(r => r.Action != "skip" && r.Outcome == "planned");
					var confirmed = true;
					if (!blocked && actionable && !json && !parse.GetValue(GlobalOptions.CiOption) && !parse.GetValue(yes) &&
						!(InputRedirectedForTests ?? Console.IsInputRedirected))
					{
						confirmed = (ConsoleForTests ?? AnsiConsole.Console).Confirm("Apply the selected AI assets?", defaultValue: true);
					}
					if (!confirmed)
					{
						foreach (var row in results.Where(r => r.Outcome == "planned")) { row.Outcome = "skipped"; row.ReasonCode = "declined"; }
					}
					else if (!blocked) await service.ExecuteAsync(request, results, ct);
					else foreach (var row in results.Where(r => r.Outcome == "planned")) row.Outcome = "not-executed";
				}
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				results.Add(new AiAsset { Kind = addKind ?? AiAssetKind.Skill, Name = "", State = "error", Outcome = "failed", ReasonCode = ex is AiEnvironmentSelectionException selection ? selection.ReasonCode : "preflight-failed", Message = ex.Message, Error = ex.Message });
			}
			var failure = results.Any(r => r.Outcome is "blocked" or "failed");
			var envelope = new JsonObject
			{
				["schemaVersion"] = 1, ["command"] = operation == "add" ? $"add {addKind!.Value.ToString().ToLowerInvariant()}" : operation,
				["dryRun"] = dryRun, ["status"] = failure ? "failed" : "success",
				["environments"] = new JsonArray(resolvedEnvironments.Select(e => (JsonNode)e.ToJson()).ToArray()),
				["results"] = new JsonArray(results.Select(r => (JsonNode)r.ToJson()).ToArray()),
				["messages"] = new JsonArray(messages.Select(m => (JsonNode?)JsonValue.Create(m)).ToArray())
			};
			if (json) Console.WriteLine(envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			else if (!humanPlanShown)
				WriteHumanAssetPlan(results, messages);
			else
			{
				Console.WriteLine("Results:");
				foreach (var row in results)
					Console.WriteLine($"{row.Kind.ToString().ToLowerInvariant()} {row.Name}: {row.Outcome} ({row.ReasonCode}){(row.Error is null ? "" : $" {row.Error}")}");
			}
			return failure ? 1 : 0;
		});
		return command;
	}

	internal static bool ShouldPromptForEnvironments(string operation, bool ci, bool json, bool dryRun, bool yes, bool inputRedirected) =>
		operation == "init" && !ci && !json && !dryRun && !yes && !inputRedirected;

	internal static AgentEnvironmentKind[] PickEnvironments(IAnsiConsole? console = null)
	{
		try
		{
			return (console ?? AnsiConsole.Console).Prompt(new MultiSelectionPrompt<AgentEnvironmentKind>()
				.Title("No configuration markers found. Select your AI clients (space to select; enter to continue; empty selection cancels).")
				.NotRequired()
				.UseConverter(kind => kind == AgentEnvironmentKind.CopilotCli ? "CopilotCli (project skills/agents; user-wide MCP)" : kind.ToString())
				.AddChoices(Enum.GetValues<AgentEnvironmentKind>())).ToArray();
		}
		catch (OperationCanceledException) { return []; }
	}

	internal static void WriteEnvironmentSelection(IEnumerable<DetectedEnvironment> environments)
	{
		Console.WriteLine("Selected AI environments (configuration evidence only; executables were not checked):");
		foreach (var environment in environments)
		{
			Console.WriteLine($"  {environment.Kind}: {environment.ReasonCode}; {(environment.MarkerPath is null ? "no marker" : $"{environment.Scope} marker {environment.MarkerPath}")}");
			Console.WriteLine($"    project skills: {environment.SkillsDirectory}");
			Console.WriteLine($"    {(environment.Kind == AgentEnvironmentKind.CopilotCli ? "user-wide" : "project")} MCP: {environment.McpConfigPath}");
		}
	}

	internal static bool ConfirmAssetPlan(IReadOnlyList<AiAsset> results, IEnumerable<string> messages, IAnsiConsole? console = null)
	{
		WriteHumanAssetPlan(results, messages);
		return (console ?? AnsiConsole.Console).Confirm("Apply the selected AI assets?", defaultValue: true);
	}

	internal static void WriteHumanAssetPlan(IReadOnlyList<AiAsset> results, IEnumerable<string> messages)
	{
		foreach (var message in messages) Console.WriteLine(message);
		if (results.Count > 0) Console.WriteLine("Scope    Kind   Action   Asset / environments / result / destination");
		foreach (var row in results)
			Console.WriteLine($"{row.Scope,-8} {row.Kind.ToString().ToLowerInvariant(),-6} {row.Action,-8} {row.Name}{(row.Recommended ? " (recommended)" : "")} [{string.Join(", ", row.Environments)}] {row.State}: {row.Outcome} ({row.ReasonCode}) {row.Path} {row.Message}");
	}

	internal sealed class AiEnvironmentSelectionException(string reasonCode, string message) : InvalidOperationException(message)
	{
		internal string ReasonCode { get; } = reasonCode;
	}

	private static Option<string[]> NamesOption(string name, string description) => new(name)
	{
		Description = description, Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = true
	};

	private static AiAssetKind ParseAssetKind(string value) => value.ToLowerInvariant() switch
	{
		"skill" => AiAssetKind.Skill, "agent" => AiAssetKind.Agent, "mcp" => AiAssetKind.Mcp,
		_ => throw new InvalidOperationException($"Unknown asset type '{value}'; use skill, agent, or mcp.")
	};
}
