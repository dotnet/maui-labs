using System.Globalization;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Devices;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal enum ExactAgentBindingSelectionKind
{
    Pending,
    Matched,
    Ambiguous,
}

internal sealed record ExactAgentBindingSelection(
    ExactAgentBindingSelectionKind Kind,
    AgentRegistration? Agent,
    bool MatchingStaleAgentObserved);

internal sealed class ExactAgentBindingResolver
{
    /// <summary>Agent platform values whose activation model can redirect into a live instance.</summary>
    private static readonly string[] WinUiPlatformAliases = ["windows", "winui"];

    private readonly Func<int, Task<AgentRegistration[]?>> _listAgentsAsync;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _pollInterval;

    public ExactAgentBindingResolver(
        Func<int, Task<AgentRegistration[]?>> listAgentsAsync,
        TimeProvider? clock = null,
        TimeSpan? pollInterval = null)
    {
        _listAgentsAsync = listAgentsAsync ?? throw new ArgumentNullException(nameof(listAgentsAsync));
        _clock = clock ?? TimeProvider.System;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public async Task<AgentRegistration[]> SnapshotAsync(
        int brokerPort,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _listAgentsAsync(brokerPort).ConfigureAwait(false)
            ?? throw FlowExecutionException.Infrastructure(
                "broker-agent-snapshot-failed",
                "The DevFlow broker could not provide the pre-launch agent snapshot.");
    }

    public async Task<AgentRegistration> WaitForNewAsync(
        int brokerPort,
        IReadOnlyCollection<AgentRegistration> snapshot,
        ExactAgentBindingExpectation expectation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = _clock.GetUtcNow() + timeout;
        var staleObserved = false;
        while (_clock.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await _listAgentsAsync(brokerPort).ConfigureAwait(false);
            if (current is null)
            {
                throw FlowExecutionException.Infrastructure(
                    "broker-agent-list-failed",
                    "The DevFlow broker became unavailable while waiting for the launched app.");
            }

            var selection = SelectNewMatch(snapshot, current, expectation);
            staleObserved |= selection.MatchingStaleAgentObserved;
            if (selection.Kind == ExactAgentBindingSelectionKind.Ambiguous)
            {
                throw FlowExecutionException.Invalid(
                    "agent-binding-ambiguous",
                    "Multiple new DevFlow agents matched the launched app and device.");
            }
            if (selection.Agent is not null)
                return selection.Agent;

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        throw FlowExecutionException.Infrastructure(
            staleObserved ? "stale-agent-rejected" : "new-agent-timeout",
            staleObserved
                ? "Only a stale pre-launch DevFlow agent matched; a new launched instance never registered."
                : "The launched app did not register a matching new DevFlow agent before the timeout.");
    }

    internal static ExactAgentBindingSelection SelectNewMatch(
        IReadOnlyCollection<AgentRegistration> snapshot,
        IReadOnlyCollection<AgentRegistration> current,
        ExactAgentBindingExpectation expectation)
    {
        var previous = snapshot.Select(InstanceKey).ToHashSet(StringComparer.Ordinal);
        var matching = current.Where(agent => Matches(agent, expectation)).ToArray();
        var newMatches = matching.Where(agent => !previous.Contains(InstanceKey(agent))).ToArray();
        if (newMatches.Length > 1)
            return new(ExactAgentBindingSelectionKind.Ambiguous, null, matching.Length > newMatches.Length);
        if (newMatches.Length == 1)
            return new(ExactAgentBindingSelectionKind.Matched, newMatches[0], matching.Length > 1);
        return new(ExactAgentBindingSelectionKind.Pending, null, matching.Length > 0);
    }

    /// <summary>
    /// Polls the newly bound agent until it reports itself running, or the budget runs out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registration and readiness are not the same event. The agent registers with the broker as
    /// soon as it starts, but its own HTTP surface answers a moment later, and a freshly installed
    /// Debug build cold-starting on an emulator can take longer still. A single probe taken right
    /// after port forwarding therefore fails on a healthy app that is merely slow, which reads as
    /// a broken product rather than a race.
    /// </para>
    /// <para>
    /// The wait shares the caller's agent budget rather than adding one of its own, so the total
    /// time spent getting an agent ready stays bounded by what the operator asked for. A read that
    /// throws is treated as not-yet-ready and retried; only the deadline ends the wait, and the
    /// deadline always ends it with one of this method's two structured outcomes.
    /// </para>
    /// </remarks>
    public async Task<AgentStatus> WaitForLiveStatusAsync(
        Func<Task<AgentStatus?>> readStatusAsync,
        ExactAgentBindingExpectation expectation,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readStatusAsync);

        var deadline = _clock.GetUtcNow() + timeout;
        AgentStatus? status = null;
        var everReachable = false;
        var attempts = 0;
        Exception? lastReadFailure = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                status = await readStatusAsync().ConfigureAwait(false);
                if (status is not null)
                    everReachable = true;
                lastReadFailure = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A probe that throws is a probe that did not answer, including the one that lands
                // on or after the deadline. Filtering this catch on the deadline let that final
                // read escape as a raw HttpRequestException, which the coordinator could only
                // report as an unexpected execution error rather than the structured outcome its
                // callers classify. The cause is kept and named in bounded form below.
                status = null;
                lastReadFailure = ex;
            }

            // A read cancelled mid-flight can surface as a transport or disposal failure rather
            // than OperationCanceledException, and swallowing that would convert a cancellation
            // whose completion is unknown into a confident infrastructure verdict.
            cancellationToken.ThrowIfCancellationRequested();

            if (status?.Running == true)
                break;

            if (_clock.GetUtcNow() >= deadline)
            {
                // Name which of the two faults happened. "Not running" sends someone looking at the
                // app; "never answered" sends them to forwarding and ports, and telling them apart
                // afterwards costs a reproduction. The last probe's exception type is named too,
                // because "check port forwarding" is the wrong remedy for a deserialization defect.
                // An agent that answered earlier and then stopped answering is still the app's
                // fault rather than forwarding's -- the port worked -- but saying it "never
                // reported itself running" describes a probe sequence that did not happen, and
                // hides the one fact worth knowing: it stopped responding, so look for a crash.
                throw everReachable
                    ? FlowExecutionException.Infrastructure(
                        "agent-not-running",
                        (status is null
                            ? $"The newly bound DevFlow agent answered earlier but stopped answering " +
                              $"its status endpoint before the {timeout.TotalSeconds:0}-second " +
                              $"readiness deadline ({attempts} probes)."
                            : $"The newly bound DevFlow agent answered but never reported itself running " +
                              $"within {timeout.TotalSeconds:0} seconds ({attempts} probes).") +
                        LastFailureSuffix(lastReadFailure),
                        lastReadFailure)
                    : FlowExecutionException.Infrastructure(
                        "agent-not-reachable",
                        $"The newly bound DevFlow agent never answered its status endpoint within " +
                        $"{timeout.TotalSeconds:0} seconds ({attempts} probes)." +
                        LastFailureSuffix(lastReadFailure) +
                        " Check agent port forwarding.",
                        lastReadFailure);
            }

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }

