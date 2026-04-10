using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Comet;

/// <summary>
/// A Microsoft.Maui.Controls.View that hosts a Comet MVU View inside MAUI pages (e.g. Shell ContentPages).
/// This is the reverse of MauiViewHost: it allows embedding Comet views inside MAUI XAML/code pages.
/// </summary>
public class CometHost : Microsoft.Maui.Controls.View, IContentView, IVisualTreeElement
{
	public static readonly BindableProperty CometViewProperty = BindableProperty.Create(
		nameof(CometView), typeof(Comet.View), typeof(CometHost));

	public Comet.View CometView
	{
		get => (Comet.View)GetValue(CometViewProperty);
		set => SetValue(CometViewProperty, value);
	}

	public CometHost() { }

	public CometHost(Comet.View view)
	{
		CometView = view;
	}

	object IContentView.Content => GetPresentedContent();

	IView IContentView.PresentedContent => GetPresentedContent();

	Thickness IPadding.Padding => Thickness.Zero;

	Size IContentView.CrossPlatformMeasure(double widthConstraint, double heightConstraint)
		=> GetPresentedContent()?.Measure(widthConstraint, heightConstraint) ?? Size.Zero;

	Size IContentView.CrossPlatformArrange(Rect bounds)
	{
		var content = GetPresentedContent();
		content?.Arrange(bounds);
		return bounds.Size;
	}

	IView GetPresentedContent()
	{
		var renderView = CometView?.GetView();
		return renderView is not null && renderView != CometView ? renderView : CometView;
	}

	IReadOnlyList<IVisualTreeElement> IVisualTreeElement.GetVisualChildren()
	{
		var presentedChild = GetPresentedContent() as IVisualTreeElement;
		if (presentedChild is not null)
			return new[] { presentedChild };

		var rootChild = CometView as IVisualTreeElement;
		return rootChild is null ? Array.Empty<IVisualTreeElement>() : new[] { rootChild };
	}

	IVisualTreeElement IVisualTreeElement.GetVisualParent() => Parent as IVisualTreeElement;
}
