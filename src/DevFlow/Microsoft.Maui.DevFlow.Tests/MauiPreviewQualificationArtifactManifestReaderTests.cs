using Microsoft.Maui.DevFlow.Testing;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.DevFlow.Tests;

public sealed class MauiPreviewQualificationArtifactManifestReaderTests
{
    private const string ReportPath =
        "artifacts/devflow/123-1/android/demo-ci-fix-drift-attempt-1/flow-run.json";
    private const string TracePath =
        "artifacts/devflow/123-1/android/demo-ci-fix-drift-attempt-1/failure.mauitrace";
    private const string ReportIdentityHex =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private const string ReportIdentityDigest = $"sha256:{ReportIdentityHex}";
    private const string ReportBytesDigest =
        "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    private const string TraceBytesDigest =
        "sha256:3333333333333333333333333333333333333333333333333333333333333333";

    [Fact]
    public void ParseJson_ProjectsFirstAttemptReportAndEvidenceArtifactReferences()
    {
        var parsed = MauiPreviewQualificationArtifactManifestReader.ParseJson(ManifestJson());

        Assert.True(parsed.Ok, string.Join(", ", parsed.Errors));
        Assert.Contains(
            parsed.Input.ArtifactRefs,
            reference =>
                reference.Kind == "report" &&
                reference.Digest == ReportIdentityDigest &&
                reference.Reference == ReportPath &&
                reference.Redacted == true);
        Assert.Contains(
            parsed.Input.ArtifactRefs,
            reference =>
                reference.Kind == "flow-run-report" &&
                reference.Digest == ReportBytesDigest &&
                reference.Reference == ReportPath &&
                reference.Redacted == true);
        Assert.Contains(
            parsed.Input.ArtifactRefs,
            reference =>
                reference.Kind == "mauitrace" &&
                reference.Digest == TraceBytesDigest &&
                reference.Reference == TracePath &&
                reference.Redacted == true);

        var qualification = MauiPreviewQualificationGateEvaluator.Evaluate(parsed.Input);
        Assert.Contains(
            qualification.ArtifactRefs,
            reference =>
                reference.Kind == "report" &&
                reference.Digest == ReportIdentityDigest &&
                reference.Reference == MauiQualificationSanitizer.Fingerprint(ReportPath));
        Assert.Contains(
            qualification.ArtifactRefs,
            reference =>
                reference.Kind == "flow-run-report" &&
                reference.Digest == ReportBytesDigest &&
                reference.Reference == MauiQualificationSanitizer.Fingerprint(ReportPath));
        Assert.Contains(
            qualification.ArtifactRefs,
            reference =>
                reference.Kind == "mauitrace" &&
                reference.Digest == TraceBytesDigest &&
                reference.Reference == MauiQualificationSanitizer.Fingerprint(TracePath));
    }

    [Fact]
    public void ParseJson_UnredactedEvidenceReference_IsIncomplete()
    {
        var manifest = JsonNode.Parse(ManifestJson())!.AsObject();
        manifest["artifacts"]![1]!["redacted"] = false;
        var parsed = MauiPreviewQualificationArtifactManifestReader.ParseJson(
            manifest.ToJsonString());

        Assert.False(parsed.Ok);
        Assert.Contains("artifact-manifest-evidence-reference-invalid", parsed.Errors);
        Assert.False(parsed.Input.Evidence?.ArtifactReferencesComplete);
        Assert.DoesNotContain(parsed.Input.ArtifactRefs, reference => reference.Kind == "mauitrace");
    }

    private static string ManifestJson() =>
        $$"""
        {
          "schema": 1,
          "kind": "devflow-flow-pilot",
          "repository": {
            "commit": "0123456789abcdef0123456789abcdef01234567"
          },
          "workflow": {
            "runId": "123-1",
            "name": "DevFlow Integration Tests",
            "attempt": "1"
          },
          "testing": {
            "packageVersion": "0.1.0-preview"
          },
          "platform": {
            "name": "android",
            "experimental": false,
            "officialCoverage": true,
            "deviceId": "sha256:4444444444444444444444444444444444444444444444444444444444444444",
            "deviceProfile": "android-emulator:test",
            "agentInstanceId": "agent-instance",
            "deviceEvidence": {
              "kind": "emulator",
              "realDevice": false
            },
            "androidSdk": {
              "apiLevel": "35",
              "avdName": "test",
              "abi": "x86_64",
              "target": "google_apis",
              "workloadVersion": "10.0.203"
            }
          },
          "app": {
            "packageId": "com.companyname.mauitodo",
            "buildFingerprint": "sha256:5555555555555555555555555555555555555555555555555555555555555555",
            "packageDigest": "sha256:6666666666666666666666666666666666666666666666666666666666666666"
          },
          "flows": [
            {
              "name": "demo-ci-fix-drift",
              "digest": "sha256:7777777777777777777777777777777777777777777777777777777777777777",
              "sourcePath": "samples/DevFlow.Sample/maui-tests/demo-ci-fix-drift.md",
              "tier": "tier-1",
              "firstAttempt": {
                "runId": "demo-ci-fix-drift-attempt-1",
                "runKind": "clean",
                "repetition": 1,
                "outcome": "failed",
                "verified": false,
                "reportPath": "{{ReportPath}}",
                "reportDigest": "{{ReportIdentityHex}}",
                "failureClass": "locator-not-found",
                "failureCode": "locator-not-found"
              },
              "cleanAttempts": [],
              "diagnosticReruns": []
            }
          ],
          "artifacts": [
            {
              "kind": "flow-run-report",
              "path": "{{ReportPath}}",
              "sha256": "{{ReportBytesDigest}}",
              "sizeBytes": 123,
              "redacted": true
            },
            {
              "kind": "mauitrace",
              "path": "{{TracePath}}",
              "sha256": "{{TraceBytesDigest}}",
              "sizeBytes": 456,
              "redacted": true
            }
          ],
          "privacy": {
            "excludedByDefault": ["screenshots", "source", "raw-model-context"]
          },
          "truncated": false,
          "truncation": {
            "maxArtifacts": 256,
            "omittedArtifacts": 0
          },
          "omissions": [],
          "validationErrors": []
        }
        """;
}
