using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Spectre.Console;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal sealed class DevFlowInitOptions
{
    public string? Project { get; init; }
    public bool All { get; init; }
    public bool ForceBlazor { get; init; }
    public bool DisableBlazor { get; init; }
    public bool ForceGtk { get; init; }
    public bool Force { get; init; }
    public string? NewTemplate { get; init; }
    public string? NewName { get; init; }
    public bool NoAi { get; init; }
    public string? AiHost { get; init; }
    public bool AiLocalOnly { get; init; }
    public bool Json { get; init; }
    public bool NoJson { get; init; }
    public bool DryRun { get; init; }
    public bool Ci { get; init; }
}

internal static class DevFlowInitCommand
{
    [RequiresUnreferencedCode("DevFlow init uses MSBuild evaluation/mutation which relies on reflection-heavy code paths.")]
    [RequiresDynamicCode("DevFlow init uses MSBuild evaluation/mutation which relies on reflection-heavy code paths.")]
    public static async Task<bool> ExecuteAsync(DevFlowInitOptions options, IDevFlowOutputWriter output, CancellationToken cancellationToken = default)
    {
        var manifest = DevFlowInitManifestLoader.Load();
        var workspaceRoot = Directory.GetCurrentDirectory();
        var reportPath = Path.Combine(workspaceRoot, "MAUI-DEVFLOW-INIT-REPORT.md");
        var jsonReportPath = Path.Combine(workspaceRoot, "MAUI-DEVFLOW-INIT-REPORT.json");
        var json = output.ResolveJsonMode(options.Json, options.NoJson);
        var interactive = !options.Ci && !json && !Console.IsInputRedirected && !Console.IsOutputRedirected;

        var report = new DevFlowInitReport
        {
            WorkspacePath = workspaceRoot,
            ReportPath = reportPath,
            JsonReportPath = jsonReportPath,
            GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
            CliVersion = typeof(DevFlowCommands).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            ManifestVersion = manifest.ManifestVersion,
            ExecutionMode = BuildExecutionMode(options, interactive)
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(options.NewTemplate))
            {
                var scaffoldResult = await ScaffoldNewProjectAsync(workspaceRoot, options.NewTemplate, options.NewName, options.DryRun, cancellationToken);
                if (scaffoldResult.Status != DevFlowInitStatus.Success)
                {
                    report.OverallStatus = scaffoldResult.Status;
                    report.Notes.Add(scaffoldResult.Detail);
                    report.AiBootstrap = new DevFlowAiBootstrapResult
                    {
                        OverallStatus = options.NoAi ? DevFlowInitStatus.Disabled : DevFlowInitStatus.Skipped,
                        BootstrapMode = options.NoAi ? "disabled" : "manual"
                    };
                    await WriteReportAsync(report, cancellationToken);
                    output.WriteResult(report, json, PrintHumanSummary);
                    return false;
                }
                report.Notes.Add($"Scaffolded new project: {scaffoldResult.Detail}");
            }

            var discovered = DevFlowProjectScanner.Discover(workspaceRoot);
            if (discovered.Count == 0)
            {
                report.OverallStatus = DevFlowInitStatus.ManualRequired;
                report.Notes.Add("No MAUI projects were found below the current directory.");
                report.Notes.Add("Create a project with `dotnet new maui` or `dotnet new maui-blazor`, then rerun `maui devflow init`.");
                report.AiBootstrap = new DevFlowAiBootstrapResult
                {
                    OverallStatus = options.NoAi ? DevFlowInitStatus.Disabled : DevFlowInitStatus.Skipped,
                    BootstrapMode = options.NoAi ? "disabled" : "manual"
                };

                await WriteReportAsync(report, cancellationToken);
                output.WriteResult(report, json, PrintHumanSummary);
                return false;
            }

            var explicitlySelected = ResolveExplicitProjectSelection(workspaceRoot, options.Project);
            var eligible = discovered.Where(candidate => candidate.IsSupported && (!candidate.IsAlreadyIntegrated || options.Force)).ToList();
            var selected = ResolveTargets(eligible, explicitlySelected, options.All, interactive);

            foreach (var candidate in discovered.Where(candidate => !candidate.IsSupported))
            {
                report.Projects.Add(new DevFlowInitProjectResult
                {
                    ProjectPath = candidate.ProjectPath,
                    RelativePath = candidate.RelativePath,
                    Flavor = candidate.Flavor,
                    OverallStatus = DevFlowInitStatus.Unsupported,
                    Operations =
                    [
                        new DevFlowInitOperationResult
                        {
                            Name = "Project support",
                            Status = DevFlowInitStatus.ManualRequired,
                            Detail = "This project flavor is not supported by the current init flow."
                        }
                    ],
                    ManualSteps = [$"Add DevFlow to {candidate.RelativePath} manually."]
                });
            }

            foreach (var candidate in discovered.Where(candidate => candidate.IsAlreadyIntegrated && !options.Force))
            {
                report.Projects.Add(new DevFlowInitProjectResult
                {
                    ProjectPath = candidate.ProjectPath,
                    RelativePath = candidate.RelativePath,
                    Flavor = candidate.Flavor,
                    OverallStatus = DevFlowInitStatus.AlreadyPresent,
                    Operations =
                    [
                        new DevFlowInitOperationResult
                        {
                            Name = "Existing DevFlow integration",
                            Status = DevFlowInitStatus.AlreadyPresent,
                            Detail = "DevFlow is already integrated in this project."
                        }
                    ],
                    VerificationCommands =
                    [
                        "dotnet build",
                        "maui devflow wait",
                        "maui devflow diagnose"
                    ],
                    ManualSteps = [$"To re-apply and update package versions, re-run with `--force`."]
                });
            }

            if (selected.Count == 0 && explicitlySelected == null)
            {
                report.OverallStatus = report.Projects.Any(project => project.OverallStatus == DevFlowInitStatus.AlreadyPresent)
                    ? DevFlowInitStatus.AlreadyPresent
                    : DevFlowInitStatus.ManualRequired;
                report.Notes.Add(report.OverallStatus == DevFlowInitStatus.AlreadyPresent
                    ? "All discovered supported MAUI projects are already onboarded."
                    : "No eligible MAUI projects were selected for onboarding.");
            }

            foreach (var candidate in selected)
            {
                if (report.Projects.Any(p => string.Equals(p.ProjectPath, candidate.ProjectPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var effectiveCandidate = ApplyOverrides(candidate, options);
                report.Projects.Add(DevFlowProjectUpdater.Apply(effectiveCandidate, manifest, options.DryRun, workspaceRoot));
            }

            report.AiBootstrap = await AiHostBootstrapper.RunAsync(
                manifest,
                workspaceRoot,
                options.AiHost,
                options.NoAi,
                options.AiLocalOnly,
                interactive,
                options.DryRun,
                cancellationToken);

            report.OverallStatus = DetermineOverallStatus(report);
            PopulateNextSteps(report);
            await WriteReportAsync(report, cancellationToken);
            output.WriteResult(report, json, PrintHumanSummary);
            return report.OverallStatus is DevFlowInitStatus.Success or DevFlowInitStatus.AlreadyPresent;
        }
        catch (MauiToolException ex)
        {
            report.OverallStatus = DevFlowInitStatus.Failed;
            report.Notes.Add(ex.Message);
            if (ex.Remediation?.ManualSteps is { Length: > 0 })
                report.Notes.AddRange(ex.Remediation.ManualSteps);

            PopulateNextSteps(report);
            await WriteReportAsync(report, cancellationToken);
            output.WriteResult(report, json, PrintHumanSummary);
            return false;
        }
        catch (Exception ex)
        {
            report.OverallStatus = DevFlowInitStatus.Failed;
            report.Notes.Add(ex.Message);
            PopulateNextSteps(report);
            await WriteReportAsync(report, cancellationToken);
            output.WriteResult(report, json, PrintHumanSummary);
            return false;
        }
    }

    static string BuildExecutionMode(DevFlowInitOptions options, bool interactive)
    {
        var mode = new List<string>();
        mode.Add(interactive ? "interactive" : "non-interactive");
        if (!string.IsNullOrWhiteSpace(options.Project))
            mode.Add("--project");
        if (options.All)
            mode.Add("--all");
        if (options.Force)
            mode.Add("--force");
        if (options.ForceGtk)
            mode.Add("--gtk");
        if (!string.IsNullOrWhiteSpace(options.NewTemplate))
            mode.Add($"--new {options.NewTemplate}");
        if (options.DryRun)
            mode.Add("--dry-run");
        return string.Join(", ", mode);
    }

    [RequiresUnreferencedCode("DevFlow init uses MSBuild evaluation which relies on reflection-heavy code paths.")]
    [RequiresDynamicCode("DevFlow init uses MSBuild evaluation which relies on reflection-heavy code paths.")]
    static DevFlowProjectCandidate? ResolveExplicitProjectSelection(string workspaceRoot, string? projectOrDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectOrDirectory))
            return null;

        var resolved = MauiProjectResolver.Resolve(projectOrDirectory);
        var candidate = DevFlowProjectScanner.DescribeProject(workspaceRoot, resolved.ProjectPath);
        if (candidate == null)
        {
            throw MauiToolException.UserActionRequired(
                ErrorCodes.InvalidArgument,
                $"'{resolved.ProjectPath}' is not a supported MAUI project for `maui devflow init`.",
                [$"Select a standard MAUI app project or run init from a workspace that contains one."]);
        }

        return candidate;
    }