        ValidateLiveStatus(status, expectation);
        return status!;
    }

    /// <summary>
    /// Names the last probe failure by type only. The type is the part that redirects the reader
    /// (a transport refusal and a deserialization defect need different remedies); the exception
    /// text can carry hosts, ports, and payload fragments, so it stays out of the report message
    /// and remains available on the inner exception.
    /// </summary>
    private static string LastFailureSuffix(Exception? failure)
        => failure is null ? "" : $" The last probe failed with {failure.GetType().Name}.";

    internal static void ValidateLiveStatus(
        AgentStatus? status,
        ExactAgentBindingExpectation expectation)
    {
        if (status?.Running != true)
            throw FlowExecutionException.Infrastructure("agent-not-running", "The newly bound DevFlow agent is not running.");
        if (!string.Equals(status.App?.PackageId, expectation.PackageId, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "agent-package-mismatch",
                "The newly bound DevFlow agent reported a different package identity.");
        }
        if (expectation.ProcessId is not null &&
            status.App?.ProcessId != expectation.ProcessId)
        {
            // Name both ids and a remedy the operator can act on. "A different process identity"
            // alone sent a reader hunting for a defect that was not there; refusing is right,
            // because binding would otherwise have tested an hours-old build.
            throw FlowExecutionException.Invalid(
                "agent-process-mismatch",
                "The newly bound DevFlow agent reported a different process identity. " +
                $"Launched process {expectation.ProcessId}, agent reported " +
                $"{status.App?.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "none"}. " +
                ProcessMismatchRemedy(expectation));
        }
        if (!string.IsNullOrWhiteSpace(status.Device?.Platform) &&
            !MatchesPlatform(status.Device.Platform, expectation.PlatformAliases))
        {
            throw FlowExecutionException.Invalid(
                "agent-platform-mismatch",
                "The newly bound DevFlow agent reported a different platform.");
        }
        if (!string.IsNullOrWhiteSpace(expectation.AppBuild) &&
            !string.IsNullOrWhiteSpace(status.App?.Build) &&
            !string.Equals(status.App.Build, expectation.AppBuild, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "agent-build-mismatch",
                "The newly bound DevFlow agent reported a different app build.");
        }
    }

    /// <summary>
    /// Explains a process-identity mismatch with a cause that is true on the target's platform.
    /// </summary>
    /// <remarks>
    /// WinUI redirects a second activation into an already running instance, so an app left over
    /// from an earlier run absorbs the launch, the freshly launched process exits, and the stale
    /// instance answers -- naming that there turns a puzzling refusal into a one-step fix. None of
    /// that mechanism exists on the other targets this resolver binds (Android, iOS, Mac Catalyst,
    /// macOS, WPF), where the same mismatch only means the launched process and the responding
    /// agent are not the same process. Those get a neutral remedy rather than a confident
    /// diagnosis that cannot apply. Both variants carry only the two process ids the caller
    /// already supplied.
    /// </remarks>
    private static string ProcessMismatchRemedy(ExactAgentBindingExpectation expectation)
        => IsWinUiTarget(expectation)
            ? "On Windows an instance left running by an earlier run can absorb the launch through " +
              "WinUI single-instance redirection and answer in place of the new process; close any " +
              "running instance of the app and run again."
            : "The launched process and the responding agent are not the same process; close any " +
              "instance of the app left over from an earlier run and run again.";

    private static bool IsWinUiTarget(ExactAgentBindingExpectation expectation)
        => WinUiPlatformAliases.Any(alias =>
            string.Equals(expectation.Platform, alias, StringComparison.OrdinalIgnoreCase) ||
            expectation.PlatformAliases.Any(candidate =>
                string.Equals(candidate, alias, StringComparison.OrdinalIgnoreCase)));

    private static bool Matches(AgentRegistration agent, ExactAgentBindingExpectation expectation)
    {
        if (!string.Equals(agent.SessionId, expectation.SessionId, StringComparison.Ordinal) ||
            !string.Equals(agent.Tfm, expectation.TargetFramework, StringComparison.OrdinalIgnoreCase) ||
            !MatchesPlatform(agent.Platform, expectation.PlatformAliases) ||
            !string.Equals(agent.PackageId, expectation.PackageId, StringComparison.Ordinal))
        {
            return false;
        }

        if (expectation.ProcessId is not null &&
            agent.ProcessId != expectation.ProcessId)
        {
            return false;
        }

        if (!expectation.RequireDeviceIdentityMatch)
            return true;
        if (string.IsNullOrWhiteSpace(agent.DeviceId))
            return false;

        var identity = DeviceIdentity.Parse(agent.DeviceId);
        if (!string.IsNullOrWhiteSpace(identity.Platform) &&
            !MatchesPlatform(identity.Platform, expectation.PlatformAliases))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectation.DeviceEmulatorId))
        {
            return string.Equals(
                identity.AvdName?.Trim(),
                expectation.DeviceEmulatorId.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(expectation.DeviceSerial))
        {
            return string.Equals(identity.Serial, expectation.DeviceSerial, StringComparison.Ordinal) ||
                string.Equals(identity.Udid, expectation.DeviceSerial, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool MatchesPlatform(string value, IReadOnlyCollection<string> aliases)
        => aliases.Any(alias => string.Equals(alias, value, StringComparison.OrdinalIgnoreCase));

    private static string InstanceKey(AgentRegistration agent)
        => !string.IsNullOrWhiteSpace(agent.InstanceId)
            ? agent.InstanceId
            : string.Join(
                "\u001f",
                agent.Id,
                agent.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                agent.ProcessId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                agent.ConnectedAt.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

}
