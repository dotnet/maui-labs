using System.CommandLine;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Resolves the CI test-identity digest carried by a <c>devflow-ci-failure</c> issue back to the
/// committed flow that produced it, and computes that digest for committed flows.
/// </summary>
/// <remarks>
/// <para>
/// The published issue deliberately carries no test name, path, log text, or branch name - only a
/// one-way <c>testIdentitySha256</c>. That is a lookup key, not a secret: it is the SHA-256 of
/// <c>devflow-ci-test-identity-v1\n&lt;platform&gt;\n&lt;tier&gt;\n&lt;flow-digest&gt;</c>, and the
/// flow digest is itself derived from flow content the reader already has on disk. Recomputing the
/// identity for each committed flow in a trusted checkout therefore discloses nothing new; it only
/// makes the issue actionable, which is the whole point of publishing it.
/// </para>
/// <para>
/// Nothing here relaxes the publisher's trust boundary. The command is read-only, performs no
/// network access, never reads the handoff archive, and never accepts a "closest" match: an
/// identity either reproduces exactly from the flow bytes in this checkout or it does not. A flow
/// edited after the run legitimately fails to resolve, and that outcome is reported as its own
/// result rather than smoothed over.
/// </para>
/// </remarks>
internal static class FlowIdentityCommands
{
    internal const string ResultSchema = "devflow-flow-identity-v1";
    internal const string IdentityPrefix = "devflow-ci-test-identity-v1";
    internal const string DefaultTier = "tier-1";

    private const long MaximumFileBytes = 1_048_576;
    private const int MaximumScannedFiles = 4096;
    private const int MaximumScannedDirectories = 4096;
    private const int MaximumReportedSkips = 32;
    private const string FlowBlockMarker = "```json maui-test";

    /// <summary>
    /// Platform values the CI handoff producer can actually emit for an identity. The handoff
    /// envelope allows a wider set for a reader, but the producer refuses anything outside this
    /// list before an identity is ever computed, so a value outside it cannot name a real
    /// published failure and this command refuses it rather than returning an unusable digest.
    /// </summary>
    internal static ImmutableArray<string> ProducerPlatforms { get; } =
        ["android", "ios", "maccatalyst", "windows"];

    /// <summary>Directories skipped while walking a search root; build output duplicates flows.</summary>
    internal static ImmutableArray<string> ExcludedDirectoryNames { get; } =
        [".git", "bin", "obj", "artifacts", "node_modules"];

    internal static Command Create(
        Option<bool> jsonOption,
        Option<bool> noJsonOption,
        IDevFlowOutputWriter output,
        Action markError)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(markError);

