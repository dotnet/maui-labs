using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.GenerativeUI.Binding;
using Microsoft.Maui.AI.GenerativeUI.Canvas;
using Microsoft.Maui.AI.GenerativeUI.Registry;
using Microsoft.Maui.Controls;
using CanvasState = Microsoft.Maui.AI.GenerativeUI.Canvas.CanvasState;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

public sealed record CompositionRenderDiff(
    bool ScaffoldReused,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Reused,
    IReadOnlyList<string> Moved,
    IReadOnlyList<string> Reconfigured,
    IReadOnlyList<string> Removed);

internal sealed record MountedCompositionSection(
    string Component,
    string DataPath,
    string? Variant,
    View View);

public sealed class CompositionSessionState
{
    private readonly Dictionary<string, MountedCompositionSection> _mounted = new(StringComparer.Ordinal);

    public CompositionPlan? CurrentPlan { get; internal set; }
    public View? ScaffoldView { get; internal set; }
    public ICompositionScaffold? Scaffold { get; internal set; }
    public CompositionRenderDiff? LastRenderDiff { get; internal set; }

    internal IDictionary<string, MountedCompositionSection> Mounted => _mounted;

    public View? GetSectionView(string sectionId)
        => _mounted.GetValueOrDefault(sectionId)?.View;

    public void Reset()
    {
        foreach (var mounted in _mounted.Values)
        {
            if (mounted.View is ICompositionComponent component)
                component.Detach();
        }

        CurrentPlan = null;
        ScaffoldView = null;
        Scaffold = null;
        LastRenderDiff = null;
        _mounted.Clear();
    }
}

