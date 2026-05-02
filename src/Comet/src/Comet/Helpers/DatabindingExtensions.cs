using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comet.HotReload;
using Comet.Reactive;
using Comet.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Devices;
using Microsoft.Maui.HotReload;

// ReSharper disable once CheckNamespace
namespace Comet
{
	public static class DatabindingExtensions
	{
		/// <summary>
		/// Assigns a <see cref="PropertySubscription{T}"/> to a view property,
		/// disposing the previous subscription and binding the new one to the view.
		/// Parallel to <see cref="SetBindingValue{T}"/> for the unified reactive system.
		/// </summary>
		public static void SetPropertySubscription<T>(this View view, ref PropertySubscription<T>? currentValue, PropertySubscription<T>? newValue, [CallerMemberName] string propertyName = "")
		{
			currentValue?.Dispose();
			currentValue = newValue;
			currentValue?.BindToView(view, propertyName);
		}

		//public static void SetValue<T>(this State state, ref T currentValue, T newValue, View view, [CallerMemberName] string propertyName = "")
		//{
		//    if (state?.IsBuilding ?? false)
		//    {
		//        var props = state.EndProperty();
		//        var propCount = props.Length;
		//        //This is databound!
		//        if (propCount > 0)
		//        {
		//            bool isGlobal = propCount > 1;
		//            if (propCount == 1)
		//            {
		//                var prop = props[0];

		//                var stateValue = state.GetValue(prop).Cast<T>();
		//                var old = state.EndProperty();
		//                //1 to 1 binding!
		//                if (EqualityComparer<T>.Default.Equals(stateValue, newValue))
		//                {
		//                    state.BindingState.AddViewProperty(prop, propertyName, view);
		//                    Debug.WriteLine($"Databinding: {propertyName} to {prop}");
		//                }
		//                else
		//                {
		//                    var errorMessage = $"Warning: {propertyName} is using formated Text. For performance reasons, please switch to a Lambda. i.e new Text(()=> \"Hello\")";
		//                    if (Debugger.IsAttached)
		//                    {
		//                        throw new Exception(errorMessage);
		//                    }

		//                    Debug.WriteLine(errorMessage);
		//                    isGlobal = true;
		//                }
		//            }
		//            else
		//            {
		//                var errorMessage = $"Warning: {propertyName} is using Multiple state Variables. For performance reasons, please switch to a Lambda.";
		//                if (Debugger.IsAttached)
		//                {
		//                    throw new Exception(errorMessage);
		//                }

		//                Debug.WriteLine(errorMessage);
		//            }

		//            if (isGlobal)
		//            {
		//                state.BindingState.AddGlobalProperties(props);
		//            }
		//        }
		//    }

		//    if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
		//        return;
		//    currentValue = newValue;

		//    view.BindingPropertyChanged(propertyName, newValue);
		//}

		public static T Cast<T>(this object val)
		{
			if (val is null)
				return default;
			try
			{
				var type = typeof(T);
				var typeName = val?.GetType().Name;
				if ((typeName == "State`1" || typeName == "Reactive`1") && type.Name != "State`1" && type.Name != "Reactive`1")
				{
					return val.GetPropValue<T>("Value");
				}
				if (type == typeof(string))
				{
					return (T)(object)val?.ToString();
				}

				return (T)val;
			}
			catch
			{
				//This is ok, sometimes the values are not the same.
				return default;
			}
		}


		//public static void SetValue<T>(this View view, State state, ref T currentValue, T newValue, [CallerMemberName] string propertyName = "")
		//{
		//    if (view.IsDisposed)
		//        return;
		//    state.SetValue<T>(ref currentValue, newValue, view, propertyName);
		//}

		public static View Diff(this View newView, View oldView, bool checkRenderers)
		{
			if (oldView is null)
				return newView;
			var v = newView.DiffUpdate(oldView,checkRenderers);
			//void callUpdateOnView(View view)
			//{
			//    if (view is IContainerView container)
			//    {
			//        foreach (var child in container.GetChildren())
			//        {
			//            callUpdateOnView(child);
			//        }
			//    }
			//    view.FinalizeUpdateFromOldView();
			//};
			//callUpdateOnView(v);
			return v;
		}

