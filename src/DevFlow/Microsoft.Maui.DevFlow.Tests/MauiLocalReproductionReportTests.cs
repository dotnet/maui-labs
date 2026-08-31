using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiLocalReproductionReportTests
{
    [Fact]
    public void Serializer_RedactsPathsAndAuthorityAndProducesStableBytes()
    {
        const string secret = "CorrectHorseBatteryStaple";
        var report = new MauiLocalReproductionReport
        {
            ImportedArtifact = MauiImportedArtifactIdentity.Create(),
            ImportedArtifactKind = "flow-run",
            ImportedArtifactDigest = new string('a', 64),
            LocalRunId = "run-local",
            LocalExitCategory = "test-failure",
            LocalManifestDigest = new string('b', 64),
            LocalReportDigest = new string('c', 64),
            Matched = true,
            TrustState = MauiArtifactTrustStates.LocallyReproduced,
            ReasonCodes = ["locally-reproduced", $"reveal {secret}"],
            MissingFacts = [@"C:\Users\person\secret"],
            ImportedFailureFingerprint = new string('d', 64),
            ImportedStepFingerprint = new string('e', 64),
            ImportedRuntimeProfileFingerprint = new string('6', 64),
            LocalFailureFingerprint = new string('f', 64),
            LocalStepFingerprint = new string('1', 64),
            LocalRuntimeProfileFingerprint = new string('7', 64),
            LocalExpectedCheckpointFingerprint = new string('2', 64),
            LocalObservedCheckpointFingerprint = new string('3', 64),
            LocalArtifacts =
            [
                new MauiTestExecutionArtifact
                {
                    ArtifactId = "report",
                    Kind = "flow-run-report",
                    RelativePath = @"C:\Users\person\raw.log",
                    Digest = new string('4', 64),
                    Redacted = true,
                },
                new MauiTestExecutionArtifact
                {
                    ArtifactId = "manifest",
                    Kind = "execution-manifest",
                    RelativePath = "execution-manifest.json",
                    Digest = new string('5', 64),
                    Redacted = true,
                },
            ],
            BrokerBindingPersisted = true,
            ApprovalGranted = true,
            ProposalCreated = true,
        };

        var first = MauiLocalReproductionReportSerializer.SerializeToUtf8Bytes(report);
        var second = MauiLocalReproductionReportSerializer.SerializeToUtf8Bytes(report);
        var json = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\", json, StringComparison.Ordinal);
        Assert.Contains("\"brokerBindingPersisted\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"approvalGranted\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"proposalCreated\": false", json, StringComparison.Ordinal);
        Assert.Contains("\"reviewAction\": \"open-inspector-workbench-repair-review\"", json, StringComparison.Ordinal);
        Assert.Contains("\"relativePath\": \"execution-manifest.json\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("raw.log", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Contract_RoundTripsExtensionDataWithSourceGeneratedContext()
    {
        const string json = """
            {
              "schema": 1,
              "importTrustState": "untrusted",
              "matched": false,
              "trustState": "untrusted",
              "reasonCodes": [],
              "missingFacts": [],
              "localArtifacts": [],
              "brokerBindingPersisted": false,
              "approvalGranted": false,
              "proposalCreated": false,
              "reviewAction": "inspect-local-reproduction-diagnostics",
              "x-local-reproduction": true
            }
            """;

        var report = JsonSerializer.Deserialize(
            json,
            MauiTestingJsonContext.Default.MauiLocalReproductionReport);

        Assert.NotNull(report);
        Assert.True(report!.ExtensionData!.ContainsKey("x-local-reproduction"));
        Assert.Contains(
            "\"x-local-reproduction\"",
            JsonSerializer.Serialize(report, MauiTestingJsonContext.Default.MauiLocalReproductionReport),
            StringComparison.Ordinal);
    }
}