/// <summary>Applies plan revisions to persistent native scaffold/component instances.</summary>
public sealed class CompositionPlanRenderer(
    GenerativeUiRegistry registry,
    IServiceProvider services,
    CanvasState canvas,
    CompositionSessionState session)
{
    public CompositionRenderDiff Render(CompositionPlan plan, UiObject stateRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(stateRoot);

        var reuseScaffold =
            session.CurrentPlan is { } current &&
            string.Equals(current.PlanId, plan.PlanId, StringComparison.Ordinal) &&
            string.Equals(current.Scaffold, plan.Scaffold, StringComparison.OrdinalIgnoreCase) &&
            session.Scaffold is not null &&
            session.ScaffoldView is not null;

        if (!reuseScaffold)
            CreateScaffold(plan.Scaffold);

        var previousPlan = reuseScaffold ? session.CurrentPlan : null;
        var previousPositions = Positions(previousPlan);
        var desiredIds = plan.Sections.Select(section => section.Id).ToHashSet(StringComparer.Ordinal);
        var removed = session.Mounted.Keys.Where(id => !desiredIds.Contains(id)).Order().ToList();
        foreach (var id in removed)
        {
            if (session.Mounted.Remove(id, out var mounted) &&
                mounted.View is ICompositionComponent component)
            {
                component.Detach();
            }
        }

        var added = new List<string>();
        var reused = new List<string>();
        var moved = new List<string>();
        var reconfigured = new List<string>();
        var slotViews = Enum.GetValues<CompositionSlot>()
            .ToDictionary(slot => slot, _ => (IReadOnlyList<View>)[]);

        foreach (var slotGroup in plan.Sections
                     .Select((section, index) => (section, index))
                     .GroupBy(item => item.section.Slot))
        {
            var ordered = slotGroup
                .OrderByDescending(item => item.section.Priority)
                .ThenBy(item => item.index)
                .Select(item => item.section)
                .ToList();
            var views = new List<View>(ordered.Count);

            for (var index = 0; index < ordered.Count; index++)
            {
                var section = ordered[index];
                if (session.Mounted.TryGetValue(section.Id, out var mounted) &&
                    string.Equals(mounted.Component, section.Component, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(mounted.DataPath, section.DataPath, StringComparison.Ordinal))
                {
                    reused.Add(section.Id);
                    if (!string.Equals(mounted.Variant, section.Variant, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyVariant(mounted.View, section);
                        session.Mounted[section.Id] = mounted with { Variant = section.Variant };
                        reconfigured.Add(section.Id);
                    }

                    mounted.View.BindingContext = ResolveData(stateRoot, section.DataPath);
                    views.Add(mounted.View);
                }
                else
                {
                    if (mounted?.View is ICompositionComponent previousComponent)
                        previousComponent.Detach();

                    var view = CreateComponent(section, stateRoot);
                    session.Mounted[section.Id] = new(
                        section.Component,
                        section.DataPath,
                        section.Variant,
                        view);
                    added.Add(section.Id);
                    views.Add(view);
                }

                if (previousPositions.TryGetValue(section.Id, out var previous) &&
                    (previous.Slot != section.Slot || previous.Index != index))
                {
                    moved.Add(section.Id);
                }
            }

            slotViews[slotGroup.Key] = views;
        }

        session.Scaffold!.Title = plan.Title;
        session.Scaffold.ApplySlots(slotViews);
        if (!reuseScaffold)
            canvas.SetView(session.ScaffoldView!);

        var diff = new CompositionRenderDiff(
            reuseScaffold,
            added,
            reused,
            moved,
            reconfigured,
            removed);
        session.CurrentPlan = plan;
        session.LastRenderDiff = diff;
        return diff;
    }

    private void CreateScaffold(string scaffoldName)
    {
        var registration = registry.GetScaffold(scaffoldName)
            ?? throw new InvalidOperationException($"Scaffold '{scaffoldName}' is not registered.");
        var instance = ActivatorUtilities.CreateInstance(services, registration.ScaffoldType);
        if (instance is not View view || instance is not ICompositionScaffold scaffold)
        {
            throw new InvalidOperationException(
                $"Scaffold '{scaffoldName}' must be a View implementing {nameof(ICompositionScaffold)}.");
        }

        session.Reset();
        session.ScaffoldView = view;
        session.Scaffold = scaffold;
    }

    private View CreateComponent(CompositionSection section, UiObject stateRoot)
    {
        var registration = registry.GetComponent(section.Component)
            ?? throw new InvalidOperationException($"Component '{section.Component}' is not registered.");
        var instance = ActivatorUtilities.CreateInstance(services, registration.ComponentType);
        if (instance is not View view || instance is not ICompositionComponent)
        {
            throw new InvalidOperationException(
                $"Component '{section.Component}' must be a View implementing {nameof(ICompositionComponent)}.");
        }

        view.BindingContext = ResolveData(stateRoot, section.DataPath);
        ApplyVariant(view, section);
        return view;
    }

    private static UiObject ResolveData(UiObject stateRoot, string dataPath)
        => UiObjectPath.ResolveDotted(stateRoot, dataPath)
           ?? throw new InvalidOperationException($"Composition dataPath '{dataPath}' was not found.");

    private static void ApplyVariant(View view, CompositionSection section)
    {
        if (view is not ICompositionComponent component)
            throw new InvalidOperationException($"Section '{section.Id}' is not a composition component.");
        component.ApplyVariant(section.Variant);
    }

    private static IReadOnlyDictionary<string, (CompositionSlot Slot, int Index)> Positions(
        CompositionPlan? plan)
    {
        if (plan is null)
            return new Dictionary<string, (CompositionSlot, int)>();

        var positions = new Dictionary<string, (CompositionSlot, int)>(StringComparer.Ordinal);
        foreach (var slotGroup in plan.Sections
                     .Select((section, index) => (section, index))
                     .GroupBy(item => item.section.Slot))
        {
            var ordered = slotGroup
                .OrderByDescending(item => item.section.Priority)
                .ThenBy(item => item.index)
                .Select(item => item.section)
                .ToList();
            for (var index = 0; index < ordered.Count; index++)
                positions[ordered[index].Id] = (slotGroup.Key, index);
        }

        return positions;
    }
}
