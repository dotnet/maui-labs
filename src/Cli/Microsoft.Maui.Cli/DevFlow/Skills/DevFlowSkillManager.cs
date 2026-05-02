using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow.Skills;

internal static class DevFlowSkillManager
{
    const string ResourceRoot = "devflow.skills";
    const string PackageId = "Microsoft.Maui.Cli";
    const string StateRootEnvironmentVariable = "MAUIDEVFLOW_STATE_ROOT";
    const string AutoTarget = "auto";
    const string DefaultTarget = "claude";
    const string StateLockExtension = ".lock";
    const int StateFileVersion = 2;
    static readonly TimeSpan StateFileRetryTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan FreshnessCheckInterval = TimeSpan.FromDays(7);
    static readonly TimeSpan FreshnessPromptInterval = TimeSpan.FromDays(1);

    static readonly DevFlowSkillDefinition[] s_skills =
    [
        new("maui-devflow-onboard", "MAUI DevFlow Onboard", "Guides first-time MAUI DevFlow project integration.", Recommended: true),
        new("maui-devflow-debug", "MAUI DevFlow Debug", "Guides build, deploy, connection recovery, inspect, and debug loops with MAUI DevFlow.", Recommended: true)
    ];

    static readonly string[] s_legacySkillIds =
    [
        "maui-ai-debugging",
        "maui-devflow-connect",
        "devflow-onboard",
        "devflow-connect",
        "devflow-debug"
    ];

