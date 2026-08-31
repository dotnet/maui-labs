using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Writes the redacted, platform-neutral artifact manifest used by device flow-pilot hosts.
/// Platform runners add observed reports and artifact references; this helper owns first-attempt
/// accounting, path normalization, integrity metadata, omissions, and atomic persistence.
/// </summary>
internal sealed class FlowPilotArtifactManifest
{
    const int MaxArtifacts = 256;
    // A run id reaches a consumer's index and its file names. The finalizer bounds and validates it
    // the same way; see InferRunId.
    const int MaxRunIdLength = 96;
    readonly string _artifactRoot;
    readonly string _repositoryRoot;
    readonly string _manifestFileName;
    readonly List<MauiFlowArtifactReference> _artifactReferences = [];
    readonly Dictionary<string, FlowPilotFlowManifest> _flowsByDigest = new(StringComparer.Ordinal);

    FlowPilotArtifactManifest(FlowPilotManifestOptions options)
    {
        _artifactRoot = Path.GetFullPath(options.ArtifactRoot);
        _repositoryRoot = Path.GetFullPath(options.RepositoryRoot);
        _manifestFileName = string.IsNullOrWhiteSpace(options.ManifestFileName)
            ? "manifest.json"
            : options.ManifestFileName;

        Repository = new FlowPilotRepositoryManifest
        {
            Commit = FirstNonEmpty(options.RepositoryCommit, ResolveRepositoryCommit(_repositoryRoot), "unknown"),
        };
        Workflow = new FlowPilotWorkflowManifest
        {
            RunId = FirstNonEmpty(options.WorkflowRunId, ResolveWorkflowRunId(), "local"),
            Name = FirstNonEmpty(options.WorkflowName, Environment.GetEnvironmentVariable("GITHUB_WORKFLOW"), "local"),
            Attempt = FirstNonEmpty(options.WorkflowAttempt, Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT")),
        };
        Testing = new FlowPilotTestingManifest
        {
            PackageVersion = FirstNonEmpty(options.TestingPackageVersion, ResolveTestingPackageVersion(), "unknown"),
        };
        Platform = new FlowPilotPlatformManifest
        {
            Name = options.Platform,
            Experimental = options.Experimental,
            Backend = options.Backend,
            OfficialCoverage = options.OfficialCoverage,
            MacCatalystEquivalent = options.MacCatalystEquivalent,
            DeviceEvidence = new FlowPilotDeviceEvidenceManifest
            {
                Kind = ResolveDeviceEvidenceKind(options),
                RealDevice = IsRealDeviceEvidence(options),
            },
            // Stated from facts this manifest owns rather than copied out of a run report. The
            // report's device profile is a display descriptor such as "411x914@2.6", which the
            // report redactor treats as an identifier and drops, so reading it back always yielded
            // null and left the manifest unable to say which device it ran on. Downstream
            // consumers refuse a manifest with no device profile, so the omission silently
            // disqualified the whole lane.
            DeviceProfile = ResolveDeviceProfile(options),
            AndroidSdk = new FlowPilotAndroidSdkManifest
            {
                ApiLevel = options.AndroidApiLevel,
                AvdName = options.AndroidAvdName,
                Abi = options.AndroidAbi,
                Target = options.AndroidTarget,
                WorkloadVersion = options.DotNetWorkloadVersion,
            },
        };
        App = new FlowPilotAppManifest
        {
            PackageId = options.PackageId,
        };
        Privacy = new FlowPilotPrivacyManifest
        {
            ExcludedByDefault =
            [
                "screenshots",
                "source",
                "raw-model-context",
            ],
        };

        // Said once, by name, at the point the fact is known. A manifest that cannot identify the
        // device it ran on states that as an omission and fails its own validation rather than
        // publishing a placeholder kind that every consumer refuses.
        if (Platform.DeviceEvidence.Kind is null)
        {
            AddOmission(
                "device-evidence",
                "No accepted device evidence kind was supplied, so this run does not state which device produced its evidence.");
        }
    }

    [JsonPropertyName("schema")]
    public int Schema { get; } = 1;

    [JsonPropertyName("kind")]
    public string Kind { get; } = "devflow-flow-pilot";

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("repository")]
    public FlowPilotRepositoryManifest Repository { get; }

