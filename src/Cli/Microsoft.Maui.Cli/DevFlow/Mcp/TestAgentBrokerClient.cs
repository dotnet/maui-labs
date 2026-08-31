using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

/// <summary>Typed loopback client for the broker-owned restricted test-agent policy boundary.</summary>
internal static class TestAgentBrokerClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(35),
    };

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> BeginAsync(
        int brokerPort,
        MauiTestAgentSessionBeginRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/sessions/begin",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionBeginRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> StatusAsync(
        int brokerPort,
        MauiTestAgentSessionAccessRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/sessions/status",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionAccessRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> AbandonAsync(
        int brokerPort,
        MauiTestAgentSessionAccessRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/sessions/abandon",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionAccessRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentApprovalResult>> SubmitApprovalAsync(
        int brokerPort,
        MauiTestAgentApprovalSubmitRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/approvals/request",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentApprovalSubmitRequest,
            MauiTestingJsonContext.Default.MauiTestAgentApprovalResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentMutationAuthorizationResult>> AuthorizeAsync(
        int brokerPort,
        MauiTestAgentMutationAuthorizationRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/mutations/authorize",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentMutationAuthorizationRequest,
            MauiTestingJsonContext.Default.MauiTestAgentMutationAuthorizationResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentExplorationResult>> AuthorizeExplorationAsync(
        int brokerPort,
        MauiTestAgentExplorationRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/exploration/authorize",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentExplorationRequest,
            MauiTestingJsonContext.Default.MauiTestAgentExplorationResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentToolResult>> CompleteAsync(
        int brokerPort,
        MauiTestAgentMutationCompletion request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/mutations/complete",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentMutationCompletion,
            MauiTestingJsonContext.Default.MauiTestAgentToolResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> AppendActionAsync(
        int brokerPort,
        MauiTestAgentActionRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/draft/action",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentActionRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> AddAssertionAsync(
        int brokerPort,
        MauiTestAgentAssertionRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/draft/assertion",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentAssertionRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> CommitAsync(
        int brokerPort,
        MauiTestAgentSessionAccessRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/draft/commit",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionAccessRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentSessionResult>> MigratePreviewAsync(
        int brokerPort,
        MauiTestAgentSessionAccessRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/draft/migrate-preview",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionAccessRequest,
            MauiTestingJsonContext.Default.MauiTestAgentSessionResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentPatchResult>> PatchAsync(
        int brokerPort,
        MauiTestAgentPatchRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/patch",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentPatchRequest,
            MauiTestingJsonContext.Default.MauiTestAgentPatchResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentAuditResult>> AuditAsync(
        int brokerPort,
        MauiTestAgentSessionAccessRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/audit",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentSessionAccessRequest,
            MauiTestingJsonContext.Default.MauiTestAgentAuditResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentRunBindingResult>> BindRunAsync(
        int brokerPort,
        MauiTestAgentRunBindingRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/runs/bind",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentRunBindingRequest,
            MauiTestingJsonContext.Default.MauiTestAgentRunBindingResult,
            cancellationToken);

    internal static Task<TestAgentBrokerResponse<MauiTestAgentRunBindingResult>> ValidateRunBindingAsync(
        int brokerPort,
        MauiTestAgentRunBindingRequest request,
        CancellationToken cancellationToken = default)
        => PostAsync(
            brokerPort,
            "/api/test-agent/runs/validate",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentRunBindingRequest,
            MauiTestingJsonContext.Default.MauiTestAgentRunBindingResult,
            cancellationToken);

    /// <summary>
    /// Asks the broker what the target's lifecycle reset owner would establish, without resetting.
    /// </summary>
    internal static Task<TestAgentBrokerRawResponse> GetResetOfferAsync(
        int brokerPort,
        MauiTestAgentRunBindingRequest request,
        CancellationToken cancellationToken = default)
        => PostWorkflowRunAsync(
            brokerPort,
            "/api/test-agent/reset-offer",
            request,
            MauiTestingJsonContext.Default.MauiTestAgentRunBindingRequest,
            cancellationToken);

    internal static string SerializeWorkflowRunRequest<TRequest>(
        TRequest request,
        JsonTypeInfo<TRequest> requestType)
        => JsonSerializer.Serialize(request, requestType);

    internal static Task<TestAgentBrokerRawResponse> PostWorkflowRunAsync<TRequest>(
        int brokerPort,
        string path,
        TRequest request,
        JsonTypeInfo<TRequest> requestType,
        CancellationToken cancellationToken = default)
        => PostWorkflowRunJsonAsync(
            brokerPort,
            path,
            SerializeWorkflowRunRequest(request, requestType),
            cancellationToken);

    internal static async Task<TestAgentBrokerRawResponse> PostWorkflowRunJsonAsync(
        int brokerPort,
        string path,
        string json,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(
                $"http://localhost:{brokerPort}{path}",
                content,
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(body);
                return new TestAgentBrokerRawResponse((int)response.StatusCode, document.RootElement.Clone(), null);
            }
            catch (JsonException)
            {
                return new TestAgentBrokerRawResponse(
                    (int)response.StatusCode,
                    default,
                    "The broker returned an invalid structured response.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new TestAgentBrokerRawResponse(0, default, "The local DevFlow broker is unavailable.");
        }
        catch (TaskCanceledException)
        {
            return new TestAgentBrokerRawResponse(0, default, "The local DevFlow broker did not respond before the bounded deadline.");
        }
    }

    internal static Task<TestAgentBrokerRawResponse> GetInspectorWorkbenchAsync(
        int brokerPort,
        string agentId,
        string path,
        string? hostApprovalToken = null,
        CancellationToken cancellationToken = default)
        => SendInspectorWorkbenchAsync(
            HttpMethod.Get,
            brokerPort,
            agentId,
            path,
            null,
            hostApprovalToken,
            cancellationToken);

    internal static Task<TestAgentBrokerRawResponse> PostInspectorWorkbenchAsync(
        int brokerPort,
        string agentId,
        string path,
        string json,
        string? hostApprovalToken = null,
        CancellationToken cancellationToken = default)
        => SendInspectorWorkbenchAsync(
            HttpMethod.Post,
            brokerPort,
            agentId,
            path,
            json,
            hostApprovalToken,
            cancellationToken);

    private static async Task<TestAgentBrokerRawResponse> SendInspectorWorkbenchAsync(
        HttpMethod method,
        int brokerPort,
        string agentId,
        string path,
        string? json,
        string? hostApprovalToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = $"http://localhost:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}{path}";
            using var message = new HttpRequestMessage(method, uri);
            if (json is not null)
                message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(hostApprovalToken))
                message.Headers.TryAddWithoutValidation("X-DevFlow-Host-Approval-Token", hostApprovalToken);
            using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(body);
                return new TestAgentBrokerRawResponse((int)response.StatusCode, document.RootElement.Clone(), null);
            }
            catch (JsonException)
            {
                return new TestAgentBrokerRawResponse(
                    (int)response.StatusCode,
                    default,
                    "The Inspector returned an invalid structured response.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new TestAgentBrokerRawResponse(0, default, "The local DevFlow broker is unavailable.");
        }
        catch (TaskCanceledException)
        {
            return new TestAgentBrokerRawResponse(0, default, "The local DevFlow broker did not respond before the bounded deadline.");
        }
    }

    private static async Task<TestAgentBrokerResponse<TResponse>> PostAsync<TRequest, TResponse>(
        int brokerPort,
        string path,
        TRequest request,
        JsonTypeInfo<TRequest> requestType,
        JsonTypeInfo<TResponse> responseType,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(request, requestType);
            using var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new("application/json");
            using var response = await Http.PostAsync(
                $"http://localhost:{brokerPort}{path}",
                content,
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var value = JsonSerializer.Deserialize(body, responseType);
            return new TestAgentBrokerResponse<TResponse>((int)response.StatusCode, value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new TestAgentBrokerResponse<TResponse>(0, default, "The local DevFlow broker is unavailable.");
        }
        catch (TaskCanceledException)
        {
            return new TestAgentBrokerResponse<TResponse>(
                0,
                default,
                "The local DevFlow broker did not respond before the bounded deadline.");
        }
        catch (JsonException)
        {
            return new TestAgentBrokerResponse<TResponse>(
                0,
                default,
                "The local DevFlow broker returned an invalid structured response.");
        }
    }
}

internal sealed record TestAgentBrokerResponse<T>(int StatusCode, T? Value, string? TransportError);

internal sealed record TestAgentBrokerRawResponse(int StatusCode, JsonElement Value, string? TransportError)
{
    internal bool HasValue => Value.ValueKind != JsonValueKind.Undefined;
}
