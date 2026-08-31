using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Broker-owned lifecycle state for bounded XAML AutomationId source proposals. This store is
/// deliberately inert: it can only record propose/preview/reject transitions and cannot write,
/// apply, or roll back source files. Source apply is deferred to a later, out-of-band review.
/// </summary>
internal sealed class WorkflowXamlSourceProposalStore
{
    private readonly object _gate = new();
    private readonly WorkflowXamlSourceProposalStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, SourceRecord> _records = new(StringComparer.Ordinal);

    internal WorkflowXamlSourceProposalStore(
        WorkflowXamlSourceProposalStoreOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = options ?? new WorkflowXamlSourceProposalStoreOptions();
        _clock = clock ?? TimeProvider.System;
        if (_options.MaxProposals < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Source proposal store limits are invalid.");
    }

    internal WorkflowXamlSourceStoreResult Propose(MauiXamlSourceProposal proposal, bool agentOriginated = false)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (_gate)
        {
            if (!ValidateProposal(proposal, out var error))
                return WorkflowXamlSourceStoreResult.Failure("proposal-invalid", error!);

            var id = string.IsNullOrWhiteSpace(proposal.ProposalId)
                ? OpaqueId("xamlproposal")
                : proposal.ProposalId!;
            if (_records.TryGetValue(id, out var existing))
                return WorkflowXamlSourceStoreResult.Success(CreateSnapshot(existing));

            EvictTerminalLocked();
            if (_records.Count >= _options.MaxProposals)
            {
                return WorkflowXamlSourceStoreResult.Failure(
                    "proposal-capacity-exhausted",
                    "The broker source proposal store is full; resolve an existing proposal first.");
            }

            var now = _clock.GetUtcNow();
            var normalized = WithMetadata(
                proposal,
                id,
                revision: 1,
                state: MauiXamlSourceProposalStates.Proposed,
                createdAt: now);
            var record = new SourceRecord(normalized, MauiXamlSourceProposalStates.Proposed, 1, agentOriginated, now);
            _records.Add(id, record);
            return WorkflowXamlSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowXamlSourceStoreResult Get(string? proposalId)
    {
        lock (_gate)
        {
            return !TryGetLocked(proposalId, out var record, out var error)
                ? WorkflowXamlSourceStoreResult.Failure("proposal-not-found", error!)
                : WorkflowXamlSourceStoreResult.Success(CreateSnapshot(record!));
        }
    }

    internal WorkflowXamlSourceStoreResult Preview(string? proposalId)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowXamlSourceStoreResult.Failure("proposal-not-found", error!);
            if (record!.State == MauiXamlSourceProposalStates.Proposed)
                TransitionLocked(record, MauiXamlSourceProposalStates.Previewed);
            if (record.State != MauiXamlSourceProposalStates.Previewed)
            {
                return WorkflowXamlSourceStoreResult.Failure(
                    "proposal-not-previewable",
                    $"Source proposal state '{record.State}' cannot be previewed.");
            }
            return WorkflowXamlSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowXamlSourceStoreResult Reject(string? proposalId, string? reviewer, string? reasonCode)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowXamlSourceStoreResult.Failure("proposal-not-found", error!);
            if (IsTerminal(record!.State))
            {
                return WorkflowXamlSourceStoreResult.Failure(
                    "proposal-terminal",
                    $"Source proposal state '{record.State}' cannot be rejected.");
            }
            record.Reviewer = Bounded(reviewer, 256);
            record.ReasonCode = Bounded(reasonCode, 128) ?? "rejected";
            TransitionLocked(record, MauiXamlSourceProposalStates.Rejected);
            return WorkflowXamlSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    private static bool ValidateProposal(MauiXamlSourceProposal proposal, out string? error)
    {
        error = null;
        if (proposal.Schema != 1 ||
            proposal.Eligibility?.Eligible != true ||
            proposal.Operation is null ||
            proposal.Patch is null ||
            proposal.Element is null ||
            string.IsNullOrWhiteSpace(proposal.Operation.OperationId) ||
            string.IsNullOrWhiteSpace(proposal.Operation.FileRelativePath) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SourceHash) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SourceAnchor) ||
            !string.Equals(proposal.Operation.Attribute, "AutomationId", StringComparison.Ordinal) ||
            !MauiXamlAutomationIdGrammar.TryValidate(proposal.Operation.NewLiteral, out _) ||
            string.IsNullOrWhiteSpace(proposal.BaseContentDigest) ||
            string.IsNullOrWhiteSpace(proposal.PatchDigest) ||
            string.IsNullOrWhiteSpace(proposal.DiffDigest) ||
            string.IsNullOrWhiteSpace(proposal.Diff) ||
            proposal.Patch.Start is < 0 ||
            proposal.Patch.Length is < 0 ||
            proposal.Patch.Replacement is null ||
            !FixedEquals(proposal.BaseContentDigest, proposal.Patch.BeforeDigest))
        {
            error = "The source proposal must contain one eligible literal AutomationId operation, exact source bindings, and a deterministic patch.";
            return false;
        }
        return true;
    }