        var command = new Command(
            "identity",
            "Compute the CI test-identity digest for committed flows, or resolve a digest from a devflow-ci-failure issue back to the flow that produced it.");
        var pathArgument = new Argument<string?>("path")
        {
            Description = "Committed Markdown flow, or a directory to scan. Defaults to the current directory when resolving.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var resolveOption = new Option<string?>("--resolve")
        {
            Description = "Test identity from a devflow-ci-failure issue (sha256:<64 hex>). Reports the committed flow that produces it.",
        };
        var searchOption = new Option<string?>("--search")
        {
            Description = "Search root for --resolve. Equivalent to the positional path argument.",
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform from the issue (android, ios, maccatalyst, windows). Defaults to trying every platform CI can publish.",
        };
        var tierOption = new Option<string>("--tier")
        {
            Description = "Flow tier used in the identity. CI only publishes tier-1.",
            DefaultValueFactory = _ => DefaultTier,
        };
        command.Add(pathArgument);
        command.Add(resolveOption);
        command.Add(searchOption);
        command.Add(platformOption);
        command.Add(tierOption);

        command.SetAction(async (ctx, ct) =>
        {
            var json = output.ResolveJsonMode(ctx.GetValue(jsonOption), ctx.GetValue(noJsonOption));
            try
            {
                var result = await ExecuteAsync(
                    ctx.GetValue(pathArgument),
                    ctx.GetValue(resolveOption),
                    ctx.GetValue(searchOption),
                    ctx.GetValue(platformOption),
                    ctx.GetValue(tierOption)!,
                    ct).ConfigureAwait(false);
                output.WriteResult(result, json, WriteHuman);
                if (!result.Ok)
                {
                    markError();
                    return 1;
                }
                return 0;
            }
            catch (FlowIdentityException ex)
            {
                output.WriteError(ex.Message, json, ex.Code);
                markError();
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Reproduces the producer's identity construction exactly:
    /// <c>Get-Sha256Text("devflow-ci-test-identity-v1`n$Platform`n$Tier`n$FlowDigest")</c> from
    /// <c>eng/devflow/New-DevFlowFailureHandoff.ps1</c>.
    /// </summary>
    /// <remarks>
    /// Two details decide whether this matches at all. The PowerShell <c>`n</c> escape is LF, so
    /// the separator must be <c>"\n"</c> and never <c>Environment.NewLine</c>, which is CRLF on
    /// Windows and would make every digest silently wrong. And the producer hashes the flow digest
    /// in the exact form the flow-pilot manifest carries it, which its <c>Test-Sha256</c> guard
    /// pins to <c>sha256:&lt;64 lowercase hex&gt;</c> - so the bare hex returned by
    /// <c>ComputeFlowDigest</c> has to be prefixed before it is hashed.
    /// </remarks>
    internal static string ComputeTestIdentity(string platform, string tier, string flowDigest)
    {
        if (string.IsNullOrEmpty(platform))
            throw new ArgumentException("A platform is required.", nameof(platform));
        if (string.IsNullOrEmpty(tier))
            throw new ArgumentException("A tier is required.", nameof(tier));

        // A separator inside a field would shift the remaining fields and let two different inputs
        // hash to one identity. The command line is already guarded, but this is the
        // security-critical primitive, so it refuses here too rather than trusting its callers.
        if (platform.AsSpan().ContainsAny('\n', '\r'))
            throw new ArgumentException("A platform cannot contain a line separator.", nameof(platform));
        if (tier.AsSpan().ContainsAny('\n', '\r'))
            throw new ArgumentException("A tier cannot contain a line separator.", nameof(tier));

        var canonicalDigest = NormalizeSha256(flowDigest)
            ?? throw new ArgumentException("The flow digest must be 64 hexadecimal characters.", nameof(flowDigest));
        var text = IdentityPrefix + "\n" + platform + "\n" + tier + "\n" + canonicalDigest;
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>Returns <c>sha256:&lt;64 lowercase hex&gt;</c>, or null when the value is not a SHA-256 digest.</summary>
    internal static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..];
        if (trimmed.Length != 64)
            return null;
        foreach (var character in trimmed)
        {
            var lower = char.ToLowerInvariant(character);
            if (lower is (< '0' or > '9') and (< 'a' or > 'f'))
                return null;
        }
        return "sha256:" + trimmed.ToLowerInvariant();
    }

    internal static async Task<FlowIdentityCliResult> ExecuteAsync(
        string? path,
        string? resolve,
        string? search,
        string? platform,
        string tier,
        CancellationToken cancellationToken = default)
    {
        var resolving = !string.IsNullOrWhiteSpace(resolve);
        string? requestedIdentity = null;
        if (resolving)
        {
            requestedIdentity = NormalizeSha256(resolve)
                ?? throw new FlowIdentityException(
                    "identity-invalid",
                    "The test identity must be 'sha256:' followed by 64 hexadecimal characters, exactly as the issue prints it.");
        }

        if (string.IsNullOrWhiteSpace(tier))
            throw new FlowIdentityException("tier-invalid", "A tier is required.");
        tier = tier.Trim();
        // A tier carrying a newline would inject extra fields into the hashed construction and
        // could report a "match" that is really a collision between two different identities.
        if (!IsSafeTier(tier))
        {
            throw new FlowIdentityException(
                "tier-invalid",
                $"'{tier}' is not a tier. CI publishes '{DefaultTier}'.");
        }

        var platforms = ResolvePlatforms(platform);
        var root = ResolveRoot(path, search, resolving);
        var scan = await ScanAsync(root, cancellationToken).ConfigureAwait(false);

        var flows = new List<FlowIdentityFlowResult>(scan.Flows.Count);
        foreach (var candidate in scan.Flows)
        {
            var identities = new List<FlowIdentityValue>(platforms.Length);
            foreach (var candidatePlatform in platforms)
            {
                identities.Add(new FlowIdentityValue
                {
                    Platform = candidatePlatform,
                    Tier = tier,
                    TestIdentitySha256 = ComputeTestIdentity(candidatePlatform, tier, candidate.FlowDigest),
                });
            }

            flows.Add(new FlowIdentityFlowResult
            {
                Path = candidate.Path,
                FlowName = candidate.FlowName,
                FlowDigest = "sha256:" + candidate.FlowDigest,
                PlanPath = candidate.PlanPath,
                PlanFlowDigest = candidate.PlanDigest is null ? null : "sha256:" + candidate.PlanDigest,
                PlanBindingCurrent = candidate.PlanDigest is null
                    ? null
                    : string.Equals(candidate.PlanDigest, candidate.FlowDigest, StringComparison.Ordinal),
                Identities = identities,
            });
        }

        var result = new FlowIdentityCliResult
        {
            Schema = ResultSchema,
            Mode = resolving ? "resolve" : "compute",
            Tier = tier,
            Platforms = [.. platforms],
            Construction = new FlowIdentityConstruction(),
            Scan = new FlowIdentityScanResult
            {
                Root = root.FullPath,
                RootKind = root.IsDirectory ? "directory" : "file",
                MarkdownFiles = scan.MarkdownFiles,
                Flows = scan.Flows.Count,
                NonFlowDocuments = scan.NonFlowDocuments,
                ExcludedDirectoryNames = [.. ExcludedDirectoryNames],
                Skipped = scan.Skipped,
                SkippedTotal = scan.SkippedTotal,
            },
        };

        if (!resolving)
        {
            result.Flows = flows;
            result.Ok = flows.Count > 0;
            result.Message = flows.Count > 0
                ? $"Computed {flows.Count * platforms.Length} CI test {(flows.Count * platforms.Length == 1 ? "identity" : "identities")} from {flows.Count} committed {(flows.Count == 1 ? "flow" : "flows")}."
                : $"No committed flow was found under '{root.FullPath}'.";
            return result;
        }

        var matches = new List<FlowIdentityMatch>();
        foreach (var flow in flows)
        {
            foreach (var identity in flow.Identities)
            {
                if (string.Equals(identity.TestIdentitySha256, requestedIdentity, StringComparison.Ordinal))
                {
                    matches.Add(new FlowIdentityMatch
                    {
                        Path = flow.Path,
                        FlowName = flow.FlowName,
                        Platform = identity.Platform,
                        Tier = identity.Tier,
                        FlowDigest = flow.FlowDigest,
                        PlanPath = flow.PlanPath,
                        PlanBindingCurrent = flow.PlanBindingCurrent,
                        Source = "committed-flow",
                    });
                }
            }
        }

        // A flow edited since the run no longer produces the published identity, but its plan
        // sidecar still names the digest the flow had when it was last bound - and `flow run`
        // refuses to execute a bundle whose sidecar and flow disagree, so at run time they agreed.
        // Matching that recorded digest is therefore still an exact match, just against the bytes
        // the flow used to have. It names the test without ever pretending the checkout can
        // reproduce it.
        var superseded = new List<FlowIdentityMatch>();
        if (matches.Count == 0)
        {
            foreach (var flow in flows)
            {
                if (flow.PlanFlowDigest is null || flow.PlanBindingCurrent is not false)
                    continue;
                foreach (var candidatePlatform in platforms)
                {
                    var identity = ComputeTestIdentity(candidatePlatform, tier, flow.PlanFlowDigest);
                    if (!string.Equals(identity, requestedIdentity, StringComparison.Ordinal))
                        continue;
                    superseded.Add(new FlowIdentityMatch
                    {
                        Path = flow.Path,
                        FlowName = flow.FlowName,
                        Platform = candidatePlatform,
                        Tier = tier,
                        FlowDigest = flow.PlanFlowDigest,
                        CurrentFlowDigest = flow.FlowDigest,
                        PlanPath = flow.PlanPath,
                        PlanBindingCurrent = false,
                        Source = "plan-sidecar",
                    });
                }
            }
        }

        var outcome = matches.Count > 0
            ? "matched"
            : superseded.Count > 0 ? "matched-superseded" : "no-match";
        result.Resolve = new FlowIdentityResolveResult
        {
            Requested = requestedIdentity!,
            Outcome = outcome,
            CandidateIdentities = flows.Count * platforms.Length,
            Matches = matches.Count > 0 ? matches : superseded,
            Explanation = BuildExplanation(outcome, root, platforms, tier, scan, superseded),
        };
        result.Ok = outcome == "matched";
        result.Message = outcome switch
        {
            "matched" => matches.Count == 1
                ? $"The identity resolves to '{matches[0].Path}' on {matches[0].Platform}."
                : $"The identity resolves to {matches.Count} committed flows with identical content.",
            "matched-superseded" =>
                $"The identity was produced by '{superseded[0].Path}', but that flow has been edited since the run. This checkout cannot reproduce it.",
            _ => $"No committed flow under '{root.FullPath}' produces this identity.",
        };
        return result;
    }

    /// <summary>
    /// Rejects a tier that could inject extra newline-separated fields into the hashed
    /// construction, which would let two different identities be reported as one match.
    /// </summary>
    private static bool IsSafeTier(string tier)
    {
        if (tier.Length is 0 or > 32)
            return false;
        foreach (var character in tier)
        {
            if (character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
                continue;
            return false;
        }
        return true;
    }

    private static ImmutableArray<string> ResolvePlatforms(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
            return ProducerPlatforms;

        var requested = platform.Trim();
        if (!ProducerPlatforms.Contains(requested, StringComparer.Ordinal))
        {
            throw new FlowIdentityException(
                "platform-invalid",
                $"'{requested}' is not a platform CI publishes an identity for. Use one of: {string.Join(", ", ProducerPlatforms)}.");
        }
        return [requested];
    }

    private static SearchRoot ResolveRoot(string? path, string? search, bool resolving)
    {
        string? selected = null;
        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(search))
        {
            if (!PathsEqual(path, search))
            {
                throw new FlowIdentityException(
                    "search-root-conflict",
                    "The path argument and --search name different roots. Pass only one.");
            }
            selected = search;
        }
        else
        {
            selected = string.IsNullOrWhiteSpace(path) ? search : path;
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            if (!resolving)
            {
                throw new FlowIdentityException(
                    "flow-path-required",
                    "A committed Markdown flow or a directory to scan is required.");
            }
            selected = Directory.GetCurrentDirectory();
        }

        string full;
        try
        {
            full = Path.GetFullPath(selected);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new FlowIdentityException("path-invalid", $"'{selected}' is not a valid path.");
        }

        if (Directory.Exists(full))
            return new SearchRoot(full, IsDirectory: true);
        if (File.Exists(full))
        {
            if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                throw new FlowIdentityException("flow-invalid", "A committed flow must be a .md file.");
            return new SearchRoot(full, IsDirectory: false);
        }

        throw new FlowIdentityException("path-missing", $"'{selected}' does not exist.");
    }

    private static bool PathsEqual(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<ScanResult> ScanAsync(SearchRoot root, CancellationToken cancellationToken)
    {
        var files = root.IsDirectory ? EnumerateMarkdown(root.FullPath, cancellationToken) : [root.FullPath];
        var flows = new List<FlowCandidate>();
        var skipped = new List<FlowIdentitySkip>();
        var skippedTotal = 0;
        var nonFlowDocuments = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = Display(root, file);

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Skip(display, "reparse-point");
                    continue;
                }
                if (info.Length > MaximumFileBytes)
                {
                    Skip(display, "file-too-large");
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Skip(display, "unreadable");
                continue;
            }

            string markdown;
            try
            {
                markdown = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                Skip(display, "unreadable");
                continue;
            }

            // A Markdown file with no replay block is ordinary prose, not a flow that failed to
            // load. Counting it separately keeps the skip list meaningful: everything listed there
            // is a document that looked like a flow and could not be read as one.
            if (!markdown.Contains(FlowBlockMarker, StringComparison.Ordinal))
            {
                nonFlowDocuments++;
                continue;
            }

            var parsed = Testing.FlowMarkdown.Parse(markdown, file);
            if (!parsed.Ok || parsed.Flow is null)
            {
                Skip(display, "flow-unparseable");
                continue;
            }

            string digest;
            try
            {
                digest = Testing.MauiFlowRunReportSerializer.ComputeFlowDigest(parsed.Flow);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                Skip(display, "flow-digest-failed");
                continue;
            }

            var planPath = Path.Combine(
                Path.GetDirectoryName(file)!,
                Path.GetFileNameWithoutExtension(file) + ".maui-plan.json");
            var planExists = File.Exists(planPath);
            var plan = planExists
                ? await ReadPlanDigestAsync(planPath, Path.GetFileName(file), cancellationToken).ConfigureAwait(false)
                : default;
            if (plan.SkipReason is { } planSkipReason)
                Skip(Display(root, planPath), planSkipReason);

            flows.Add(new FlowCandidate(
                display,
                parsed.Flow.Name,
                digest,
                planExists ? Display(root, planPath) : null,
                plan.Digest));
        }

        return new ScanResult(
            files.Count,
            flows,
            nonFlowDocuments,
            skipped,
            skippedTotal);

        void Skip(string display, string reason)
        {
            skippedTotal++;
            if (skipped.Count < MaximumReportedSkips)
                skipped.Add(new FlowIdentitySkip { Path = display, Reason = reason });
        }
    }

    /// <summary>
    /// Walks the search root, refusing rather than truncating when it is too large. A resolver that
    /// silently stopped scanning would report "no match" for a flow it never looked at, which is
    /// the one answer this command must never give.
    /// </summary>
    private static List<string> EnumerateMarkdown(string root, CancellationToken cancellationToken)
    {
        // Case-sensitive matching would miss "Checkout.MD" on Linux and macOS even though both
        // CommittedFlowBundleLoader and this command's own single-file path accept it, which would
        // be a false "no match" for a flow CI really can run.
        var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive };
        var files = new List<string>();
        var pending = new Stack<string>();
        var visitedDirectories = 0;
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (++visitedDirectories > MaximumScannedDirectories)
            {
                throw new FlowIdentityException(
                    "search-root-too-large",
                    $"More than {MaximumScannedDirectories} directories are under '{root}'. Narrow the search root so the scan stays exhaustive.");
            }

            string[] entries;
            try
            {
                entries = Directory.GetFiles(directory, "*.md", options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in entries)
            {
                files.Add(file);
                if (files.Count > MaximumScannedFiles)
                {
                    throw new FlowIdentityException(
                        "search-root-too-large",
                        $"More than {MaximumScannedFiles} Markdown files are under '{root}'. Narrow the search root so the scan stays exhaustive.");
                }
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                try
                {
                    if ((new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                pending.Push(child);
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>
    /// Reads <c>flow.digest</c> from a plan sidecar, but only once the sidecar is proven to be
    /// bound to this flow file.
    /// </summary>
    /// <remarks>
    /// A superseded match rests on one invariant: <c>CommittedFlowBundleLoader</c> refuses to run a
    /// bundle whose sidecar and flow disagree, so at CI time the sidecar digest was that flow's own
    /// digest. That loader enforces the binding on both <c>flow.path</c> and <c>flow.digest</c>, so
    /// this must check both. A sidecar naming a different flow file - a copy-paste, say - could
    /// never have run, and honouring its digest would let this command name a flow that did not
    /// produce the identity.
    /// <para>
    /// Full plan validation is still deliberately not applied: a sidecar that no longer validates
    /// can carry the digest that names the test, and refusing it would lose the answer over an
    /// unrelated defect. A sidecar that cannot be read at all is reported as a skip rather than
    /// dropped, so a "no match" answer never quietly means "stopped looking".
    /// </para>
    /// </remarks>
    private static async Task<PlanDigestRead> ReadPlanDigestAsync(
        string planPath,
        string flowFileName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(planPath).Length > MaximumFileBytes)
                return new PlanDigestRead(null, "plan-sidecar-too-large");
            var text = await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false);
            var document = JsonNode.Parse(text)?.AsObject();
            if (document?["flow"] is not JsonObject reference)
                return new PlanDigestRead(null, "plan-sidecar-unreadable");

            var boundPath = reference["path"] is JsonValue pathValue && pathValue.TryGetValue<string>(out var bound)
                ? bound
                : null;
            if (!string.Equals(boundPath, flowFileName, StringComparison.Ordinal))
                return new PlanDigestRead(null, "plan-sidecar-bound-to-another-flow");

            var raw = reference["digest"] is JsonValue digestValue && digestValue.TryGetValue<string>(out var digest)
                ? digest
                : null;
            var normalized = NormalizeSha256(raw);
            return normalized is null
                ? new PlanDigestRead(null, "plan-sidecar-unreadable")
                : new PlanDigestRead(normalized[7..], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                       or InvalidOperationException or FormatException or DecoderFallbackException)
        {
            return new PlanDigestRead(null, "plan-sidecar-unreadable");
        }
    }

    private readonly record struct PlanDigestRead(string? Digest, string? SkipReason);

    private static List<string> BuildExplanation(
        string outcome,
        SearchRoot root,
        ImmutableArray<string> platforms,
        string tier,
        ScanResult scan,
        List<FlowIdentityMatch> superseded)
    {
        var explanation = new List<string>();
        switch (outcome)
        {
            case "matched":
                explanation.Add("The identity reproduces exactly from the flow bytes in this checkout.");
                break;

            case "matched-superseded":
                explanation.Add(
                    $"'{superseded[0].Path}' has been edited since the run. Its plan sidecar still records the flow digest that produces this identity, but the flow's current bytes produce a different one.");
                explanation.Add(
                    "The identity covers flow content, so any edit inside the fenced 'json maui-test' block changes it.");
                explanation.Add(
                    "Check out the commit named in the issue and re-run this command there before reproducing the failure.");
                break;

            default:
                explanation.Add(
                    "The flow was edited after the run. The identity covers flow content, so any edit inside the fenced 'json maui-test' block changes it; check out the commit named in the issue and re-run this command there.");
                explanation.Add(
                    platforms.Length == 1
                        ? $"The platform is wrong. Only '{platforms[0]}' was tried; omit --platform to try every platform CI can publish."
                        : $"The platform is outside the published set. Tried: {string.Join(", ", platforms)}.");
                explanation.Add(
                    string.Equals(tier, DefaultTier, StringComparison.Ordinal)
                        ? $"The tier is wrong. Only '{tier}' was tried, which is the only tier the CI producer publishes."
                        : $"The tier is wrong. Only '{tier}' was tried; CI publishes '{DefaultTier}'.");
                explanation.Add(
                    $"The flow lives outside the search root. {scan.Flows.Count} committed {(scan.Flows.Count == 1 ? "flow was" : "flows were")} scanned under '{root.FullPath}'; pass a wider --search root.");
                if (root.IsDirectory)
                {
                    explanation.Add(
                        $"The flow lives under a directory the scan excludes ({string.Join(", ", ExcludedDirectoryNames)}) or exceeds the {MaximumFileBytes / 1024} KiB per-file limit.");
                }
                if (scan.SkippedTotal > 0)
                {
                    explanation.Add(
                        $"{scan.SkippedTotal} {(scan.SkippedTotal == 1 ? "file" : "files")} could not be read and {(scan.SkippedTotal == 1 ? "was" : "were")} not considered; the reported skips name them.");
                }
                break;
        }
        return explanation;
    }

    private static string Display(SearchRoot root, string file)
    {
        if (!root.IsDirectory)
            return file;
        var relative = Path.GetRelativePath(root.FullPath, file);
        return relative.StartsWith("..", StringComparison.Ordinal) ? file : relative;
    }

    private static void WriteHuman(FlowIdentityCliResult value)
    {
        if (value.Resolve is { } resolve)
        {
            Console.WriteLine($"Test identity: {resolve.Requested}");
            Console.WriteLine($"Outcome: {resolve.Outcome}");
            Console.WriteLine(value.Message);
            foreach (var match in resolve.Matches)
            {
                Console.WriteLine($"  {match.Path}");
                Console.WriteLine($"    platform    : {match.Platform}");
                Console.WriteLine($"    tier        : {match.Tier}");
                Console.WriteLine($"    flow digest : {match.FlowDigest}");
                if (match.CurrentFlowDigest is not null)
                    Console.WriteLine($"    current     : {match.CurrentFlowDigest} (differs)");
                if (match.PlanPath is not null)
                    Console.WriteLine($"    plan        : {match.PlanPath}");
            }
            foreach (var line in resolve.Explanation)
                Console.WriteLine($"  - {line}");
        }
        else
        {
            Console.WriteLine(value.Message);
            foreach (var flow in value.Flows)
            {
                Console.WriteLine($"  {flow.Path}");
                Console.WriteLine($"    flow digest : {flow.FlowDigest}");
                foreach (var identity in flow.Identities)
                    Console.WriteLine($"    {identity.Platform} / {identity.Tier} : {identity.TestIdentitySha256}");
                if (flow.PlanBindingCurrent is false)
                    Console.WriteLine("    plan sidecar is stale; run 'maui devflow flow commit' before reproducing.");
            }
        }

        Console.WriteLine(
            $"Scanned {value.Scan.MarkdownFiles} Markdown file(s) under {value.Scan.Root}: {value.Scan.Flows} flow(s), {value.Scan.NonFlowDocuments} non-flow document(s), {value.Scan.SkippedTotal} skipped.");
    }

    private readonly record struct SearchRoot(string FullPath, bool IsDirectory);

    private sealed record FlowCandidate(
        string Path,
        string? FlowName,
        string FlowDigest,
        string? PlanPath,
        string? PlanDigest);

    private sealed record ScanResult(
        int MarkdownFiles,
        List<FlowCandidate> Flows,
        int NonFlowDocuments,
        List<FlowIdentitySkip> Skipped,
        int SkippedTotal);
}

internal sealed class FlowIdentityException : Exception
{
    public FlowIdentityException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

/// <summary>The exact construction reproduced by this command, restated for a machine consumer.</summary>
internal sealed class FlowIdentityConstruction
{
    [JsonPropertyName("input")]
    public string Input { get; init; } =
        FlowIdentityCommands.IdentityPrefix + "\\n<platform>\\n<tier>\\n<flow-digest>";

    [JsonPropertyName("newline")] public string Newline { get; init; } = "LF";
    [JsonPropertyName("encoding")] public string Encoding { get; init; } = "utf-8";
    [JsonPropertyName("flowDigestForm")] public string FlowDigestForm { get; init; } = "sha256:<64 lowercase hex>";
    [JsonPropertyName("identityForm")] public string IdentityForm { get; init; } = "sha256:<64 lowercase hex>";
    [JsonPropertyName("producer")] public string Producer { get; init; } = "eng/devflow/New-DevFlowFailureHandoff.ps1";
}

internal sealed class FlowIdentitySkip
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("reason")] public string Reason { get; init; } = "";
}

internal sealed class FlowIdentityScanResult
{
    [JsonPropertyName("root")] public string Root { get; init; } = "";
    [JsonPropertyName("rootKind")] public string RootKind { get; init; } = "";
    [JsonPropertyName("markdownFiles")] public int MarkdownFiles { get; init; }
    [JsonPropertyName("flows")] public int Flows { get; init; }
    [JsonPropertyName("nonFlowDocuments")] public int NonFlowDocuments { get; init; }
    [JsonPropertyName("excludedDirectoryNames")] public string[] ExcludedDirectoryNames { get; init; } = [];
    [JsonPropertyName("skipped")] public List<FlowIdentitySkip> Skipped { get; init; } = [];
    [JsonPropertyName("skippedTotal")] public int SkippedTotal { get; init; }
}

internal sealed class FlowIdentityValue
{
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("tier")] public string Tier { get; init; } = "";
    [JsonPropertyName("testIdentitySha256")] public string TestIdentitySha256 { get; init; } = "";
}

internal sealed class FlowIdentityFlowResult
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("flowName")] public string? FlowName { get; init; }
    [JsonPropertyName("flowDigest")] public string FlowDigest { get; init; } = "";
    [JsonPropertyName("planPath")] public string? PlanPath { get; init; }
    [JsonPropertyName("planFlowDigest")] public string? PlanFlowDigest { get; init; }
    [JsonPropertyName("planBindingCurrent")] public bool? PlanBindingCurrent { get; init; }
    [JsonPropertyName("identities")] public List<FlowIdentityValue> Identities { get; init; } = [];
}

internal sealed class FlowIdentityMatch
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("flowName")] public string? FlowName { get; init; }
    [JsonPropertyName("platform")] public string Platform { get; init; } = "";
    [JsonPropertyName("tier")] public string Tier { get; init; } = "";
    [JsonPropertyName("flowDigest")] public string FlowDigest { get; init; } = "";
    [JsonPropertyName("currentFlowDigest")] public string? CurrentFlowDigest { get; init; }
    [JsonPropertyName("planPath")] public string? PlanPath { get; init; }
    [JsonPropertyName("planBindingCurrent")] public bool? PlanBindingCurrent { get; init; }
    [JsonPropertyName("source")] public string Source { get; init; } = "";
}

internal sealed class FlowIdentityResolveResult
{
    [JsonPropertyName("requested")] public string Requested { get; init; } = "";

    /// <summary><c>matched</c>, <c>matched-superseded</c>, or <c>no-match</c>.</summary>
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = "";

    [JsonPropertyName("candidateIdentities")] public int CandidateIdentities { get; init; }
    [JsonPropertyName("matches")] public List<FlowIdentityMatch> Matches { get; init; } = [];
    [JsonPropertyName("explanation")] public List<string> Explanation { get; init; } = [];
}

internal sealed class FlowIdentityCliResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("schema")] public string Schema { get; init; } = "";
    [JsonPropertyName("mode")] public string Mode { get; init; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; init; } = "";
    [JsonPropertyName("platforms")] public string[] Platforms { get; init; } = [];
    [JsonPropertyName("construction")] public FlowIdentityConstruction Construction { get; init; } = new();
    [JsonPropertyName("scan")] public FlowIdentityScanResult Scan { get; init; } = new();
    [JsonPropertyName("flows")] public List<FlowIdentityFlowResult> Flows { get; set; } = [];
    [JsonPropertyName("resolve")] public FlowIdentityResolveResult? Resolve { get; set; }
}