		/// <summary>
		/// Attempts to merge Component instances when both views are Components of the same type.
		/// Reuses the old Component instance (for instance stability) and updates its props/state from the new instance.
		/// Returns the Component instance to use (old one, updated with new props).
		/// Returns null if merge was not applicable.
		/// </summary>
		static bool IsHotReloadReplacement(View newView, View oldView)
		{
			return CometHotReloadHelper.IsReplacedView(newView, oldView) ||
				MauiHotReloadHelper.IsReplacedView(newView, oldView) ||
				MauiHotReloadHelper.IsReplacedView(oldView, newView);
		}

		static View TryMergeComponents(View newView, View oldView, out bool reusedOldInstance)
		{
			reusedOldInstance = false;

			// Both must be IComponentWithState (all Component<T> variants implement this)
			if (!(newView is IComponentWithState) || !(oldView is IComponentWithState))
			{
				return null;
			}

			if (ReferenceEquals(newView, oldView))
			{
				var replacement = CometHotReloadHelper.CreateReplacement(oldView);
				if (replacement is not null && replacement != oldView)
				{
					oldView.SetHotReloadReplacement(replacement);
					return replacement;
				}
			}

			var isHotReloadReplacement = IsHotReloadReplacement(newView, oldView);

			// Same-type component diffs reuse the old instance; hot reload replacements
			// use the new instance so updated code runs while state/props are preserved.
			if (!isHotReloadReplacement && newView.GetType() != oldView.GetType())
			{
				return null;
			}

			if (isHotReloadReplacement)
			{
				oldView.SetHotReloadReplacement(newView);
				return newView;
			}

			// Strategy: Reuse the OLD Component instance for stability.
			// Update OLD's props/state from NEW, then return OLD.

			var componentType = oldView.GetType();
			var baseType = componentType.BaseType;

			// Walk up the hierarchy to find Component<TState, TProps> or Component<TState>
			while (baseType is not null && !baseType.Name.StartsWith("Component"))
			{
				baseType = baseType.BaseType;
			}

			if (baseType is null)
			{
				return null;
			}

			if (baseType.Name == "Component`2")
			{
				// Component<TState, TProps> — update props from new to old
				// Use the internal UpdatePropsFromDiff method to avoid triggering Reload
				var updateMethod = componentType.GetMethod("UpdatePropsFromDiff", 
					BindingFlags.NonPublic | BindingFlags.Instance);
				
				if (updateMethod is not null)
				{
					var propsProperty = baseType.GetProperty("Props");
					if (propsProperty is not null)
					{
						var newProps = propsProperty.GetValue(newView);
						updateMethod.Invoke(oldView, new[] { newProps });
					}
				}
			}
			else if (baseType.Name == "Component`1" || baseType.Name == "Component")
			{
				// Component<TState> or Component — no props to update, just reuse old instance
			}

			// Return OLD instance (updated with new props if applicable)
			// The caller will diff the BuiltView to reconcile the Render() output
			reusedOldInstance = true;
			return oldView;
		}

		static void DetachMergedChild(IContainerView oldContainer, IContainerView newContainer, View mergedChild)
		{
			if (mergedChild is null)
				return;
			if (ReferenceEquals(oldContainer, newContainer))
				return;
			if (oldContainer is IList<View> oldContainerList && oldContainerList.Contains(mergedChild))
			{
				oldContainerList.Remove(mergedChild);
			}
		}

		static void DetachRetainedOldChild(IContainerView oldContainer, IContainerView newContainer, View oldChild)
		{
			if (oldChild is null)
				return;
			if (ReferenceEquals(oldContainer, newContainer))
				return;
			if (oldContainer is IList<View> oldContainerList && oldContainerList.Contains(oldChild))
			{
				oldContainerList.Remove(oldChild);
			}
		}