    static List<DevFlowProjectCandidate> ResolveTargets(
        IReadOnlyList<DevFlowProjectCandidate> eligible,
        DevFlowProjectCandidate? explicitProject,
        bool all,
        bool interactive)
    {
        if (explicitProject != null)
            return [explicitProject];

        if (all)
            return eligible.ToList();

        if (eligible.Count == 1)
            return [eligible[0]];

        if (eligible.Count == 0)
            return [];

        if (!interactive)
        {
            throw MauiToolException.UserActionRequired(
                ErrorCodes.InvalidArgument,
                "Multiple eligible MAUI projects were found.",
                ["Re-run with `--project <path-to-app.csproj>` or `--all`."]);
        }

        var selection = AnsiConsole.Prompt(
            new MultiSelectionPrompt<DevFlowProjectCandidate>()
                .Title("[bold]Select the MAUI project(s) to onboard[/]")
                .NotRequired()
                .UseConverter(candidate => $"{candidate.RelativePath} [grey]({candidate.Flavor})[/]")
                .AddChoices(eligible));

        return selection.ToList();
    }

    static DevFlowProjectCandidate ApplyOverrides(DevFlowProjectCandidate candidate, DevFlowInitOptions options)
    {
        var needsBlazor = candidate.NeedsBlazor;
        if (options.ForceBlazor)
            needsBlazor = true;
        if (options.DisableBlazor)
            needsBlazor = false;

        var isGtk = candidate.Flavor.StartsWith("gtk", StringComparison.OrdinalIgnoreCase) || options.ForceGtk;
        var flavor = isGtk
            ? (needsBlazor ? "gtk-blazor" : "gtk")
            : needsBlazor
                ? "standard-maui-blazor"
                : candidate.Flavor;

        return new DevFlowProjectCandidate
        {
            ProjectPath = candidate.ProjectPath,
            RelativePath = candidate.RelativePath,
            Flavor = flavor,
            IsSupported = candidate.IsSupported,
            NeedsBlazor = needsBlazor,
            IsAlreadyIntegrated = candidate.IsAlreadyIntegrated,
            MauiProgramPath = candidate.MauiProgramPath
        };
    }

