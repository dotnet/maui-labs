using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Broker-owned lifecycle for reviewed C# source proposals. This store is deliberately inert:
/// it can only record propose/preview/reject transitions and cannot read, write, or revert
/// source files. Source apply is deferred to a later, out-of-band review.
/// </summary>
internal sealed class WorkflowCSharpSourceProposalStore
{
    private readonly object _gate = new();
    private readonly WorkflowCSharpSourceProposalStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, Record> _records = new(StringComparer.Ordinal);

    internal WorkflowCSharpSourceProposalStore(
        WorkflowCSharpSourceProposalStoreOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = options ?? new WorkflowCSharpSourceProposalStoreOptions();
        _clock = clock ?? TimeProvider.System;
        if (_options.MaxProposals < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "C# source proposal store limits are invalid.");
    }

    internal WorkflowCSharpSourceStoreResult Propose(MauiCSharpSourceProposal proposal, bool agentOriginated = false)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (_gate)
        {
            if (agentOriginated)
            {
                return WorkflowCSharpSourceStoreResult.Failure(
                    "agent-source-proposal-forbidden",
                    "Agent MCP cannot create, approve, apply, or write C# source proposals.");
            }
            if (!ValidateProposal(proposal, out var error))
                return WorkflowCSharpSourceStoreResult.Failure("proposal-invalid", error!);

            var id = string.IsNullOrWhiteSpace(proposal.ProposalId)
                ? OpaqueId("csharpproposal")
                : proposal.ProposalId!;
            if (_records.TryGetValue(id, out var existing))
                return WorkflowCSharpSourceStoreResult.Success(CreateSnapshot(existing));

            EvictTerminalLocked();
            if (_records.Count >= _options.MaxProposals)
            {
                return WorkflowCSharpSourceStoreResult.Failure(
                    "proposal-capacity-exhausted",
                    "The broker C# source proposal store is full; resolve a terminal proposal first.");
            }

            var now = _clock.GetUtcNow();
            var normalized = WithMetadata(
                proposal,
                id,
                revision: 1,
                state: MauiCSharpSourceProposalStates.Proposed,
                createdAt: now);
            var record = new Record(normalized, MauiCSharpSourceProposalStates.Proposed, 1, now);
            _records.Add(id, record);
            return WorkflowCSharpSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowCSharpSourceStoreResult Get(string? proposalId)
    {
        lock (_gate)
        {
            return !TryGetLocked(proposalId, out var record, out var error)
                ? WorkflowCSharpSourceStoreResult.Failure("proposal-not-found", error!)
                : WorkflowCSharpSourceStoreResult.Success(CreateSnapshot(record!));
        }
    }

    internal WorkflowCSharpSourceStoreResult Preview(string? proposalId)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowCSharpSourceStoreResult.Failure("proposal-not-found", error!);
            if (record!.State == MauiCSharpSourceProposalStates.Proposed)
                TransitionLocked(record, MauiCSharpSourceProposalStates.Previewed);
            if (record.State != MauiCSharpSourceProposalStates.Previewed)
            {
                return WorkflowCSharpSourceStoreResult.Failure(
                    "proposal-not-previewable",
                    $"C# source proposal state '{record.State}' cannot be previewed.");
            }
            return WorkflowCSharpSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowCSharpSourceStoreResult Reject(string? proposalId, string? reviewer, string? reasonCode)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowCSharpSourceStoreResult.Failure("proposal-not-found", error!);
            if (IsTerminal(record!.State))
            {
                return WorkflowCSharpSourceStoreResult.Failure(
                    "proposal-terminal",
                    $"C# source proposal state '{record.State}' cannot be rejected.");
            }
            record.Reviewer = Bounded(reviewer, 256);
            record.ReasonCode = Bounded(reasonCode, 128) ?? "rejected";
            TransitionLocked(record, MauiCSharpSourceProposalStates.Rejected);
            return WorkflowCSharpSourceStoreResult.Success(CreateSnapshot(record));
        }
    }

    private static bool ValidateProposal(MauiCSharpSourceProposal proposal, out string? error)
    {
        error = null;
        if (proposal.Schema != 1 ||
            !string.Equals(proposal.Language, "CSharp", StringComparison.Ordinal) ||
            proposal.Eligibility?.Eligible != true ||
            proposal.Operation is null ||
            proposal.Element is null ||
            proposal.Patch is null ||
            proposal.RollbackPatch is null ||
            string.IsNullOrWhiteSpace(proposal.Operation.OperationId) ||
            string.IsNullOrWhiteSpace(proposal.Operation.FileRelativePath) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SourceHash) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SourceAnchor) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SymbolId) ||
            string.IsNullOrWhiteSpace(proposal.Operation.SemanticType) ||
            !string.Equals(proposal.Operation.Attribute, "AutomationId", StringComparison.Ordinal) ||
            !MauiAutomationIdProposalPolicy.TryValidate(proposal.Operation.NewLiteral, out _) ||
            string.IsNullOrWhiteSpace(proposal.BaseContentDigest) ||
            string.IsNullOrWhiteSpace(proposal.PatchDigest) ||
            string.IsNullOrWhiteSpace(proposal.RollbackPatchDigest) ||
            string.IsNullOrWhiteSpace(proposal.DiffDigest) ||
            string.IsNullOrWhiteSpace(proposal.Diff) ||
            proposal.Patch.Start is < 0 ||
            proposal.Patch.Length is < 0 ||
            proposal.Patch.Replacement is null ||
            proposal.RollbackPatch.Start is < 0 ||
            proposal.RollbackPatch.Length is < 0 ||
            proposal.RollbackPatch.Replacement is null ||
            !FixedEquals(proposal.BaseContentDigest, proposal.Patch.BeforeDigest) ||
            !FixedEquals(proposal.Patch.AfterDigest, proposal.RollbackPatch.BeforeDigest) ||
            !FixedEquals(proposal.RollbackPatch.AfterDigest, proposal.BaseContentDigest))
        {
            error = "The C# source proposal must contain one eligible Roslyn-proven literal AutomationId operation and exact forward/rollback patches.";
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
        MauiCSharpSourceProposalStates.Rejected or
        MauiCSharpSourceProposalStates.Stale;

    private void TransitionLocked(Record record, string state)
    {
        record.State = state;
        record.Revision++;
        record.UpdatedAt = _clock.GetUtcNow();
    }

    private bool TryGetLocked(string? proposalId, out Record? record, out string? error)
    {
        record = null;
        error = null;
        if (string.IsNullOrWhiteSpace(proposalId) || !_records.TryGetValue(proposalId, out record))
        {
            error = "The C# source proposal was not found.";
            return false;
        }
        return true;
    }

    private static WorkflowCSharpSourceProposalSnapshot CreateSnapshot(Record record) => new()
    {
        Proposal = WithMetadata(
            record.Proposal,
            record.Proposal.ProposalId!,
            record.Revision,
            record.State,
            record.Proposal.CreatedAt ?? record.CreatedAt),
        State = record.State,
        Revision = record.Revision,
        Reviewer = record.Reviewer,
        ReasonCode = record.ReasonCode,
    };

    private static MauiCSharpSourceProposal WithMetadata(
        MauiCSharpSourceProposal source,
        string id,
        int revision,
        string state,
        DateTimeOffset createdAt)
    {
        var node = JsonSerializer.SerializeToNode(source, MauiTestingJsonContext.Default.MauiCSharpSourceProposal)!.AsObject();
        node["proposalId"] = id;
        node["revision"] = revision;
        node["state"] = state;
        node["createdAt"] = createdAt;
        return node.Deserialize(MauiTestingJsonContext.Default.MauiCSharpSourceProposal)
            ?? throw new InvalidOperationException("A C# source proposal could not be normalized.");
    }

    internal static string ComputeFlowReferencesDigest(IReadOnlyList<MauiCSharpSourceFlowFollowUp>? flows)
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
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static bool FixedEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string? Bounded(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maximum ? trimmed : null;
    }

    private sealed class Record
    {
        public Record(MauiCSharpSourceProposal proposal, string state, int revision, DateTimeOffset createdAt)
        {
            Proposal = proposal;
            State = state;
            Revision = revision;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public MauiCSharpSourceProposal Proposal { get; }
        public string State { get; set; }
        public int Revision { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? Reviewer { get; set; }
        public string? ReasonCode { get; set; }
    }
}

internal sealed class WorkflowCSharpSourceProposalStoreOptions
{
    public int MaxProposals { get; init; } = 128;
}

internal sealed class WorkflowCSharpSourceProposalSnapshot
{
    public MauiCSharpSourceProposal Proposal { get; init; } = new();
    public string State { get; init; } = "";
    public int Revision { get; init; }
    public string? Reviewer { get; init; }
    public string? ReasonCode { get; init; }
}

internal sealed class WorkflowCSharpSourceStoreResult
{
    public bool Ok { get; private init; }
    public WorkflowCSharpSourceProposalSnapshot? Proposal { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowCSharpSourceStoreResult Success(WorkflowCSharpSourceProposalSnapshot proposal)
        => new() { Ok = true, Proposal = proposal };

    public static WorkflowCSharpSourceStoreResult Failure(
        string code,
        string error,
        WorkflowCSharpSourceProposalSnapshot? proposal = null)
        => new() { Code = code, Error = error, Proposal = proposal };
}

/// <summary>Append-only redacted C# proposal lifecycle history; no source text or literals are written.</summary>
internal sealed class WorkflowCSharpSourceHistoryStore
{
    private const int MaxEntries = 256;
    private const int MaxBytes = 1_048_576;
    private static readonly ConcurrentDictionary<string, object> Gates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly string _projectRoot;

    internal WorkflowCSharpSourceHistoryStore(string projectRoot)
        => _projectRoot = Path.GetFullPath(projectRoot);

    internal WorkflowCSharpSourceHistoryAppendResult Append(WorkflowCSharpSourceProposalSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string? error = null;
        if (!IsSafeProposalId(snapshot.Proposal.ProposalId) ||
            !TryGetHistoryPath(snapshot.Proposal.ProposalId!, out var path, out error))
        {
            return WorkflowCSharpSourceHistoryAppendResult.Failure(
                "source-history-invalid",
                error ?? "The C# source proposal history identity is invalid.");
        }

        lock (Gates.GetOrAdd(path!, static _ => new object()))
        {
            try
            {
                var existing = File.Exists(path) ? File.ReadAllText(path!) : string.Empty;
                if (Encoding.UTF8.GetByteCount(existing) > MaxBytes)
                {
                    return WorkflowCSharpSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "The bounded C# source proposal history is too large.");
                }
                var lines = existing.ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                if (lines.Count >= MaxEntries)
                {
                    return WorkflowCSharpSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "The bounded C# source proposal history has reached its entry limit.");
                }

                var previousHash = ValidateChain(lines, out error);
                if (error is not null)
                    return WorkflowCSharpSourceHistoryAppendResult.Failure("source-history-invalid", error);

                var node = new JsonObject
                {
                    ["schema"] = 1,
                    ["language"] = "CSharp",
                    ["sequence"] = lines.Count + 1,
                    ["at"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    ["previousHash"] = previousHash,
                    ["proposalId"] = Token(snapshot.Proposal.ProposalId),
                    ["revision"] = snapshot.Revision,
                    ["state"] = snapshot.State,
                    ["operationId"] = Token(snapshot.Proposal.Operation.OperationId),
                    ["fileDigest"] = Token(snapshot.Proposal.Operation.FileRelativePath),
                    ["sourceHash"] = SafeDigest(snapshot.Proposal.Operation.SourceHash),
                    ["sourceAnchor"] = SafeDigest(snapshot.Proposal.Operation.SourceAnchor),
                    ["baseContentDigest"] = SafeDigest(snapshot.Proposal.BaseContentDigest),
                    ["patchDigest"] = SafeDigest(snapshot.Proposal.PatchDigest),
                    ["rollbackPatchDigest"] = SafeDigest(snapshot.Proposal.RollbackPatchDigest),
                    ["diffDigest"] = SafeDigest(snapshot.Proposal.DiffDigest),
                    ["flowReferencesDigest"] = WorkflowCSharpSourceProposalStore.ComputeFlowReferencesDigest(snapshot.Proposal.AffectedFlows),
                    ["reasonCode"] = Token(snapshot.ReasonCode),
                };
                var material = Canonicalize(node);
                node["hash"] = "sha256:" + Hash(material);
                var line = Canonicalize(node);
                var content = existing + (existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty) + line + "\n";
                if (Encoding.UTF8.GetByteCount(content) > MaxBytes)
                {
                    return WorkflowCSharpSourceHistoryAppendResult.Failure(
                        "source-history-invalid",
                        "Appending the C# source proposal history would exceed its size cap.");
                }
                AtomicWrite(path!, content);
                return WorkflowCSharpSourceHistoryAppendResult.Success(path!);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return WorkflowCSharpSourceHistoryAppendResult.Failure(
                    "source-history-write-failed",
                    "The redacted C# source proposal history could not be written.");
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
                error = "The C# source proposal history directory cannot use a symbolic link or reparse point.";
                return false;
            }
            var candidate = Path.GetFullPath(Path.Combine(root, "csharp-" + proposalId + ".history.jsonl"));
            if (!IsUnderRoot(candidate, root))
            {
                error = "The C# source proposal history path escapes its trusted root.";
                return false;
            }
            path = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = "The C# source proposal history root could not be resolved safely.";
            return false;
        }
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
                var declaredPrevious = node?["previousHash"]?.GetValue<string>();
                var hash = node?["hash"]?.GetValue<string>();
                if (node is null ||
                    !string.Equals(previous, declaredPrevious, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(hash))
                {
                    error = "The existing C# source proposal history hash chain is invalid.";
                    return null;
                }
                node.Remove("hash");
                if (!FixedEquals(hash, "sha256:" + Hash(Canonicalize(node))))
                {
                    error = "The existing C# source proposal history hash chain failed verification.";
                    return null;
                }
                previous = hash;
            }
            catch (JsonException)
            {
                error = "The existing C# source proposal history contains invalid JSON.";
                return null;
            }
        }
        return previous;
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool IsSafeProposalId(string? value)
        => value is { Length: > 0 and <= 128 } &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

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

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

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
}

internal sealed class WorkflowCSharpSourceHistoryAppendResult
{
    public bool Ok { get; private init; }
    public string? HistoryPath { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowCSharpSourceHistoryAppendResult Success(string path)
        => new() { Ok = true, HistoryPath = path };

    public static WorkflowCSharpSourceHistoryAppendResult Failure(string code, string error)
        => new() { Code = code, Error = error };
}