		static View DiffUpdate(this View newView, View oldView, bool checkRenderers)
		{
			if (!newView.AreSameType(oldView, checkRenderers))
			{
				return newView;
			}

			// Component-specific merge logic
			// When both views are Components of the same type, reuse the old instance (updated with new props)
			var mergedComponent = TryMergeComponents(newView, oldView, out var reusedOldComponentInstance);
			if (mergedComponent is not null)
			{
				// Same-type diffs reuse the old instance; hot reload replacements keep the
				// new instance after state transfer so updated code executes.
				newView = mergedComponent;
				if (reusedOldComponentInstance)
					oldView = mergedComponent;
			}
			
			// Always diff the built views (the result of Body/Render)
			// This is especially important for Components — their Render() output needs diffing
			if (newView.BuiltView is not null && oldView.BuiltView is not null)
			{
				newView.BuiltView.Diff(oldView.BuiltView,checkRenderers);
			}

			if (newView is ContentView ncView && oldView is ContentView ocView)
			{
				ncView.Content?.DiffUpdate(ocView.Content, checkRenderers);
			}
			//Yes if one is IContainer, the other is too!
			else if (newView is IContainerView newContainer && oldView is IContainerView oldContainer)
			{
				var newChildren = newContainer.GetChildren();
				var oldChildren = oldContainer.GetChildren().ToList();
				
				// Check if any children have keys — if so, use key-based reconciliation
				var hasKeys = newChildren.Any(c => !string.IsNullOrEmpty(c?.GetKey()));
				
				if (hasKeys)
				{
					// Key-aware reconciliation: build a map of old children by key
					var oldByKey = new Dictionary<string, View>();
					var oldUnkeyed = new List<(int index, View view)>();
					
					for (var i = 0; i < oldChildren.Count; i++)
					{
						var oldChild = oldChildren[i];
						if (oldChild is null)
							continue;
						
						var key = oldChild.GetKey();
						if (!string.IsNullOrEmpty(key))
							oldByKey[key] = oldChild;
						else
							oldUnkeyed.Add((i, oldChild));
					}
					
					// Match new children to old by key, then diff
					var unkeyedIndex = 0;
					for (var i = 0; i < newChildren.Count; i++)
					{
						var newChild = newChildren.GetViewAtIndex(i);
						if (newChild is null)
							continue;
						
						var key = newChild.GetKey();
						View matchedOld = null;
						
						if (!string.IsNullOrEmpty(key))
						{
							// Match by key AND type (critical for keyed Components)
							if (oldByKey.TryGetValue(key, out var candidate) && 
								newChild.AreSameType(candidate, checkRenderers))
							{
								matchedOld = candidate;
							}
						}
						else if (unkeyedIndex < oldUnkeyed.Count)
						{
							// Fall back to positional matching for unkeyed children
							matchedOld = oldUnkeyed[unkeyedIndex].view;
							unkeyedIndex++;
						}
						
						if (matchedOld is not null && newChild.AreSameType(matchedOld, checkRenderers))
						{
							// Key-aware reconciliation preserves the OLD instance for identity stability.
							// For Components, DiffUpdate already returns the old instance (via TryMergeComponents).
							// For non-Component views (e.g. Text), DiffUpdate returns the new instance and
							// transfers the handler away from old — so we skip DiffUpdate and directly
							// reuse the old instance, which retains its handler and state.
							if (newChild is IComponentWithState || matchedOld is IComponentWithState)
							{
								var mergedChild = DiffUpdate(newChild, matchedOld, checkRenderers);
								if (newContainer is IList<View> mutableContainer)
								{
									DetachMergedChild(oldContainer, newContainer, mergedChild);
									DetachRetainedOldChild(oldContainer, newContainer, matchedOld);
									mutableContainer[i] = mergedChild;
								}
							}
							else if (newContainer is IList<View> mutableList)
							{
								DetachMergedChild(oldContainer, newContainer, matchedOld);
								mutableList[i] = matchedOld;
							}
						}
					}
				}
				else
				{
					// Original index-based diffing (backward compatible)
					for (var i = 0; i < Math.Max(newChildren.Count, oldChildren.Count); i++)
					{
						var n = newChildren.GetViewAtIndex(i);
						var o = oldChildren.GetViewAtIndex(i);
						if (n.AreSameType(o, checkRenderers))
						{
							var merged = DiffUpdate(n, o, checkRenderers);
							
							// CRITICAL FIX: If DiffUpdate returned a different instance (e.g., merged component),
							// update the container to reference the merged instance
							if (merged != n && newContainer is IList<View> mutableContainer)
							{
								DetachMergedChild(oldContainer, newContainer, merged);
								DetachRetainedOldChild(oldContainer, newContainer, o);
								mutableContainer[i] = merged;
							}
							continue;
						}

						if (i + 1 >= newChildren.Count || i + 1 >= oldChildren.Count)
						{
							//We are at the end, no point in searching
							continue;
						}

						//Lets see if the next 2 match
						var o1 = oldChildren.GetViewAtIndex(i + 1);
						var n1 = newChildren.GetViewAtIndex(i + 1);
						if (n1.AreSameType(o1, checkRenderers))
						{
							Debug.WriteLine("The controls were replaced!");
							//No big deal the control was replaced!
							continue;
						}

						if (n.AreSameType(o1, checkRenderers))
						{
							//we removed one from the old Children and use the next one

							Debug.WriteLine("One control was removed");
							var merged = DiffUpdate(n, o1, checkRenderers);
							
							// CRITICAL FIX: If DiffUpdate returned a different instance (e.g., merged component),
							// update the container to reference the merged instance
							if (merged != n && newContainer is IList<View> mutableContainer)
							{
								DetachMergedChild(oldContainer, newContainer, merged);
								DetachRetainedOldChild(oldContainer, newContainer, o1);
								mutableContainer[i] = merged;
								Debug.WriteLine($"Component merge: replaced child at index {i} with merged instance");
							}
							oldChildren.RemoveAt(i);
							continue;
						}

						if (n1.AreSameType(o, checkRenderers))
						{
							//The next ones line up, so this was just a new one being inserted!
							//Lets add an empty one to make them line up

							Debug.WriteLine("One control was added");
							var merged = DiffUpdate(n1, o, checkRenderers);
							
							// CRITICAL FIX: If DiffUpdate returned a different instance (e.g., merged component),
							// update the container to reference the merged instance at index i+1
							if (merged != n1 && newContainer is IList<View> mutableContainer)
							{
								DetachMergedChild(oldContainer, newContainer, merged);
								mutableContainer[i + 1] = merged;
								Debug.WriteLine($"Component merge: replaced child at index {i + 1} with merged instance");
							}
							oldChildren.Insert(i, null);
							continue;
						}

						//They don't line up. Maybe we check if 2 were inserted? But for now we are just going to say oh well.
						//The view will jsut be recreated for the restof these!
						Debug.WriteLine("Oh WEll");
						break;
					}
				}
			}
			
			// Only call UpdateFromOldView if we're actually returning a different view
			// (for Components, newView and oldView are now the same instance)
			// Run synchronously so handler transfer completes before ResetView
			// disposes the old view (async dispatch caused a race condition where
			// old handlers were already null by the time transfer ran).
			if (mergedComponent is null || !reusedOldComponentInstance)
			{
				newView.UpdateFromOldView(oldView);
			}

			return newView;
		}