    static string DetermineOverallStatus(DevFlowInitReport report)
    {
        var statuses = report.Projects.Select(project => project.OverallStatus).ToList();
        if (report.AiBootstrap.OverallStatus == DevFlowInitStatus.Failed || statuses.Contains(DevFlowInitStatus.Failed, StringComparer.Ordinal))
            return DevFlowInitStatus.Failed;
        if (report.AiBootstrap.OverallStatus == DevFlowInitStatus.ManualRequired ||
            statuses.Contains(DevFlowInitStatus.ManualRequired, StringComparer.Ordinal) ||
            statuses.Contains(DevFlowInitStatus.Unsupported, StringComparer.Ordinal))
            return DevFlowInitStatus.ManualRequired;
        if (statuses.Count == 0 && report.AiBootstrap.OverallStatus == DevFlowInitStatus.Disabled)
            return DevFlowInitStatus.Skipped;
        if (statuses.Count > 0 && statuses.All(status => status == DevFlowInitStatus.AlreadyPresent))
            return DevFlowInitStatus.AlreadyPresent;
        return DevFlowInitStatus.Success;
    }

    static void PopulateNextSteps(DevFlowInitReport report)
    {
        // Per-project verification commands for successfully onboarded projects
        foreach (var project in report.Projects)
        {
            if (project.VerificationCommands.Count > 0)
                continue; // Already populated (e.g. already-onboarded)

            if (project.OverallStatus is DevFlowInitStatus.Success)
            {
                project.VerificationCommands.Add($"dotnet build {project.RelativePath}");
                project.VerificationCommands.Add("maui devflow wait");
                project.VerificationCommands.Add("maui devflow tree");
            }
            else if (project.OverallStatus is DevFlowInitStatus.ManualRequired or DevFlowInitStatus.Unsupported)
            {
                project.VerificationCommands.Add($"# Review manual steps above, then:");
                project.VerificationCommands.Add($"dotnet build {project.RelativePath}");
            }
        }

        // Top-level next steps based on overall outcome
        if (report.OverallStatus is DevFlowInitStatus.Success)
        {
            report.NextSteps.Add("Build and run your MAUI app with DevFlow enabled.");
            report.NextSteps.Add("Run `maui devflow wait` to connect DevFlow to your running app.");
            report.NextSteps.Add("Use `maui devflow tree` to inspect the visual tree.");
            report.NextSteps.Add("Use `maui devflow diagnose` if connection issues occur.");
        }
        else if (report.OverallStatus is DevFlowInitStatus.AlreadyPresent)
        {
            report.NextSteps.Add("DevFlow is already integrated in this workspace.");
            report.NextSteps.Add("Run `maui devflow wait` to connect to your running app.");
            report.NextSteps.Add("To update package versions, re-run `maui devflow init --force`.");
        }
        else if (report.OverallStatus is DevFlowInitStatus.ManualRequired)
        {
            report.NextSteps.Add("Some steps require manual intervention — see project details above.");
            report.NextSteps.Add("After manual fixes, run `dotnet build` to verify.");
            report.NextSteps.Add("Then run `maui devflow wait` to verify the agent starts.");
        }
        else if (report.OverallStatus is DevFlowInitStatus.Failed)
        {
            report.NextSteps.Add("Init failed — review the errors in the report above.");
            report.NextSteps.Add("Re-run `maui devflow init` after addressing the issues.");
        }

        // AI-related next steps
        if (report.AiBootstrap.OverallStatus == DevFlowInitStatus.ManualRequired)
        {
            report.NextSteps.Add("AI bootstrap requires manual setup — see the AI bootstrap section.");
        }
    }

