using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The sample flows are executed from their committed <c>.maui-plan.json</c> sidecars, so every
/// flow needs one and every sidecar has to stay bound to the replay block that defines the flow.
/// This guard fails when a flow is edited without refreshing its sidecar, which is precisely the
/// drift the committed-bundle contract exists to reject. The binding is the flow digest, which is
/// computed over the parsed flow, so prose outside the fenced <c>json maui-test</c> block is not
/// covered by it.
/// </summary>
public sealed class SampleFlowSidecarTests
{
    private const string UpdateEnvironmentVariable = "UPDATE_DEVFLOW_SAMPLE_FLOW_SIDECARS";

    public static IEnumerable<object[]> SampleFlows =>
        EnumerateSampleFlows().Select(static path => new object[]
        {
            Path.GetRelativePath(FindRepositoryRoot(), path).Replace('\\', '/'),
        });

    [Theory]
    [MemberData(nameof(SampleFlows))]
    public void SampleFlow_HasSidecarBoundToItsCommittedBytes(string relativeFlowPath)
    {
        var flowPath = Path.Combine(FindRepositoryRoot(), relativeFlowPath.Replace('/', Path.DirectorySeparatorChar));
        var sidecarPath = Path.ChangeExtension(flowPath, ".maui-plan.json");
        var parsed = FlowMarkdown.Parse(File.ReadAllText(flowPath), flowPath);
        Assert.True(parsed.Ok, $"{relativeFlowPath} could not be parsed: {parsed.Error}");

        var digest = MauiFlowRunReportSerializer.ComputeFlowDigest(parsed.Flow!);

        // Regeneration authors or rebinds the sidecar and then falls through to the same
        // assertions, so a rebind can never launder a sidecar that is invalid or that widens the
        // mutation policy.
        if (string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal))
            UpdateSidecar(flowPath, sidecarPath, parsed.Flow!, digest);

        Assert.True(
            File.Exists(sidecarPath),
            $"Missing plan sidecar for '{relativeFlowPath}'. Set {UpdateEnvironmentVariable}=1 to author it, then review the result.");

        var json = File.ReadAllText(sidecarPath);
        var validation = MauiTestPlanValidator.ValidateJson(json, out var plan);
        Assert.True(validation.IsValid, $"{relativeFlowPath} sidecar is invalid: {string.Join("; ", validation.Errors)}");
        Assert.Equal(Path.GetFileName(flowPath), plan!.Flow?.Path);
        Assert.Equal(digest, plan.Flow?.Digest, ignoreCase: true);

        // The flow lanes replay these bundles against real devices, so a committed plan must not
        // widen the mutation policy the lanes are qualified for.
        Assert.Equal(MauiFlowSideEffectPolicies.None, plan.SideEffectPolicy);
    }

    [Fact]
    public void EverySampleFlowIsCovered()
    {
        var flows = EnumerateSampleFlows().ToArray();

        Assert.NotEmpty(flows);
        Assert.All(flows, path => Assert.True(
            File.Exists(Path.ChangeExtension(path, ".maui-plan.json")),
            $"Missing plan sidecar for '{path}'."));
    }

    /// <summary>
    /// Authors a missing sidecar, or rebinds an existing one to the current flow bytes. An
    /// existing sidecar is edited in place rather than regenerated, so review decisions already
    /// committed in it - goals, risks, scenarios, provenance - are never silently rewritten.
    /// </summary>
    private static void UpdateSidecar(string flowPath, string sidecarPath, MauiFlow flow, string digest)
    {
        if (File.Exists(sidecarPath))
        {
            var text = File.ReadAllText(sidecarPath);
            var document = JsonNode.Parse(text)?.AsObject()
                ?? throw new InvalidOperationException($"'{sidecarPath}' is not a JSON object.");
            var expectedPath = Path.GetFileName(flowPath);
            if (document["flow"] is JsonObject bound &&
                string.Equals((string?)bound["path"], expectedPath, StringComparison.Ordinal) &&
                string.Equals((string?)bound["digest"], digest, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (document["flow"] is not JsonObject binding)
            {
                binding = [];
                document["flow"] = binding;
            }
            binding["path"] = expectedPath;
            binding["digest"] = digest;
            File.WriteAllText(
                sidecarPath,
                document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        var name = Path.GetFileNameWithoutExtension(flowPath);
        var plan = new MauiTestPlan
        {
            Schema = 1,
            PlanId = $"plan-{name}",
            Revision = 1,
            Title = flow.Name ?? name,
            Goal = $"Replay the committed {flow.Name ?? name} flow against the sample app.",
            Flow = new MauiFlowReference
            {
                Path = Path.GetFileName(flowPath),
                Digest = digest,
            },
            Reset = new MauiTestResetRequirement { Required = false },
            Provenance = new MauiActorProvenance
            {
                ActorKind = "human",
                Channel = "repository",
                Intent = "human-authored",
            },
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            Requirements = new MauiFlowRequirements(),
        };

        File.WriteAllText(
            sidecarPath,
            JsonSerializer.Serialize(plan, MauiTestingJsonContext.Default.MauiTestPlan) + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IEnumerable<string> EnumerateSampleFlows()
    {
        var root = FindRepositoryRoot();
        foreach (var sample in new[] { "DevFlow.Sample", "DevFlow.Sample.MacOS" })
        {
            var directory = Path.Combine(root, "samples", sample, "maui-tests");
            if (!Directory.Exists(directory))
                continue;
            foreach (var path in Directory.GetFiles(directory, "*.md")
                         .Where(static path => !string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                yield return path;
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MauiLabs.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