    [JsonPropertyName("workflow")]
    public FlowPilotWorkflowManifest Workflow { get; }

    [JsonPropertyName("testing")]
    public FlowPilotTestingManifest Testing { get; }

    [JsonPropertyName("platform")]
    public FlowPilotPlatformManifest Platform { get; }

    [JsonPropertyName("app")]
    public FlowPilotAppManifest App { get; }

    [JsonPropertyName("flows")]
    public List<FlowPilotFlowManifest> Flows { get; } = [];

    [JsonPropertyName("artifacts")]
    public List<FlowPilotArtifactManifestEntry> Artifacts { get; } = [];

    [JsonPropertyName("privacy")]
    public FlowPilotPrivacyManifest Privacy { get; }

    [JsonPropertyName("truncated")]
    public bool Truncated { get; private set; }

    [JsonPropertyName("truncation")]
    public FlowPilotTruncationManifest Truncation { get; } = new()
    {
        MaxArtifacts = MaxArtifacts,
    };

    [JsonPropertyName("omissions")]
    public List<FlowPilotManifestOmission> Omissions { get; } = [];

    [JsonPropertyName("validationErrors")]
    public List<string> ValidationErrors { get; } = [];

    [JsonIgnore]
    public string ArtifactRoot => _artifactRoot;