    static async Task WriteReportAsync(DevFlowInitReport report, CancellationToken cancellationToken = default)
    {
        var markdown = BuildMarkdown(report);
        await File.WriteAllTextAsync(report.ReportPath, markdown, cancellationToken);

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(report, DevFlowInitReportJsonContext.Default.DevFlowInitReport);
        await File.WriteAllBytesAsync(report.JsonReportPath, jsonBytes, cancellationToken);
    }

    static string BuildMarkdown(DevFlowInitReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MAUI DEVFLOW INIT REPORT");
        builder.AppendLine();
        builder.AppendLine($"- **Generated:** {report.GeneratedAtUtc}");
        builder.AppendLine($"- **Workspace:** `{report.WorkspacePath}`");
        builder.AppendLine($"- **CLI version:** `{report.CliVersion}`");
        builder.AppendLine($"- **Manifest version:** `{report.ManifestVersion}`");
        builder.AppendLine($"- **Execution mode:** `{report.ExecutionMode}`");
        builder.AppendLine($"- **Overall status:** `{report.OverallStatus}`");
        builder.AppendLine();
        builder.AppendLine("## AI bootstrap");
        builder.AppendLine();
        builder.AppendLine($"- **Status:** `{report.AiBootstrap.OverallStatus}`");
        builder.AppendLine($"- **Detected hosts:** {(report.AiBootstrap.DetectedHosts.Count == 0 ? "_none_" : string.Join(", ", report.AiBootstrap.DetectedHosts))}");
        builder.AppendLine($"- **Selected host:** {(string.IsNullOrWhiteSpace(report.AiBootstrap.SelectedHostDisplayName) ? "_none_" : report.AiBootstrap.SelectedHostDisplayName)}");
        builder.AppendLine($"- **Bootstrap mode:** `{report.AiBootstrap.BootstrapMode}`");
        if (report.AiBootstrap.FilesChanged.Count > 0)
        {
            builder.AppendLine("- **Files changed:**");
            foreach (var file in report.AiBootstrap.FilesChanged.Distinct(StringComparer.OrdinalIgnoreCase))
                builder.AppendLine($"  - `{Path.GetRelativePath(report.WorkspacePath, file)}`");
        }
        if (report.AiBootstrap.ManualSteps.Count > 0)
        {
            builder.AppendLine("- **Manual steps:**");
            foreach (var step in report.AiBootstrap.ManualSteps)
                builder.AppendLine($"  - {step}");
        }

        foreach (var project in report.Projects.OrderBy(project => project.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine($"## Project: `{project.RelativePath}`");
            builder.AppendLine();
            builder.AppendLine($"- **Flavor:** `{project.Flavor}`");
            builder.AppendLine($"- **Status:** `{project.OverallStatus}`");
            if (project.FilesChanged.Count > 0)
            {
                builder.AppendLine("- **Files changed:**");
                foreach (var file in project.FilesChanged.Distinct(StringComparer.OrdinalIgnoreCase))
                    builder.AppendLine($"  - `{Path.GetRelativePath(report.WorkspacePath, file)}`");
            }
            builder.AppendLine();
            builder.AppendLine("| Operation | Status | Detail |");
            builder.AppendLine("|---|---|---|");
            foreach (var operation in project.Operations)
                builder.AppendLine($"| {EscapePipe(operation.Name)} | `{operation.Status}` | {EscapePipe(operation.Detail)} |");
            if (project.ManualSteps.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Manual steps");
                builder.AppendLine();
                foreach (var step in project.ManualSteps)
                    builder.AppendLine($"- {step}");
            }
            if (project.VerificationCommands.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("### Verification");
                builder.AppendLine();
                builder.AppendLine("```bash");
                foreach (var cmd in project.VerificationCommands)
                    builder.AppendLine(cmd);
                builder.AppendLine("```");
            }
        }

        if (report.Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            foreach (var note in report.Notes)
                builder.AppendLine($"- {note}");
        }

        if (report.NextSteps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Next steps");
            builder.AppendLine();
            foreach (var step in report.NextSteps)
                builder.AppendLine($"- {step}");
        }

        return builder.ToString();
    }

    static string EscapePipe(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    static void PrintHumanSummary(DevFlowInitReport report)
    {
        Console.WriteLine($"DevFlow init status: {report.OverallStatus}");
        Console.WriteLine($"Report: {report.ReportPath}");
        Console.WriteLine($"JSON report: {report.JsonReportPath}");
        Console.WriteLine($"Projects: {report.Projects.Count}");
        if (!string.IsNullOrWhiteSpace(report.AiBootstrap.SelectedHostDisplayName))
            Console.WriteLine($"AI host: {report.AiBootstrap.SelectedHostDisplayName} ({report.AiBootstrap.BootstrapMode})");
        if (report.Notes.Count > 0)
        {
            Console.WriteLine();
            foreach (var note in report.Notes)
                Console.WriteLine($"- {note}");
        }
        if (report.NextSteps.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            foreach (var step in report.NextSteps)
                Console.WriteLine($"  - {step}");
        }
    }

    static readonly HashSet<string> s_validTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        "maui", "maui-blazor"
    };

    static async Task<(string Status, string Detail)> ScaffoldNewProjectAsync(
        string workspaceRoot,
        string template,
        string? name,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (!s_validTemplates.Contains(template))
        {
            return (DevFlowInitStatus.Failed,
                $"Unknown template '{template}'. Supported templates: {string.Join(", ", s_validTemplates)}.");
        }

        var projectName = name ?? "MauiApp1";

        // Validate project name to prevent path traversal and argument injection
        if (!System.Text.RegularExpressions.Regex.IsMatch(projectName, @"^[A-Za-z0-9._-]+$"))
            return (DevFlowInitStatus.Failed, $"Invalid project name: '{projectName}'. Use only letters, digits, dots, hyphens, or underscores.");

        var outputDir = Path.GetFullPath(Path.Combine(workspaceRoot, projectName));
        if (!outputDir.StartsWith(Path.GetFullPath(workspaceRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (DevFlowInitStatus.Failed, "Project name would escape workspace root.");

        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            return (DevFlowInitStatus.Failed,
                $"Directory '{projectName}' already exists and is not empty.");
        }

        if (dryRun)
        {
            return (DevFlowInitStatus.Success,
                $"Would create '{template}' project named '{projectName}' at {outputDir}.");
        }

        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("new");
        psi.ArgumentList.Add(template);
        psi.ArgumentList.Add("-n");
        psi.ArgumentList.Add(projectName);
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputDir);

        var argsDisplay = $"new {template} -n {projectName} -o \"{outputDir}\"";
        try
        {
            var process = System.Diagnostics.Process.Start(psi);

            if (process == null)
            {
                return (DevFlowInitStatus.Failed,
                    $"Failed to start `dotnet {argsDisplay}`.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                return (DevFlowInitStatus.Failed,
                    $"`dotnet {argsDisplay}` exited with code {process.ExitCode}: {detail}");
            }

            return (DevFlowInitStatus.Success,
                $"Created '{template}' project '{projectName}' at {outputDir}.");
        }
        catch (Exception ex)
        {
            return (DevFlowInitStatus.Failed,
                $"Failed to scaffold project: {ex.Message}");
        }
    }
}
