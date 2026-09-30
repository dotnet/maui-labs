using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Maui.Controls;
using WAutomationProperties = System.Windows.Automation.AutomationProperties;

namespace Microsoft.Maui.Handlers.WPF;

internal sealed class MauiCollectionListBoxAutomationPeer(MauiCollectionListBox owner) : ListBoxAutomationPeer(owner)
{
	protected override ItemAutomationPeer CreateItemAutomationPeer(object item)
		=> new MauiCollectionItemAutomationPeer(item, this);
}

internal sealed class MauiCollectionItemAutomationPeer(object item, ListBoxAutomationPeer parent)
	: ListBoxItemAutomationPeer(item, parent)
{
	protected override string GetNameCore()
	{
		var list = (MauiCollectionListBox)ItemsControlAutomationPeer.Owner;
		if (list.ItemContainerGenerator.ContainerFromItem(Item) is UIElement container)
			return UIElementAutomationPeer.CreatePeerForElement(container)?.GetName() ?? string.Empty;

		// WPF's default item peer falls back to the model's ToString(), even for an
		// empty template. Do not expose model internals while an item is virtualized.
		return string.Empty;
	}
}

internal sealed class MauiCollectionListBoxItem : ListBoxItem
{
	internal View? MauiItemView { get; set; }

	protected override AutomationPeer OnCreateAutomationPeer() => new MauiCollectionItemWrapperAutomationPeer(this);
}

internal sealed class MauiCollectionItemWrapperAutomationPeer(MauiCollectionListBoxItem owner)
	: ListBoxItemWrapperAutomationPeer(owner)
{
	protected override string GetNameCore()
	{
		var name = WAutomationProperties.GetName(owner);
		if (!string.IsNullOrEmpty(name))
			return name;
		if (WAutomationProperties.GetLabeledBy(owner) is UIElement label &&
			UIElementAutomationPeer.CreatePeerForElement(label) is { } labelPeer)
			return labelPeer.GetName();

		return owner.MauiItemView is { } view ? GetViewName(view)
			: (owner.Content as TextBlock)?.Text ?? string.Empty;
	}

	static string GetViewName(View view)
	{
		if (!view.IsVisible)
			return string.Empty;
		var description = SemanticProperties.GetDescription(view);
		if (!string.IsNullOrEmpty(description))
			return description;
		if (view is Microsoft.Maui.Controls.Label label)
			return label.FormattedText?.ToString() ?? label.Text ?? string.Empty;
		if (view is Microsoft.Maui.Controls.Button button)
			return button.Text ?? string.Empty;

		return string.Join(" ", ((IVisualTreeElement)view).GetVisualChildren()
			.OfType<View>().Select(GetViewName).Where(text => !string.IsNullOrEmpty(text)));
	}
}