    public static FlowPilotArtifactManifest Create(FlowPilotManifestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ArtifactRoot))
            throw new ArgumentException("An artifact root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.RepositoryRoot))
            throw new ArgumentException("A repository root is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Platform))
            throw new ArgumentException("A platform is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.PackageId))
            throw new ArgumentException("A package ID is required.", nameof(options));
        if (!string.IsNullOrWhiteSpace(options.ManifestFileName) &&
            (!string.Equals(Path.GetFileName(options.ManifestFileName), options.ManifestFileName, StringComparison.Ordinal) ||
             !options.ManifestFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("ManifestFileName must be a top-level .json file name.", nameof(options));
        }

        return new FlowPilotArtifactManifest(options);
    }

    public FlowPilotFlowManifest RegisterFlow(MauiFlow flow, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
        if (_flowsByDigest.TryGetValue(digest, out var existing))
            return existing;

        var entry = new FlowPilotFlowManifest
        {
            Name = FirstNonEmpty(flow.Name, Path.GetFileNameWithoutExtension(sourcePath), "unnamed-flow"),
            Digest = $"sha256:{digest}",
            SourcePath = ToRepositoryRelativePath(sourcePath),
            Tier = "tier-1",
        };
        _flowsByDigest.Add(digest, entry);
        Flows.Add(entry);
        return entry;
    }

    public void RecordCleanAttempt(
        MauiFlow flow,
        string sourcePath,
        int repetition,
        MauiFlowRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (repetition < 1)
            throw new ArgumentOutOfRangeException(nameof(repetition));

        var flowEntry = RegisterFlow(flow, sourcePath);
        var attempt = CreateAttempt(report, "clean", repetition);
        flowEntry.CleanAttempts.Add(attempt);

        // Never assign this after its first value. Later clean repetitions and manually requested
        // diagnostics are supporting evidence, not a replacement for the first observed outcome.
        flowEntry.FirstAttempt ??= attempt;
        RecordReportMetadata(report, attempt);
    }

    public void RecordDiagnosticRerun(
        MauiFlow flow,
        string sourcePath,
        MauiFlowRunReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var flowEntry = RegisterFlow(flow, sourcePath);
        if (flowEntry.FirstAttempt is null)
            throw new InvalidOperationException("A diagnostic rerun requires a recorded first clean attempt.");

        var attempt = CreateAttempt(report, "diagnostic-rerun", flowEntry.DiagnosticReruns.Count + 1);
        flowEntry.DiagnosticReruns.Add(attempt);
        RecordReportMetadata(report, attempt);
    }

    public void RecordInfrastructureFailure(
        MauiFlow flow,
        string sourcePath,
        int repetition,
        string phase)
    {
        if (repetition < 1)
            throw new ArgumentOutOfRangeException(nameof(repetition));

        var flowEntry = RegisterFlow(flow, sourcePath);
        var attempt = new FlowPilotAttemptManifest
        {
            RunKind = "clean",
            Repetition = repetition,
            Outcome = MauiFlowRunOutcomes.InfrastructureError,
            FailureClass = MauiFlowFailureClasses.Infrastructure,
            FailureCode = MauiFlowFailureClasses.Infrastructure,
            FailurePhase = phase,
            Verified = false,
        };
        flowEntry.CleanAttempts.Add(attempt);
        flowEntry.FirstAttempt ??= attempt;
        AddOmission(
            "flow-run-report",
            $"No flow-run.json was produced because {Platform.Name ?? "platform"} infrastructure failed during {phase}.",
            flowEntry.Name);
    }

    public void AddOmission(string kind, string reason, string? flowName = null)
    {
        if (Omissions.Any(omission =>
                string.Equals(omission.Kind, kind, StringComparison.Ordinal) &&
                string.Equals(omission.Flow, flowName, StringComparison.Ordinal) &&
                string.Equals(omission.Reason, reason, StringComparison.Ordinal)))
        {
            return;
        }

        Omissions.Add(new FlowPilotManifestOmission
        {
            Kind = kind,
            Reason = reason,
            Flow = flowName,
        });
    }

    public void RecordArtifact(MauiFlowArtifactReference artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        _artifactReferences.Add(artifact);
    }

    public FlowPilotManifestValidation Validate()
    {
        var validation = new FlowPilotManifestValidation();
        var infrastructureOnly = Flows.Count > 0 &&
            Flows.All(flow => string.Equals(
                flow.FirstAttempt?.Outcome,
                MauiFlowRunOutcomes.InfrastructureError,
                StringComparison.Ordinal));
        if (Schema != 1)
            validation.Errors.Add("schema must be 1.");
        if (string.IsNullOrWhiteSpace(Repository.Commit))
            validation.Errors.Add("repository.commit is required.");
        if (string.IsNullOrWhiteSpace(Workflow.RunId))
            validation.Errors.Add("workflow.runId is required.");
        if (string.IsNullOrWhiteSpace(Testing.PackageVersion))
            validation.Errors.Add("testing.packageVersion is required.");
        if (string.IsNullOrWhiteSpace(Platform.Name))
            validation.Errors.Add("platform.name is required.");
        if (string.Equals(Platform.Name, "android", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(Platform.AndroidSdk.ApiLevel))
        {
            validation.Errors.Add("platform.androidSdk.apiLevel is required for Android.");
        }
        if (string.Equals(Platform.Name, "macos", StringComparison.Ordinal))
        {
            if (Platform.Experimental != true ||
                !string.Equals(Platform.Backend, "appkit", StringComparison.Ordinal) ||
                Platform.OfficialCoverage != false ||
                Platform.MacCatalystEquivalent != false)
            {
                validation.Errors.Add(
                    "Experimental AppKit manifests require experimental=true, backend=appkit, officialCoverage=false, and macCatalystEquivalent=false.");
            }
        }
        // The kinds accepted here are exactly the ones the finalizer and the qualification readers
        // credit. "unknown" used to be produced when nothing identified the device, which no
        // consumer accepts, so it is refused at the producer instead of published as evidence.
        if (!IsAcceptedDeviceEvidenceKind(Platform.DeviceEvidence.Kind))
        {
            validation.Errors.Add(
                Platform.DeviceEvidence.Kind is null
                    ? "platform.deviceEvidence.kind is required and must be physical-device, real-device, emulator, or desktop-host."
                    : "platform.deviceEvidence.kind must be physical-device, real-device, emulator, or desktop-host.");
        }
        if (Platform.DeviceEvidence.RealDevice == true &&
            Platform.DeviceEvidence.Kind is not "physical-device" and not "real-device")
        {
            validation.Errors.Add("platform.deviceEvidence.realDevice requires a physical-device kind.");
        }
        if (string.IsNullOrWhiteSpace(App.PackageId))
            validation.Errors.Add("app.packageId is required.");
        if (!infrastructureOnly && string.IsNullOrWhiteSpace(App.BuildFingerprint))
            validation.Errors.Add("app.buildFingerprint is required.");
        if (!infrastructureOnly && string.IsNullOrWhiteSpace(App.PackageDigest))
            validation.Errors.Add("app.packageDigest is required.");
        if (Flows.Count == 0)
            validation.Errors.Add("At least one flow is required.");
        if (!infrastructureOnly && Artifacts.Count == 0)
            validation.Errors.Add("At least one artifact is required.");

        foreach (var flow in Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.Digest))
                validation.Errors.Add($"Flow '{flow.Name}' is missing its digest.");
            if (flow.FirstAttempt is null)
                validation.Errors.Add($"Flow '{flow.Name}' is missing its first attempt.");
            else
            {
                if (!string.Equals(flow.FirstAttempt.RunKind, "clean", StringComparison.Ordinal))
                    validation.Errors.Add($"Flow '{flow.Name}' firstAttempt must be a clean attempt.");
                if (string.IsNullOrWhiteSpace(flow.FirstAttempt.Outcome))
                    validation.Errors.Add($"Flow '{flow.Name}' firstAttempt is missing its outcome.");
                if (!infrastructureOnly && string.IsNullOrWhiteSpace(flow.FirstAttempt.AppBuildFingerprint))
                    validation.Errors.Add($"Flow '{flow.Name}' firstAttempt is missing its app build fingerprint.");
                if (!infrastructureOnly && string.IsNullOrWhiteSpace(flow.FirstAttempt.ResetFingerprint))
                    validation.Errors.Add($"Flow '{flow.Name}' firstAttempt is missing its reset fingerprint.");
                if (!infrastructureOnly && string.IsNullOrWhiteSpace(flow.FirstAttempt.SeedFingerprint))
                    validation.Errors.Add($"Flow '{flow.Name}' firstAttempt is missing its seed fingerprint.");
            }
        }

        foreach (var artifact in Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifact.Path) ||
                string.IsNullOrWhiteSpace(artifact.Sha256) ||
                artifact.SizeBytes is null)
            {
                validation.Errors.Add("Every artifact requires a relative path, SHA-256, and size.");
            }
        }

        return validation;
    }

    public FlowPilotManifestWriteResult WriteAtomic()
    {
        try
        {
            PopulateArtifacts();
            var validation = Validate();
            ValidationErrors.Clear();
            ValidationErrors.AddRange(validation.Errors);

            Directory.CreateDirectory(_artifactRoot);
            var target = Path.Combine(_artifactRoot, _manifestFileName);
            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
                File.WriteAllBytes(temporary, bytes);
                File.Move(temporary, target, overwrite: true);
                return new FlowPilotManifestWriteResult
                {
                    Ok = true,
                    Path = target,
                    Validation = validation,
                };
            }
            finally
            {
                try { File.Delete(temporary); } catch { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new FlowPilotManifestWriteResult
            {
                Error = $"The flow-pilot manifest could not be written ({ex.GetType().Name}).",
            };
        }
    }

    void RecordReportMetadata(MauiFlowRunReport report, FlowPilotAttemptManifest attempt)
    {
        if (!string.IsNullOrWhiteSpace(report.ReportPath) &&
            IsUnderDirectory(Path.GetFullPath(report.ReportPath), _repositoryRoot))
        {
            attempt.ReportPath = ToRepositoryRelativePath(report.ReportPath);
        }

        var target = report.Target;
        if (target is not null)
        {
            App.BuildFingerprint ??= target.AppBuildFingerprint;
            App.PackageDigest ??= target.AppBuildFingerprint;
            App.PackageId = FirstNonEmpty(App.PackageId, target.AppId);
            Platform.DeviceId ??= FingerprintIdentifier(target.DeviceId);
            Platform.DeviceProfile ??= target.DeviceProfile ?? target.DisplayProfile;
            Platform.AgentInstanceId ??= target.AgentInstanceId;
        }

        attempt.ResetFingerprint = report.Reset?.ResetIdentity;
        attempt.SeedFingerprint = report.Reset?.SeedFingerprint ?? report.Preconditions?.Observed?.SeedFingerprint;
        attempt.BackendStateFingerprint = report.Reset?.BackendStateFingerprint ?? report.Preconditions?.Observed?.BackendStateFingerprint;
        attempt.AppBuildFingerprint = target?.AppBuildFingerprint;
        attempt.AgentInstanceId = target?.AgentInstanceId;

        foreach (var artifact in report.Artifacts)
            _artifactReferences.Add(artifact);

        if (!report.Artifacts.Any(static artifact =>
                string.Equals(artifact.Kind, "android-host-diagnostics", StringComparison.Ordinal) ||
                string.Equals(artifact.Kind, "host-diagnostics", StringComparison.Ordinal)))
        {
            AddOmission(
                "host-diagnostics",
                $"Bounded {Platform.Name ?? "platform"} host diagnostics were unavailable for this terminal outcome.",
                report.LegacyFlowIdentity);
        }

        if (IsFailure(report.Outcome?.Status) &&
            !report.Artifacts.Any(static artifact => string.Equals(artifact.Kind, "mauitrace", StringComparison.Ordinal)))
        {
            AddOmission(
                "failure-evidence",
                "No failure .mauitrace was available for this terminal outcome.",
                report.LegacyFlowIdentity);
        }
    }

    static FlowPilotAttemptManifest CreateAttempt(
        MauiFlowRunReport report,
        string runKind,
        int repetition)
        => new()
        {
            RunId = report.RunId,
            RunKind = runKind,
            Repetition = repetition,
            Outcome = report.Outcome?.Status ?? "unknown",
            Verified = report.Outcome?.Verified,
            ReportPath = null,
            ReportDigest = report.ReportDigest,
            FailureClass = report.Failure?.Class,
            FailureCode = report.Failure?.Code,
            FailurePhase = report.Failure?.Phase,
        };

    void PopulateArtifacts()
    {
        Artifacts.Clear();
        var recordedRunIds = CollectRecordedRunIds();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in _artifactReferences)
        {
            if (Artifacts.Count >= MaxArtifacts)
            {
                // Every reference past the cap is counted. Recording only the first told a
                // consumer that one reference was dropped when hundreds may have been.
                Truncated = true;
                Truncation.OmittedArtifacts++;
                AddOmission("artifact-limit", $"Only the first {MaxArtifacts} artifact references were hashed.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(reference.Path))
            {
                AddOmission("artifact-path", "An artifact reference did not include a path.");
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(reference.Path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                AddOmission("artifact-path", "An artifact reference had an invalid path.");
                continue;
            }

            if (!IsUnderDirectory(fullPath, _repositoryRoot))
            {
                AddOmission("artifact-path", "An artifact outside the repository was excluded.");
                continue;
            }
            if (!File.Exists(fullPath))
            {
                AddOmission("artifact-missing", "An expected artifact was unavailable when the manifest was finalized.");
                continue;
            }

            var relativePath = ToRepositoryRelativePath(fullPath);
            if (!seen.Add(relativePath))
                continue;

            var measured = MeasureFile(fullPath);
            if (measured is null)
            {
                AddOmission("artifact-hash", "An artifact could not be hashed.");
                continue;
            }

            Artifacts.Add(new FlowPilotArtifactManifestEntry
            {
                Kind = reference.Kind,
                Path = relativePath,
                Sha256 = measured.Value.Sha256,
                SizeBytes = measured.Value.SizeBytes,
                MediaType = reference.MediaType,
                Redacted = reference.Redacted,
                RunId = InferRunId(fullPath, recordedRunIds),
            });
        }
    }

    /// <summary>
    /// The run a piece of evidence belongs to is the first directory below the artifact root - but
    /// only when that directory is a run at all. The shipped layout also puts run-independent
    /// evidence in fixed directories directly below the root, so "host-diagnostics/summary.json"
    /// was credited to a run named "host-diagnostics" that no attempt in this manifest matches.
    /// <c>Finalize-DevFlowFlowPilotManifest.ps1</c> refuses exactly the same segments: the two
    /// write the same artifacts[] entries, and a rule they disagreed on would make the finalized
    /// manifest contradict the one the test process published.
    /// </summary>
    string? InferRunId(string fullPath, IReadOnlySet<string> recordedRunIds)
    {
        var relativeToArtifacts = Path.GetRelativePath(_artifactRoot, fullPath);
        if (relativeToArtifacts.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeToArtifacts))
            return null;

        var segments = relativeToArtifacts.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Length <= 1)
            return null;

        var candidate = segments[0];
        if (!IsPublishableRunId(candidate))
            return null;

        return recordedRunIds.Contains(candidate) ? candidate : null;
    }

    IReadOnlySet<string> CollectRecordedRunIds()
    {
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in Flows)
        {
            foreach (var attempt in flow.CleanAttempts.Concat(flow.DiagnosticReruns).Append(flow.FirstAttempt))
            {
                if (attempt?.RunId is { } runId && IsPublishableRunId(runId))
                    runIds.Add(runId);
            }
        }

        return runIds;
    }

    /// <summary>
    /// A run id is published as evidence and is read back as a directory name, so it is accepted
    /// only when it is a bounded, plain identifier. This is the same rule the finalizer applies.
    /// </summary>
    static bool IsPublishableRunId(string? runId)
        => !string.IsNullOrWhiteSpace(runId) &&
            runId.Length <= MaxRunIdLength &&
            char.IsAsciiLetterOrDigit(runId[0]) &&
            runId.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    string ToRepositoryRelativePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.GetRelativePath(_repositoryRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    static bool IsUnderDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// The digest and the size describe one read of one file. Stat-then-hash published the length
    /// the file had a moment ago beside a hash of the bytes it has now, which is a pair nothing on
    /// disk matches - and a consumer that verifies this manifest refuses the whole run over it.
    /// The finalizer measures the same way.
    /// </summary>
    static (string Sha256, long SizeBytes)? MeasureFile(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var hash = SHA256.Create();
            var digest = hash.ComputeHash(stream);
            return ($"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}", stream.Position);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static string? FingerprintIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
            value.Length == 71 &&
            value.AsSpan(7).ToString().All(Uri.IsHexDigit))
        {
            return "sha256:" + value[7..].ToLowerInvariant();
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";
    }

    static bool IsFailure(string? outcome)
        => !string.Equals(outcome, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal);

    static string ResolveTestingPackageVersion()
    {
        var assembly = typeof(MauiFlowRunner).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return FirstNonEmpty(
            informational?.Split('+', 2)[0],
            assembly.GetName().Version?.ToString(),
            "unknown");
    }

    static string ResolveWorkflowRunId()
    {
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        var attempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT");
        if (!string.IsNullOrWhiteSpace(runId))
            return string.IsNullOrWhiteSpace(attempt) ? runId : $"{runId}-{attempt}";

        return $"local-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    static string ResolveRepositoryCommit(string repositoryRoot)
    {
        var configured = Environment.GetEnvironmentVariable("GITHUB_SHA")
            ?? Environment.GetEnvironmentVariable("DEVFLOW_FLOW_PILOT_REPOSITORY_COMMIT");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", $"-C \"{repositoryRoot}\" rev-parse HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null || !process.WaitForExit(10_000) || process.ExitCode != 0)
                return "unknown";

            var commit = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(commit) ? "unknown" : commit;
        }
        catch
        {
            return "unknown";
        }
    }

    static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    static bool IsAcceptedDeviceEvidenceKind(string? kind) =>
        kind is "physical-device" or "real-device" or "emulator" or "desktop-host";

    /// <summary>
    /// Returns an accepted device evidence kind, or <see langword="null"/> when nothing this
    /// manifest holds identifies the device. "unknown" used to be produced here and then rejected
    /// by this manifest's own validator, so a run could publish a device evidence kind no consumer
    /// credits while the producer reported it as a value it had chosen.
    /// </summary>
    static string? ResolveDeviceEvidenceKind(FlowPilotManifestOptions options)
    {
        var configured = options.DeviceEvidenceKind?.Trim().ToLowerInvariant();
        if (IsAcceptedDeviceEvidenceKind(configured))
            return configured!;
        // An AVD name is evidence that an emulator really ran. Nothing else may invent a kind.
        return string.IsNullOrWhiteSpace(options.AndroidAvdName) ? null : "emulator";
    }

    static bool IsRealDeviceEvidence(FlowPilotManifestOptions options) =>
        options.RealDevice == true &&
        ResolveDeviceEvidenceKind(options) is "physical-device" or "real-device";

    /// <summary>
    /// Names the device this manifest covers, using only identifier-safe facts it already holds.
    /// </summary>
    static string ResolveDeviceProfile(FlowPilotManifestOptions options)
    {
        var kind = ResolveDeviceEvidenceKind(options) ?? "unidentified";
        var name = options.AndroidAvdName?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? $"{options.Platform}-{kind}"
            : $"{options.Platform}-{kind}:{name}";
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}

internal sealed class FlowPilotManifestOptions
{
    public required string ArtifactRoot { get; init; }
    public required string RepositoryRoot { get; init; }
    public required string Platform { get; init; }
    public required string PackageId { get; init; }
    public string? RepositoryCommit { get; init; }
    public string? WorkflowRunId { get; init; }
    public string? WorkflowName { get; init; }
    public string? WorkflowAttempt { get; init; }
    public string? TestingPackageVersion { get; init; }
    public string? AndroidApiLevel { get; init; }
    public string? AndroidAvdName { get; init; }
    public string? AndroidAbi { get; init; }
    public string? AndroidTarget { get; init; }
    public string? DotNetWorkloadVersion { get; init; }
    public string? DeviceEvidenceKind { get; init; }
    public bool? RealDevice { get; init; }
    public bool? Experimental { get; init; }
    public string? Backend { get; init; }
    public bool? OfficialCoverage { get; init; }
    public bool? MacCatalystEquivalent { get; init; }
    /// <summary>
    /// Optional top-level manifest file name. It is limited to a single JSON file under the
    /// fixture artifact root so a platform host cannot replace the script-owned manifest.
    /// </summary>
    public string? ManifestFileName { get; init; }
}

internal sealed class FlowPilotRepositoryManifest
{
    [JsonPropertyName("commit")]
    public string? Commit { get; set; }
}

internal sealed class FlowPilotWorkflowManifest
{
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("attempt")]
    public string? Attempt { get; set; }
}

