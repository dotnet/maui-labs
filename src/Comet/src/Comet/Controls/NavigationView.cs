using System;
using Comet.Backend;

namespace Comet
{
	public partial class NavigationView : ContentView, IStackNavigationView
	{
		readonly object _viewsLock = new();
		List<IView> _views = new List<IView>();
		object _logicalOwnerIdentity = new();
		bool _retainLogicalStackAfterOwnerTransfer = true;

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
			RebindNavigationOwner(view, this);

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
		Func<View> CurrentViewProvider { get; set; }

		internal void SetCurrentViewProvider(Func<View> provider)
			=> CurrentViewProvider = provider;

		// Only the NavigationView currently owned by a rendered backend has this callback.
		// The registry retains inactive navigation roots across shell section swaps.
		internal bool HasActiveBackendCallbacks => CurrentViewProvider is not null;

		internal void DetachBackendCallbacks()
		{
			PerformNavigate = null;
			PerformPop = null;
			PerformContentReset = null;
			CurrentViewProvider = null;
		}

		internal bool RetainsLogicalStackAfterOwnerTransfer
			=> _retainLogicalStackAfterOwnerTransfer;

		internal void SetRetainsLogicalStackAfterOwnerTransfer(bool retain)
			=> _retainLogicalStackAfterOwnerTransfer = retain;

		internal override bool TryRetainForOwnerTransfer()
		{
			if (!_retainLogicalStackAfterOwnerTransfer ||
				!string.IsNullOrEmpty(this.GetKey()))
				return false;

			_retainLogicalStackAfterOwnerTransfer = false;
			DetachBackendCallbacks();
			return true;
		}

		/// <summary>
		/// Carries logical owner identity across a fresh declaration at the same reconciled
		/// position. Persistent NavigationView instances keep their own identity.
		/// </summary>
		internal void AdoptReconciledOwnerIdentityFrom(NavigationView previous)
		{
			if (previous is null)
				throw new ArgumentNullException(nameof(previous));
			if (ReferenceEquals(this, previous) ||
				!string.IsNullOrEmpty(this.GetKey()) ||
				!string.IsNullOrEmpty(previous.GetKey()) ||
				!WasFreshlyDeclaredAtSameBodyPositionAs(previous))
				return;

			_logicalOwnerIdentity = previous._logicalOwnerIdentity;
		}

		internal bool HasSameLogicalOwnerIdentity(NavigationView other)
			=> other is not null &&
				ReferenceEquals(_logicalOwnerIdentity, other._logicalOwnerIdentity);

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

		internal static bool HasSameBackendRoot(View current, View next)
		{
			if (ReferenceEquals(current, next))
				return true;
			if (current is null || next is null || current.GetType() != next.GetType())
				return false;

			var currentKey = current.GetKey();
			var nextKey = next.GetKey();
			if (!string.IsNullOrEmpty(currentKey) || !string.IsNullOrEmpty(nextKey))
				return string.Equals(currentKey, nextKey, StringComparison.Ordinal);

			var currentId = current.AutomationId;
			var nextId = next.AutomationId;
			return string.IsNullOrEmpty(currentId) && string.IsNullOrEmpty(nextId)
				|| string.Equals(currentId, nextId, StringComparison.Ordinal);
		}

		internal IReadOnlyList<View> GetBackendNavigationStack()
		{
			lock (_viewsLock)
			{
				if (Content is not null &&
					(_views.Count == 0 ||
					 _views[0] is not View first ||
					 !HasSameBackendRoot(first, Content)))
					_views.Insert(0, Content);
				return _views.OfType<View>().ToArray();
			}
		}

		internal void SetBackendNavigationStack(IReadOnlyList<View> stack)
		{
			if (stack is null)
				throw new ArgumentNullException(nameof(stack));

			List<View> previous;
			lock (_viewsLock)
			{
				previous = _views.OfType<View>().ToList();
				_views = new List<IView>(stack.Count);
				foreach (var view in stack)
				{
					RebindNavigationOwner(view, this);
					_views.Add(view);
				}
			}

			NavigationStackLifecycle.DisposeRemovedOwnedPages(
				previous,
				stack,
				this,
				(_, _) => { });
		}

		internal static void RebindNavigationOwner(View view, NavigationView owner)
		{
			RebindNavigationOwner(view, owner, new HashSet<View>());
		}