    static readonly Dictionary<string, string> s_targetDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude"] = Path.Combine(".claude", "skills"),
        ["github"] = Path.Combine(".github", "skills"),
        ["agent"] = Path.Combine(".agent", "skills"),
        ["agents"] = Path.Combine(".agents", "skills")
    };

    internal static string? StateRootOverrideForTests { get; set; }
    internal static string? UserRootOverrideForTests { get; set; }
    internal static DateTimeOffset? UtcNowOverrideForTests { get; set; }

    public static async Task<JsonObject> InstallRecommendedAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await InstallRecommendedAsync(scope, target, customPath: null, force, allowDowngrade, confirm: null, cancellationToken);

    internal static async Task<JsonObject> InstallRecommendedAsync(string scope, string target, string? customPath, bool force, bool allowDowngrade, Func<SkillActionPrompt, bool>? confirm, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Where(s => s.Recommended).Select(s => s.Id), scope, target, customPath, force, allowDowngrade, "install", allowAllScopes: false, confirm, cancellationToken);

    public static async Task<JsonObject> InstallAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await InstallAsync(scope, target, customPath: null, force, allowDowngrade, confirm: null, cancellationToken);

    internal static async Task<JsonObject> InstallAsync(string scope, string target, string? customPath, bool force, bool allowDowngrade, Func<SkillActionPrompt, bool>? confirm, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Select(s => s.Id), scope, target, customPath, force, allowDowngrade, "install", allowAllScopes: false, confirm, cancellationToken);

    public static async Task<JsonObject> UpdateAsync(string scope, string target, bool force, bool allowDowngrade, CancellationToken cancellationToken)
        => await UpdateAsync(scope, target, customPath: null, force, allowDowngrade, confirm: null, cancellationToken);

    internal static async Task<JsonObject> UpdateAsync(string scope, string target, string? customPath, bool force, bool allowDowngrade, Func<SkillActionPrompt, bool>? confirm, CancellationToken cancellationToken)
        => await WriteSkillsAsync(s_skills.Select(s => s.Id), scope, target, customPath, force, allowDowngrade, "update", allowAllScopes: true, confirm, cancellationToken);

    public static async Task<JsonObject> ListAsync(string scope, string target, CancellationToken cancellationToken)
        => await ListAsync(scope, target, customPath: null, cancellationToken);

    internal static async Task<JsonObject> ListAsync(string scope, string target, string? customPath, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("list", scope, target);
        result["skills"] = await BuildSkillStatusesAsync(scope, target, customPath, cancellationToken);
        return result;
    }

    public static async Task<JsonObject> CheckAsync(string scope, string target, bool online, CancellationToken cancellationToken)
        => await CheckAsync(scope, target, customPath: null, online, cancellationToken);

    internal static async Task<JsonObject> CheckAsync(string scope, string target, string? customPath, bool online, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("check", scope, target);
        result["online"] = online;
        if (online)
        {
            result["onlineMessage"] = "Online skill file checks are intentionally not implemented. Update Microsoft.Maui.Cli to get newer bundled skills.";
        }

        result["skills"] = await BuildSkillStatusesAsync(scope, target, customPath, cancellationToken);
        return result;
    }

    public static async Task<JsonObject> DoctorAsync(string scope, string target, bool online, CancellationToken cancellationToken)
        => await DoctorAsync(scope, target, customPath: null, online, cancellationToken);

    internal static async Task<JsonObject> DoctorAsync(string scope, string target, string? customPath, bool online, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult("doctor", scope, target);
        result["online"] = online;
        var warnings = new JsonArray();
        AddStateFileWarnings(warnings, scope, target, customPath);

        result["skills"] = await BuildSkillStatusesAsync(scope, target, customPath, cancellationToken);

        var diagnostics = new JsonObject
        {
            ["cliVersion"] = GetCurrentCliVersion(),
            ["baseDirectory"] = AppContext.BaseDirectory,
            ["processPath"] = Environment.ProcessPath
        };

        var localToolVersion = TryGetLocalToolVersion(FindWorkspaceRoot(Directory.GetCurrentDirectory()));
        if (localToolVersion != null)
            diagnostics["localToolManifestVersion"] = localToolVersion;

        result["diagnostics"] = diagnostics;

        if (localToolVersion != null && !VersionsEquivalent(localToolVersion, GetCurrentCliVersion()))
        {
            AddJsonString(warnings, $"This repo declares local {PackageId} version {localToolVersion}, but the running CLI is {GetCurrentCliVersion()}. Run `dotnet tool restore` and `dotnet tool run maui devflow init` if project-scoped skills should come from the local tool.");
        }

        AddScopeConflictWarnings(warnings, target);
        result["warnings"] = warnings;
        return result;
    }

    public static async Task<JsonObject> RemoveAsync(string skillId, string scope, string target, bool force, CancellationToken cancellationToken)
    {
        var skill = TryGetSkill(skillId);
        var isLegacySkill = skill == null && IsLegacySkill(skillId);
        if (skill == null && !isLegacySkill)
            throw new InvalidOperationException($"Unknown DevFlow skill '{skillId}'.");

        var installTargets = ResolveInstallTargets(scope, target, customPath: null, allowAll: true);
        var bundle = skill != null ? await LoadSkillBundleAsync(skill, cancellationToken) : null;
        var result = CreateBaseResult("remove", scope, target);
        var results = new JsonArray();

        foreach (var installTarget in installTargets)
        {
            var status = bundle != null
                ? CreateStatusObject(installTarget, skill!.Id, bundle)
                : CreateLegacyStatusObject(installTarget, skillId, ReadSkillState(installTarget.StateFilePath));
            var statusValue = status["status"]?.GetValue<string>();
            if (statusValue == "dirty" && !force && !isLegacySkill)
            {
                status["action"] = "skipped";
                status["message"] = "Skill files differ from the install state. Re-run with --force to remove anyway.";
                AddJsonObject(results, status);
                continue;
            }

            if (statusValue == "unknown-or-unmanaged" && !force && !isLegacySkill)
            {
                status["action"] = "skipped";
                status["message"] = "Skill files are not managed by this CLI. Re-run with --force to remove anyway.";
                AddJsonObject(results, status);
                continue;
            }

            await UpdateSkillStateAsync(installTarget, cancellationToken, skillState =>
            {
                RemoveStateEntry(skillState, installTarget, skillId);
                return true;
            });

            var skillDirectory = GetSkillDirectory(installTarget, skillId);
            if (Directory.Exists(skillDirectory))
            {
                try
                {
                    Directory.Delete(skillDirectory, recursive: true);
                }
                catch (DirectoryNotFoundException)
                {
                    // Another concurrent remove already deleted the directory.
                }
            }

            status["action"] = "removed";
            status["status"] = "removed";
            AddJsonObject(results, status);
        }

        result["results"] = results;
        return result;
    }

    static async Task<JsonArray> BuildSkillStatusesAsync(string scope, string target, string? customPath, CancellationToken cancellationToken)
    {
        var items = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, customPath, allowAll: true);
        var skillBundles = await LoadSkillBundlesAsync(s_skills, cancellationToken);
        foreach (var installTarget in installTargets)
        {
            foreach (var (skill, bundle) in skillBundles)
                AddJsonObject(items, CreateStatusObject(installTarget, skill.Id, bundle));

            await RecordSkillCheckAsync(installTarget, skillBundles, cancellationToken);
        }

        return items;
    }

    internal static async Task<string?> GetFreshnessHintAsync(bool machineReadableOutput, string target, CancellationToken cancellationToken)
    {
        if (machineReadableOutput)
            return null;

        var installTarget = ResolveInstallTargets("project", target, customPath: null, allowAll: false)[0];
        var skillState = ReadSkillState(installTarget.StateFilePath);
        var now = GetUtcNow();
        if (!ShouldShowFreshnessHint(skillState, now))
            return null;

        await UpdateSkillStateAsync(installTarget, cancellationToken, state =>
        {
            state["lastPromptedUtc"] = now.ToString("O");
            return true;
        });

        return "DevFlow skills have not been checked recently. Run `maui devflow skills check` to compare installed skills with this CLI.";
    }

    static async Task<JsonObject> WriteSkillsAsync(IEnumerable<string> skillIds, string scope, string target, string? customPath, bool force, bool allowDowngrade, string action, bool allowAllScopes, Func<SkillActionPrompt, bool>? confirm, CancellationToken cancellationToken)
    {
        var result = CreateBaseResult(action, scope, target);
        if (!string.IsNullOrWhiteSpace(customPath))
            result["path"] = customPath;
        var results = new JsonArray();
        var installTargets = ResolveInstallTargets(scope, target, customPath, allowAll: allowAllScopes);
        var skillBundles = new List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>();
        foreach (var skillId in skillIds)
        {
            var skill = GetSkill(skillId);
            var bundle = await LoadSkillBundleAsync(skill, cancellationToken);
            skillBundles.Add((skill, bundle));
        }

        foreach (var installTarget in installTargets)
        {
            var targetResults = new List<JsonObject>();
            await UpdateSkillStateAsync(installTarget, cancellationToken, skillState =>
            {
                foreach (var (skill, bundle) in skillBundles)
                {
                    var status = CreateStatusObject(installTarget, skill.Id, bundle, skillState);
                    var statusValue = status["status"]?.GetValue<string>();

                    if (statusValue == "dirty" &&
                        !ConfirmAction(confirm, status, "overwrite", "Current skill files differ from the install state."))
                    {
                        status["action"] = "skipped";
                        status["message"] = "Skipped by interactive choice.";
                        targetResults.Add(status);
                        continue;
                    }

                    if (statusValue == "unknown-or-unmanaged" &&
                        !ConfirmAction(confirm, status, "overwrite", "Current skill files already exist but are not managed by this CLI."))
                    {
                        status["action"] = "skipped";
                        status["message"] = "Skipped by interactive choice.";
                        targetResults.Add(status);
                        continue;
                    }

                    if (statusValue == "installed-from-newer-cli" && !allowDowngrade && !force)
                    {
                        status["action"] = "skipped";
                        status["message"] = "Installed skill was written by a newer CLI. Re-run with --allow-downgrade or --force to replace it.";
                        targetResults.Add(status);
                        continue;
                    }

                    var existingEntry = FindStateEntry(skillState, installTarget, skill.Id);
                    var removedObsoleteFiles = statusValue == "up-to-date"
                        ? RemoveObsoleteTrackedFiles(GetSkillDirectory(installTarget, skill.Id), bundle, existingEntry)
                        : WriteSkillBundle(installTarget, skill, bundle, existingEntry, replaceDirectory: statusValue is "dirty" or "unknown-or-unmanaged");
                    UpsertStateEntry(skillState, installTarget, skill, bundle);

                    status = CreateStatusObject(installTarget, skill.Id, bundle, skillState);
                    status["action"] = statusValue == "up-to-date" && !removedObsoleteFiles ? "unchanged" : "written";
                    var message = GetWriteMessage(statusValue, removedObsoleteFiles);
                    if (!string.IsNullOrWhiteSpace(message))
                        status["message"] = message;
                    targetResults.Add(status);
                }

                MarkSkillStateChecked(skillState, HashBundleSet(skillBundles));
                return true;
            });

            foreach (var targetResult in targetResults)
                AddJsonObject(results, targetResult);
        }

        foreach (var cleanupTarget in GetLegacyCleanupTargets(installTargets))
        {
            if (!HasLegacySkillData(cleanupTarget))
                continue;

            var targetResults = new List<JsonObject>();
            await UpdateSkillStateAsync(cleanupTarget, cancellationToken, skillState =>
                MigrateLegacySkills(cleanupTarget, skillState, targetResults, confirm));

            foreach (var targetResult in targetResults)
                AddJsonObject(results, targetResult);
        }

        result["results"] = results;
        return result;
    }

    static async Task RecordSkillCheckAsync(InstallTarget installTarget, IReadOnlyList<(DevFlowSkillDefinition Skill, SkillBundle Bundle)> skillBundles, CancellationToken cancellationToken)
        => await UpdateSkillStateAsync(installTarget, cancellationToken, skillState =>
        {
            MarkSkillStateChecked(skillState, HashBundleSet(skillBundles));
            return true;
        });

    static JsonObject CreateBaseResult(string action, string scope, string target)
        => new()
        {
            ["action"] = action,
            ["scope"] = scope,
            ["target"] = target,
            ["cliVersion"] = GetCurrentCliVersion()
        };

    static JsonObject CreateStatusObject(InstallTarget installTarget, string skillId, SkillBundle bundle, JsonObject? knownSkillState = null)
    {
        var skillState = knownSkillState ?? ReadSkillState(installTarget.StateFilePath);
        var entry = FindStateEntry(skillState, installTarget, skillId);
        var skillDirectory = GetSkillDirectory(installTarget, skillId);
        var exists = Directory.Exists(skillDirectory);

        var status = "missing";
        if (entry == null && exists)
        {
            status = "unknown-or-unmanaged";
        }
        else if (entry != null && !exists)
        {
            status = "missing";
        }
        else if (entry != null)
        {
            if (IsDirty(skillDirectory, entry))
            {
                status = "dirty";
            }
            else if (string.Equals(GetString(entry, "contentHash"), bundle.ContentHash, StringComparison.Ordinal))
            {
                status = "up-to-date";
            }
            else
            {
                var installedByVersion = GetString(entry, "installedByCliVersion");
                var comparison = CompareVersionLike(installedByVersion, GetCurrentCliVersion());
                status = comparison > 0
                    ? "installed-from-newer-cli"
                    : comparison == 0
                        ? "installed-from-different-cli-same-version"
                        : "update-available-from-current-cli";
            }
        }

        return new JsonObject
        {
            ["skillId"] = skillId,
            ["scope"] = installTarget.Scope,
            ["target"] = installTarget.TargetKind,
            ["status"] = status,
            ["path"] = installTarget.GetDisplayPath(skillId),
            ["statePath"] = installTarget.StateFilePath,
            ["installedVersion"] = entry != null ? GetString(entry, "skillVersion") : null,
            ["bundledVersion"] = bundle.Version,
            ["installedByCliVersion"] = entry != null ? GetString(entry, "installedByCliVersion") : null,
            ["contentHash"] = entry != null ? GetString(entry, "contentHash") : null,
            ["bundledContentHash"] = bundle.ContentHash
        };
    }

    static JsonObject CreateLegacyStatusObject(InstallTarget installTarget, string skillId, JsonObject skillState)
    {
        var entry = FindStateEntry(skillState, installTarget, skillId);
        var skillDirectory = GetSkillDirectory(installTarget, skillId);
        var exists = Directory.Exists(skillDirectory);

        var status = "missing";
        if (entry == null && exists)
            status = "unknown-or-unmanaged";
        else if (entry != null && !exists)
            status = "missing";
        else if (entry != null)
            status = IsDirty(skillDirectory, entry) ? "dirty" : "legacy-managed";

        return new JsonObject
        {
            ["skillId"] = skillId,
            ["scope"] = installTarget.Scope,
            ["target"] = installTarget.TargetKind,
            ["status"] = status,
            ["path"] = installTarget.GetDisplayPath(skillId),
            ["statePath"] = installTarget.StateFilePath,
            ["installedVersion"] = entry != null ? GetString(entry, "skillVersion") : null,
            ["bundledVersion"] = null,
            ["installedByCliVersion"] = entry != null ? GetString(entry, "installedByCliVersion") : null,
            ["contentHash"] = entry != null ? GetString(entry, "contentHash") : null,
            ["bundledContentHash"] = null
        };
    }

    static bool MigrateLegacySkills(InstallTarget installTarget, JsonObject skillState, List<JsonObject> targetResults, Func<SkillActionPrompt, bool>? confirm)
    {
        var changed = false;
        foreach (var skillId in s_legacySkillIds)
        {
            var entry = FindStateEntry(skillState, installTarget, skillId);
            var skillDirectory = GetSkillDirectory(installTarget, skillId);
            var exists = Directory.Exists(skillDirectory);
            if (entry == null && !exists)
                continue;

            var status = CreateLegacyStatusObject(installTarget, skillId, skillState);
            if (!ConfirmAction(confirm, status, "remove", "Legacy DevFlow skill will be removed because current bundled skills replace it."))
            {
                status["action"] = "skipped";
                status["message"] = "Skipped by interactive choice.";
                targetResults.Add(status);
                continue;
            }

            if (exists)
                Directory.Delete(skillDirectory, recursive: true);

            if (entry != null)
                RemoveStateEntry(skillState, installTarget, skillId);

            status["action"] = "removed";
            status["status"] = "legacy-removed";
            status["message"] = "Legacy DevFlow skill was removed because current bundled skills were installed.";
            targetResults.Add(status);
            changed = true;
        }

        return changed;
    }

    static async Task<List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>> LoadSkillBundlesAsync(IEnumerable<DevFlowSkillDefinition> skills, CancellationToken cancellationToken)
    {
        var bundles = new List<(DevFlowSkillDefinition Skill, SkillBundle Bundle)>();
        foreach (var skill in skills)
        {
            var bundle = await LoadSkillBundleAsync(skill, cancellationToken);
            bundles.Add((skill, bundle));
        }

        return bundles;
    }

    static async Task<SkillBundle> LoadSkillBundleAsync(DevFlowSkillDefinition skill, CancellationToken cancellationToken)
    {
        var assembly = typeof(DevFlowSkillManager).Assembly;
        var prefix = $"{ResourceRoot}/{skill.Id}/";
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
            throw new InvalidOperationException($"No embedded DevFlow skill resources found for '{skill.Id}'.");

        var files = new List<SkillAssetFile>();
        foreach (var resourceName in resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(resourceName[prefix.Length..]);
            if (ShouldExcludeSkillAsset(relativePath))
                continue;

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded skill resource not found: {resourceName}");
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(cancellationToken);
            files.Add(new SkillAssetFile(relativePath, content, HashContent(content)));
        }

        return new SkillBundle(skill.Id, GetCurrentCliVersion(), files, HashBundle(files));
    }

    static bool ShouldExcludeSkillAsset(string relativePath)
        => relativePath.StartsWith($"evals{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(relativePath, "evals", StringComparison.OrdinalIgnoreCase);

    static bool WriteSkillBundle(InstallTarget installTarget, DevFlowSkillDefinition skill, SkillBundle bundle, JsonObject? existingEntry, bool replaceDirectory)
    {
        var skillDirectory = GetSkillDirectory(installTarget, skill.Id);
        if (replaceDirectory && Directory.Exists(skillDirectory))
            Directory.Delete(skillDirectory, recursive: true);

        Directory.CreateDirectory(skillDirectory);

        foreach (var file in bundle.Files)
        {
            if (!TryResolveSkillFilePath(skillDirectory, file.RelativePath, out var filePath))
                throw new InvalidOperationException($"Embedded DevFlow skill asset '{file.RelativePath}' for '{skill.Id}' resolves outside the skill directory.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, file.Content);
        }

        return replaceDirectory
            ? false
            : RemoveObsoleteTrackedFiles(skillDirectory, bundle, existingEntry);
    }

    static bool RemoveObsoleteTrackedFiles(string skillDirectory, SkillBundle bundle, JsonObject? existingEntry)
    {
        if (existingEntry?["files"] is not JsonArray files)
            return false;

        var deletedObsoleteFile = false;
        var currentFiles = bundle.Files.Select(f => NormalizeRelativePath(f.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OfType<JsonObject>())
        {
            var relativePath = GetString(file, "path");
            if (relativePath == null || currentFiles.Contains(NormalizeRelativePath(relativePath)))
                continue;

            if (!TryResolveSkillFilePath(skillDirectory, relativePath, out var obsoletePath))
                continue;

            if (File.Exists(obsoletePath))
            {
                File.Delete(obsoletePath);
                deletedObsoleteFile = true;
            }
        }

        if (deletedObsoleteFile)
            PruneEmptyDirectories(skillDirectory);

        return deletedObsoleteFile;
    }

    static string? GetWriteMessage(string? statusValue, bool removedObsoleteFiles)
        => statusValue switch
        {
            "missing" => "Installed missing skill files from the current CLI bundle.",
            "dirty" => "Overwrote dirty current skill files with the current CLI bundle.",
            "unknown-or-unmanaged" => "Replaced unmanaged existing skill folder with the current CLI bundle.",
            "update-available-from-current-cli" => "Updated skill files from the current CLI bundle.",
            "installed-from-different-cli-same-version" => "Rewrote skill files because installed content differed from this CLI bundle.",
            "installed-from-newer-cli" => "Replaced skill files that were installed by a newer CLI.",
            "up-to-date" when removedObsoleteFiles => "Removed obsolete files no longer present in the current CLI bundle.",
            _ => null
        };

    static bool IsDirty(string skillDirectory, JsonObject entry)
    {
        if (entry["files"] is not JsonArray files)
            return true;

        var trackedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.OfType<JsonObject>())
        {
            var relativePath = GetString(file, "path");
            var expectedHash = GetString(file, "hash");
            if (relativePath == null || expectedHash == null)
                return true;

            var normalizedRelativePath = NormalizeSkillRelativePath(relativePath);
            if (!trackedPaths.Add(normalizedRelativePath))
                return true;

            if (!TryResolveSkillFilePath(skillDirectory, relativePath, out var filePath))
                return true;

            if (!File.Exists(filePath))
                return true;

            string actualHash;
            try
            {
                actualHash = HashContent(File.ReadAllText(filePath));
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }

            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                return true;
        }

        try
        {
            foreach (var filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                var relativePath = NormalizeSkillRelativePath(Path.GetRelativePath(skillDirectory, filePath));
                if (!trackedPaths.Contains(relativePath))
                    return true;
            }

            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    static JsonObject ReadSkillState(string stateFilePath)
    {
        if (!File.Exists(stateFilePath))
            return CreateEmptySkillState();

        try
        {
            var contents = File.ReadAllText(stateFilePath);
            if (string.IsNullOrWhiteSpace(contents))
                return CreateEmptySkillState();

            return ParseSkillState(contents);
        }
        catch (JsonException)
        {
            return CreateEmptySkillState();
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Could not read DevFlow skills state file '{stateFilePath}'.", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Could not access DevFlow skills state file '{stateFilePath}'.", ex);
        }
    }

    static JsonObject ParseSkillState(string contents)
    {
        var parsed = CliJson.ParseNode(contents) as JsonObject;
        if (parsed == null)
            return CreateEmptySkillState();

        if (parsed["entries"] is not JsonArray)
            parsed["entries"] = new JsonArray();
        return parsed;
    }

    static JsonObject CreateEmptySkillState()
        => new()
        {
            ["version"] = StateFileVersion,
            ["entries"] = new JsonArray()
        };

    static void WriteSkillState(InstallTarget installTarget, JsonObject skillState)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installTarget.StateFilePath)!);
        PrepareSkillStateForWrite(installTarget, skillState);
        var tempFilePath = Path.Combine(
            Path.GetDirectoryName(installTarget.StateFilePath)!,
            $".{Path.GetFileName(installTarget.StateFilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = Encoding.UTF8.GetBytes(CliJson.SerializeUntyped(skillState));
            using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempFilePath, installTarget.StateFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
        }
    }

    static void PrepareSkillStateForWrite(InstallTarget installTarget, JsonObject skillState)
    {
        skillState["version"] = StateFileVersion;
        skillState["scope"] = installTarget.Scope;
        skillState["target"] = installTarget.TargetKind;
        skillState["targetRoot"] = installTarget.RootDirectory;
        skillState["relativeSkillDirectory"] = installTarget.RelativeSkillDirectory.Replace(Path.DirectorySeparatorChar, '/');
        if (installTarget.Scope == "project")
            skillState["workspaceRoot"] = installTarget.WorkspaceRoot;
        else
            skillState.Remove("workspaceRoot");
        skillState.Remove("updatedAtUtc");

        if (skillState["entries"] is not JsonArray entries)
        {
            entries = new JsonArray();
            skillState["entries"] = entries;
        }

        foreach (var entry in entries.OfType<JsonObject>())
        {
            entry.Remove("installedByCommandPath");
            entry.Remove("installedAtUtc");
        }
    }

    static async Task UpdateSkillStateAsync(InstallTarget installTarget, CancellationToken cancellationToken, Func<JsonObject, bool> update)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(installTarget.StateFilePath)!);
        using (await OpenSkillStateLockStreamAsync(installTarget.StateFilePath, cancellationToken))
        {
            var stateFileExists = File.Exists(installTarget.StateFilePath);
            var skillState = ReadSkillState(installTarget.StateFilePath);

            if (update(skillState) || !stateFileExists)
                WriteSkillState(installTarget, skillState);
        }
    }

    static async Task<FileStream> OpenSkillStateLockStreamAsync(string stateFilePath, CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        var lockFilePath = stateFilePath + StateLockExtension;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow - startedAt < StateFileRetryTimeout)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Could not acquire exclusive access to DevFlow skills state lock file '{lockFilePath}'.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"Could not access DevFlow skills state lock file '{lockFilePath}'.", ex);
            }
        }
    }

    static JsonObject? FindStateEntry(JsonObject skillState, InstallTarget installTarget, string skillId)
    {
        if (skillState["entries"] is not JsonArray entries)
            return null;

        return entries.OfType<JsonObject>().FirstOrDefault(entry =>
            string.Equals(GetString(entry, "skillId"), skillId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetString(entry, "scope"), installTarget.Scope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(GetString(entry, "target"), installTarget.TargetKind, StringComparison.OrdinalIgnoreCase));
    }

    static bool ConfirmAction(Func<SkillActionPrompt, bool>? confirm, JsonObject status, string action, string message)
    {
        if (confirm == null)
            return true;

        return confirm(new SkillActionPrompt(
            GetString(status, "skillId") ?? "unknown",
            action,
            GetString(status, "status") ?? "unknown",
            GetString(status, "path") ?? string.Empty,
            message));
    }

    static void UpsertStateEntry(JsonObject skillState, InstallTarget installTarget, DevFlowSkillDefinition skill, SkillBundle bundle)
    {
        RemoveStateEntry(skillState, installTarget, skill.Id);
        var entries = (JsonArray)skillState["entries"]!;

        var files = new JsonArray();
        foreach (var file in bundle.Files)
        {
            AddJsonObject(files, new JsonObject
            {
                ["path"] = NormalizeRelativePath(file.RelativePath).Replace(Path.DirectorySeparatorChar, '/'),
                ["hash"] = file.Hash
            });
        }

        AddJsonObject(entries, new JsonObject
        {
            ["skillId"] = skill.Id,
            ["skillVersion"] = bundle.Version,
            ["scope"] = installTarget.Scope,
            ["target"] = installTarget.TargetKind,
            ["targetPath"] = installTarget.GetDisplayPath(skill.Id),
            ["contentHash"] = bundle.ContentHash,
            ["installedByCliVersion"] = GetCurrentCliVersion(),
            ["installedByBundleHash"] = bundle.ContentHash,
            ["files"] = files
        });
    }

    static void RemoveStateEntry(JsonObject skillState, InstallTarget installTarget, string skillId)
    {
        if (skillState["entries"] is not JsonArray entries)
            return;

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i] is JsonObject entry &&
                string.Equals(GetString(entry, "skillId"), skillId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetString(entry, "scope"), installTarget.Scope, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetString(entry, "target"), installTarget.TargetKind, StringComparison.OrdinalIgnoreCase))
            {
                entries.RemoveAt(i);
            }
        }
    }

    static IReadOnlyList<InstallTarget> ResolveInstallTargets(string scope, string target, string? customPath, bool allowAll)
    {
        var workspaceRoot = Path.GetFullPath(FindWorkspaceRoot(Directory.GetCurrentDirectory()));
        var scopes = ResolveScopes(scope, allowAll);
        var customTargetDirectory = string.IsNullOrWhiteSpace(customPath) ? null : ResolveCustomTargetDirectory(customPath);
        var targets = new List<InstallTarget>();

        foreach (var resolvedScope in scopes)
        {
            var root = resolvedScope == "user" ? GetUserRoot() : workspaceRoot;
            var (targetKind, targetDirectory) = ResolveInstallTargetDirectory(root, target, customTargetDirectory);
            targets.Add(CreateInstallTarget(resolvedScope, targetKind, targetDirectory, workspaceRoot));
        }

        return targets;
    }

    static (string TargetKind, string TargetDirectory) ResolveInstallTargetDirectory(string rootDirectory, string target, string? customTargetDirectory)
    {
        if (customTargetDirectory != null)
            return (CreateCustomTargetKind(customTargetDirectory), customTargetDirectory);

        if (string.Equals(target, AutoTarget, StringComparison.OrdinalIgnoreCase))
            return InferTargetDirectory(rootDirectory);

        return (target, ResolveTargetDirectory(target));
    }

    static (string TargetKind, string TargetDirectory) InferTargetDirectory(string rootDirectory)
    {
        foreach (var (targetKind, targetDirectory) in s_targetDirectories)
        {
            if (ContainsCurrentDevFlowSkill(rootDirectory, targetDirectory))
                return (targetKind, targetDirectory);
        }

        foreach (var (targetKind, targetDirectory) in s_targetDirectories)
        {
            if (ContainsAnySkillDirectory(rootDirectory, targetDirectory))
                return (targetKind, targetDirectory);
        }

        return (DefaultTarget, s_targetDirectories[DefaultTarget]);
    }

    static bool ContainsCurrentDevFlowSkill(string rootDirectory, string targetDirectory)
        => s_skills.Any(skill => Directory.Exists(Path.Combine(rootDirectory, targetDirectory, skill.Id)));

    static bool ContainsAnySkillDirectory(string rootDirectory, string targetDirectory)
    {
        var directory = Path.Combine(rootDirectory, targetDirectory);
        if (!Directory.Exists(directory))
            return false;

        try
        {
            return Directory.EnumerateDirectories(directory).Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    static IReadOnlyList<InstallTarget> GetLegacyCleanupTargets(IReadOnlyList<InstallTarget> installTargets)
    {
        var targets = new List<InstallTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var installTarget in installTargets)
        {
            AddLegacyCleanupTarget(targets, seen, installTarget);

            foreach (var (targetKind, targetDirectory) in s_targetDirectories)
                AddLegacyCleanupTarget(targets, seen, CreateInstallTarget(installTarget.Scope, targetKind, targetDirectory, installTarget.WorkspaceRoot));

            if (installTarget.Scope != "user")
                AddLegacyCleanupTarget(targets, seen, CreateInstallTarget("user", installTarget.TargetKind, installTarget.RelativeSkillDirectory, installTarget.WorkspaceRoot));
        }

        var workspaceRoot = installTargets.Count > 0 ? installTargets[0].WorkspaceRoot : Path.GetFullPath(FindWorkspaceRoot(Directory.GetCurrentDirectory()));
        foreach (var (targetKind, targetDirectory) in s_targetDirectories)
            AddLegacyCleanupTarget(targets, seen, CreateInstallTarget("user", targetKind, targetDirectory, workspaceRoot));

        return targets;
    }

    static void AddLegacyCleanupTarget(List<InstallTarget> targets, HashSet<string> seen, InstallTarget target)
    {
        var key = string.Join('\u001f', target.Scope, target.TargetKind, target.RootDirectory, target.RelativeSkillDirectory);
        if (seen.Add(key))
            targets.Add(target);
    }

    static bool HasLegacySkillData(InstallTarget installTarget)
    {
        if (s_legacySkillIds.Any(skillId => Directory.Exists(GetSkillDirectory(installTarget, skillId))))
            return true;

        if (!File.Exists(installTarget.StateFilePath))
            return false;

        var skillState = ReadSkillState(installTarget.StateFilePath);
        return s_legacySkillIds.Any(skillId => FindStateEntry(skillState, installTarget, skillId) != null);
    }

    static InstallTarget CreateInstallTarget(string scope, string targetKind, string targetDirectory, string workspaceRoot)
    {
        var root = scope == "user" ? GetUserRoot() : workspaceRoot;
        return new InstallTarget(
            scope,
            targetKind,
            root,
            targetDirectory,
            ResolveStateFilePath(workspaceRoot, scope, targetKind),
            workspaceRoot);
    }

    static string ResolveStateFilePath(string workspaceRoot, string scope, string target)
    {
        var stateRoot = GetDevFlowStateRoot();
        var fileName = $"{target.ToLowerInvariant()}.json";
        return scope == "user"
            ? Path.Combine(stateRoot, "user", "skills", fileName)
            : Path.Combine(stateRoot, "workspaces", GetWorkspaceStateId(workspaceRoot), "skills", fileName);
    }

    static IReadOnlyList<string> ResolveScopes(string scope, bool allowAll)
    {
        if (string.Equals(scope, "project", StringComparison.OrdinalIgnoreCase))
            return ["project"];
        if (string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase))
            return ["user"];
        if (string.Equals(scope, "both", StringComparison.OrdinalIgnoreCase) ||
            (allowAll && string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)))
            return ["project", "user"];

        throw new InvalidOperationException(allowAll
            ? "Scope must be project, user, both, or all."
            : "Scope must be project, user, or both.");
    }

    static string ResolveTargetDirectory(string target)
    {
        if (s_targetDirectories.TryGetValue(target, out var directory))
            return directory;

        throw new InvalidOperationException($"Target must be {AutoTarget} or one of: {string.Join(", ", s_targetDirectories.Keys)}.");
    }

    static string ResolveCustomTargetDirectory(string customPath)
    {
        var normalized = NormalizeRelativePath(customPath.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidOperationException("Custom skill path must not be empty.");
        if (Path.IsPathRooted(normalized))
            throw new InvalidOperationException("Custom skill path must be relative to the selected scope root.");

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
            throw new InvalidOperationException("Custom skill path must not contain . or .. path segments.");

        return Path.Combine(segments);
    }

    static string CreateCustomTargetKind(string targetDirectory)
    {
        var normalized = targetDirectory.Replace(Path.DirectorySeparatorChar, '/');
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16].ToLowerInvariant();
        return $"path-{hash}";
    }

    static string FindWorkspaceRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;

            current = current.Parent;
        }

        return startDirectory;
    }

    static string GetUserRoot()
    {
        if (!string.IsNullOrWhiteSpace(UserRootOverrideForTests))
            return Path.GetFullPath(UserRootOverrideForTests);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        throw new InvalidOperationException("Cannot determine the user profile directory for user-scope skill installation.");
    }

    static string GetDevFlowStateRoot()
    {
        var root = StateRootOverrideForTests;
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetEnvironmentVariable(StateRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(GetUserRoot(), ".maui", "devflow");

        return Path.GetFullPath(root);
    }

    static string GetWorkspaceStateId(string workspaceRoot)
    {
        var normalized = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    static string GetSkillDirectory(InstallTarget installTarget, string skillId)
        => Path.Combine(installTarget.RootDirectory, installTarget.RelativeSkillDirectory, skillId);

    static DevFlowSkillDefinition? TryGetSkill(string skillId)
        => s_skills.FirstOrDefault(skill => string.Equals(skill.Id, skillId, StringComparison.OrdinalIgnoreCase));

    static DevFlowSkillDefinition GetSkill(string skillId)
        => TryGetSkill(skillId)
           ?? throw new InvalidOperationException($"Unknown DevFlow skill '{skillId}'.");

    static bool IsLegacySkill(string skillId)
        => s_legacySkillIds.Contains(skillId, StringComparer.OrdinalIgnoreCase);

    static string NormalizeRelativePath(string path)
        => path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

    static string NormalizeSkillRelativePath(string path)
        => NormalizeRelativePath(path).Replace(Path.DirectorySeparatorChar, '/');

    static bool TryResolveSkillFilePath(string skillDirectory, string relativePath, out string filePath)
    {
        filePath = string.Empty;

        try
        {
            var skillRoot = Path.GetFullPath(skillDirectory);
            if (!Path.EndsInDirectorySeparator(skillRoot))
                skillRoot += Path.DirectorySeparatorChar;

            filePath = Path.GetFullPath(Path.Combine(skillDirectory, NormalizeRelativePath(relativePath)));
            return filePath.StartsWith(skillRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    static string HashContent(string content)
        => HashString(content.ReplaceLineEndings("\n"));

    static string HashBundle(IReadOnlyList<SkillAssetFile> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(file.RelativePath.Replace(Path.DirectorySeparatorChar, '/'));
            builder.Append('\n');
            builder.Append(file.Hash);
            builder.Append('\n');
        }

        return HashString(builder.ToString());
    }

    static string HashBundleSet(IEnumerable<(DevFlowSkillDefinition Skill, SkillBundle Bundle)> skillBundles)
    {
        var builder = new StringBuilder();
        foreach (var (skill, bundle) in skillBundles.OrderBy(item => item.Skill.Id, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(skill.Id);
            builder.Append('\n');
            builder.Append(bundle.ContentHash);
            builder.Append('\n');
        }

        return HashString(builder.ToString());
    }

    static string HashString(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "sha256-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    static string GetCurrentCliVersion()
        => typeof(DevFlowSkillManager).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? typeof(DevFlowSkillManager).Assembly.GetName().Version?.ToString()
           ?? "unknown";

    static string? GetString(JsonObject node, string propertyName)
        => node.TryGetPropertyValue(propertyName, out var value) ? value?.GetValue<string>() : null;

    static DateTimeOffset GetUtcNow()
        => UtcNowOverrideForTests ?? DateTimeOffset.UtcNow;

    static DateTimeOffset? GetDateTimeOffset(JsonObject node, string propertyName)
    {
        var value = GetString(node, propertyName);
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    static void MarkSkillStateChecked(JsonObject skillState, string bundleHash)
    {
        var now = GetUtcNow().ToString("O");
        skillState["lastCheckedUtc"] = now;
        skillState["lastCheckedCliVersion"] = GetCurrentCliVersion();
        skillState["lastCheckedBundleHash"] = bundleHash;
    }

    static bool ShouldShowFreshnessHint(JsonObject skillState, DateTimeOffset now)
    {
        var lastChecked = GetDateTimeOffset(skillState, "lastCheckedUtc");
        var lastCheckedCliVersion = GetString(skillState, "lastCheckedCliVersion");
        var checkedRecently = lastChecked.HasValue && now - lastChecked.Value < FreshnessCheckInterval;
        var checkedWithCurrentCli = !string.IsNullOrWhiteSpace(lastCheckedCliVersion) &&
            VersionsEquivalent(lastCheckedCliVersion, GetCurrentCliVersion());
        if (checkedRecently && checkedWithCurrentCli)
            return false;

        var lastPrompted = GetDateTimeOffset(skillState, "lastPromptedUtc");
        return !lastPrompted.HasValue || now - lastPrompted.Value >= FreshnessPromptInterval;
    }

    static int CompareVersionLike(string? left, string? right)
    {
        if (TryParseSemanticVersion(left, out var leftVersion) &&
            TryParseSemanticVersion(right, out var rightVersion))
            return leftVersion.CompareTo(rightVersion);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    static bool VersionsEquivalent(string left, string right)
        => CompareVersionLike(left, right) == 0;

    static bool TryParseSemanticVersion(string? value, out ComparableSemanticVersion version)
    {
        version = default!;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        var buildMetadataIndex = normalized.IndexOf('+');
        if (buildMetadataIndex >= 0)
            normalized = normalized[..buildMetadataIndex];

        string[] prereleaseIdentifiers = [];
        var prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            var prerelease = normalized[(prereleaseIndex + 1)..];
            if (string.IsNullOrWhiteSpace(prerelease))
                return false;

            prereleaseIdentifiers = prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries);
            normalized = normalized[..prereleaseIndex];
        }

        var releaseParts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (releaseParts.Length == 0 || releaseParts.Length > 4)
            return false;

        var releaseNumbers = new List<int>(releaseParts.Length);
        foreach (var part in releaseParts)
        {
            if (!int.TryParse(part, out var number) || number < 0)
                return false;

            releaseNumbers.Add(number);
        }

        while (releaseNumbers.Count < 3)
            releaseNumbers.Add(0);

        version = new ComparableSemanticVersion(releaseNumbers.ToArray(), prereleaseIdentifiers);
        return true;
    }

    static string? TryGetLocalToolVersion(string workspaceRoot)
    {
        var current = new DirectoryInfo(workspaceRoot);
        while (current != null)
        {
            var manifestPath = Path.Combine(current.FullName, ".config", "dotnet-tools.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifest = CliJson.ParseNode(File.ReadAllText(manifestPath)) as JsonObject;
                    if (manifest?["tools"] is JsonObject tools)
                    {
                        foreach (var tool in tools)
                        {
                            if (!string.Equals(tool.Key, PackageId, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (tool.Value is JsonObject toolObject)
                                return GetString(toolObject, "version");
                        }
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    static void AddScopeConflictWarnings(JsonArray warnings, string target)
    {
        if (!s_targetDirectories.ContainsKey(target) &&
            !string.Equals(target, AutoTarget, StringComparison.OrdinalIgnoreCase))
            return;

        var projectTarget = ResolveInstallTargets("project", target, customPath: null, allowAll: true)[0];
        var userTarget = ResolveInstallTargets("user", target, customPath: null, allowAll: true)[0];
        var projectState = ReadSkillState(projectTarget.StateFilePath);
        var userState = ReadSkillState(userTarget.StateFilePath);

        foreach (var skill in s_skills)
        {
            var projectEntry = FindStateEntry(projectState, projectTarget, skill.Id);
            var userEntry = FindStateEntry(userState, userTarget, skill.Id);
            if (projectEntry == null || userEntry == null)
                continue;

            var projectHash = GetString(projectEntry, "contentHash");
            var userHash = GetString(userEntry, "contentHash");
            if (!string.Equals(projectHash, userHash, StringComparison.Ordinal))
                AddJsonString(warnings, $"{skill.Id} is installed in both project and user scope with different content. AI host precedence may choose a different version than expected.");
        }
    }

    static void AddStateFileWarnings(JsonArray warnings, string scope, string target, string? customPath)
    {
        foreach (var installTarget in ResolveInstallTargets(scope, target, customPath, allowAll: true))
        {
            var warning = GetStateFileWarning(installTarget.StateFilePath);
            if (!string.IsNullOrWhiteSpace(warning))
                AddJsonString(warnings, warning);
        }
    }

    static string? GetStateFileWarning(string stateFilePath)
    {
        if (!File.Exists(stateFilePath))
            return null;

        try
        {
            var contents = File.ReadAllText(stateFilePath);
            if (string.IsNullOrWhiteSpace(contents))
                return $"DevFlow skills state file '{stateFilePath}' is empty and will be rebuilt.";

            if (CliJson.ParseNode(contents) is not JsonObject)
                return $"DevFlow skills state file '{stateFilePath}' must contain a JSON object and will be rebuilt.";

            return null;
        }
        catch (JsonException)
        {
            return $"DevFlow skills state file '{stateFilePath}' is not valid JSON and will be rebuilt.";
        }
    }

    static void AddJsonObject(JsonArray array, JsonObject item)
        => array.Add((JsonNode)item);

    static void AddJsonString(JsonArray array, string value)
        => array.Add((JsonNode?)JsonValue.Create(value));

    static void PruneEmptyDirectories(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            return;

        foreach (var directory in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover empty directory is harmless.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup; a leftover empty directory is harmless.
            }
        }
    }

    sealed record DevFlowSkillDefinition(string Id, string DisplayName, string Description, bool Recommended);
    sealed record SkillAssetFile(string RelativePath, string Content, string Hash);
    sealed record SkillBundle(string Id, string Version, IReadOnlyList<SkillAssetFile> Files, string ContentHash);

    sealed record ComparableSemanticVersion(int[] ReleaseParts, string[] PrereleaseIdentifiers)
    {
        public int CompareTo(ComparableSemanticVersion other)
        {
            var maxReleaseParts = Math.Max(ReleaseParts.Length, other.ReleaseParts.Length);
            for (var i = 0; i < maxReleaseParts; i++)
            {
                var left = i < ReleaseParts.Length ? ReleaseParts[i] : 0;
                var right = i < other.ReleaseParts.Length ? other.ReleaseParts[i] : 0;
                var releaseComparison = left.CompareTo(right);
                if (releaseComparison != 0)
                    return releaseComparison;
            }

            if (PrereleaseIdentifiers.Length == 0 && other.PrereleaseIdentifiers.Length == 0)
                return 0;

            if (PrereleaseIdentifiers.Length == 0)
                return 1;

            if (other.PrereleaseIdentifiers.Length == 0)
                return -1;

            var maxPrereleaseIdentifiers = Math.Max(PrereleaseIdentifiers.Length, other.PrereleaseIdentifiers.Length);
            for (var i = 0; i < maxPrereleaseIdentifiers; i++)
            {
                if (i >= PrereleaseIdentifiers.Length)
                    return -1;

                if (i >= other.PrereleaseIdentifiers.Length)
                    return 1;

                var left = PrereleaseIdentifiers[i];
                var right = other.PrereleaseIdentifiers[i];
                var leftIsNumeric = long.TryParse(left, out var leftNumber);
                var rightIsNumeric = long.TryParse(right, out var rightNumber);

                int prereleaseComparison;
                if (leftIsNumeric && rightIsNumeric)
                    prereleaseComparison = leftNumber.CompareTo(rightNumber);
                else if (leftIsNumeric)
                    prereleaseComparison = -1;
                else if (rightIsNumeric)
                    prereleaseComparison = 1;
                else
                    prereleaseComparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

                if (prereleaseComparison != 0)
                    return prereleaseComparison;
            }

            return 0;
        }
    }

    sealed record InstallTarget(
        string Scope,
        string TargetKind,
        string RootDirectory,
        string RelativeSkillDirectory,
        string StateFilePath,
        string WorkspaceRoot)
    {
        public string GetDisplayPath(string skillId)
        {
            var path = Path.Combine(RelativeSkillDirectory, skillId).Replace(Path.DirectorySeparatorChar, '/');
            return Scope == "user" ? $"~/{path}" : path;
        }
    }

    internal sealed record SkillActionPrompt(
        string SkillId,
        string Action,
        string Status,
        string Path,
        string Message);
}
