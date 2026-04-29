using System.CommandLine;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace Microsoft.Maui.Cli.DevFlow.Skills;

internal static class DevFlowSkillCommands
{
    public static Command CreateInitCommand(Option<bool> jsonOption, Option<bool> noJsonOption, IDevFlowOutputWriter output)
    {
        var scopeOption = CreateScopeOption("project");
        var targetOption = CreateTargetOption();
        var pathOption = CreatePathOption();
        var forceOption = new Option<bool>("--force", "-y") { Description = "Force replacement, including skills installed by a newer CLI" };
        var allowDowngradeOption = new Option<bool>("--allow-downgrade") { Description = "Allow replacing skills installed by a newer CLI" };
        var interactiveOption = CreateInteractiveOption();

        var command = new Command("init", "Initialize MAUI DevFlow skills for this workspace")
        {
            scopeOption,
            targetOption,
            pathOption,
            forceOption,
            allowDowngradeOption,
            interactiveOption
        };

        command.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.InstallRecommendedAsync(
                ctx.GetValue(scopeOption)!,
                ctx.GetValue(targetOption)!,
                ctx.GetValue(pathOption),
                ctx.GetValue(forceOption),
                ctx.GetValue(allowDowngradeOption),
                CreateConfirmation(ctx.GetValue(interactiveOption)),
                ct);

            output.WriteResult(result, isJson, PrintInitSummary);
        });

        return command;
    }

    public static Command CreateSkillsCommand(Option<bool> jsonOption, Option<bool> noJsonOption, IDevFlowOutputWriter output)
    {
        var skillsCommand = new Command("skills", "Install, check, update, and remove MAUI DevFlow skills");

        var installScopeOption = CreateScopeOption("project");
        var installTargetOption = CreateTargetOption();
        var installPathOption = CreatePathOption();
        var installForceOption = new Option<bool>("--force", "-y") { Description = "Force replacement, including skills installed by a newer CLI" };
        var installAllowDowngradeOption = new Option<bool>("--allow-downgrade") { Description = "Allow replacing skills installed by a newer CLI" };
        var installInteractiveOption = CreateInteractiveOption();
        var installCommand = new Command("install", "Install bundled MAUI DevFlow skills")
        {
            installScopeOption,
            installTargetOption,
            installPathOption,
            installForceOption,
            installAllowDowngradeOption,
            installInteractiveOption
        };
        installCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.InstallAsync(
                ctx.GetValue(installScopeOption)!,
                ctx.GetValue(installTargetOption)!,
                ctx.GetValue(installPathOption),
                ctx.GetValue(installForceOption),
                ctx.GetValue(installAllowDowngradeOption),
                CreateConfirmation(ctx.GetValue(installInteractiveOption)),
                ct);
            output.WriteResult(result, isJson, PrintOperationResults);
        });
        skillsCommand.Add(installCommand);

        var listScopeOption = CreateScopeOption("project", allowAll: true);
        var listTargetOption = CreateTargetOption();
        var listPathOption = CreatePathOption();
        var listCommand = new Command("list", "List MAUI DevFlow skill install status")
        {
            listScopeOption,
            listTargetOption,
            listPathOption
        };
        listCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.ListAsync(ctx.GetValue(listScopeOption)!, ctx.GetValue(listTargetOption)!, ctx.GetValue(listPathOption), ct);
            output.WriteResult(result, isJson, PrintSkillStatuses);
        });
        skillsCommand.Add(listCommand);

        var checkScopeOption = CreateScopeOption("project", allowAll: true);
        var checkTargetOption = CreateTargetOption();
        var checkPathOption = CreatePathOption();
        var onlineOption = new Option<bool>("--online") { Description = "Check for newer CLI packages instead of raw skill file updates (reserved)" };
        var checkCommand = new Command("check", "Check installed MAUI DevFlow skills against the current CLI bundle")
        {
            checkScopeOption,
            checkTargetOption,
            checkPathOption,
            onlineOption
        };
        checkCommand.Aliases.Add("outdated");
        checkCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.CheckAsync(
                ctx.GetValue(checkScopeOption)!,
                ctx.GetValue(checkTargetOption)!,
                ctx.GetValue(checkPathOption),
                ctx.GetValue(onlineOption),
                ct);
            output.WriteResult(result, isJson, PrintSkillStatuses);
        });
        skillsCommand.Add(checkCommand);

        skillsCommand.Add(CreateUpdateCommand("update", hidden: false, jsonOption, noJsonOption, output));

        var removeScopeOption = CreateScopeOption("project", allowAll: true);
        var removeTargetOption = CreateTargetOption();
        var removeForceOption = new Option<bool>("--force", "-y") { Description = "Remove dirty or unmanaged skill files" };
        var removeSkillArg = new Argument<string>("skill-name") { Description = "Skill name to remove" };
        var removeCommand = new Command("remove", "Remove an installed MAUI DevFlow skill")
        {
            removeSkillArg,
            removeScopeOption,
            removeTargetOption,
            removeForceOption
        };
        removeCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.RemoveAsync(
                ctx.GetValue(removeSkillArg)!,
                ctx.GetValue(removeScopeOption)!,
                ctx.GetValue(removeTargetOption)!,
                ctx.GetValue(removeForceOption),
                ct);
            output.WriteResult(result, isJson, PrintOperationResults);
        });
        skillsCommand.Add(removeCommand);

        var doctorScopeOption = CreateScopeOption("project", allowAll: true);
        var doctorTargetOption = CreateTargetOption();
        var doctorPathOption = CreatePathOption();
        var doctorOnlineOption = new Option<bool>("--online") { Description = "Include online CLI update diagnostics when available (reserved)" };
        var doctorCommand = new Command("doctor", "Validate DevFlow skill files, state, scope conflicts, and CLI drift")
        {
            doctorScopeOption,
            doctorTargetOption,
            doctorPathOption,
            doctorOnlineOption
        };
        doctorCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.DoctorAsync(
                ctx.GetValue(doctorScopeOption)!,
                ctx.GetValue(doctorTargetOption)!,
                ctx.GetValue(doctorPathOption),
                ctx.GetValue(doctorOnlineOption),
                ct);
            output.WriteResult(result, isJson, PrintDoctor);
        });
        skillsCommand.Add(doctorCommand);

        return skillsCommand;
    }

    public static Command CreateUpdateCommand(string name, bool hidden, Option<bool> jsonOption, Option<bool> noJsonOption, IDevFlowOutputWriter output)
    {
        var updateScopeOption = CreateScopeOption("project", allowAll: true);
        var updateTargetOption = CreateTargetOption();
        var updatePathOption = CreatePathOption();
        var updateForceOption = new Option<bool>("--force", "-y") { Description = "Force replacement, including skills installed by a newer CLI" };
        var updateAllowDowngradeOption = new Option<bool>("--allow-downgrade") { Description = "Allow replacing skills installed by a newer CLI" };
        var updateInteractiveOption = CreateInteractiveOption();
        var updateCommand = new Command(name, "Update or install MAUI DevFlow skills from the current CLI bundle")
        {
            updateScopeOption,
            updateTargetOption,
            updatePathOption,
            updateForceOption,
            updateAllowDowngradeOption,
            updateInteractiveOption
        };
        updateCommand.Hidden = hidden;
        updateCommand.SetAction(async (ctx, ct) =>
        {
            var isJson = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            var result = await DevFlowSkillManager.UpdateAsync(
                ctx.GetValue(updateScopeOption)!,
                ctx.GetValue(updateTargetOption)!,
                ctx.GetValue(updatePathOption),
                ctx.GetValue(updateForceOption),
                ctx.GetValue(updateAllowDowngradeOption),
                CreateConfirmation(ctx.GetValue(updateInteractiveOption)),
                ct);
            output.WriteResult(result, isJson, PrintOperationResults);
        });

        return updateCommand;
    }

    static Option<string> CreateScopeOption(string defaultScope, bool allowAll = false)
    {
        var option = new Option<string>("--scope")
        {
            Description = allowAll
                ? "Install scope: project, user, both, or all"
                : "Install scope: project, user, or both",
            DefaultValueFactory = _ => defaultScope
        };
        option.AcceptOnlyFromAmong(allowAll
            ? ["project", "user", "both", "all"]
            : ["project", "user", "both"]);
        return option;
    }

    static Option<string> CreateTargetOption()
    {
        var option = new Option<string>("--target")
        {
            Description = "Skill target preset: auto, claude (.claude/skills), github (.github/skills), agent (.agent/skills), or agents (.agents/skills)",
            DefaultValueFactory = _ => "auto"
        };
        option.AcceptOnlyFromAmong("auto", "claude", "github", "agent", "agents");
        return option;
    }

    static Option<string?> CreatePathOption()
        => new Option<string?>("--path")
        {
            Description = "Custom skill directory relative to the selected scope root, for example .agent/skills"
        };

    static Option<bool> CreateInteractiveOption()
        => new Option<bool>("--interactive")
        {
            Description = "Ask before deleting legacy skills or overwriting dirty current skills"
        };

    static Func<DevFlowSkillManager.SkillActionPrompt, bool>? CreateConfirmation(bool interactive)
    {
        if (!interactive)
            return null;

        return prompt =>
        {
            Console.Error.WriteLine($"{prompt.SkillId}: {prompt.Message}");
            Console.Error.WriteLine($"  {prompt.Status}: {prompt.Path}");
            Console.Error.Write($"{prompt.Action} this skill? [y/N] ");
            var response = Console.ReadLine()?.Trim();
            return response is "y" or "Y" or "yes" or "YES" or "Yes";
        };
    }

    internal static void PrintInitSummary(JsonObject result, IAnsiConsole console)
    {
        console.MarkupLine("[green]✓[/] [bold]MAUI DevFlow skills initialized[/]");
        console.WriteLine();
        PrintOperationResults(result, console);
        console.WriteLine();

        console.Write(new Rule("[yellow]Next prompt for your AI agent[/]").LeftJustified());
        console.MarkupLine("  Use the [bold cyan]maui-devflow-onboard[/] skill to add MAUI DevFlow to this project.");
        console.MarkupLine("  Use [bold cyan]maui-devflow-debug[/] after the app is running for build/deploy/inspect/fix loops.");
        console.WriteLine();

        console.MarkupLine("[yellow]Manual fallback:[/]");
        console.MarkupLine($"  Add the DevFlow agent package, call [cyan]{Markup.Escape("builder.AddMauiDevFlowAgent()")}[/] under [grey]{Markup.Escape("#if DEBUG")}[/], build and run the app.");
        console.WriteLine();

        console.MarkupLine("[yellow]After integration, verify with:[/]");
        WriteCommand(console, "maui devflow diagnose");
        WriteCommand(console, "maui devflow wait");
        WriteCommand(console, "maui devflow ui tree --depth 1");
    }

    static void PrintOperationResults(JsonObject result, IAnsiConsole console)
    {
        if (result["results"] is not JsonArray results || results.Count == 0)
        {
            console.MarkupLine("[grey]No skill changes.[/]");
            return;
        }

        foreach (var item in results.OfType<JsonObject>())
        {
            var action = GetString(item, "action") ?? "checked";
            var skillId = GetString(item, "skillId") ?? "unknown";
            var status = GetString(item, "status") ?? "unknown";
            var path = GetString(item, "path") ?? "";
            var pathText = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : $" [grey]{Markup.Escape(path)}[/]";
            console.MarkupLine($"[cyan]{Markup.Escape(skillId)}[/]: [{GetActionColor(action)}]{Markup.Escape(action)}[/] ([{GetStatusColor(status)}]{Markup.Escape(status)}[/]){pathText}");
            var message = GetString(item, "message");
            if (!string.IsNullOrWhiteSpace(message))
                console.MarkupLine($"  [grey]{Markup.Escape(message)}[/]");
        }
    }

    static void PrintSkillStatuses(JsonObject result, IAnsiConsole console)
    {
        if (result["onlineMessage"] is JsonValue onlineMessage)
            console.MarkupLine($"[cyan]ℹ[/] {Markup.Escape(onlineMessage.GetValue<string>())}");

        if (result["skills"] is not JsonArray skills || skills.Count == 0)
        {
            console.MarkupLine("[grey]No DevFlow skills found.[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn(new TableColumn("[cyan]Skill[/]"))
            .AddColumn(new TableColumn("[cyan]Scope[/]"))
            .AddColumn(new TableColumn("[cyan]Status[/]"))
            .AddColumn(new TableColumn("[cyan]Path[/]"));

        foreach (var item in skills.OfType<JsonObject>())
        {
            var skillId = GetString(item, "skillId") ?? "unknown";
            var scope = GetString(item, "scope") ?? "unknown";
            var status = GetString(item, "status") ?? "unknown";
            var path = GetString(item, "path") ?? "";
            table.AddRow(
                Markup.Escape(skillId),
                Markup.Escape(scope),
                $"[{GetStatusColor(status)}]{Markup.Escape(status)}[/]",
                string.IsNullOrWhiteSpace(path) ? string.Empty : $"[grey]{Markup.Escape(path)}[/]");
        }

        console.Write(table);
    }

    static void PrintDoctor(JsonObject result, IAnsiConsole console)
    {
        PrintSkillStatuses(result, console);

        if (result["diagnostics"] is JsonObject diagnostics)
        {
            console.WriteLine();
            console.MarkupLine("[yellow]Diagnostics:[/]");
            foreach (var item in diagnostics)
                console.MarkupLine($"  [cyan]{Markup.Escape(item.Key)}[/]: {Markup.Escape(item.Value?.ToString() ?? string.Empty)}");
        }

        if (result["warnings"] is JsonArray warnings && warnings.Count > 0)
        {
            console.WriteLine();
            console.MarkupLine("[yellow]Warnings:[/]");
            foreach (var warning in warnings)
            {
                var warningText = warning?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(warningText))
                    console.MarkupLine($"  [yellow]⚠[/] {Markup.Escape(warningText)}");
            }
        }
    }

    static void WriteCommand(IAnsiConsole console, string command)
        => console.MarkupLine($"  [blue]{Markup.Escape(command)}[/]");

    static string GetActionColor(string action)
        => action switch
        {
            "written" => "green",
            "unchanged" => "grey",
            "checked" => "cyan",
            "removed" => "red",
            "skipped" => "yellow",
            _ => "white"
        };

    static string GetStatusColor(string status)
        => status switch
        {
            "up-to-date" => "green",
            "missing" => "red",
            "dirty" => "yellow",
            "unknown-or-unmanaged" => "yellow",
            "installed-from-newer-cli" => "yellow",
            "installed-from-different-cli-same-version" => "yellow",
            "update-available-from-current-cli" => "yellow",
            "legacy-managed" => "yellow",
            "removed" => "red",
            _ => "white"
        };

    static string? GetString(JsonObject node, string propertyName)
        => node.TryGetPropertyValue(propertyName, out var value) ? value?.GetValue<string>() : null;
}
