using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class FlowQualificationCliTests : IDisposable
{
    private readonly string _root = Path.Combine(
        AppContext.BaseDirectory,
        "flow-qualification-cli-tests",
        Guid.NewGuid().ToString("N"));

    public FlowQualificationCliTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FlowQualify_StaticCorpusWritesAdvisoryNotQualifiedJsonWithoutConnectingToAnAgent()
    {
        var output = Path.Combine(_root, "qualification.json");
        var corpus = Path.Combine(FindRepositoryRoot(), "tests", "DevFlow", "InspectorCorpus");
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync(
            "devflow",
            "flow",
            "qualify",
            "--platform",
            "android",
            "--corpus",
            corpus,
            "--output",
            output,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output));
        var envelope = result.ParseJsonOutput();

        // Without --accumulate or --baseline the envelope carries the report alone; a consumer that
        // sees an accumulation or a regression list here would be reading evidence that never ran.
        Assert.False(envelope.TryGetProperty("accumulation", out _));
        Assert.False(envelope.TryGetProperty("baselineRegressions", out _));
        var json = envelope.GetProperty("report");

        // --out keeps the bare report shape so a committed baseline stays diffable against itself.
        var written = JsonDocument.Parse(await File.ReadAllTextAsync(output)).RootElement;
        Assert.Equal("maui-preview-qualification", written.GetProperty("kind").GetString());
        Assert.False(written.TryGetProperty("report", out _));

        Assert.Equal("maui-preview-qualification", json.GetProperty("kind").GetString());
        Assert.Equal("not-qualified", json.GetProperty("status").GetString());
        Assert.Equal(316, json.GetProperty("metrics").GetProperty("falseHeals").GetProperty("denominator").GetInt32());
        Assert.Equal(0, json.GetProperty("metrics").GetProperty("falseHeals").GetProperty("numerator").GetInt32());

        // 0/316 pools 300 mutants of a handful of seeds behind 16 curated cases. Only the curated
        // share is independent, and the gate minimum is compared against that share alone.
        Assert.Equal(
            16,
            json.GetProperty("metrics").GetProperty("falseHeals").GetProperty("independentEvaluations").GetInt32());

        // 0/316 must be readable as its curated and generated shares, never as 316 independent trials.
        var sourceCounts = json.GetProperty("metrics").GetProperty("falseHeals").GetProperty("sourceCounts")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("source").GetString()!,
                item => item.GetProperty("denominator").GetInt32(),
                StringComparer.Ordinal);
        Assert.Equal(16, sourceCounts["curated"]);
        Assert.Equal(300, sourceCounts["generated"]);

        var corpusSummary = json.GetProperty("corpus");
        Assert.Equal(58, corpusSummary.GetProperty("curatedCases").GetInt32());
        Assert.Equal(31, corpusSummary.GetProperty("curatedRepairPositiveCases").GetInt32());
        Assert.Equal(16, corpusSummary.GetProperty("curatedNoRepairCases").GetInt32());
        Assert.Equal(300, corpusSummary.GetProperty("generatedNoRepairCases").GetInt32());
        Assert.True(corpusSummary.GetProperty("provenanceComplete").GetBoolean());
        Assert.Equal(
            "synthetic",
            corpusSummary.GetProperty("provenanceSourceCounts").EnumerateArray().Single().GetProperty("sourceKind").GetString());

        var classification = json.GetProperty("metrics").GetProperty("classificationAccuracy");
        Assert.Equal(45, classification.GetProperty("denominator").GetInt32());
        Assert.Equal(42, classification.GetProperty("numerator").GetInt32());

        // 42/45 is mostly the classifier agreeing with a label it was handed. Only the cases whose
        // class had to be inferred count toward the gate.
        Assert.Equal(8, classification.GetProperty("independentEvaluations").GetInt32());
        var matrix = json.GetProperty("metrics").GetProperty("classificationMatrix");
        Assert.Equal("measured", matrix.GetProperty("state").GetString());
        Assert.Equal(8, matrix.GetProperty("inferredSampleCount").GetInt32());
        Assert.Equal(8, matrix.GetProperty("inferredCorrect").GetInt32());
        Assert.Equal(37, matrix.GetProperty("stampHonouredSampleCount").GetInt32());
        Assert.Equal(34, matrix.GetProperty("stampHonouredCorrect").GetInt32());

        // 31/31 repair precision is one curated seed plus 30 restatements of it.
        var repairPrecision = json.GetProperty("metrics").GetProperty("repairPrecision");
        Assert.Equal(31, repairPrecision.GetProperty("denominator").GetInt32());
        Assert.Equal(1, repairPrecision.GetProperty("independentEvaluations").GetInt32());

        Assert.Equal(
            "missing",
            json.GetProperty("metrics").GetProperty("runtimeOverhead").GetProperty("deviceOverhead").GetProperty("state").GetString());
    }

    [Fact]
    public async Task FlowQualify_AccumulateMergesAcrossRunsAndRefusesToCountTheSameEvidenceTwice()
    {
        var accumulate = Path.Combine(_root, "accumulation");
        var corpus = Path.Combine(FindRepositoryRoot(), "tests", "DevFlow", "InspectorCorpus");
        var cli = new CliTestHarness(mockAgentPort: 1);

        for (var run = 0; run < 3; run++)
        {
            var result = await cli.InvokeRawAsync(
                "devflow",
                "flow",
                "qualify",
                "--platform",
                "android",
                "--corpus",
                corpus,
                "--accumulate",
                accumulate,
                "--json");
            Assert.Equal(0, result.ExitCode);

            // The accumulation is what a caller is actually asking for here, so it has to be in the
            // stdout envelope rather than only in a file the caller has to know to go and read.
            var envelope = result.ParseJsonOutput();
            Assert.Equal(
                "maui-preview-qualification-accumulation",
                envelope.GetProperty("accumulation").GetProperty("kind").GetString());
        }

        var accumulated = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(accumulate, "accumulated.json"))).RootElement;
        Assert.Equal("maui-preview-qualification-accumulation", accumulated.GetProperty("kind").GetString());

        // Three identical static runs are one observation. Re-running the same corpus must never
        // manufacture the independent trials the repair-precision gate is waiting for.
        Assert.Equal(1, accumulated.GetProperty("consideredRuns").GetInt32());
        Assert.Equal(1, accumulated.GetProperty("acceptedRuns").GetInt32());
        Assert.Equal(
            31,
            accumulated.GetProperty("metrics").GetProperty("repairPrecision").GetProperty("denominator").GetInt32());
        Assert.Equal("not-qualified", accumulated.GetProperty("status").GetString());
        Assert.Contains(
            accumulated.GetProperty("gates").EnumerateArray(),
            gate => gate.GetProperty("gateId").GetString() == "accumulated-repair-precision" &&
                gate.GetProperty("status").GetString() == "not-qualified");
    }

    [Fact]
    public async Task FlowQualify_BaselineDiffPassesOnTheCommittedBaselineAndFailsOnARegression()
    {
        var repositoryRoot = FindRepositoryRoot();
        var corpus = Path.Combine(repositoryRoot, "tests", "DevFlow", "InspectorCorpus");
        var baseline = Path.Combine(corpus, "baselines", "qualification.json");
        var cli = new CliTestHarness(mockAgentPort: 1);

        var clean = await cli.InvokeRawAsync(
            "devflow", "flow", "qualify",
            "--platform", "android",
            "--corpus", corpus,
            "--baseline", baseline,
            "--json");
        Assert.Equal(0, clean.ExitCode);

        // A baseline claiming better numbers than the current run must fail the diff, so a
        // regression in repair precision or false-heal cleanliness cannot land silently.
        var stricter = Path.Combine(_root, "stricter-baseline.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(baseline))!;
        document["metrics"]!["falseHeals"]!["denominator"] = 400;
        document["metrics"]!["repairPrecision"]!["numerator"] = 31;
        document["metrics"]!["repairPrecision"]!["denominator"] = 31;
        document["metrics"]!["classificationAccuracy"]!["numerator"] = 45;
        document["metrics"]!["classificationAccuracy"]!["denominator"] = 45;
        await File.WriteAllTextAsync(stricter, document.ToJsonString());

        var regressed = await cli.InvokeRawAsync(
            "devflow", "flow", "qualify",
            "--platform", "android",
            "--corpus", corpus,
            "--baseline", stricter,
            "--json");
        Assert.NotEqual(0, regressed.ExitCode);

        // The failure has to name what regressed, otherwise CI reports a red build with no evidence.
        var regressions = regressed.ParseJsonOutput().GetProperty("baselineRegressions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Contains(regressions, value => value.Contains("falseHeals", StringComparison.Ordinal));
        Assert.Contains(regressions, value => value.Contains("classificationAccuracy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlowQualify_BaselineDiffCatchesARelabelThatLeavesEveryNumberUnchanged()
    {
        // What a number measures can regress without the number moving. A baseline that recorded
        // repair scoring as the shipped analyzer, against a run that scores it with harness rules,
        // is a silent loss of meaning that no count comparison would notice.
        var repositoryRoot = FindRepositoryRoot();
        var corpus = Path.Combine(repositoryRoot, "tests", "DevFlow", "InspectorCorpus");
        var baseline = Path.Combine(corpus, "baselines", "qualification.json");
        var cli = new CliTestHarness(mockAgentPort: 1);

        var relabelled = Path.Combine(_root, "relabelled-baseline.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(baseline))!;
        document["metrics"]!["falseHeals"]!["exercises"]!["kind"] = "shipped-analyzer";
        document["metrics"]!["repairPrecision"]!["exercises"]!["component"] = "MauiSelectorHealthAnalyzer.Analyze";
        document["corpus"]!["exercisesShippedAnalyzer"] = true;
        await File.WriteAllTextAsync(relabelled, document.ToJsonString());

        var result = await cli.InvokeRawAsync(
            "devflow", "flow", "qualify",
            "--platform", "android",
            "--corpus", corpus,
            "--baseline", relabelled,
            "--json");

        Assert.NotEqual(0, result.ExitCode);
        var regressions = result.ParseJsonOutput().GetProperty("baselineRegressions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Contains(regressions, value => value.Contains("falseHeals exercises.kind", StringComparison.Ordinal));
        Assert.Contains(regressions, value => value.Contains("repairPrecision exercises.component", StringComparison.Ordinal));
        Assert.Contains(regressions, value => value.Contains("corpus.exercisesShippedAnalyzer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlowQualify_BaselineDiffFailsWhenCuratedEvidenceIsDeleted()
    {
        var repositoryRoot = FindRepositoryRoot();
        var corpus = Path.Combine(repositoryRoot, "tests", "DevFlow", "InspectorCorpus");
        var baseline = Path.Combine(corpus, "baselines", "qualification.json");
        var thinned = Path.Combine(_root, "thinned-corpus");
        CopyDirectory(corpus, thinned);

        // Deleting the awkward cases is the cheapest way to make a rate look better. It must fail
        // the diff rather than quietly re-baseline on a smaller corpus.
        var casesDirectory = Path.Combine(thinned, "cases");
        foreach (var file in Directory.GetFiles(casesDirectory, "*.json").OrderBy(value => value, StringComparer.Ordinal).Take(5))
        {
            File.Delete(file);
        }

        var cli = new CliTestHarness(mockAgentPort: 1);
        var result = await cli.InvokeRawAsync(
            "devflow", "flow", "qualify",
            "--platform", "android",
            "--corpus", thinned,
            "--baseline", baseline,
            "--json");

        Assert.NotEqual(0, result.ExitCode);
        var regressions = result.ParseJsonOutput().GetProperty("baselineRegressions").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
        Assert.Contains(regressions, value => value.Contains("corpus.curatedCases", StringComparison.Ordinal));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    [Fact]
    public async Task FlowQualify_AppleQaManifestUsesTheAppleAdapterAndPreservesSimulatorEvidence()
    {
        var output = Path.Combine(_root, "apple-qualification.json");
        var manifest = Path.Combine(_root, "apple-manifest.json");
        var corpus = Path.Combine(FindRepositoryRoot(), "tests", "DevFlow", "InspectorCorpus");
        await File.WriteAllTextAsync(manifest, CreateAppleManifest());
        var cli = new CliTestHarness(mockAgentPort: 1);

        var result = await cli.InvokeRawAsync(
            "devflow",
            "flow",
            "qualify",
            "--platform",
            "ios",
            "--corpus",
            corpus,
            "--artifact-manifest",
            manifest,
            "--output",
            output,
            "--json");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output));
        var json = result.ParseJsonOutput().GetProperty("report");
        var apple = json.GetProperty("appleQa");
        Assert.Equal("ios", apple.GetProperty("platform").GetString());
        Assert.True(apple.GetProperty("foregroundProof").GetBoolean());
        Assert.True(apple.GetProperty("authenticatedTransport").GetBoolean());
        Assert.Equal(1, apple.GetProperty("firstAttemptCount").GetInt32());
        var profile = Assert.Single(
            json.GetProperty("profiles").EnumerateArray(),
            value => value.GetProperty("deviceEvidenceKind").GetString() == "simulator");
        Assert.False(profile.GetProperty("realDevice").GetBoolean());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static string CreateAppleManifest()
    {
        var hash = "sha256:" + new string('a', 64);
        return $$"""
            {
              "schema": 1,
              "kind": "devflow-flow-qa",
              "repository": { "commit": "commit" },
              "workflow": { "runId": "run-1" },
              "experimental": false,
              "backend": null,
              "officialCoverage": true,
              "macCatalystEquivalent": null,
              "testing": { "packageVersion": "testing" },
              "platform": {
                "name": "ios",
                "host": {
                  "xcode": "Xcode 26.3",
                  "runtime": "iOS 18.0",
                  "deviceEvidence": {
                    "kind": "simulator",
                    "realDevice": false,
                    "deviceIdFingerprint": "{{hash}}",
                    "profile": "iPhone"
                  }
                }
              },
              "app": {
                "project": "samples/DevFlow.Sample/DevFlow.Sample.csproj",
                "sourceDigest": "{{hash}}",
                "buildFingerprint": "{{hash}}",
                "packageDigest": "{{hash}}"
              },
              "flows": [
                { "path": "samples/DevFlow.Sample/maui-tests/tier-one.md", "sha256": "{{hash}}" }
              ],
              "appleQa": {
                "schema": 1,
                "kind": "devflow-apple-flow-qa",
                "platform": "ios",
                "experimental": false,
                "backend": null,
                "officialCoverage": true,
                "macCatalystEquivalent": null,
                "spike": {
                  "status": "proved",
                  "foregroundProof": true,
                  "authenticatedTransport": true,
                  "receipt": true,
                  "cancellation": true,
                  "parity": true
                },
                "checkpoint": {
                  "resetFingerprint": "{{hash}}",
                  "seedFingerprint": "{{hash}}",
                  "backendStateFingerprint": "{{hash}}"
                },
                "apple": {
                  "xcodeVersion": "Xcode 26.3",
                  "simulatorRuntime": "iOS 18.0",
                  "simulatorDeviceFingerprint": "{{hash}}",
                  "simulatorDeviceProfile": "iPhone"
                },
                "flows": [
                  {
                    "name": "tier-one",
                    "firstAttempt": {
                      "status": "passed",
                      "report": "artifacts/devflow/run-1/ios/apple-flow-runs/tier-one/flow-run.json",
                      "reportDigest": "{{hash}}"
                    },
                    "cleanAttempts": [
                      {
                        "status": "passed",
                        "report": "artifacts/devflow/run-1/ios/apple-flow-runs/tier-one/flow-run.json",
                        "reportDigest": "{{hash}}"
                      }
                    ]
                  }
                ]
              },
              "artifacts": [
                {
                  "kind": "flow-run-report",
                  "path": "artifacts/devflow/run-1/ios/apple-flow-runs/tier-one/flow-run.json",
                  "sha256": "{{hash}}",
                  "sizeBytes": 1,
                  "redacted": true
                }
              ],
              "omissions": []
            }
            """;
    }
}
