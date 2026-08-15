namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// A job as JobScheduler sees it, parsed from <c>dumpsys jobscheduler</c>.
///
/// <para>
/// This is deliberately JobScheduler's view rather than WorkManager's: it is what the system
/// will actually run, it needs no cooperation from the app, and <see cref="JobId"/> is the
/// numeric id that <c>cmd jobscheduler run</c> requires. The in-app WorkManager query returns
/// the richer WorkSpec view (UUID, tags, state) — the two are complementary.
/// </para>
/// </summary>
public sealed class AndroidScheduledJob
{
    /// <summary>Numeric JobScheduler id — the one <c>cmd jobscheduler run</c> takes.</summary>
    public required string JobId { get; init; }

    /// <summary>JobScheduler namespace, e.g. <c>androidx.work.systemjobscheduler</c>. Null when unnamespaced.</summary>
    public string? Namespace { get; init; }

    /// <summary>Worker class name from the job's debug tag, when WorkManager scheduled it.</summary>
    public string? Worker { get; init; }

    /// <summary>The service that will run the job, e.g. WorkManager's SystemJobService.</summary>
    public string? Service { get; init; }

    /// <summary>Whether this job was scheduled by WorkManager rather than direct JobScheduler use.</summary>
    public bool IsWorkManager { get; init; }

    public override string ToString() =>
        $"{JobId}{(Worker is null ? "" : $" ({Worker})")}{(Namespace is null ? "" : $" [{Namespace}]")}";
}