		static View GetViewAtIndex(this IReadOnlyList<View> list, int index)
		{
			if (index >= list.Count)
				return null;
			return list[index];
		}


		public static bool AreSameType(this View view, View compareView, bool checkRenderers)
		{
			static bool AreSameType(View view, View compareView)
			{
				if (CometHotReloadHelper.IsReplacedView(view, compareView) ||
					MauiHotReloadHelper.IsReplacedView(view, compareView))
					return true;
				//Add in more edge cases
				var viewView = view?.GetView();
				var compareViewView = compareView?.GetView();

				if (CometHotReloadHelper.IsReplacedView(viewView, compareViewView) ||
					MauiHotReloadHelper.IsReplacedView(viewView, compareViewView))
					return true;

				return viewView?.GetType() == compareViewView?.GetType();
			}
			var areSame = AreSameType(view, compareView);
			if (areSame && checkRenderers && compareView?.ViewHandler is not null)
			{
				var mauiContext = compareView.ViewHandler.MauiContext ??
					view?.ViewHandler?.MauiContext ??
					CometContext.Current;
				var renderType = mauiContext?.Handlers?.GetHandlerType(view.GetType());
				if (renderType is not null)
					areSame = renderType == compareView.ViewHandler.GetType();
			}
			return areSame;
		}
	}
}