		static void RebindNavigationOwner(
			View view,
			NavigationView owner,
			HashSet<View> visited)
		{
			if (view is null || !visited.Add(view))
				return;

			if (view is NavigationView nestedNavigation)
			{
				nestedNavigation.Navigation = owner;
				nestedNavigation.RebindOwnedPages(visited);
				return;
			}

			view.Navigation = owner;

			var rendered = view.BuiltView;
			if (rendered is not null && !ReferenceEquals(rendered, view))
				RebindNavigationOwner(rendered, owner, visited);

			if (view is not IContainerView container)
				return;

			foreach (var child in container.GetChildren())
				RebindNavigationOwner(child, owner, visited);
		}

		void RebindOwnedPages(HashSet<View> visited)
		{
			View[] pages;
			lock (_viewsLock)
			{
				if (Content is not null &&
					(_views.Count == 0 ||
					 _views[0] is not View first ||
					 !HasSameBackendRoot(first, Content)))
					_views.Insert(0, Content);
				pages = _views.OfType<View>().ToArray();
			}

			foreach (var page in pages)
				RebindNavigationOwner(page, this, visited);
		}

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
			=> PopCore();

		/// <summary>Requests a guarded back navigation. A page BackButtonBehavior command
		/// intercepts the request and decides whether to call Pop.</summary>
		public bool RequestBack()
		{
			if (TryHandleBackBehavior(CurrentViewProvider?.Invoke() ?? _views.LastOrDefault() as View))
				return true;
			PopCore();
			return true;
		}

		void PopCore()
		{
			if (PerformPop is null && Navigation is not null)
				Navigation.Pop();
			else
			{
				if (PerformPop is not null)
					PerformPop();
				else
				{
					var stack = GetBackendNavigationStack().ToList();
					if (!NavigationStackLifecycle.TryPop(
						stack,
						(_, _) => { },
						out _))
						return;
					ReplaceBackendNavigationStackWithoutDisposal(stack);
					((IStackNavigationView)this).RequestNavigation(new NavigationRequest(_views, true));
				}
			}

		}

		bool TryHandleBackBehavior(View view)
		{
			var behavior = view?.GetBackButtonBehavior();
			if (behavior is null)
				return false;
			if (!behavior.IsEnabled)
				return true;
			if (behavior.Command is null)
				return false;
			if (!behavior.Command.CanExecute(behavior.CommandParameter))
				return false;

			behavior.Command.Execute(behavior.CommandParameter);
			return true;
		}

		public override void Add(View view)
		{
			base.Add(view);
			if (view is not null)
			{
				RebindNavigationOwner(view, this);
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
			if (PerformContentReset is not null && Content is not null)
			{
				PerformContentReset(Content);
				return;
			}

			var stack = GetBackendNavigationStack().ToList();
			if (stack.Count <= 1)
				return;
			NavigationStackLifecycle.ResetToRoot(
				stack,
				stack[0],
				(_, _) => { });
			ReplaceBackendNavigationStackWithoutDisposal(stack);
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

		void ReplaceBackendNavigationStackWithoutDisposal(IReadOnlyList<View> stack)
		{
			lock (_viewsLock)
			{
				_views = new List<IView>(stack.Count);
				foreach (var view in stack)
					_views.Add(view);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing)
			{
				base.Dispose(disposing);
				return;
			}
			_retainLogicalStackAfterOwnerTransfer = false;
			_retainLogicalStackAfterOwnerTransfer = false;
			var content = Content;
			var stack = GetBackendNavigationStack().ToList();
			if (content is not null &&
				!stack.Any(page => ReferenceEquals(page, content)))
				stack.Insert(0, content);
			lock (_viewsLock)
				_views.Clear();
			NavigationStackLifecycle.DisposeOwnedPages(
				stack,
				this,
				(_, _) => { });
			Content = null;
			DetachBackendCallbacks();

			base.Dispose(disposing);
		}

		void IStackNavigation.RequestNavigation(NavigationRequest eventArgs) =>
			ViewHandler?.Invoke(nameof(IStackNavigationView.RequestNavigation), eventArgs);
		void IStackNavigation.NavigationFinished(IReadOnlyList<IView> newStack) => _views = newStack.ToList();
	}
}
