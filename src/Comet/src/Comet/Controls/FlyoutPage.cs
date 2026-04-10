using System;
using System.Collections;
using System.Collections.Generic;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	/// <summary>
	/// A page that manages a flyout panel and a detail page.
	/// Commonly used for master-detail layouts and navigation menus.
	/// </summary>
	public class FlyoutPage : View, IEnumerable, IContainerView
	{
		View _flyout;
		View _detail;
		PropertySubscription<bool> _isPresented;
		PropertySubscription<FlyoutBehavior> _flyoutBehavior;

		public FlyoutPage()
		{
		}

		/// <summary>
		/// Gets or sets the flyout (master) page content.
		/// This is typically a navigation menu or list of options.
		/// </summary>
		public View Flyout
		{
			get => _flyout;
			set
			{
				if (_flyout is not null)
					_flyout.Parent = null;
				_flyout = value;
				if (_flyout is not null)
				{
					_flyout.Parent = this;
					_flyout.Navigation = Parent?.Navigation;
				}
				TypeHashCode = GetContentTypeHashCode();
			}
		}

		/// <summary>
		/// Gets or sets the detail page content.
		/// This is the main content area shown when the flyout is hidden.
		/// </summary>
		public View Detail
		{
			get => _detail;
			set
			{
				if (_detail is not null)
					_detail.Parent = null;
				_detail = value;
				if (_detail is not null)
				{
					_detail.Parent = this;
					_detail.Navigation = Parent?.Navigation;
				}
				TypeHashCode = GetContentTypeHashCode();
			}
		}

		/// <summary>
		/// Gets or sets whether the flyout is currently presented (visible).
		/// Supports two-way binding with State.
		/// </summary>
		public PropertySubscription<bool> IsPresented
		{
			get => _isPresented;
			set => this.SetPropertySubscription(ref _isPresented, value);
		}

		/// <summary>
		/// Gets or sets the flyout presentation behavior.
		/// Controls how the flyout appears and interacts with the detail page.
		/// </summary>
		public PropertySubscription<FlyoutBehavior> FlyoutLayoutBehavior
		{
			get => _flyoutBehavior;
			set => this.SetPropertySubscription(ref _flyoutBehavior, value);
		}

		/// <summary>
		/// Gets or sets the flyout width.
		/// Set to -1 for platform default width.
		/// </summary>
		public double FlyoutWidth
		{
			get => this.GetEnvironment<double?>(nameof(FlyoutWidth)) ?? -1;
			set => this.SetEnvironment(nameof(FlyoutWidth), value);
		}

		/// <summary>
		/// Gets or sets whether gesture navigation is enabled for the flyout.
		/// </summary>
		public bool IsGestureEnabled
		{
			get => this.GetEnvironment<bool?>(nameof(IsGestureEnabled)) ?? true;
			set => this.SetEnvironment(nameof(IsGestureEnabled), value);
		}

		/// <summary>
		/// Gets the child views (flyout and detail).
		/// </summary>
		public IReadOnlyList<View> GetChildren()
		{
			var children = new List<View>();
			if (_flyout is not null)
				children.Add(_flyout);
			if (_detail is not null)
				children.Add(_detail);
			return children.AsReadOnly();
		}

		public override int GetContentTypeHashCode()
		{
			var hash = base.GetContentTypeHashCode();
			hash = (hash * 31) + (_flyout?.GetContentTypeHashCode() ?? 0);
			hash = (hash * 31) + (_detail?.GetContentTypeHashCode() ?? 0);
			return hash;
		}

		protected override void OnParentChange(View parent)
		{
			base.OnParentChange(parent);
			if (_flyout is not null)
				_flyout.Parent = this;
			if (_detail is not null)
				_detail.Parent = this;
		}

		internal override void ContextPropertyChanged(string property, object value, bool cascades)
		{
			base.ContextPropertyChanged(property, value, cascades);
			_flyout?.ContextPropertyChanged(property, value, cascades);
			_detail?.ContextPropertyChanged(property, value, cascades);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_flyout?.Dispose();
				_detail?.Dispose();
				_flyout = null;
				_detail = null;
			}
			base.Dispose(disposing);
		}

		internal override void Reload(bool isHotReload)
		{
			_flyout?.Reload(isHotReload);
			_detail?.Reload(isHotReload);
			base.Reload(isHotReload);
		}

		public override void ViewDidAppear()
		{
			_flyout?.ViewDidAppear();
			_detail?.ViewDidAppear();
			base.ViewDidAppear();
		}

		public override void ViewDidDisappear()
		{
			_flyout?.ViewDidDisappear();
			_detail?.ViewDidDisappear();
			base.ViewDidDisappear();
		}

		public override void PauseAnimations()
		{
			_flyout?.PauseAnimations();
			_detail?.PauseAnimations();
			base.PauseAnimations();
		}

		public override void ResumeAnimations()
		{
			_flyout?.ResumeAnimations();
			_detail?.ResumeAnimations();
			base.ResumeAnimations();
		}

		#region IEnumerable
		IEnumerator IEnumerable.GetEnumerator() => GetChildren().GetEnumerator();
		#endregion
	}
}
