using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

internal static class TestAgentToolSupport
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string Success(string? requestId, object? data = null, IEnumerable<MauiTestAgentUntrustedInput>? untrusted = null)
    {
        var response = new JsonObject
        {
            ["ok"] = true,
            ["requestId"] = requestId,
        };
        if (data is not null)
            response["data"] = JsonSerializer.SerializeToNode(data, OutputJsonOptions);
        var labels = untrusted?.ToArray() ?? [];
        if (labels.Length > 0)
            response["untrustedInputs"] = JsonSerializer.SerializeToNode(labels, OutputJsonOptions);
        return CliJson.SerializeUntyped(response, indented: false);
    }

    internal static string Failure(
        string? requestId,
        MauiTestAgentError error,
        TestAgentRunRecovery? recovery = null)
    {
        var response = new JsonObject
        {
            ["ok"] = false,
            ["requestId"] = requestId,
            ["error"] = JsonSerializer.SerializeToNode(error, OutputJsonOptions),
        };
        if (recovery is not null)
            response["recovery"] = JsonSerializer.SerializeToNode(recovery, OutputJsonOptions);
        return CliJson.SerializeUntyped(response, indented: false);
    }

    internal static string BrokerFailure<T>(string? requestId, TestAgentBrokerResponse<T> response)
        where T : class
    {
        var error = response.Value switch
        {
            MauiTestAgentSessionResult value => value.Error,
            MauiTestAgentGrantIssueResult value => value.Error,
            MauiTestAgentApprovalResult value => value.Error,
            MauiTestAgentMutationAuthorizationResult value => value.Error,
            MauiTestAgentExplorationResult value => value.Error,
            MauiTestAgentToolResult value => value.Error,
            MauiTestAgentPatchResult value => value.Error,
            MauiTestAgentAuditResult value => value.Error,
            MauiTestAgentRunBindingResult value => value.Error,
            _ => null,
        };
        return Failure(
            requestId,
            error ?? Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                response.TransportError is null
                    ? MauiTestAgentErrorCategories.Transport
                    : MauiTestAgentErrorCategories.Target,
                response.TransportError ?? "The broker rejected the restricted test-agent request.",
                retryable: response.StatusCode == 0));
    }

    internal static MauiTestAgentError Error(string code, string category, string message, bool retryable)
        => new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };

    internal static async Task<TestAgentTargetResolution> ResolveTargetAsync(
        McpAgentSession session,
        MauiTestAgentTarget? target)
    {
        try
        {
            var registration = await session.ResolveTestAgentAsync(target).ConfigureAwait(false);
            using var agent = await session.GetTestAgentClientAsync(target).ConfigureAwait(false);
            var status = await agent.GetStatusAsync().ConfigureAwait(false);
            if (status is null)
            {
                return TestAgentTargetResolution.Failure(Error(
                    MauiTestAgentErrorCodes.TargetUnavailable,
                    MauiTestAgentErrorCategories.Target,
                    "The explicitly selected target did not return status.",
                    retryable: true));
            }

            var state = new MauiTestAgentTargetState
            {
                AgentId = registration.Id,
                AgentInstanceId = registration.InstanceId,
                AppBuildFingerprint = BuildAppFingerprint(status),
                // Seed/backend fingerprints require a trusted live attestation. The MCP caller
                // cannot promote echoed target fields into broker-observed state.
                SeedFingerprint = null,
                BackendStateFingerprint = null,
                Route = status.Route,
                Window = status.Window,
                ObservedAt = DateTimeOffset.UtcNow,
            };
            if (target is not null && !TargetFingerprintMatches(target, state))
            {
                return TestAgentTargetResolution.Failure(Error(
                    MauiTestAgentErrorCodes.TargetStale,
                    MauiTestAgentErrorCategories.Target,
                    "The explicit target no longer matches the connected app build or trusted state fingerprints.",
                    retryable: false));
            }
            return TestAgentTargetResolution.Success(registration, state);
        }
        catch (McpException)
        {
            return TestAgentTargetResolution.Failure(Error(
                MauiTestAgentErrorCodes.ExplicitTargetRequired,
                MauiTestAgentErrorCategories.Target,
                "The restricted test-agent profile requires a currently connected explicit agentId and agentInstanceId.",
                retryable: false));
        }
        catch (HttpRequestException)
        {
            return TestAgentTargetResolution.Failure(Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Target,
                "The explicitly selected target is unavailable.",
                retryable: true));
        }
    }

    internal static string? BuildAppFingerprint(AgentStatus status)
    {
        var version = NormalizeFingerprintPart(status.App?.Version);
        var build = NormalizeFingerprintPart(status.App?.Build);
        return version is null && build is null
            ? null
            : $"{version ?? "unknown"}:{build ?? "unknown"}";
    }

    internal static async Task<TestAgentSessionTargetResolution> ResolveSessionTargetAsync(
        McpAgentSession session,
        MauiTestAgentRequestEnvelope? envelope,
        CancellationToken cancellationToken = default)
    {
        var snapshotResponse = await SessionAsync(session, envelope, cancellationToken).ConfigureAwait(false);
        if (snapshotResponse.Value?.Ok != true ||
            snapshotResponse.Value.Snapshot is not { Target: not null } snapshot)
        {
            return TestAgentSessionTargetResolution.Failure(
                snapshotResponse.Value?.Error ?? Error(
                    MauiTestAgentErrorCodes.TargetUnavailable,
                    snapshotResponse.TransportError is null
                        ? MauiTestAgentErrorCategories.Authorization
                        : MauiTestAgentErrorCategories.Transport,
                    snapshotResponse.TransportError ?? "The broker did not return the canonical authoring target.",
                    retryable: snapshotResponse.StatusCode == 0));
        }

        var live = await ResolveTargetAsync(session, snapshot.Target).ConfigureAwait(false);
        if (live.Error is not null)
            return TestAgentSessionTargetResolution.Failure(live.Error);
        if (!CanonicalTargetFingerprintMatches(snapshot.Target, live.State))
        {
            return TestAgentSessionTargetResolution.Failure(Error(
                MauiTestAgentErrorCodes.TargetStale,
                MauiTestAgentErrorCategories.Target,
                "The canonical authoring target no longer matches the connected app build or trusted state fingerprints.",
                retryable: false));
        }

        return TestAgentSessionTargetResolution.Success(snapshot, snapshot.Target, live);
    }

    private static string? NormalizeFingerprintPart(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ||
               string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    internal static async Task<TestAgentBrokerResponse<MauiTestAgentMutationAuthorizationResult>> AuthorizeAsync(
        McpAgentSession session,
        MauiTestAgentRequestEnvelope? envelope,
        string action,
        FlowSelector? selector = null,
        string? route = null,
        string? sideEffectClass = null,
        string? value = null,
        CancellationToken cancellationToken = default)
    {
        if (envelope is null)
        {
            return new TestAgentBrokerResponse<MauiTestAgentMutationAuthorizationResult>(
                400,
                new MauiTestAgentMutationAuthorizationResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.InvalidRequest,
                        MauiTestAgentErrorCategories.Validation,
                        "A protocol envelope is required.",
                        retryable: false),
                },
                null);
        }

        var sessionTarget = await ResolveSessionTargetAsync(session, envelope, cancellationToken).ConfigureAwait(false);
        if (sessionTarget.Error is not null)
        {
            return new TestAgentBrokerResponse<MauiTestAgentMutationAuthorizationResult>(
                409,
                new MauiTestAgentMutationAuthorizationResult { Error = sessionTarget.Error },
                null);
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        return await TestAgentBrokerClient.AuthorizeAsync(
            brokerPort,
            new MauiTestAgentMutationAuthorizationRequest
            {
                Envelope = envelope,
                Action = action,
                Selector = selector,
                Route = route,
                SideEffectClass = sideEffectClass,
                ValueLength = value is null ? null : System.Text.Encoding.UTF8.GetByteCount(value),
                ValueDigest = value is null ? null : Digest(value),
                CurrentTargetState = sessionTarget.LiveTarget!.State,
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<TestAgentBrokerResponse<MauiTestAgentExplorationResult>> AuthorizeExplorationAsync(
        McpAgentSession session,
        MauiTestAgentRequestEnvelope? envelope,
        string action,
        string explorationScope,
        FlowSelector? selector = null,
        string? route = null,
        double? deltaX = null,
        double? deltaY = null,
        int? itemIndex = null,
        CancellationToken cancellationToken = default)
    {
        if (envelope is null)
        {
            return new TestAgentBrokerResponse<MauiTestAgentExplorationResult>(
                400,
                new MauiTestAgentExplorationResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.InvalidRequest,
                        MauiTestAgentErrorCategories.Validation,
                        "A protocol envelope is required.",
                        retryable: false),
                },
                null);
        }

        var sessionTarget = await ResolveSessionTargetAsync(session, envelope, cancellationToken).ConfigureAwait(false);
        if (sessionTarget.Error is not null)
        {
            return new TestAgentBrokerResponse<MauiTestAgentExplorationResult>(
                409,
                new MauiTestAgentExplorationResult { Error = sessionTarget.Error },
                null);
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        return await TestAgentBrokerClient.AuthorizeExplorationAsync(
            brokerPort,
            new MauiTestAgentExplorationRequest
            {
                Envelope = envelope,
                Action = action,
                Scope = explorationScope,
                Selector = selector,
                Route = route,
                DeltaX = deltaX,
                DeltaY = deltaY,
                ItemIndex = itemIndex,
                CurrentTargetState = sessionTarget.LiveTarget!.State,
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> SessionAsync(
        McpAgentSession session,
        MauiTestAgentRequestEnvelope? envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 })
        {
            return new TestAgentBrokerResponse<MauiTestAgentSessionResult>(
                400,
                new MauiTestAgentSessionResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.SessionNotFound,
                        MauiTestAgentErrorCategories.Validation,
                        "The envelope must name an authoringSessionId.",
                        retryable: false),
                },
                null);
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        return await TestAgentBrokerClient.StatusAsync(
            brokerPort,
            new MauiTestAgentSessionAccessRequest
            {
                SessionId = envelope.Correlation.AuthoringSessionId,
                ReadCapabilityId = envelope.ReadCapabilityId,
                Envelope = envelope,
            },
            cancellationToken).ConfigureAwait(false);
    }

    internal static MauiTestAgentUntrustedInput Untrusted(string kind)
        => new()
        {
            Kind = kind,
            PolicyInfluencing = false,
        };

    internal static string Digest(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    internal static MauiTestAgentRequestEnvelope CreateSubrequestEnvelope(
        MauiTestAgentRequestEnvelope source,
        string purpose)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            source,
            MauiTestingJsonContext.Default.MauiTestAgentRequestEnvelope);
        var clone = JsonSerializer.Deserialize(
            bytes,
            MauiTestingJsonContext.Default.MauiTestAgentRequestEnvelope)
            ?? throw new InvalidOperationException("The restricted request envelope could not be cloned.");
        clone.RequestId = SubrequestId(source.RequestId, purpose);
        clone.IdempotencyKey = SubrequestId(source.IdempotencyKey, purpose);
        return clone;
    }

    private static bool TargetFingerprintMatches(
        MauiTestAgentTarget expected,
        MauiTestAgentTargetState? observed)
        => observed is not null &&
           string.Equals(expected.AgentId, observed.AgentId, StringComparison.Ordinal) &&
           string.Equals(expected.AgentInstanceId, observed.AgentInstanceId, StringComparison.Ordinal) &&
           MatchesExpectedFingerprint(expected.AppBuildFingerprint, observed.AppBuildFingerprint) &&
           MatchesExpectedFingerprint(expected.SeedFingerprint, observed.SeedFingerprint) &&
           MatchesExpectedFingerprint(expected.BackendStateFingerprint, observed.BackendStateFingerprint);

    private static bool CanonicalTargetFingerprintMatches(
        MauiTestAgentTarget expected,
        MauiTestAgentTargetState? observed)
        => observed is not null &&
           string.Equals(expected.AgentId, observed.AgentId, StringComparison.Ordinal) &&
           string.Equals(expected.AgentInstanceId, observed.AgentInstanceId, StringComparison.Ordinal) &&
           string.Equals(expected.AppBuildFingerprint, observed.AppBuildFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.SeedFingerprint, observed.SeedFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.BackendStateFingerprint, observed.BackendStateFingerprint, StringComparison.Ordinal);

    private static bool MatchesExpectedFingerprint(string? expected, string? observed)
        => string.IsNullOrWhiteSpace(expected) ||
           string.Equals(expected, observed, StringComparison.Ordinal);

    private static string SubrequestId(string? source, string purpose)
    {
        var prefix = string.IsNullOrWhiteSpace(source) ? "request" : source;
        var suffix = ":" + purpose;
        if (prefix.Length + suffix.Length <= 256)
            return prefix + suffix;
        return prefix[..Math.Max(1, 256 - suffix.Length)] + suffix;
    }
}

