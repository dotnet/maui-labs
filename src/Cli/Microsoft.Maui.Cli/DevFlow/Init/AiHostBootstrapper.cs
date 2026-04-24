using Spectre.Console;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal static class AiHostBootstrapper
{
    public static async Task<DevFlowAiBootstrapResult> RunAsync(
        DevFlowInitManifest manifest,
        string workspaceRoot,
        string? explicitHost,
        bool noAi,
        bool aiLocalOnly,
        bool interactive,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        if (noAi)
        {
            return new DevFlowAiBootstrapResult
            {
                OverallStatus = DevFlowInitStatus.Disabled,
                BootstrapMode = "disabled"
            };
        }

        var detectedHosts = manifest.Hosts
            .Where(host => IsHostDetected(host, workspaceRoot))
            .ToList();

        var result = new DevFlowAiBootstrapResult
        {
            OverallStatus = DevFlowInitStatus.Skipped,
            BootstrapMode = "manual"
        };
        result.DetectedHosts.AddRange(detectedHosts.Select(host => host.DisplayName));

        var selectedHost = ResolveHost(manifest, detectedHosts, explicitHost, interactive);
        if (selectedHost == null)
        {
            result.OverallStatus = DevFlowInitStatus.ManualRequired;
            result.ManualSteps.Add("No AI host could be selected automatically.");
            result.ManualSteps.Add("Re-run `maui devflow init --ai-host <host>` or set up the desired host manually.");
            return result;
        }

        result.SelectedHostId = selectedHost.Id;
        result.SelectedHostDisplayName = selectedHost.DisplayName;

        var fallback = selectedHost.RepoLocalFallbacks.FirstOrDefault();
        if (!aiLocalOnly && selectedHost.MarketplaceInstalls.Any(install => !string.Equals(install.InstallStrategy, "manual", StringComparison.OrdinalIgnoreCase)))
        {
            // Reserved for future host-native automation strategies.
        }

        if (fallback != null)
        {
            using var http = GitHubDirectorySync.CreateHttpClient();
            var destinationRoot = Path.Combine(workspaceRoot, fallback.TargetPathTemplate.Replace('/', Path.DirectorySeparatorChar));
            var syncResult = await GitHubDirectorySync.SyncAsync(
                http,
                new GitHubSyncRequest
                {
                    Repo = fallback.SourceRepo,
                    RepoUrl = fallback.SourceRepoUrl,
                    SourcePath = fallback.SourcePath,
                    Ref = fallback.DesiredRef,
                    DestinationRoot = destinationRoot,
                    MetadataFileName = fallback.SyncMetadataFileName,
                    ManifestVersion = manifest.ManifestVersion,
                    DryRun = dryRun
                },
                cancellationToken);

            result.OverallStatus = DevFlowInitStatus.Success;
            result.BootstrapMode = "local-skill-sync";
            result.FilesChanged.AddRange(syncResult.DownloadedFiles);
            result.FilesChanged.Add(syncResult.MetadataPath);
            result.ManualSteps.AddRange(selectedHost.Verify.ManualSteps);
            return result;
        }

        result.OverallStatus = DevFlowInitStatus.ManualRequired;
        result.BootstrapMode = "manual";
        foreach (var install in selectedHost.MarketplaceInstalls)
            result.ManualSteps.AddRange(install.ManualSteps);
        result.ManualSteps.AddRange(selectedHost.Verify.ManualSteps);
        return result;
    }

    static bool IsHostDetected(DevFlowAiHostManifest host, string workspaceRoot)
    {
        return host.Detect.Executables.Any(IsExecutableOnPath) ||
               host.Detect.RepoMarkers.Any(marker => File.Exists(Path.Combine(workspaceRoot, marker)) || Directory.Exists(Path.Combine(workspaceRoot, marker))) ||
               host.Detect.ConfigMarkers.Any(marker => File.Exists(Path.Combine(workspaceRoot, marker)) || Directory.Exists(Path.Combine(workspaceRoot, marker)));
    }

    static DevFlowAiHostManifest? ResolveHost(
        DevFlowInitManifest manifest,
        IReadOnlyList<DevFlowAiHostManifest> detectedHosts,
        string? explicitHost,
        bool interactive)
    {
        if (!string.IsNullOrWhiteSpace(explicitHost))
        {
            return manifest.Hosts.FirstOrDefault(host =>
                string.Equals(host.Id, explicitHost, StringComparison.OrdinalIgnoreCase));
        }

        if (detectedHosts.Count == 1)
            return detectedHosts[0];

        if (detectedHosts.Count > 1 && interactive)
        {
            return AnsiConsole.Prompt(
                new SelectionPrompt<DevFlowAiHostManifest>()
                    .Title("[bold]Select the AI host to configure[/]")
                    .UseConverter(host => host.DisplayName)
                    .AddChoices(detectedHosts));
        }

        if (detectedHosts.Count == 0 && interactive)
        {
            var localHosts = manifest.Hosts.Where(host => host.RepoLocalFallbacks.Count > 0).ToList();
            if (localHosts.Count > 0)
            {
                return AnsiConsole.Prompt(
                    new SelectionPrompt<DevFlowAiHostManifest>()
                        .Title("[bold]No AI host was detected. Select a repo-local skill target[/]")
                        .UseConverter(host => host.DisplayName)
                        .AddChoices(localHosts));
            }
        }

        return null;
    }

    static bool IsExecutableOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var pathExtensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in pathExtensions)
            {
                var candidate = Path.Combine(directory, executableName + extension);
                if (File.Exists(candidate))
                    return true;
            }
        }

        return false;
    }
}