    private void EvictTerminalLocked()
    {
        while (_records.Count >= _options.MaxProposals)
        {
            var candidate = _records.Values
                .Where(static record => IsTerminal(record.State))
                .OrderBy(static record => record.UpdatedAt)
                .FirstOrDefault();
            if (candidate is null)
                return;
            _records.Remove(candidate.Proposal.ProposalId!);
        }
    }

    private static bool IsTerminal(string state) => state is
        MauiXamlSourceProposalStates.Rejected or
        MauiXamlSourceProposalStates.Stale;

    private void TransitionLocked(SourceRecord record, string state)
    {
        record.State = state;
        record.Revision++;
        record.UpdatedAt = _clock.GetUtcNow();
    }

    private bool TryGetLocked(string? proposalId, out SourceRecord? record, out string? error)
    {
        record = null;
        error = null;
        if (string.IsNullOrWhiteSpace(proposalId) || !_records.TryGetValue(proposalId, out record))
        {
            error = "The source proposal was not found.";
            return false;
        }
        return true;
    }

    private static WorkflowXamlSourceProposalSnapshot CreateSnapshot(SourceRecord record) => new()
    {
        Proposal = WithMetadata(
            record.Proposal,
            record.Proposal.ProposalId!,
            record.Revision,
            record.State,
            record.Proposal.CreatedAt ?? record.CreatedAt),
        State = record.State,
        Revision = record.Revision,
        AgentOriginated = record.AgentOriginated,
        Reviewer = record.Reviewer,
        ReasonCode = record.ReasonCode,
    };

    private static MauiXamlSourceProposal WithMetadata(
        MauiXamlSourceProposal source,
        string id,
        int revision,
        string state,
        DateTimeOffset createdAt)
    {
        var node = JsonSerializer.SerializeToNode(source, MauiTestingJsonContext.Default.MauiXamlSourceProposal)!.AsObject();
        node["proposalId"] = id;
        node["revision"] = revision;
        node["state"] = state;
        node["createdAt"] = createdAt;
        return node.Deserialize(MauiTestingJsonContext.Default.MauiXamlSourceProposal)
            ?? throw new InvalidOperationException("A source proposal could not be normalized.");
    }

    internal static string ComputeFlowReferencesDigest(
        IReadOnlyList<MauiXamlSourceFlowFollowUp>? flows)
    {
        var canonical = string.Join("\n", (flows ?? [])
            .OrderBy(flow => flow.FlowPath, StringComparer.Ordinal)
            .ThenBy(flow => flow.FlowId, StringComparer.Ordinal)
            .Select(flow => string.Join("|",
                flow.FlowPath ?? string.Empty,
                flow.FlowId ?? string.Empty,
                flow.FlowDigest ?? string.Empty,
                string.Join(",", flow.StepIds.OrderBy(static step => step, StringComparer.Ordinal)))));
        return Hash(canonical);
    }

