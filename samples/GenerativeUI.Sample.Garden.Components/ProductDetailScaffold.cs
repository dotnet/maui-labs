using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace GenerativeUI.Sample.Garden.Components;

public sealed class ProductDetailScaffold : ContentView, ICompositionScaffold
{
    private readonly Label _title;
    private readonly IReadOnlyDictionary<CompositionSlot, Layout> _hosts;

    public ProductDetailScaffold()
    {
        AutomationId = "ProductDetailScaffold";
        SemanticProperties.SetDescription(this, "Composed product detail");

        _title = new Label
        {
            AutomationId = "ProductDetailScaffoldTitle",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = GardenComponentVisuals.SecondaryText,
        };

        var hero = Host("ProductDetailHeroSlot", 12);
        var primary = Host("ProductDetailPrimarySlot", 12);
        var supporting = Host("ProductDetailSupportingSlot", 12);
        var actions = new HorizontalStackLayout
        {
            AutomationId = "ProductDetailActionsSlot",
            Spacing = 8,
        };

        _hosts = new Dictionary<CompositionSlot, Layout>
        {
            [CompositionSlot.Hero] = hero,
            [CompositionSlot.Primary] = primary,
            [CompositionSlot.Supporting] = supporting,
            [CompositionSlot.Actions] = actions,
        };

        Content = new VerticalStackLayout
        {
            AutomationId = "ProductDetailScaffoldContent",
            Spacing = 14,
            Children = { _title, hero, primary, supporting, actions },
        };
    }

    public string? Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public IReadOnlyList<View> GetSlotChildren(CompositionSlot slot)
        => _hosts.TryGetValue(slot, out var host)
            ? [.. host.Children.OfType<View>()]
            : [];

    public void ApplySlots(IReadOnlyDictionary<CompositionSlot, IReadOnlyList<View>> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        foreach (var (slot, host) in _hosts)
        {
            var desired = slots.GetValueOrDefault(slot) ?? [];
            foreach (var current in host.Children.OfType<View>().ToList())
            {
                if (!desired.Contains(current))
                    host.Children.Remove(current);
            }
        }

        foreach (var (slot, host) in _hosts)
        {
            var desired = slots.GetValueOrDefault(slot) ?? [];
            for (var index = 0; index < desired.Count; index++)
            {
                var view = desired[index];
                RemoveFromOtherHost(view, host);

                var currentIndex = host.Children.IndexOf(view);
                if (currentIndex == index)
                    continue;

                if (currentIndex >= 0)
                    host.Children.RemoveAt(currentIndex);
                host.Children.Insert(index, view);
            }
        }
    }

    private void RemoveFromOtherHost(View view, Layout desiredHost)
    {
        foreach (var host in _hosts.Values)
        {
            if (ReferenceEquals(host, desiredHost))
                continue;
            host.Children.Remove(view);
        }
    }

    private static VerticalStackLayout Host(string automationId, double spacing)
        => new()
        {
            AutomationId = automationId,
            Spacing = spacing,
        };
}
