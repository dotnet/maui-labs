using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.ComponentModel;
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
	readonly List<View> _observedViews = [];
	readonly List<(UIElement Element, DependencyPropertyDescriptor Property)> _observedLabels = [];
	View? _mauiItemView;
	bool _isPrepared;

	public MauiCollectionListBoxItem()
	{
		Loaded += (_, _) =>
		{
			ObserveView();
			ObserveLabeledBy();
		};
		Unloaded += (_, _) =>
		{
			StopObservingView();
			StopObservingLabeledBy();
		};
	}

	internal void PrepareAccessibility()
	{
		ClearAccessibility();
		_isPrepared = true;
		ObserveLabeledBy();
	}

	internal void ClearAccessibility()
	{
		_isPrepared = false;
		MauiItemView = null;
		StopObservingLabeledBy();
	}

	internal View? MauiItemView
	{
		get => _mauiItemView;
		set
		{
			StopObservingView();
			_mauiItemView = value;
			if (IsLoaded)
				ObserveView();
			InvalidateName();
		}
	}

	protected override AutomationPeer OnCreateAutomationPeer() => new MauiCollectionItemWrapperAutomationPeer(this);

	protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
	{
		base.OnPropertyChanged(e);
		if (e.Property == WAutomationProperties.LabeledByProperty)
			ObserveLabeledBy();
		if (e.Property == WAutomationProperties.NameProperty || e.Property == WAutomationProperties.LabeledByProperty)
			InvalidateName();
	}

	void ObserveLabeledBy()
	{
		StopObservingLabeledBy();
		if (_isPrepared && IsLoaded && WAutomationProperties.GetLabeledBy(this) is { } label)
			ObserveNativeLabel(label, []);
		InvalidateName();
	}

	void ObserveNativeLabel(UIElement label, HashSet<UIElement> visited)
	{
		if (!visited.Add(label))
			return;

		ObserveLabelProperty(label, DependencyPropertyDescriptor.FromProperty(WAutomationProperties.NameProperty, label.GetType()));
		ObserveLabelProperty(label, DependencyPropertyDescriptor.FromProperty(WAutomationProperties.LabeledByProperty, label.GetType()));
		if (WAutomationProperties.GetLabeledBy(label) is { } referencedLabel)
			ObserveNativeLabel(referencedLabel, visited);

		// WPF text and content controls derive their default peer names from
		// these properties; content may itself be another native label.
		foreach (var propertyName in new[] { "Text", "Content", "Header" })
		{
			var property = DependencyPropertyDescriptor.FromName(propertyName, label.GetType(), label.GetType());
			if (property == null)
				continue;
			ObserveLabelProperty(label, property);
			if (property.GetValue(label) is UIElement content)
				ObserveNativeLabel(content, visited);
		}
	}

	void ObserveLabelProperty(UIElement label, DependencyPropertyDescriptor property)
	{
		property.AddValueChanged(label, OnNativeLabelChanged);
		_observedLabels.Add((label, property));
	}

	void StopObservingLabeledBy()
	{
		foreach (var (label, property) in _observedLabels)
			property.RemoveValueChanged(label, OnNativeLabelChanged);
		_observedLabels.Clear();
	}

	void OnNativeLabelChanged(object? sender, EventArgs e) => ObserveLabeledBy();

	void ObserveView()
	{
		StopObservingView();
		if (_mauiItemView == null)
			return;
		_mauiItemView.DescendantAdded += OnDescendantsChanged;
		_mauiItemView.DescendantRemoved += OnDescendantsChanged;
		ObserveSubtree(_mauiItemView);
		InvalidateName();
	}

	void ObserveSubtree(View view)
	{
		_observedViews.Add(view);
		view.PropertyChanged += OnViewPropertyChanged;
		foreach (var child in ((IVisualTreeElement)view).GetVisualChildren().OfType<View>())
			ObserveSubtree(child);
	}

	void StopObservingView()
	{
		if (_mauiItemView != null)
		{
			_mauiItemView.DescendantAdded -= OnDescendantsChanged;
			_mauiItemView.DescendantRemoved -= OnDescendantsChanged;
		}
		foreach (var view in _observedViews)
			view.PropertyChanged -= OnViewPropertyChanged;
		_observedViews.Clear();
	}

	void OnDescendantsChanged(object? sender, ElementEventArgs e) => ObserveView();

	void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (string.IsNullOrEmpty(e.PropertyName) ||
			e.PropertyName == SemanticProperties.DescriptionProperty.PropertyName ||
			e.PropertyName is nameof(View.IsVisible) or nameof(Microsoft.Maui.Controls.Label.Text)
				or nameof(Microsoft.Maui.Controls.Label.FormattedText))
			InvalidateName();
	}

	void InvalidateName()
	{
		// Semantic changes need not change native layout (notably clearing a
		// description). Ask WPF to refresh its cached name and notify UIA clients.
		if (UIElementAutomationPeer.FromElement(this) is { } peer)
		{
			peer.InvalidatePeer();
			peer.EventsSource?.InvalidatePeer();
		}
	}
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
