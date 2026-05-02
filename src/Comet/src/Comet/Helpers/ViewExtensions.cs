using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comet.Internal;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Comet
{
	public static class ViewExtensions
	{
		public static View GetViewWithTag(this View view, string tag)
		{
			if (view is null) return null;

			if (view.Tag == tag)
				return view;

			if (view is AbstractLayout layout)
			{
				foreach (var subView in layout)
				{
					var match = subView.GetViewWithTag(tag);
					if (match is not null)
						return match;
				}
			}

			if (view.GetType() == typeof(ContentView))
				return ((ContentView)view).Content.GetViewWithTag(tag);

			return view.BuiltView.GetViewWithTag(tag);
		}

		public static T GetViewWithTag<T>(this View view, string tag) where T : View => view.GetViewWithTag(tag) as T;

		public static T Tag<T>(this T view, string tag) where T : View
		{
			view.Tag = tag;
			return view;
		}

		public static T Key<T>(this T view, string key) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.View.Key, key, cascades: false);
			return view;
		}

		public static string GetKey(this View view)
		{
			return view?.GetEnvironment<string>(EnvironmentKeys.View.Key, cascades: false);
		}

		public static ListView<T> OnSelected<T>(this ListView<T> listview, Action<T> selected)
		{
			listview.ItemSelected = (o) => {
				selected?.Invoke((T)o.item);
			};
			return listview;
		}

		public static CollectionView<T> OnSelected<T>(this CollectionView<T> collectionView, Action<T> selected)
		{
			collectionView.ItemSelected = (o) => {
				selected?.Invoke((T)o.item);
			};
			return collectionView;
		}

		public static List<FieldInfo> GetFieldsWithAttribute(this object obj, Type attribute)
		{
			var type = obj.GetType();
			var fields = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(x => Attribute.IsDefined(x, attribute)).ToList();
			return fields;
		}

		public static T Title<T>(this T view, string title, bool cascades = true) where T : View =>
			view.SetEnvironment(EnvironmentKeys.View.Title, (object)title, cascades, ControlState.Default);
		public static T Title<T>(this T view, Func<string> title, bool cascades = true) where T : View =>
			view.Title(title(), cascades);

		public static T Enabled<T>(this T view, bool enabled, bool cascades = true) where T : View =>
			view.SetEnvironment(nameof(IView.IsEnabled), (object)enabled, cascades, ControlState.Default);
		public static T Enabled<T>(this T view, Func<bool> enabled, bool cascades = true) where T : View =>
			view.Enabled(enabled(), cascades);

		public static string GetTitle(this View view)
		{
			var title = view?.GetEnvironment<string>(EnvironmentKeys.View.Title);
			title ??= view?.BuiltView?.GetEnvironment<string>(EnvironmentKeys.View.Title,true) ?? "";
			return title;
		}

		/// <summary>
		/// Sets toolbar items on a view for display in the navigation bar when pushed.
		/// </summary>
		public static T ToolbarItems<T>(this T view, params ToolbarItem[] items) where T : View
		{
			view.SetEnvironment(EnvironmentKeys.View.ToolbarItems, items.ToList(), false);
			return view;
		}

		/// <summary>
		/// Gets toolbar items from a view (set via the .ToolbarItems() extension).
		/// Returns an empty list if none are set.
		/// </summary>
		public static List<ToolbarItem> GetToolbarItems(this View view)
		{
			var items = view?.GetEnvironment<List<ToolbarItem>>(EnvironmentKeys.View.ToolbarItems);
			items ??= view?.BuiltView?.GetEnvironment<List<ToolbarItem>>(EnvironmentKeys.View.ToolbarItems, true);
			return items ?? new List<ToolbarItem>();
		}

		public static T AddGesture<T>(this T view, Gesture gesture) where T : View
		{
			var gestures = (List<Gesture>)(view.Gestures ?? (view.Gestures = new List<Gesture>()));
			gestures.Add(gesture);
			view?.ViewHandler?.UpdateValue(Comet.Gesture.AddGestureProperty);
			return view;
		}
		public static T RemoveGesture<T>(this T view, Gesture gesture) where T : View
		{
			var gestures = (List<Gesture>)view.Gestures;
			gestures.Remove(gesture);
			view?.ViewHandler?.UpdateValue(Comet.Gesture.RemoveGestureProperty);
			return view;
		}

		public static T OnTap<T>(this T view, Action<T> action) where T : View
			=> view.AddGesture(new TapGesture((g) => action?.Invoke(view)));

		public static T OnLongPress<T>(this T view, Action<T> action) where T : View
			=> view.AddGesture(new LongPressGesture((g) => action?.Invoke(view)));

		public static T OnPan<T>(this T view, Action<PanGesture> action) where T : View
			=> view.AddGesture(new PanGesture(action));

		public static T OnPinch<T>(this T view, Action<PinchGesture> action) where T : View
			=> view.AddGesture(new PinchGesture(action));

		public static T OnSwipe<T>(this T view, Action<SwipeGesture> action, SwipeDirection direction = SwipeDirection.Left) where T : View
			=> view.AddGesture(new SwipeGesture(action) { Direction = direction });

		public static T OnTapNavigate<T>(this T view, Func<View> destination) where T : View
			=> view.OnTap((v) => NavigationView.Navigate(view, destination.Invoke()));

		public static void Navigate(this View view, View destination) => NavigationView.Navigate(view, destination);

		public static void Dismiss(this View view) => NavigationView.Pop(view);

		public static ListView<T> OnSelectedNavigate<T>(this ListView<T> view, Func<T, View> destination) => view.OnSelected(v => NavigationView.Navigate(view, destination?.Invoke(v)));

		public static void SetResult<T>(this View view, T value)
		{
			var resultView = view.FindParentOfType<ResultView<T>>();
			resultView.SetResult(value);
		}


		public static void SetResult<T>(this View view, Reactive<T> value)
		{
			var resultView = view.FindParentOfType<ResultView<T>>();
			resultView.SetResult(value.Value);
		}

		public static void SetResultCanceled<T>(this View view)
		{
			var resultView = view.FindParentOfType<ResultView<T>>();
			resultView.Cancel();
		}
		public static void SetResultException<T>(this View view, Exception ex)
		{
			var resultView = view.FindParentOfType<ResultView<T>>();
			resultView.SetException(ex);
		}

		public static string GetAutomationId(this View view)
			=> view.GetEnvironment<string>(view, EnvironmentKeys.View.AutomationId, cascades: false) ?? view.AccessibilityId;
		public static void SetAutomationId(this View view, string automationId)
		{
			view.AccessibilityId = automationId;
			view.SetEnvironment(EnvironmentKeys.View.AutomationId, automationId, cascades: false);
		}

		public static T AutomationId<T>(this T view, string automationId) where T : View
		{
			view.SetAutomationId(automationId);
			return view;
		}

		/// <summary>
		/// Hunts through the parents to find the current Context.
		/// </summary>
		/// <param name="view"></param>
		/// <returns></returns>
		public static IMauiContext GetMauiContext(this View view)
		{
			//IF there is only one app, with one window, then there is only one context.
			//Don't go hunting!
			if (CometApp.CurrentApp.Windows.Count == 1)
				return CometApp.MauiContext;
			return view.FindParentOfType<IMauiContextHolder>()?.MauiContext ?? CometApp.MauiContext;
		}

		public static T Aspect<T>(this T image, Aspect aspect) where T : Image =>
			image.SetEnvironment(nameof(Aspect), aspect);

		public static T Opacity<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.Opacity), value);

		public static T Background<T>(this T view, Paint value) where T : View =>
			view.SetEnvironment(nameof(IView.Background), value,false);

		public static T TranslationX<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.TranslationX), value,false);

		public static T TranslationY<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.TranslationY), value, false);

		public static T Scale<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.Scale), value, false);
		public static T ScaleX<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.ScaleX), value, false);
		public static T ScaleY<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.ScaleY), value, false);


		public static T Rotation<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.Rotation), value, false);
		public static T RotationX<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.RotationX), value, false);
		public static T RotationY<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.RotationY), value, false);


		public static T AnchorX<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.AnchorX), value, false);
		public static T AnchorY<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.AnchorY), value, false);

		// Accessibility / Semantic Properties
		public static T SemanticDescription<T>(this T view, string description) where T : View
		{
			var semantics = view.GetEnvironment<Semantics>(nameof(IView.Semantics)) ?? new Semantics();
			semantics.Description = description;
			view.SetEnvironment(nameof(IView.Semantics), semantics, true);
			return view;
		}

		public static T SemanticHint<T>(this T view, string hint) where T : View
		{
			var semantics = view.GetEnvironment<Semantics>(nameof(IView.Semantics)) ?? new Semantics();
			semantics.Hint = hint;
			view.SetEnvironment(nameof(IView.Semantics), semantics, true);
			return view;
		}

		public static T SemanticHeadingLevel<T>(this T view, SemanticHeadingLevel level) where T : View
		{
			var semantics = view.GetEnvironment<Semantics>(nameof(IView.Semantics)) ?? new Semantics();
			semantics.HeadingLevel = level;
			view.SetEnvironment(nameof(IView.Semantics), semantics, true);
			return view;
		}

		public static T IsReadOnly<T>(this T view, bool isReadOnly = true) where T : View
		{
			view.SetEnvironment("View.IsReadOnly", isReadOnly);
			return view;
		}

		// Visibility
		public static T IsVisible<T>(this T view, bool visible = true) where T : View =>
			view.SetEnvironment(nameof(IView.Visibility), visible ? Visibility.Visible : Visibility.Collapsed);

		public static T InputTransparent<T>(this T view, bool value = true) where T : View =>
			view.SetEnvironment(nameof(IView.InputTransparent), value);

		public static T ZIndex<T>(this T view, int value) where T : View =>
			view.SetEnvironment(nameof(IView.ZIndex), value, false);

		public static T FlowDirection<T>(this T view, FlowDirection direction) where T : View =>
			view.SetEnvironment(nameof(IView.FlowDirection), direction);

		public static T Shadow<T>(this T view, Graphics.Shadow shadow) where T : View =>
			view.SetEnvironment(EnvironmentKeys.View.Shadow, (object)shadow, false);

		public static T Shadow<T>(this T view, Func<Graphics.Shadow> shadow) where T : View => view.Shadow(shadow());

		public static T IsEnabled<T>(this T view, bool enabled = true) where T : View =>
			view.SetEnvironment(nameof(IView.IsEnabled), enabled);

		public static T MinimumHeight<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.MinimumHeight), value, false);

		public static T MaximumHeight<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.MaximumHeight), value, false);

		public static T MinimumWidth<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.MinimumWidth), value, false);

		public static T MaximumWidth<T>(this T view, double value) where T : View =>
			view.SetEnvironment(nameof(IView.MaximumWidth), value, false);

		// Behaviors
		public static T AddBehavior<T>(this T view, Behavior behavior) where T : View
		{
			if (behavior is null)
				throw new ArgumentNullException(nameof(behavior));

			var behaviors = view.Behaviors as List<Behavior>;
			behaviors.Add(behavior);
			behavior.Attach(view);
			return view;
		}

		public static T RemoveBehavior<T>(this T view, Behavior behavior) where T : View
		{
			if (behavior is null)
				throw new ArgumentNullException(nameof(behavior));

			var behaviors = view.Behaviors as List<Behavior>;
			if (behaviors.Remove(behavior))
				behavior.Detach();
			return view;
		}

		// Triggers
		public static T AddTrigger<T>(this T view, DataTrigger trigger) where T : View
		{
			if (trigger is null)
				throw new ArgumentNullException(nameof(trigger));

			var triggers = view.Triggers as List<DataTrigger>;
			triggers.Add(trigger);
			trigger.Attach(view);
			return view;
		}

		public static T RemoveTrigger<T>(this T view, DataTrigger trigger) where T : View
		{
			if (trigger is null)
				throw new ArgumentNullException(nameof(trigger));

			var triggers = view.Triggers as List<DataTrigger>;
			if (triggers.Remove(trigger))
				trigger.Detach();
			return view;
		}

		// Effects
		public static T AddEffect<T>(this T view, PlatformBehavior effect) where T : View
		{
			return view.AddBehavior(effect);
		}

		// Visual States — removed in Phase 1; superseded by IControlStyle<T, TConfig> + ControlState.
		// See docs/architecture/STYLE_THEME_SPEC.md §9.

		// Lifecycle convenience methods
		public static T OnLoaded<T>(this T view, Action action) where T : View
		{
			view.Loaded += (s, e) => action?.Invoke();
			return view;
		}

		public static T OnUnloaded<T>(this T view, Action action) where T : View
		{
			view.Unloaded += (s, e) => action?.Invoke();
			return view;
		}

		public static T OnAppearing<T>(this T view, Action action) where T : View
		{
			view.Appearing += (s, e) => action?.Invoke();
			return view;
		}

		public static T OnDisappearing<T>(this T view, Action action) where T : View
		{
			view.Disappearing += (s, e) => action?.Invoke();
			return view;
		}

		public static T OnHandlerChanged<T>(this T view, Action action) where T : View
		{
			view.HandlerChanged += (s, e) => action?.Invoke();
			return view;
		}

		public static T OnHandlerChanging<T>(this T view, Action<HandlerChangingEventArgs> action) where T : View
		{
			view.HandlerChanging += (s, e) => action?.Invoke(e);
			return view;
		}

		/// <summary>
		/// Attaches a context menu (MenuFlyout) to the view.
		/// </summary>
		public static T ContextMenu<T>(this T view, MenuFlyout menu) where T : View
		{
			view.SetEnvironment(nameof(ContextMenu), menu, true);
			return view;
		}

	}
}
