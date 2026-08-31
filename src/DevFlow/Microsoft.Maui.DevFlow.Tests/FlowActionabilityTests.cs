using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

public class FlowActionabilityTests
{
    [Fact]
    public async Task ResolveAsync_ScopedStableItemSelector_ResolvesRepeatedControl()
    {
        var firstKey = "sha256:" + new string('1', 64);
        var secondKey = "sha256:" + new string('2', 64);
        var driver = new RepeatedItemDriver(
        [
            new ElementInfo
            {
                Id = "first",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = firstKey,
                CollectionScope = "TodoList",
            },
            new ElementInfo
            {
                Id = "second",
                Type = "CheckBox",
                AutomationId = "TodoCheckBox",
                StableItemKey = secondKey,
                CollectionScope = "TodoList",
            },
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var ambiguous = await engine.ResolveAsync(new FlowSelector { AutomationId = "TodoCheckBox" });
        Assert.False(ambiguous.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, ambiguous.Kind);

        var resolved = await engine.ResolveAsync(new FlowSelector
        {
            AutomationId = "TodoCheckBox",
            StableItemKey = secondKey,
            CollectionScope = "TodoList",
        });

        Assert.True(resolved.Ok);
        Assert.Equal("second", resolved.Element?.Id);
        Assert.Equal("stable-item-key", resolved.Quality);
        Assert.Equal(1, resolved.MatchCount);
    }

    [Fact]
    public async Task ResolveAsync_NullSelector_ReturnsNotFound()
    {
        var engine = new FlowActionabilityEngine(new FakeDriver(), tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(null);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("No target selector was supplied.", resolution.Error);
        Assert.Null(resolution.Element);
        Assert.Empty(resolution.Candidates);
    }

    [Fact]
    public async Task ResolveAsync_EmptySelector_ReturnsNotFoundWithoutQueryingTheApp()
    {
        var driver = new FakeDriver();
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector());

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("No target selector was supplied.", resolution.Error);
        Assert.Equal(0, driver.QueryCount);
    }

    [Fact]
    public async Task ResolveAsync_NoUsableSelectorForm_ReturnsNotFound()
    {
        // Non-empty (a typeIndex object exists) but unusable: no type to query on.
        var driver = new FakeDriver();
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            TypeIndex = new FlowTypeIndex { Type = "", Index = 0 },
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("No usable target selector was supplied.", resolution.Error);
        Assert.Equal(0, driver.QueryCount);
    }

    [Fact]
    public async Task ResolveAsync_ScopedItemMissingCollectionScope_ReturnsNotFound()
    {
        var driver = new FakeDriver([Element("first", automationId: "TodoCheckBox")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            AutomationId = "TodoCheckBox",
            StableItemKey = "sha256:" + new string('1', 64),
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal(
            "A scoped item selector requires AutomationId, stableItemKey, and collectionScope.",
            resolution.Error);
    }

    [Fact]
    public async Task ResolveAsync_ScopedItemMissingStableItemKey_ReturnsNotFound()
    {
        var driver = new FakeDriver([Element("first", automationId: "TodoCheckBox")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            AutomationId = "TodoCheckBox",
            CollectionScope = "TodoList",
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal(
            "A scoped item selector requires AutomationId, stableItemKey, and collectionScope.",
            resolution.Error);
    }

    [Fact]
    public async Task ResolveAsync_NonOpaqueStableItemKey_ReturnsNotFound()
    {
        var driver = new FakeDriver(
        [
            Element("first", automationId: "TodoCheckBox", stableItemKey: "todo-1", collectionScope: "TodoList"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            AutomationId = "TodoCheckBox",
            StableItemKey = "todo-1",
            CollectionScope = "TodoList",
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("A stable item key must be an opaque SHA-256 identity.", resolution.Error);
    }

    [Fact]
    public async Task ResolveAsync_ScopedItemWrongCollectionScope_ReturnsNotFound()
    {
        var key = "sha256:" + new string('1', 64);
        var driver = new FakeDriver(
        [
            Element("first", automationId: "TodoCheckBox", stableItemKey: key, collectionScope: "TodoList"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            AutomationId = "TodoCheckBox",
            StableItemKey = key,
            CollectionScope = "ArchiveList",
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("scoped item selector found no elements.", resolution.Error);
        Assert.Equal(0, resolution.MatchCount);
    }

    [Fact]
    public async Task ResolveAsync_AutomationIdNoMatch_ReturnsNotFoundWithZeroCandidates()
    {
        var driver = new FakeDriver([Element("first", automationId: "Present")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector { AutomationId = "Absent" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("AutomationId selector found no elements.", resolution.Error);
        Assert.Equal(0, resolution.MatchCount);
        Assert.Empty(resolution.Candidates);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousAutomationId_SummarisesAtMostThreeCandidates()
    {
        var driver = new FakeDriver(
        [
            Element("a", automationId: "Dup", type: "Button"),
            Element("b", automationId: "Dup", type: "Button"),
            Element("c", automationId: "Dup", type: "Button"),
            Element("d", automationId: "Dup", type: "Button"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector { AutomationId = "Dup" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, resolution.Kind);
        Assert.Equal(4, resolution.MatchCount);
        Assert.Equal(4, resolution.Candidates.Count);
        Assert.Contains("4 elements matched", resolution.Error);
        Assert.Equal("Button[Dup], Button[Dup], Button[Dup]", CandidateList(resolution.Error!));
    }

    [Fact]
    public async Task ResolveAsync_TextSelector_MatchesOrdinalOnly()
    {
        // The agent-side text query is deliberately loose; the engine must narrow it to an
        // ordinal match so "save" can never satisfy a selector recorded as "Save".
        var driver = new FakeDriver(
        [
            Element("upper", text: "Save"),
            Element("lower", text: "save"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector { Text = "Save" });

        Assert.True(resolution.Ok);
        Assert.Equal("upper", resolution.Element?.Id);
        Assert.Equal("text", resolution.Quality);
        Assert.Equal(1, resolution.MatchCount);
    }

    [Fact]
    public async Task ResolveAsync_TypeIndexOutOfRange_ReturnsNotFoundAndKeepsCandidates()
    {
        var driver = new FakeDriver(
        [
            Element("first", type: "Button"),
            Element("second", type: "Button"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            TypeIndex = new FlowTypeIndex { Type = "Button", Index = 5 },
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("Type+index selector found 2 candidate(s), not index 5.", resolution.Error);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public async Task ResolveAsync_NegativeTypeIndex_ReturnsNotFound()
    {
        var driver = new FakeDriver([Element("first", type: "Button")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            TypeIndex = new FlowTypeIndex { Type = "Button", Index = -1 },
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("Type+index selector found 1 candidate(s), not index -1.", resolution.Error);
    }

    [Fact]
    public async Task ResolveAsync_TypeIndex_ReturnsFragileQuality()
    {
        var driver = new FakeDriver(
        [
            Element("first", type: "Button"),
            Element("second", type: "Button"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            TypeIndex = new FlowTypeIndex { Type = "Button", Index = 1 },
        });

        Assert.True(resolution.Ok);
        Assert.Equal("second", resolution.Element?.Id);
        Assert.Equal("fragile", resolution.Quality);
        Assert.Equal(2, resolution.MatchCount);
    }

    [Fact]
    public async Task ResolveAsync_RecorderTypeAndIndexFields_ResolveAsTypeIndex()
    {
        var driver = new FakeDriver(
        [
            Element("first", type: "Entry"),
            Element("second", type: "Entry"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector
        {
            SelectorKind = "typeIndex",
            Type = "Entry",
            Index = 0,
        });

        Assert.True(resolution.Ok);
        Assert.Equal("first", resolution.Element?.Id);
        Assert.Equal("fragile", resolution.Quality);
    }

    [Fact]
    public async Task ResolveAsync_RawIdMissing_ReturnsNotFound()
    {
        var driver = new FakeDriver([Element("known")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector { Id = "unknown" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal("Raw element id was not found.", resolution.Error);
    }

    [Fact]
    public async Task ResolveAsync_RawIdFound_ReturnsFragileQuality()
    {
        var driver = new FakeDriver([Element("known")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.ResolveAsync(new FlowSelector { Id = "known" });

        Assert.True(resolution.Ok);
        Assert.Equal("known", resolution.Element?.Id);
        Assert.Equal("fragile", resolution.Quality);
        Assert.Equal(1, resolution.MatchCount);
    }

    [Fact]
    public async Task ResolveAsync_Cancellation_Throws()
    {
        var driver = new FakeDriver([Element("known", automationId: "known")]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.ResolveAsync(new FlowSelector { AutomationId = "known" }, cts.Token));
        Assert.Equal(0, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForActionableAsync_NotVisible_ReturnsNotVisibleAfterEveryTry()
    {
        var driver = new FakeDriver([Element("hidden", automationId: "hidden", visible: false)]);
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "hidden" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotVisible, resolution.Kind);
        Assert.Equal("The resolved element is not visible.", resolution.Error);
        Assert.Equal("hidden", resolution.Element?.Id);
        Assert.Equal(3, driver.QueryCount);
        Assert.Equal(3, observations.Count(o => o.Kind == "visibility"));
        Assert.All(observations.Where(o => o.Kind == "visibility"), o => Assert.False(o.Visible));
    }

    [Fact]
    public async Task WaitForActionableAsync_Disabled_ReturnsDisabled()
    {
        var driver = new FakeDriver([Element("greyed", automationId: "greyed", enabled: false)]);
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 2, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "greyed" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Disabled, resolution.Kind);
        Assert.Equal("The resolved element is disabled.", resolution.Error);
        Assert.Equal(2, observations.Count(o => o.Kind == "enabled"));
        Assert.All(observations.Where(o => o.Kind == "enabled"), o => Assert.False(o.Enabled));
    }

    [Theory]
    [InlineData(0d, 40d)]
    [InlineData(100d, 0d)]
    [InlineData(-1d, 40d)]
    public async Task WaitForActionableAsync_ZeroBounds_ReturnsUnstable(double width, double height)
    {
        var element = Element("btn", automationId: "btn");
        element.Bounds = new BoundsInfo { X = 0, Y = 0, Width = width, Height = height };
        var driver = new FakeDriver([element]);
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Unstable, resolution.Kind);
        Assert.Equal("The tap target has empty or non-positive bounds.", resolution.Error);
        // No second poll is worth doing when there is nothing to compare.
        Assert.Equal(0, driver.GetElementCount);
        var bounds = Assert.Single(observations, o => o.Kind == "bounds");
        Assert.False(bounds.BoundsStable);
        Assert.False(bounds.HasBounds);
    }

    [Fact]
    public async Task WaitForActionableAsync_MissingBounds_ReturnsUnstable()
    {
        var element = Element("btn", automationId: "btn");
        element.Bounds = null;
        var driver = new FakeDriver([element]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Unstable, resolution.Kind);
    }

    [Fact]
    public async Task WaitForActionableAsync_BoundsChangeBetweenPolls_ReturnsUnstable()
    {
        var element = Element("btn", automationId: "btn");
        var driver = new FakeDriver([element])
        {
            // The second read is the confirmation poll; move the element under it.
            OnGetElement = (id, call) => Element(
                id,
                automationId: "btn",
                bounds: new BoundsInfo { X = call * 10, Y = 0, Width = 100, Height = 40 }),
        };
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 2, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Unstable, resolution.Kind);
        Assert.Equal(
            "The tap target bounds changed while waiting for a stable layout.",
            resolution.Error);
        Assert.Equal(2, driver.GetElementCount);
        Assert.All(observations.Where(o => o.Kind == "bounds"), o => Assert.False(o.BoundsStable));
    }

    [Fact]
    public async Task WaitForActionableAsync_VanishesBeforeConfirmationPoll_ReturnsUnstable()
    {
        var driver = new FakeDriver([Element("btn", automationId: "btn")])
        {
            OnGetElement = (_, _) => null,
        };
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Unstable, resolution.Kind);
    }

    [Fact]
    public async Task WaitForActionableAsync_StableBounds_ReturnsResolvedAndObservesStable()
    {
        var driver = new FakeDriver([Element("btn", automationId: "btn")]);
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: true);

        Assert.True(resolution.Ok);
        Assert.Equal("btn", resolution.Element?.Id);
        Assert.Equal(1, driver.QueryCount);
        var bounds = Assert.Single(observations, o => o.Kind == "bounds");
        Assert.True(bounds.BoundsStable);
        Assert.True(bounds.HasBounds);
        Assert.Equal("stable", bounds.Outcome);
        Assert.Equal(1, bounds.Attempt);
    }

    [Fact]
    public async Task WaitForActionableAsync_StableBoundsNotRequired_SkipsConfirmationPoll()
    {
        var driver = new FakeDriver([Element("btn", automationId: "btn")]);
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "btn" },
            requireStableBounds: false);

        Assert.True(resolution.Ok);
        Assert.Equal(0, driver.GetElementCount);
    }

    [Fact]
    public async Task WaitForActionableAsync_BecomesVisibleOnSecondTry_Succeeds()
    {
        var driver = new FakeDriver
        {
            OnQuery = (_, _, call) => call == 1
                ? new[] { Element("late", automationId: "late", visible: false) }
                : new[] { Element("late", automationId: "late") },
        };
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0, observations.Add);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "late" },
            requireStableBounds: false);

        Assert.True(resolution.Ok);
        Assert.Equal("late", resolution.Element?.Id);
        Assert.Equal(2, driver.QueryCount);
        Assert.Single(observations, o => o.Kind == "visibility");
    }

    [Fact]
    public async Task WaitForActionableAsync_Ambiguous_ReturnsImmediatelyWithoutRetrying()
    {
        var driver = new FakeDriver(
        [
            Element("a", automationId: "Dup"),
            Element("b", automationId: "Dup"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 4, gapMs: 50);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "Dup" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, resolution.Kind);
        // Ambiguity is a defect in the selector, not a timing problem: retrying only wastes time.
        // The query count, not the gap, is the signal, so the gap stays small enough that a
        // regression fails fast instead of sleeping through every retry.
        Assert.Equal(1, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForActionableAsync_NotFound_RetriesThenReturnsTheLastFailure()
    {
        var driver = new FakeDriver([Element("other", automationId: "other")]);
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "missing" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal(3, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForActionableAsync_Cancellation_Throws()
    {
        var driver = new FakeDriver([Element("btn", automationId: "btn")]);
        var engine = new FlowActionabilityEngine(driver, tries: 4, gapMs: 0);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.WaitForActionableAsync(
                new FlowSelector { AutomationId = "btn" },
                requireStableBounds: true,
                cts.Token));
        Assert.Equal(0, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForResolvedAsync_Ambiguous_ReturnsImmediately()
    {
        var driver = new FakeDriver(
        [
            Element("a", automationId: "Dup"),
            Element("b", automationId: "Dup"),
        ]);
        var engine = new FlowActionabilityEngine(driver, tries: 4, gapMs: 50);

        var resolution = await engine.WaitForResolvedAsync(new FlowSelector { AutomationId = "Dup" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, resolution.Kind);
        Assert.Equal(1, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForResolvedAsync_IgnoresVisibilityAndEnabledState()
    {
        // Resolution-only waits are used by assertions, which must be able to observe an
        // element that an interaction would refuse to touch.
        var driver = new FakeDriver([Element("btn", automationId: "btn", visible: false, enabled: false)]);
        var engine = new FlowActionabilityEngine(driver, tries: 2, gapMs: 0);

        var resolution = await engine.WaitForResolvedAsync(new FlowSelector { AutomationId = "btn" });

        Assert.True(resolution.Ok);
        Assert.Equal(1, driver.QueryCount);
    }

    [Fact]
    public async Task WaitForResolvedAsync_NotFound_RetriesThenReturnsTheLastFailure()
    {
        var driver = new FakeDriver();
        var engine = new FlowActionabilityEngine(driver, tries: 3, gapMs: 0);

        var resolution = await engine.WaitForResolvedAsync(new FlowSelector { AutomationId = "missing" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotFound, resolution.Kind);
        Assert.Equal(3, driver.QueryCount);
    }

    private static string CandidateList(string error)
    {
        var open = error.LastIndexOf('(');
        var close = error.LastIndexOf(')');
        return error[(open + 1)..close];
    }

    private static readonly string StableItemKey = "sha256:" + new string('a', 64);

    [Fact]
    public void Constructor_NullDriver_Throws()
        => Assert.Throws<ArgumentNullException>(() => new FlowActionabilityEngine((IMauiFlowDriver)null!));

    [Fact]
    public async Task Constructor_ClampsNonPositiveTriesToASingleAttempt()
    {
        var driver = new FakeDriver();
        var engine = new FlowActionabilityEngine(driver, tries: 0, gapMs: -5);

        var resolution = await engine.WaitForResolvedAsync(new FlowSelector { AutomationId = "missing" });

        Assert.False(resolution.Ok);
        Assert.Equal(1, driver.QueryCount);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousText_SummarisesCandidatesWithoutAnAutomationId()
    {
        // The candidate summary has a second shape for elements with no AutomationId, which is
        // the normal case for a text selector.
        var driver = new FakeDriver(
        [
            Element("lbl-1", type: "Label", text: "Go"),
            Element("lbl-2", type: "Label", text: "Go"),
        ]);

        var resolution = await new FlowActionabilityEngine(driver).ResolveAsync(new FlowSelector { Text = "Go" });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, resolution.Kind);
        Assert.Equal(2, resolution.MatchCount);
        Assert.StartsWith("text selector is ambiguous", resolution.Error, StringComparison.Ordinal);
        Assert.Equal("Label#lbl-1, Label#lbl-2", CandidateList(resolution.Error!));
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousScopedItem_IsReportedAsAScopedItemFailure()
    {
        // Two rows carrying the same opaque identity in the same collection is a data defect the
        // engine must refuse to guess about, and it must say which selector form was ambiguous.
        var driver = new FakeDriver(
        [
            Element("row-1", automationId: "Row", stableItemKey: StableItemKey, collectionScope: "orders"),
            Element("row-2", automationId: "Row", stableItemKey: StableItemKey, collectionScope: "orders"),
        ]);

        var resolution = await new FlowActionabilityEngine(driver).ResolveAsync(new FlowSelector
        {
            AutomationId = "Row",
            StableItemKey = StableItemKey,
            CollectionScope = "orders",
        });

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.Ambiguous, resolution.Kind);
        Assert.StartsWith("scoped item selector is ambiguous", resolution.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveAsync_TypeIndexKindWithoutAnIndex_FallsThroughToTheRawId()
    {
        // A recorder that writes selectorKind=typeIndex but omits the index has produced an
        // incomplete selector; it must not silently become "index 0".
        var driver = new FakeDriver([Element("btn", automationId: "submit")]);

        var resolution = await new FlowActionabilityEngine(driver).ResolveAsync(new FlowSelector
        {
            SelectorKind = "typeIndex",
            Type = "Button",
            Id = "btn",
        });

        Assert.True(resolution.Ok);
        Assert.Equal("btn", resolution.Element?.Id);
        Assert.Equal("fragile", resolution.Quality);
        Assert.Equal(0, driver.QueryCount);
        Assert.Equal(1, driver.GetElementCount);
    }

    [Fact]
    public async Task Observer_SeesAFailedResolutionWithNoElement()
    {
        var observations = new List<FlowActionabilityObservation>();
        var engine = new FlowActionabilityEngine(
            new FakeDriver(),
            tries: 1,
            gapMs: 0,
            observe: observations.Add);

        await engine.WaitForActionableAsync(new FlowSelector { AutomationId = "missing" }, requireStableBounds: true);

        var observation = Assert.Single(observations);
        Assert.False(observation.Resolved);
        Assert.Null(observation.Visible);
        Assert.Null(observation.Enabled);
        Assert.Equal(FlowFailureKinds.NotFound, observation.Outcome);
        Assert.False(observation.HasBounds);
    }

    [Fact]
    public async Task WithFailure_KeepsTheResolvedElementMatchCountQualityAndCandidates()
    {
        // A downstream repair proposal reads Candidates and Quality off the failure, so demoting a
        // resolved element to not-visible must not erase what was resolved.
        var driver = new FakeDriver([Element("btn", automationId: "submit", visible: false)]);
        var engine = new FlowActionabilityEngine(driver, tries: 1, gapMs: 0);

        var resolution = await engine.WaitForActionableAsync(
            new FlowSelector { AutomationId = "submit" },
            requireStableBounds: true);

        Assert.False(resolution.Ok);
        Assert.Equal(FlowFailureKinds.NotVisible, resolution.Kind);
        Assert.Equal("btn", resolution.Element?.Id);
        Assert.Equal(1, resolution.MatchCount);
        Assert.Equal("AutomationId", resolution.Quality);
        Assert.Equal("btn", Assert.Single(resolution.Candidates).Id);
    }

    private static ElementInfo Element(
        string id,
        string? automationId = null,
        string type = "Button",
        string? text = null,
        bool visible = true,
        bool enabled = true,
        string? stableItemKey = null,
        string? collectionScope = null,
        BoundsInfo? bounds = null)
        => new()
        {
            Id = id,
            Type = type,
            AutomationId = automationId,
            Text = text,
            IsVisible = visible,
            IsEnabled = enabled,
            StableItemKey = stableItemKey,
            CollectionScope = collectionScope,
            Bounds = bounds ?? new BoundsInfo { X = 0, Y = 0, Width = 100, Height = 40 },
        };

    /// <summary>
    /// A driver whose answers can vary per call, so retry, stability and drift paths are
    /// exercised without a real agent or wall-clock waits.
    /// </summary>
    private sealed class FakeDriver(IReadOnlyList<ElementInfo>? elements = null) : IMauiFlowDriver
    {
        private readonly IReadOnlyList<ElementInfo> _elements = elements ?? [];

        /// <summary>Query override receiving (automationId, text/type-aware filter args, 1-based call count).</summary>
        public Func<string?, string?, int, IReadOnlyList<ElementInfo>>? OnQuery { get; init; }

        /// <summary>Element override receiving (id, 1-based call count).</summary>
        public Func<string, int, ElementInfo?>? OnGetElement { get; init; }

        public int QueryCount { get; private set; }
        public int GetElementCount { get; private set; }

        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
        {
            QueryCount++;
            if (OnQuery is not null)
                return Task.FromResult(OnQuery(automationId, text, QueryCount).ToList());

            // Mirrors a real agent: automationId and type match exactly, text matching is loose.
            return Task.FromResult(_elements.Where(element =>
                (type is null || string.Equals(element.Type, type, StringComparison.Ordinal)) &&
                (automationId is null || string.Equals(element.AutomationId, automationId, StringComparison.Ordinal)) &&
                (text is null || string.Equals(element.Text, text, StringComparison.OrdinalIgnoreCase)))
                .ToList());
        }

        public Task<ElementInfo?> GetElementAsync(string id)
        {
            GetElementCount++;
            return Task.FromResult(OnGetElement is not null
                ? OnGetElement(id, GetElementCount)
                : _elements.FirstOrDefault(element => element.Id == id));
        }

        public Task<bool> TapAsync(string elementId) => Task.FromResult(true);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(null);
    }

    private sealed class RepeatedItemDriver(IReadOnlyList<ElementInfo> elements) : IMauiFlowDriver
    {
        public WorkflowCommandReceipt? LastWorkflowCommandReceipt => null;

        public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
            => Task.FromResult(elements.Where(element =>
                (type is null || element.Type == type) &&
                (automationId is null || element.AutomationId == automationId) &&
                (text is null || element.Text == text)).ToList());

        public Task<ElementInfo?> GetElementAsync(string id)
            => Task.FromResult(elements.FirstOrDefault(element => element.Id == id));

        public Task<bool> TapAsync(string elementId) => Task.FromResult(true);
        public Task<bool> FillAsync(string elementId, string text) => Task.FromResult(true);
        public Task<bool> SetPropertyAsync(string elementId, string propertyName, string value) => Task.FromResult(true);
        public Task<bool> ScrollAsync(string? elementId = null, double deltaX = 0, double deltaY = 0, bool animated = true, int? itemIndex = null, string? scrollToPosition = null) => Task.FromResult(true);
        public Task<bool> NavigateAsync(string route) => Task.FromResult(true);
        public Task<bool> BackAsync() => Task.FromResult(true);
        public Task<ThemeResult> SetThemeAsync(DevFlowTheme theme) => Task.FromResult(new ThemeResult { Success = true });
        public Task<string?> GetPropertyAsync(string elementId, string propertyName) => Task.FromResult<string?>(null);
        public Task<AgentStatus?> GetStatusAsync() => Task.FromResult<AgentStatus?>(null);
    }
}
