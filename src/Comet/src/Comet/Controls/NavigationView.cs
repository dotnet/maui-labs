using System;

namespace Comet
{
	public class NavigationView : ContentView, IStackNavigationView
	{
		readonly object _viewsLock = new();
		List<IView> _views = new List<IView>();

		/// <summary>
		/// Action and icon for the leading (left) navigation bar button.
		/// Used for hamburger menu icons in flyout navigation.
		/// </summary>
		public Action LeadingBarAction { get; set; }

		/// <summary>
		/// Unicode character or system icon name for the leading bar button.
		/// Default is "☰" (hamburger icon).
		/// </summary>
		public string LeadingBarIcon { get; set; } = "☰";

		/// <summary>
		/// Collection of toolbar items to display in the navigation bar.
		/// </summary>
		public List<ToolbarItem> ToolbarItems { get; } = new();

		public void Navigate(View view)
		{
			view.Navigation = this;
			view.UpdateNavigation();

			if (PerformNavigate is null && Navigation is not null)
				Navigation.Navigate(view);
			else
			{
				_views.Add(view);
				if (PerformNavigate is not null)
					PerformNavigate(view);
				else
					((IStackNavigationView)this).RequestNavigation(new NavigationRequest(_views, true));
			}
		}

		public void Navigate<TView>() where TView : View, new()
			=> Navigate(new TView());

		public void Navigate<TView>(object parameters) where TView : View, new()
		{
			var view = new TView();
			NavigationParameterHelper.Apply(view, parameters);
			Navigate(view);
		}

		public void Navigate<TView, TParameters>(TParameters parameters) where TView : View, new()
			=> Navigate<TView>((object)parameters);

		public void SetPerformPop(Action action) => PerformPop = action;
		public void SetPerformPop(NavigationView navView)
			=> PerformPop = navView.PerformPop;
		protected Action PerformPop { get; set; }

		public void SetPerformNavigate(Action<View> action)
			=> PerformNavigate = action;
		public void SetPerformNavigate(NavigationView navView)
			=> PerformNavigate = navView.PerformNavigate;

		protected Action<View> PerformNavigate { get; set; }

		/// <summary>
		/// Action that pops the platform navigation controller to root
		/// and updates the root view controller's content.
		/// </summary>
		public void SetPerformContentReset(Action<View> action) => PerformContentReset = action;
		public void SetPerformContentReset(NavigationView navView)
			=> PerformContentReset = navView.PerformContentReset;
		protected Action<View> PerformContentReset { get; set; }

		//IToolbar IToolbarElement.Toolbar => CometWindow.Toolbar;

		protected override void OnHandlerChange()
		{
			if (_views.Count == 0 && Content is not null)
				_views.Add(Content);

			// When the handler is transferred from another NavigationView (during diff),
			// the platform navigation controller may have a stale stack.
			// Reset the root content to match the current Content.
			if (PerformContentReset is not null && Content is not null)
				PerformContentReset(Content);
			else
				((IStackNavigationView)this).RequestNavigation(new NavigationRequest(_views, false));

			base.OnHandlerChange();
		}

		public void Pop()
		{
			if (PerformPop is null && Navigation is not null)
				Navigation.Pop();
			else
			{
				if (PerformPop is not null)
					PerformPop();
				else
				{
					var lastIndex = _views.Count - 1;
					if (lastIndex < 0)
						return;
					_views.RemoveAt(lastIndex);
					((IStackNavigationView)this).RequestNavigation(new NavigationRequest(_views, true));
				}
			}
		}

		public override void Add(View view)
		{
			base.Add(view);
			if (view is not null)
			{
				view.Navigation = this;
				view.Parent = this;
			}
		}

		public static void Navigate(View fromView, View view)
		{
			if (view is ModalView modal)
			{
				ModalView.Present(modal.Content);
			}
			else if (fromView.Navigation is not null)
			{
				fromView.Navigation.Navigate(view);
			}
			else
			{
				ModalView.Present(view);
			}
		}

		public static void Pop(View view)
		{
			var parent = FindParentNavigationView(view);
			if (parent is ModalView)
			{
				ModalView.Dismiss();
			}
			else if (parent is NavigationView nav)
			{
				nav.Pop();
			}
		}

		/// <summary>
		/// Pops all views from the navigation stack back to the root content.
		/// </summary>
		public void PopToRoot()
		{
			lock (_viewsLock)
			{
				if (_views.Count <= 1) return;
				var root = _views[0];
				_views.Clear();
				_views.Add(root);
			}
			if (PerformContentReset is not null && Content is not null)
				PerformContentReset(Content);
		}

		public static void PopToRoot(View view)
		{
			var parent = FindParentNavigationView(view);
			if (parent is NavigationView nav)
				nav.PopToRoot();
		}

		static View FindParentNavigationView(View view)
		{
			if (view is null)
				return null;

			if (view.Parent is NavigationView || view.Parent is ModalView)
			{
				return view.Parent;
			}

			return FindParentNavigationView(view?.Parent) ?? view.Navigation;
		}

		void IStackNavigation.RequestNavigation(NavigationRequest eventArgs) =>
			ViewHandler?.Invoke(nameof(IStackNavigationView.RequestNavigation), eventArgs);
		void IStackNavigation.NavigationFinished(IReadOnlyList<IView> newStack) => _views = newStack.ToList();
	}
}