internal sealed class FlowPilotTestingManifest
{
    [JsonPropertyName("packageVersion")]
    public string? PackageVersion { get; set; }
}

internal sealed class FlowPilotPlatformManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("experimental")]
    public bool? Experimental { get; set; }

    [JsonPropertyName("backend")]
    public string? Backend { get; set; }

    [JsonPropertyName("officialCoverage")]
    public bool? OfficialCoverage { get; set; }

    [JsonPropertyName("macCatalystEquivalent")]
    public bool? MacCatalystEquivalent { get; set; }

    [JsonPropertyName("androidSdk")]
    public FlowPilotAndroidSdkManifest AndroidSdk { get; set; } = new();

    [JsonPropertyName("deviceEvidence")]
    public FlowPilotDeviceEvidenceManifest DeviceEvidence { get; set; } = new();

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("deviceProfile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("agentInstanceId")]
    public string? AgentInstanceId { get; set; }
}

internal sealed class FlowPilotDeviceEvidenceManifest
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("realDevice")]
    public bool? RealDevice { get; set; }
}

internal sealed class FlowPilotAndroidSdkManifest
{
    [JsonPropertyName("apiLevel")]
    public string? ApiLevel { get; set; }

    [JsonPropertyName("avdName")]
    public string? AvdName { get; set; }

