using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;

namespace Microsoft.Maui.Testing;

internal sealed class MauiTestResultConsumer : IDataConsumer
{
    private int _passed;
    private int _failed;
    private int _skipped;
    private string? _trxReportPath;

    public int Passed => _passed;

    public int Failed => _failed;

    public int Skipped => _skipped;

    public string? TrxReportPath => _trxReportPath;

    public event Action<MauiTestCompletedEvent>? TestCompleted;

    public string Uid => nameof(MauiTestResultConsumer);

    public string DisplayName => "MAUI test results";

    public string Description => "Collects test outcomes and report artifacts.";

    public string Version => "1.0";

    public Type[] DataTypesConsumed => [typeof(TestNodeUpdateMessage), typeof(SessionFileArtifact)];

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public Task ConsumeAsync(
        IDataProducer dataProducer,
        IData value,
        CancellationToken cancellationToken)
    {
        if (value is SessionFileArtifact artifact)
        {
            if (string.Equals(artifact.FileInfo.Extension, ".trx", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.CompareExchange(ref _trxReportPath, artifact.FileInfo.FullName, null);
            }

            return Task.CompletedTask;
        }

        if (value is not TestNodeUpdateMessage { TestNode: var node })
        {
            return Task.CompletedTask;
        }

        var completed = CreateCompletedEvent(node);
        if (completed is null)
        {
            return Task.CompletedTask;
        }

        _ = completed.Outcome switch
        {
            "passed" => Interlocked.Increment(ref _passed),
            "failed" => Interlocked.Increment(ref _failed),
            _ => Interlocked.Increment(ref _skipped),
        };

        TestCompleted?.Invoke(completed);
        return Task.CompletedTask;
    }

    internal static MauiTestCompletedEvent? CreateCompletedEvent(TestNode node)
    {
        var state = node.Properties.SingleOrDefault<TestNodeStateProperty>();
        var outcome = state switch
        {
            PassedTestNodeStateProperty => "passed",
            FailedTestNodeStateProperty or ErrorTestNodeStateProperty or TimeoutTestNodeStateProperty => "failed",
            SkippedTestNodeStateProperty => "skipped",
            _ => null,
        };
        if (outcome is null)
        {
            return null;
        }

        var identifier = node.Properties.SingleOrDefault<TestMethodIdentifierProperty>();
        var className = identifier is null
            ? null
            : string.Join(
                ".",
                new[] { identifier.Namespace, identifier.TypeName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        var exception = state switch
        {
            FailedTestNodeStateProperty failed => failed.Exception,
            ErrorTestNodeStateProperty error => error.Exception,
            TimeoutTestNodeStateProperty timeout => timeout.Exception,
            _ => null,
        };
        return new MauiTestCompletedEvent(
            node.Uid.Value,
            node.DisplayName,
            className,
            outcome,
            state?.Explanation ?? exception?.Message,
            exception?.StackTrace);
    }

    public MauiTestRunResult CreateResult(int exitCode) =>
        new(exitCode, Passed, Failed, Skipped, TrxReportPath);
}