internal sealed class TestAgentTargetResolution
{
    public AgentRegistration? Registration { get; private init; }
    public MauiTestAgentTargetState? State { get; private init; }
    public MauiTestAgentError? Error { get; private init; }

    public static TestAgentTargetResolution Success(AgentRegistration registration, MauiTestAgentTargetState state)
        => new() { Registration = registration, State = state };

    public static TestAgentTargetResolution Failure(MauiTestAgentError error)
        => new() { Error = error };
}

internal sealed class TestAgentSessionTargetResolution
{
    public MauiTestAgentAuthoringSnapshot? Snapshot { get; private init; }
    public MauiTestAgentTarget? Target { get; private init; }
    public TestAgentTargetResolution? LiveTarget { get; private init; }
    public MauiTestAgentError? Error { get; private init; }

    public static TestAgentSessionTargetResolution Success(
        MauiTestAgentAuthoringSnapshot snapshot,
        MauiTestAgentTarget target,
        TestAgentTargetResolution liveTarget)
        => new() { Snapshot = snapshot, Target = target, LiveTarget = liveTarget };

    public static TestAgentSessionTargetResolution Failure(MauiTestAgentError error)
        => new() { Error = error };
}

/// <summary>
/// Bounded manual-recovery context returned only when the broker confirmed a run start but the
/// restricted profile could not persist its authoring-session binding. It is deliberately a
/// failure payload so callers must not automatically retry the original mutation.
/// </summary>
internal sealed class TestAgentRunRecovery
{
    public string State { get; init; } = "started-unbound";
    public string RunId { get; init; } = "";
    public string RunCapabilityToken { get; init; } = "";
    public bool AutomaticRetryAllowed { get; init; }
}