    private static string OpaqueId(string prefix)
        => prefix + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string Hash(string? value)
        => "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static bool FixedEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           CryptographicOperations.FixedTimeEquals(
               Encoding.UTF8.GetBytes(left),
               Encoding.UTF8.GetBytes(right));

    private static string? Bounded(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : null;
    }

    private sealed class SourceRecord
    {
        public SourceRecord(
            MauiXamlSourceProposal proposal,
            string state,
            int revision,
            bool agentOriginated,
            DateTimeOffset createdAt)
        {
            Proposal = proposal;
            State = state;
            Revision = revision;
            AgentOriginated = agentOriginated;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public MauiXamlSourceProposal Proposal { get; }
        public string State { get; set; }
        public int Revision { get; set; }
        public bool AgentOriginated { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? Reviewer { get; set; }
        public string? ReasonCode { get; set; }
    }
}

internal sealed class WorkflowXamlSourceProposalStoreOptions
{
    public int MaxProposals { get; init; } = 128;
}

internal sealed class WorkflowXamlSourceProposalSnapshot
{
    public MauiXamlSourceProposal Proposal { get; init; } = new();
    public string State { get; init; } = "";
    public int Revision { get; init; }
    public bool AgentOriginated { get; init; }
    public string? Reviewer { get; init; }
    public string? ReasonCode { get; init; }
}

internal sealed class WorkflowXamlSourceStoreResult
{
    public bool Ok { get; private init; }
    public WorkflowXamlSourceProposalSnapshot? Proposal { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowXamlSourceStoreResult Success(WorkflowXamlSourceProposalSnapshot proposal)
        => new() { Ok = true, Proposal = proposal };

    public static WorkflowXamlSourceStoreResult Failure(
        string code,
        string error,
        WorkflowXamlSourceProposalSnapshot? proposal = null)
        => new() { Code = code, Error = error, Proposal = proposal };
}

/// <summary>
/// Append-only redacted history for source proposals. It intentionally records hashes and state
/// only: source excerpts, patch replacements, prompts, IDs, and reviewer names never leave the
/// in-memory proposal into this audit file.
/// </summary>
internal sealed class WorkflowXamlSourceHistoryStore
{
    internal const int MaxEntries = 256;
    internal const int MaxLineBytes = 16_384;
    internal const int MaxBytes = 1_048_576;
    private static readonly ConcurrentDictionary<string, object> Gates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _projectRoot;

    internal WorkflowXamlSourceHistoryStore(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    internal WorkflowXamlSourceHistoryAppendResult Append(
        WorkflowXamlSourceProposalSnapshot snapshot,
        string? state = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var proposal = snapshot.Proposal;
        if (!IsSafeProposalId(proposal.ProposalId))
        {
            return WorkflowXamlSourceHistoryAppendResult.Failure(
                "source-history-invalid",
                "The source proposal identity is invalid for a history path.");
        }
        if (!TryGetHistoryPath(proposal.ProposalId!, out var historyPath, out var error))
            return WorkflowXamlSourceHistoryAppendResult.Failure("source-history-invalid", error!);

        lock (Gates.GetOrAdd(historyPath!, static _ => new object()))
        {
            try
            {
                var existing = File.Exists(historyPath) ? File.ReadAllText(historyPath!) : string.Empty;
                if (Encoding.UTF8.GetByteCount(existing) > MaxBytes)
                {
                    return WorkflowXamlSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "The bounded source proposal history is too large to append safely.");
                }
                var lines = existing.ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                if (lines.Count >= MaxEntries)
                {
                    return WorkflowXamlSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "The bounded source proposal history has reached its entry limit.");
                }
                var previous = ValidateChain(lines, out error);
                if (error is not null)
                    return WorkflowXamlSourceHistoryAppendResult.Failure("source-history-invalid", error);

                var node = CreateSafeNode(snapshot, state ?? snapshot.State, lines.Count + 1, previous);
                var material = Canonicalize(node);
                node["hash"] = "sha256:" + Hash(material);
                var line = Canonicalize(node);
                if (Encoding.UTF8.GetByteCount(line) > MaxLineBytes)
                {
                    return WorkflowXamlSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "The source proposal history entry exceeds its bounded size.");
                }

                var content = existing;
                if (!string.IsNullOrEmpty(content) && !content.EndsWith('\n'))
                    content += "\n";
                content += line + "\n";
                if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
                {
                    return WorkflowXamlSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "Appending the source proposal history entry would exceed its size cap.");
                }

                AtomicWriteText(historyPath!, content);
                return WorkflowXamlSourceHistoryAppendResult.Success(historyPath!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return WorkflowXamlSourceHistoryAppendResult.Failure(
                    "source-history-write-failed",
                    "The redacted source proposal history could not be written.");
            }
        }
    }

    private bool TryGetHistoryPath(string proposalId, out string? path, out string? error)
    {
        path = null;
        error = null;
        try
        {
            if (!Directory.Exists(_projectRoot) || IsReparsePoint(_projectRoot))
            {
                error = "The project root is not a safe local directory.";
                return false;
            }
            var root = Path.Combine(_projectRoot, "maui-tests", ".devflow", "source-proposals");
            Directory.CreateDirectory(root);
            if (PathContainsReparsePoint(_projectRoot, root))
            {
                error = "The source proposal history directory cannot use a symbolic link or reparse point.";
                return false;
            }
            var candidate = Path.GetFullPath(Path.Combine(root, proposalId + ".history.jsonl"));
            if (!IsUnderRoot(candidate, root))
            {
                error = "The source proposal history path escapes its trusted root.";
                return false;
            }
            path = candidate;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = "The source proposal history root could not be resolved safely.";
            return false;
        }
    }

    private static JsonObject CreateSafeNode(
        WorkflowXamlSourceProposalSnapshot snapshot,
        string state,
        int sequence,
        string? previousHash)
    {
        var proposal = snapshot.Proposal;
        return new JsonObject
        {
            ["schema"] = 1,
            ["sequence"] = sequence,
            ["at"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["previousHash"] = previousHash,
            ["proposalId"] = Token(proposal.ProposalId),
            ["proposalRevision"] = snapshot.Revision,
            ["state"] = SafeState(state),
            ["operationId"] = Token(proposal.Operation.OperationId),
            ["fileDigest"] = Token(proposal.Operation.FileRelativePath),
            ["sourceHash"] = SafeDigest(proposal.Operation.SourceHash),
            ["sourceAnchor"] = SafeDigest(proposal.Operation.SourceAnchor),
            ["baseContentDigest"] = SafeDigest(proposal.BaseContentDigest),
            ["patchDigest"] = SafeDigest(proposal.PatchDigest),
            ["diffDigest"] = SafeDigest(proposal.DiffDigest),
            ["flowReferencesDigest"] = WorkflowXamlSourceProposalStore.ComputeFlowReferencesDigest(proposal.AffectedFlows),
            ["reviewerDigest"] = Token(snapshot.Reviewer),
            ["reasonCode"] = Token(snapshot.ReasonCode),
        };
    }

    private static string? ValidateChain(IReadOnlyList<string> lines, out string? error)
    {
        error = null;
        string? previous = null;
        foreach (var line in lines)
        {
            try
            {
                var node = JsonNode.Parse(line) as JsonObject;
                var hash = node?["hash"]?.GetValue<string>();
                var declaredPrevious = node?["previousHash"]?.GetValue<string>();
                if (node is null ||
                    !string.Equals(previous, declaredPrevious, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(hash))
                {
                    error = "The existing source proposal history hash chain is invalid.";
                    return null;
                }
                node.Remove("hash");
                var expected = "sha256:" + Hash(Canonicalize(node));
                if (!FixedEquals(hash, expected))
                {
                    error = "The existing source proposal history hash chain failed verification.";
                    return null;
                }
                previous = hash;
            }
            catch (JsonException)
            {
                error = "The existing source proposal history contains invalid JSON.";
                return null;
            }
            catch (InvalidOperationException)
            {
                error = "The existing source proposal history has an invalid safe projection.";
                return null;
            }
        }
        return previous;
    }

    private static void AtomicWriteText(string path, string content)
    {
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.bak");
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, backup, ignoreMetadataErrors: true);
                TryDelete(backup);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            TryDelete(temp);
            TryDelete(backup);
        }
    }

    private static string Canonicalize(JsonObject node)
        => node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string? Token(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : "sha256:" + Hash(value.Trim());

    private static string? SafeDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var candidate = value.Trim();
        var hex = candidate.StartsWith("sha256:", StringComparison.Ordinal) ? candidate[7..] : candidate;
        return hex.Length == 64 && hex.All(Uri.IsHexDigit)
            ? candidate.StartsWith("sha256:", StringComparison.Ordinal) ? candidate : "sha256:" + candidate
            : Token(candidate);
    }

    private static string SafeState(string value)
    {
        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            MauiXamlSourceProposalStates.Proposed,
            MauiXamlSourceProposalStates.Previewed,
            MauiXamlSourceProposalStates.Stale,
            MauiXamlSourceProposalStates.Rejected,
        };
        return known.Contains(value) ? value : Token(value)!;
    }

    private static bool IsSafeProposalId(string? value)
        => value is { Length: > 0 and <= 128 } &&
           value.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');

    private static bool IsUnderRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) &&
               !string.Equals(relative, "..", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool PathContainsReparsePoint(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
                return true;
        }
        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        var info = Directory.Exists(path)
            ? (FileSystemInfo)new DirectoryInfo(path)
            : new FileInfo(path);
        info.Refresh();
        return (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.LinkTarget is not null;
    }

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed class WorkflowXamlSourceHistoryAppendResult
{
    public bool Ok { get; private init; }
    public string? HistoryPath { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowXamlSourceHistoryAppendResult Success(string path)
        => new() { Ok = true, HistoryPath = path };

    public static WorkflowXamlSourceHistoryAppendResult Failure(string code, string error)
        => new() { Code = code, Error = error };
}
