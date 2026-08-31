using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Compat = Microsoft.Maui.Cli.DevFlow.Flows;
using Testing = Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class BinaryCompatibilityTests
{
#pragma warning disable CS0618
    [Fact]
    public void Pr397Apis_RetainTheirExactOriginalPublicClrSignatures()
    {
        // These are the member references emitted by consumers compiled against PR 397's
        // d255d19a baseline. Wider implementation overloads must not replace them.
        Assert.NotNull(typeof(VisualTreeWalker).GetMethod(
            nameof(VisualTreeWalker.WalkTree),
            [typeof(Application), typeof(int), typeof(int?)]));

        Assert.NotNull(typeof(BrokerServer).GetConstructor(
            [typeof(int), typeof(TimeSpan?), typeof(Action<string>)]));

        Assert.NotNull(typeof(Compat.FlowRecorder).GetConstructor(
            [typeof(string), typeof(string), typeof(string), typeof(string)]));

        Assert.NotNull(typeof(Compat.FlowReplayer).GetConstructor(
            [typeof(AgentClient), typeof(int), typeof(int)]));
    }

    [Fact]
    public void Pr397FlowApis_RetainTheirCompletePublicSurface()
    {
        Assert.Equal(1, Compat.MauiFlow.CurrentSchema);
        Assert.Equal(5000, Compat.FlowRecorder.MaxSteps);
        Assert.Equal(32, Compat.FlowRecordingStore.MaxActive);

        AssertProperties<Compat.FlowSelector>(
            "AutomationId", "Text", "Id", "TypeIndex", "Type", "Index", "SelectorKind", "IsEmpty");
        AssertProperties<Compat.FlowTypeIndex>("Type", "Index");
        AssertProperties<Compat.FlowAssert>(
            "Kind", "Selector", "Name", "Expected", "Verify", "Note");
        AssertProperties<Compat.FlowStepArgs>(
            "Selector", "Text", "Name", "Value", "Route", "Theme", "Element", "Dx", "Dy",
            "ItemIndex", "Position", "Animated");
        AssertProperties<Compat.FlowStep>(
            "Seq", "Action", "Target", "Value", "Args", "Page", "Navigated", "Fragile",
            "Screenshot", "Asserts");
        AssertProperties<Compat.MauiFlow>(
            "Schema", "Name", "App", "Platform", "RecordedAt", "Preconditions", "Steps");
        AssertProperties<Compat.FlowParseResult>("Ok", "Flow", "Error", "File");
        AssertProperties<Compat.FlowValidation>("Errors", "Warnings", "Ok");
        AssertProperties<Compat.FlowAssertResult>(
            "Kind", "Ok", "Skipped", "Name", "Expected", "Actual");
        AssertProperties<Compat.FlowStepResult>(
            "Seq", "Action", "Label", "Ok", "Error", "Asserts");
        AssertProperties<Compat.FlowReplayReport>(
            "Ok", "Name", "File", "Total", "Passed", "Failed", "Results");

        Assert.NotNull(typeof(Compat.FlowParseResult).GetMethod(
            nameof(Compat.FlowParseResult.Success),
            [typeof(Compat.MauiFlow), typeof(string)]));
        Assert.NotNull(typeof(Compat.FlowParseResult).GetMethod(
            nameof(Compat.FlowParseResult.Fail),
            [typeof(string), typeof(string)]));
        Assert.NotNull(typeof(Compat.FlowMarkdown).GetMethod(
            nameof(Compat.FlowMarkdown.Parse),
            [typeof(string), typeof(string)]));
        Assert.NotNull(typeof(Compat.FlowMarkdown).GetMethod(
            nameof(Compat.FlowMarkdown.Serialize),
            [typeof(Compat.MauiFlow)]));
        Assert.NotNull(typeof(Compat.FlowValidator).GetMethod(
            nameof(Compat.FlowValidator.Validate),
            [typeof(Compat.MauiFlow)]));

        Assert.NotNull(typeof(Compat.FlowRecorder).GetMethod(
            nameof(Compat.FlowRecorder.Touch),
            Type.EmptyTypes));
        Assert.NotNull(typeof(Compat.FlowRecorder).GetMethod(
            nameof(Compat.FlowRecorder.AppendStep),
            [
                typeof(string),
                typeof(Compat.FlowSelector),
                typeof(string),
                typeof(Compat.FlowStepArgs),
                typeof(string),
                typeof(bool),
                typeof(List<Compat.FlowAssert>)
            ]));
        Assert.NotNull(typeof(Compat.FlowRecorder).GetMethod(
            nameof(Compat.FlowRecorder.Snapshot),
            Type.EmptyTypes));
        Assert.NotNull(typeof(Compat.FlowRecorder).GetMethod(
            nameof(Compat.FlowRecorder.Finish),
            Type.EmptyTypes));
        AssertProperties<Compat.FlowRecorder>(
            "Name", "CreatedAtUtc", "LastTouchedUtc", "StepCount");

        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetMethod(
            nameof(Compat.FlowRecordingStore.Start),
            [typeof(string), typeof(string), typeof(string), typeof(string)]));
        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetMethod(
            nameof(Compat.FlowRecordingStore.TryGet),
            [typeof(string), typeof(Compat.FlowRecorder).MakeByRefType()]));
        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetMethod(
            nameof(Compat.FlowRecordingStore.Remove),
            [typeof(string)]));
        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetMethod(
            nameof(Compat.FlowRecordingStore.List),
            Type.EmptyTypes));
        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetProperty(
            nameof(Compat.FlowRecordingStore.Instance)));
        Assert.NotNull(typeof(Compat.FlowRecordingStore).GetField(
            nameof(Compat.FlowRecordingStore.IdleTtl)));

        var replay = typeof(Compat.FlowReplayer).GetMethod(
            nameof(Compat.FlowReplayer.ReplayAsync),
            [typeof(Compat.MauiFlow), typeof(string), typeof(CancellationToken)]);
        Assert.NotNull(replay);
        Assert.Equal(typeof(Task<Compat.FlowReplayReport>), replay!.ReturnType);

        Assert.NotNull(typeof(Compat.FlowActions).GetField(nameof(Compat.FlowActions.All)));
        Assert.Equal("tap", Compat.FlowActions.Tap);
        Assert.Equal("fill", Compat.FlowActions.Fill);
        Assert.Equal("scroll", Compat.FlowActions.Scroll);
        Assert.Equal("navigate", Compat.FlowActions.Navigate);
        Assert.Equal("back", Compat.FlowActions.Back);
        Assert.Equal("setTheme", Compat.FlowActions.SetTheme);
        Assert.Equal("setProperty", Compat.FlowActions.SetProperty);
        Assert.Equal("assert", Compat.FlowActions.Assert);
    }

    [Fact]
    public void Pr397FlowAdapters_RemainFunctional()
    {
        var recorder = new Compat.FlowRecorder("compat", "App", "Windows", "start");
        var sequence = recorder.AppendStep(
            Compat.FlowActions.Tap,
            new Compat.FlowSelector { AutomationId = "GoButton" },
            null,
            null,
            "/",
            navigated: false,
            asserts: null);

        Assert.Equal(1, sequence);
        var snapshot = recorder.Snapshot();
        Assert.Equal(Compat.MauiFlow.CurrentSchema, snapshot.Schema);
        Assert.Single(snapshot.Steps);

        var markdown = Compat.FlowMarkdown.Serialize(recorder.Finish());
        var parsed = Compat.FlowMarkdown.Parse(markdown, "compat.md");
        Assert.True(parsed.Ok, parsed.Error);
        Assert.Equal("compat", parsed.Flow!.Name);
        Assert.True(Compat.FlowValidator.Validate(parsed.Flow).Ok);

        var store = Compat.FlowRecordingStore.Instance;
        var recordingId = store.Start("stored", null, null, null);
        Assert.NotNull(recordingId);
        Assert.True(store.TryGet(recordingId!, out var stored));
        Assert.Equal("stored", stored.Name);
        Assert.Same(stored, store.Remove(recordingId!));
    }
#pragma warning restore CS0618

    [Fact]
    public void TestingPackage_ExposesTheCanonicalFlowRuntimeSignatures()
    {
        Assert.Equal(
            "Microsoft.Maui.DevFlow.Testing",
            typeof(Testing.FlowRecorder).Assembly.GetName().Name);
        Assert.NotNull(typeof(Testing.FlowRecorder).GetConstructor(
            [typeof(string), typeof(string), typeof(string), typeof(string)]));
        Assert.NotNull(typeof(Testing.FlowReplayer).GetConstructor(
            [typeof(AgentClient), typeof(int), typeof(int)]));
    }

    private static void AssertProperties<T>(params string[] names)
    {
        foreach (var name in names)
            Assert.NotNull(typeof(T).GetProperty(name));
    }
}
