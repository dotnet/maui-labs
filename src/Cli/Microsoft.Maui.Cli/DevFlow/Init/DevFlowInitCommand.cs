using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
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
    public static async Task<bool> ExecuteAsync(DevFlowInitOptions options, IDevFlowOutputWriter output)
    {
        var manifest = DevFlowInitManifestLoader.Load();
        var workspaceRoot = Directory.GetCurrentDirectory();
        var reportPath = Path.Combine(workspaceRoot, "MAUI-DEVFLOW-INIT-REPORT.md");
        var json = output.ResolveJsonMode(options.Json, options.NoJson);
        var interactive = !options.Ci && !json && !Console.IsInputRedirected && !Console.IsOutputRedirected;

        var report = new DevFlowInitReport
        {
            WorkspacePath = workspaceRoot,
            ReportPath = reportPath,
            GeneratedAtUtc = DateTime.UtcNow.ToString("o"),
            CliVersion = typeof(DevFlowCommands).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            ManifestVersion = manifest.ManifestVersion,
            ExecutionMode = BuildExecutionMode(options, interactive)
        };

        try
        {
            var discovered = DevFlowProjectScanner.Discover(workspaceRoot);
            if (discovered.Count == 0)
            {
                report.OverallStatus = DevFlowInitStatus.ManualRequired;
                report.Notes.Add("No MAUI projects were found below the current directory.");
                report.Notes.Add("Create a project with `dotnet new maui` or rerun init from an existing MAUI workspace.");
                report.AiBootstrap = new DevFlowAiBootstrapResult
                {
                    OverallStatus = options.NoAi ? DevFlowInitStatus.Disabled : DevFlowInitStatus.Skipped,
                    BootstrapMode = options.NoAi ? "disabled" : "manual"
                };

                await WriteReportAsync(report);
                output.WriteResult(report, json, PrintHumanSummary);
                return false;
            }

            var explicitlySelected = ResolveExplicitProjectSelection(workspaceRoot, options.Project);
            var eligible = discovered.Where(candidate => candidate.IsSupported && !candidate.IsAlreadyIntegrated).ToList();
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

            foreach (var candidate in discovered.Where(candidate => candidate.IsAlreadyIntegrated))
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
                    ]
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
                var effectiveCandidate = ApplyOverrides(candidate, options);
                report.Projects.Add(DevFlowProjectUpdater.Apply(effectiveCandidate, manifest, options.DryRun));
            }

            report.AiBootstrap = await AiHostBootstrapper.RunAsync(
                manifest,
                workspaceRoot,
                options.AiHost,
                options.NoAi,
                options.AiLocalOnly,
                interactive,
                options.DryRun);

            report.OverallStatus = DetermineOverallStatus(report);
            await WriteReportAsync(report);
            output.WriteResult(report, json, PrintHumanSummary);
            return report.OverallStatus is DevFlowInitStatus.Success or DevFlowInitStatus.AlreadyPresent;
        }
        catch (MauiToolException ex)
        {
            report.OverallStatus = DevFlowInitStatus.Failed;
            report.Notes.Add(ex.Message);
            if (ex.Remediation?.ManualSteps is { Length: > 0 })
                report.Notes.AddRange(ex.Remediation.ManualSteps);

            await WriteReportAsync(report);
            output.WriteResult(report, json, PrintHumanSummary);
            return false;
        }
        catch (Exception ex)
        {
            report.OverallStatus = DevFlowInitStatus.Failed;
            report.Notes.Add(ex.Message);
            await WriteReportAsync(report);
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

        var flavor = needsBlazor ? "standard-maui-blazor" : candidate.Flavor;
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

    static async Task WriteReportAsync(DevFlowInitReport report)
    {
        var markdown = BuildMarkdown(report);
        await File.WriteAllTextAsync(report.ReportPath, markdown);
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
        }

        if (report.Notes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            foreach (var note in report.Notes)
                builder.AppendLine($"- {note}");
        }

        return builder.ToString();
    }

    static string EscapePipe(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    static void PrintHumanSummary(DevFlowInitReport report)
    {
        Console.WriteLine($"DevFlow init status: {report.OverallStatus}");
        Console.WriteLine($"Report: {report.ReportPath}");
        Console.WriteLine($"Projects: {report.Projects.Count}");
        if (!string.IsNullOrWhiteSpace(report.AiBootstrap.SelectedHostDisplayName))
            Console.WriteLine($"AI host: {report.AiBootstrap.SelectedHostDisplayName} ({report.AiBootstrap.BootstrapMode})");
        if (report.Notes.Count > 0)
        {
            Console.WriteLine();
            foreach (var note in report.Notes)
                Console.WriteLine($"- {note}");
        }
    }
}