    [JsonPropertyName("abi")]
    public string? Abi { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("workloadVersion")]
    public string? WorkloadVersion { get; set; }
}

internal sealed class FlowPilotAppManifest
{
    [JsonPropertyName("packageId")]
    public string? PackageId { get; set; }

    [JsonPropertyName("buildFingerprint")]
    public string? BuildFingerprint { get; set; }

    [JsonPropertyName("packageDigest")]
    public string? PackageDigest { get; set; }
}

internal sealed class FlowPilotFlowManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("sourcePath")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("tier")]
    public string? Tier { get; init; }

    [JsonPropertyName("firstAttempt")]
    public FlowPilotAttemptManifest? FirstAttempt { get; set; }

    [JsonPropertyName("cleanAttempts")]
    public List<FlowPilotAttemptManifest> CleanAttempts { get; } = [];

    [JsonPropertyName("diagnosticReruns")]
    public List<FlowPilotAttemptManifest> DiagnosticReruns { get; } = [];
}

internal sealed class FlowPilotAttemptManifest
{
    [JsonPropertyName("runId")]
    public string? RunId { get; set; }

    [JsonPropertyName("runKind")]
    public string? RunKind { get; set; }

    [JsonPropertyName("repetition")]
    public int? Repetition { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("verified")]
    public bool? Verified { get; set; }

