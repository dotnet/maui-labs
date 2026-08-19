using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.TestHost;

namespace Microsoft.Maui.Testing.Tests;

[TestClass]
public sealed class MauiTestResultConsumerTests
{
    [TestMethod]
    public void CreateCompletedEvent_ParameterizedFailure_PreservesIdentityAndDiagnostics()
    {
        Exception exception;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        var node = new TestNode
        {
            Uid = new TestNodeUid("TestMethod(row: 2)"),
            DisplayName = "TestMethod(row: 2)",
            Properties = new PropertyBag(
                new FailedTestNodeStateProperty(exception, "assertion failed")),
        };

        var completed = MauiTestResultConsumer.CreateCompletedEvent(node);

        Assert.IsNotNull(completed);
        Assert.AreEqual("TestMethod(row: 2)", completed.Uid);
        Assert.AreEqual("TestMethod(row: 2)", completed.Name);
        Assert.AreEqual("failed", completed.Outcome);
        Assert.AreEqual("assertion failed", completed.Message);
        StringAssert.Contains(completed.StackTrace, nameof(CreateCompletedEvent_ParameterizedFailure_PreservesIdentityAndDiagnostics));
    }

    [TestMethod]
    public async Task ConsumeAsync_MultipleFileArtifacts_KeepsFirstTrxReport()
    {
        var consumer = new MauiTestResultConsumer();
        var session = new SessionUid("session");
        var firstTrx = Path.GetFullPath("first.trx");

        await consumer.ConsumeAsync(
            null!,
            new SessionFileArtifact(session, new FileInfo("diagnostics.log"), "diagnostics"),
            CancellationToken.None);
        await consumer.ConsumeAsync(
            null!,
            new SessionFileArtifact(session, new FileInfo(firstTrx), "TRX report"),
            CancellationToken.None);
        await consumer.ConsumeAsync(
            null!,
            new SessionFileArtifact(session, new FileInfo("second.trx"), "another TRX report"),
            CancellationToken.None);

        Assert.AreEqual(firstTrx, consumer.TrxReportPath);
    }
}
