using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Jobs;

internal sealed class AndroidJobRunner(AgentClient client, AndroidAppDriver driver)
{
    private static readonly string[] TerminalStates = ["SUCCEEDED", "FAILED", "CANCELLED"];

    public async Task<AndroidJobRunResult> RunAsync(
        string identifier, string? package, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operation.CancelAfter(timeout);
        var ct = operation.Token;
        var status = await client.GetStatusAsync().WaitAsync(ct);
        var agentPackage = status?.App?.PackageId;
        if (string.IsNullOrWhiteSpace(agentPackage))
            throw new InvalidOperationException("The agent did not report its Android package.");
        if (!string.IsNullOrWhiteSpace(package) && package != agentPackage)
            throw new InvalidOperationException("--package must match the app connected to the agent.");

        var initial = await client.GetJobsAsync().WaitAsync(ct);
        GetWorkInfos(initial);
        if (!initial.TryGetProperty("agentInstanceId", out var identity) ||
            !Guid.TryParse(identity.GetString(), out _) ||
            !initial.TryGetProperty("agentPort", out var port))
            throw new InvalidOperationException("Update the agent to provide verified Android job targeting.");
        var instanceId = identity.GetString()!;
        var deviceJobs = await driver.ReadJobsOnDeviceAsync(port.GetInt32(), ct);
        VerifyAgent(deviceJobs, instanceId);

        var claimed = await client.ControlMutationLeaseAsync("claim").WaitAsync(ct);
        RequireLease(claimed);
        var transactionId = Guid.NewGuid().ToString("N");
        using var renewStop = new CancellationTokenSource();
        Exception? renewalFailure = null;
        Task renewal = Task.CompletedTask;
        AndroidJobRunResult result;
        try
        {
            var begun = await ControlTransactionAsync("begin", transactionId).WaitAsync(ct);
            RequireLease(begun, transactionId);
            renewal = RenewAsync(begun.ExpiresInMs);
            result = await ExecuteAsync();
        }
        catch (OperationCanceledException) when (renewalFailure is not null)
        {
            throw new InvalidOperationException("The mutation lease was lost while running Android work.", renewalFailure);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Android job execution timed out before an outcome could be verified. Work already triggered may continue.");
        }
        finally
        {
            await renewStop.CancelAsync();
            await renewal;
            var ended = await ControlTransactionAsync("end", transactionId);
            if (!ended.Ok)
                throw new InvalidOperationException(ended.Error ?? "Could not end the Android job mutation transaction.");
            var released = await client.ControlMutationLeaseAsync("release");
            if (!released.Ok)
                throw new InvalidOperationException(released.Error ?? "Could not release the Android job mutation lease.");
        }
        if (renewalFailure is not null)
            throw new InvalidOperationException("The mutation lease was lost while running Android work.", renewalFailure);
        return result;

        async Task RenewAsync(long expiresInMs)
        {
            var interval = TimeSpan.FromMilliseconds(Math.Clamp(expiresInMs / 3, 25, 1000));
            try
            {
                while (true)
                {
                    await Task.Delay(interval, renewStop.Token);
                    // Re-begin refreshes both the lease and transaction, preventing force-takeover
                    // while adb or a slow agent query is in flight.
                    var renewed = await ControlTransactionAsync("begin", transactionId);
                    RequireLease(renewed, transactionId);
                }
            }
            catch (OperationCanceledException) when (renewStop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                renewalFailure = ex;
                await operation.CancelAsync();
            }
        }

        async Task<AndroidJobRunResult> ExecuteAsync()
        {
            var scheduled = await driver.ListScheduledJobsAsync(agentPackage, ct);
            var jobs = await client.GetJobsAsync().WaitAsync(ct);
            VerifyAgent(jobs, instanceId);
            var target = ResolveJob(jobs, scheduled, identifier);
            var (before, _) = ReadState(jobs, target.WorkSpecId, target.Generation);
            if (before != "ENQUEUED")
                throw new InvalidOperationException($"Work '{target.WorkSpecId}' is {before}; only enqueued work can be forced.");
            var output = await driver.RunScheduledJobAsync(
                agentPackage, target.Job.JobId, target.Job.Namespace, cancellationToken: ct);
            var state = before;
            var attempts = 0;
            try
            {
                while (!TerminalStates.Contains(state, StringComparer.Ordinal))
                {
                    await Task.Delay(250, ct);
                    jobs = await client.GetJobsAsync().WaitAsync(ct);
                    VerifyAgent(jobs, instanceId);
                    (state, attempts) = ReadState(jobs, target.WorkSpecId, target.Generation);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && renewalFailure is null)
            {
                return new(target, before, state, attempts, output, TimedOut: true);
            }
            return new(target, before, state, attempts, output, TimedOut: false);
        }
    }

    private Task<MutationLeaseStatus> ControlTransactionAsync(string action, string transactionId) =>
        client.ControlMutationLeaseAsync(action, force: false, leaseId: null, holderKind: null, label: null, transactionId);

    private static void RequireLease(MutationLeaseStatus status, string? transactionId = null)
    {
        if (!status.Ok || !status.Allowed || !status.YouHold || status.Authority == "unsupported" ||
            (transactionId is not null && status.TransactionId != transactionId))
            throw new MutationLeaseException(status);
    }

    private static void VerifyAgent(JsonElement jobs, string expectedId)
    {
        GetWorkInfos(jobs);
        if (!jobs.TryGetProperty("agentInstanceId", out var id) || id.GetString() != expectedId)
            throw new InvalidOperationException(
                "The selected adb device does not host the connected agent instance, or the app restarted; execution cannot be verified.");
    }

    internal static (string State, int Attempts) ReadState(JsonElement jobs, string workSpecId, int generation)
    {
        foreach (var job in GetWorkInfos(jobs).EnumerateArray())
        {
            if (!string.Equals(job.GetProperty("identifier").GetString(), workSpecId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (job.GetProperty("generation").GetInt32() != generation)
                throw new InvalidOperationException($"Work '{workSpecId}' changed generation; its outcome cannot be attributed to this run.");
            return (job.GetProperty("state").GetString()
                ?? throw new InvalidOperationException($"Work '{workSpecId}' has no state."),
                job.GetProperty("runAttemptCount").GetInt32());
        }
        throw new InvalidOperationException($"Work '{workSpecId}' disappeared from the agent listing; its outcome cannot be verified.");
    }

    internal static AndroidJobTarget ResolveJob(
        JsonElement jobs, IReadOnlyList<AndroidScheduledJob> scheduled, string identifier)
    {
        var workInfos = GetWorkInfos(jobs).EnumerateArray().ToList();
        if (!jobs.TryGetProperty("scheduledJobs", out var mappings) || mappings.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Update the agent to provide exact JobScheduler mappings.");

        var explicitUuid = Guid.TryParse(identifier, out var uuid);
        var numericJobId = int.TryParse(identifier, out _);
        var candidates = new List<AndroidJobTarget>();
        foreach (var mapping in mappings.EnumerateArray())
        {
            var workSpecId = mapping.GetProperty("workSpecId").GetString()!;
            var generation = mapping.GetProperty("generation").GetInt32();
            var jobId = mapping.GetProperty("jobId").GetString();
            var jobNamespace = mapping.GetProperty("namespace").GetString();
            var info = workInfos.FirstOrDefault(w =>
                string.Equals(w.GetProperty("identifier").GetString(), workSpecId, StringComparison.OrdinalIgnoreCase));
            if (info.ValueKind != JsonValueKind.Object || info.GetProperty("generation").GetInt32() != generation)
                continue;
            var matches = explicitUuid
                ? Guid.TryParse(workSpecId, out var mappedUuid) && mappedUuid == uuid
                : numericJobId ? jobId == identifier
                : info.GetProperty("tags").EnumerateArray().Any(t => t.GetString() is string tag &&
                    (tag == identifier || tag.EndsWith("." + identifier, StringComparison.Ordinal)));
            if (!matches) continue;
            candidates.AddRange(scheduled
                .Where(j => j.IsWorkManager && j.JobId == jobId && j.Namespace == jobNamespace)
                .Select(j => new AndroidJobTarget(j, workSpecId, generation)));
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException($"No scheduled WorkManager job matched '{identifier}' by exact WorkSpec mapping.");
        if (candidates.Select(c => (c.WorkSpecId.ToUpperInvariant(), c.Generation)).Distinct().Count() != 1)
            throw new InvalidOperationException($"'{identifier}' is ambiguous. Use a unique WorkSpec UUID.");
        // WorkManager double-schedules the same WorkSpec on API 23. Either backing job is valid.
        return candidates.OrderBy(c => int.Parse(c.Job.JobId, System.Globalization.CultureInfo.InvariantCulture)).First();
    }

    private static JsonElement GetWorkInfos(JsonElement result)
    {
        if (result.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"WorkManager query failed: {error.GetString()}");
        if (!result.TryGetProperty("supported", out var supported) || !supported.GetBoolean())
            throw new InvalidOperationException(result.TryGetProperty("reason", out var reason)
                ? reason.GetString() : "WorkManager inspection is unsupported.");
        if (!result.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("WorkManager query returned no job collection.");
        return jobs;
    }
}

internal sealed record AndroidJobTarget(AndroidScheduledJob Job, string WorkSpecId, int Generation);
internal sealed record AndroidJobRunResult(
    AndroidJobTarget Target, string StateBefore, string State, int Attempts, string AdbOutput, bool TimedOut)
{
    public bool Success => !TimedOut && State == "SUCCEEDED";
}