    [JsonPropertyName("reportPath")]
    public string? ReportPath { get; set; }

    [JsonPropertyName("reportDigest")]
    public string? ReportDigest { get; set; }

    [JsonPropertyName("failureClass")]
    public string? FailureClass { get; set; }

    [JsonPropertyName("failureCode")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failurePhase")]
    public string? FailurePhase { get; set; }

    [JsonPropertyName("resetFingerprint")]
    public string? ResetFingerprint { get; set; }

    [JsonPropertyName("seedFingerprint")]
    public string? SeedFingerprint { get; set; }

    [JsonPropertyName("backendStateFingerprint")]
    public string? BackendStateFingerprint { get; set; }

    [JsonPropertyName("appBuildFingerprint")]
    public string? AppBuildFingerprint { get; set; }

    [JsonPropertyName("agentInstanceId")]
    public string? AgentInstanceId { get; set; }
}

internal sealed class FlowPilotArtifactManifestEntry
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("redacted")]
    public bool? Redacted { get; set; }

    [JsonPropertyName("runId")]
    public string? RunId { get; set; }
}

internal sealed class FlowPilotPrivacyManifest
{
    [JsonPropertyName("excludedByDefault")]
    public List<string> ExcludedByDefault { get; init; } = [];
}

internal sealed class FlowPilotTruncationManifest
{
    [JsonPropertyName("maxArtifacts")]
    public int MaxArtifacts { get; init; }

    [JsonPropertyName("omittedArtifacts")]
    public int OmittedArtifacts { get; set; }
}

internal sealed class FlowPilotManifestOmission
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("flow")]
    public string? Flow { get; init; }
}

internal sealed class FlowPilotManifestValidation
{
    public List<string> Errors { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

internal sealed class FlowPilotManifestWriteResult
{
    public bool Ok { get; init; }
    public string? Path { get; init; }
    public string? Error { get; init; }
    public FlowPilotManifestValidation? Validation { get; init; }
}
