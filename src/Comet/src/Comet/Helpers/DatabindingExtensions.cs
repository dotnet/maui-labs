using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Comet.Backend;
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
				// Reuse an already-installed replacement (AreSameType→GetView may have
				// installed one moments earlier); creating a second instance would strand
				// the state/backed node already transferred to the first.
				var replacement = oldView.HotReloadReplacedView
					?? CometHotReloadHelper.CreateReplacement(oldView);
				if (replacement is not null && replacement != oldView)
				{
					if (!ReferenceEquals(oldView.HotReloadReplacedView, replacement))
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
				if (newContainer is View newOwner)
				{
					mergedChild.Parent = newOwner;
					mergedChild.Navigation = newOwner.Navigation;
				}
			}
		}

		static void DetachRetainedChild(
			IContainerView oldContainer,
			IContainerView newContainer,
			View mergedChild,
			View oldChild)
		{
			if (ReferenceEquals(mergedChild, oldChild))
				DetachMergedChild(oldContainer, newContainer, oldChild);
		}

		static bool HasBackendManagedContent(View view)
			=> view?.Node is IBackendManagesOwnContent;

		static bool ReconcilesBackendManagedContent(View view)
			=> view?.Node is IBackendReconcilesOwnContent;

		static bool RetainsLogicalContentOnOwnerTransfer(View view)
			=> view?.Node is IBackendRetainsLogicalContentOnOwnerTransfer;

		static bool HasMatchingReconciliationIdentity(
			View newView,
			View oldView,
			bool checkRenderers)
		{
			if (!newView.AreSameType(oldView, checkRenderers))
				return false;

			var newKey = newView.GetKey();
			var oldKey = oldView.GetKey();
			return string.IsNullOrEmpty(newKey) && string.IsNullOrEmpty(oldKey)
				|| string.Equals(newKey, oldKey, StringComparison.Ordinal);
		}

		static bool ShouldDetachOutgoingOwner(
			View mergedChild,
			View oldChild,
			bool retainsLogicalContent)
			=> ReferenceEquals(mergedChild, oldChild) ||
				ShouldRetainOutgoingOwner(oldChild, retainsLogicalContent);

		static bool ShouldRetainOutgoingOwner(
			View oldChild,
			bool retainsLogicalContent)
			=> retainsLogicalContent &&
				string.IsNullOrEmpty(oldChild.GetKey()) &&
				(oldChild is not NavigationView navigation ||
				 navigation.RetainsLogicalStackAfterOwnerTransfer);

		static void ReconcileEquivalentNavigationRoot(
			NavigationView newNavigation,
			NavigationView oldNavigation,
			bool checkRenderers)
		{
			var incoming = newNavigation.Content;
			var outgoing = oldNavigation.Content;
			if (incoming is null ||
				outgoing is null ||
				!NavigationView.HasSameBackendRoot(outgoing, incoming))
				return;

			if (ReferenceEquals(incoming, outgoing))
			{
				oldNavigation.Content = null;
				incoming.Parent = newNavigation;
				incoming.Navigation = newNavigation;
				return;
			}

			var retainsLogicalContent =
				RetainsLogicalContentOnOwnerTransfer(outgoing);
			var merged = incoming.Diff(outgoing, checkRenderers);
			if (!ReferenceEquals(merged, incoming))
				newNavigation.Content = merged;

			if (ShouldDetachOutgoingOwner(
				merged,
				outgoing,
				retainsLogicalContent))
				oldNavigation.Content = null;
		}

		static View ReconcileKeyedPlainView(
			View newView,
			View oldView,
			bool checkRenderers)
		{
			// Keys retain and move backend identity, not stale declaration objects. The
			// replacement view already owns the complete declarative payload: constructor
			// scalars, subscriptions, callbacks, and owned content. DiffUpdate transfers the
			// retained native node into that replacement.
			//
			return DiffUpdate(newView, oldView, checkRenderers);
		}

		static void DetachReplacedBackendManagedChild(
			IContainerView oldContainer,
			IContainerView newContainer,
			View mergedChild,
			View oldChild,
			bool retainsLogicalContent)
		{
			// Unkeyed persistent controls can deliberately keep logical generations across
			// an owner transfer. A keyed declaration is different: its replacement owns the
			// complete new payload, so the outgoing owner remains in the old tree and is
			// disposed normally after its backend node has moved.
			if (ShouldRetainOutgoingOwner(oldChild, retainsLogicalContent) &&
				!ReferenceEquals(mergedChild, oldChild))
				DetachMergedChild(oldContainer, newContainer, oldChild);
		}

		static void InsertBackendChild(View parent, View child, int index)
		{
			var parentNode = parent.Node;
			if (parentNode is null)
				return;

			var childNode = Backend.CometBackendBridge.MaterializeChild(child, parent);
			parentNode.InsertChild(index, childNode);
		}

		static void RemoveBackendChild(
			View parent,
			IContainerView oldContainer,
			IContainerView newContainer,
			View child,
			int index)
		{
			parent.Node?.RemoveChildAt(index);
			DetachMergedChild(oldContainer, newContainer, child);
			child.Dispose();
		}

		static void ReplaceBackendTail(
			View parent,
			IContainerView oldContainer,
			IContainerView newContainer,
			IReadOnlyList<View> oldChildren,
			IReadOnlyList<View> newChildren,
			int startIndex)
		{
			for (var removeIndex = oldChildren.Count - 1;
				removeIndex >= startIndex;
				removeIndex--)
			{
				RemoveBackendChild(
					parent,
					oldContainer,
					newContainer,
					oldChildren[removeIndex],
					removeIndex);
			}

			for (var insertIndex = startIndex;
				insertIndex < newChildren.Count;
				insertIndex++)
				InsertBackendChild(parent, newChildren[insertIndex], insertIndex);
		}

		static void ReconcileContentView(
			ContentView newContentView,
			ContentView oldContentView,
			bool checkRenderers,
			bool backendManagesContent)
		{
			var incoming = newContentView.Content;
			var outgoing = oldContentView.Content;

			if (incoming is not null &&
				outgoing is not null &&
				HasMatchingReconciliationIdentity(incoming, outgoing, checkRenderers))
			{
				var retainsLogicalContent =
					RetainsLogicalContentOnOwnerTransfer(outgoing);
				var merged = incoming.DiffUpdate(outgoing, checkRenderers);
				if (!ReferenceEquals(merged, incoming))
					newContentView.Content = merged;

				if (!ReferenceEquals(newContentView, oldContentView) &&
					ShouldDetachOutgoingOwner(
						merged,
						outgoing,
						retainsLogicalContent))
					oldContentView.Content = null;
				return;
			}

			if (outgoing is not null)
			{
				if (!backendManagesContent)
					oldContentView.Node?.RemoveChildAt(0);
				oldContentView.Content = null;
				outgoing.Dispose();
			}

			if (incoming is not null && !backendManagesContent)
				InsertBackendChild(oldContentView, incoming, 0);
		}

		static List<View> NonNullChildren(IContainerView container)
			=> container.GetChildren()
				.Where(child => child is not null)
				.ToList();

		static void ReplaceContainerChildren(
			IList<View> container,
			IReadOnlyList<View> desiredChildren)
		{
			if (container.Any(child => child is null))
			{
				container.Clear();
				for (var i = 0; i < desiredChildren.Count; i++)
					container.Add(desiredChildren[i]);
				return;
			}

			for (var i = 0; i < desiredChildren.Count; i++)
			{
				if (i < container.Count)
				{
					if (!ReferenceEquals(container[i], desiredChildren[i]))
						container[i] = desiredChildren[i];
				}
				else
				{
					container.Add(desiredChildren[i]);
				}
			}

			for (var i = container.Count - 1; i >= desiredChildren.Count; i--)
				container.RemoveAt(i);
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
			
			// Node backend: a fresh hot-reload replacement has never rendered (BuiltView is
			// null) and there is no lazy platform pull — patches are pushed at diff time —
			// so build it now or the retained nodes never receive the replacement's output.
			if (newView.BuiltView is null && oldView.BuiltView?.Node is not null)
				_ = newView.GetView();

			if (newView is NavigationView reconciledNavigation &&
				oldView is NavigationView previousNavigation &&
				!ReferenceEquals(reconciledNavigation, previousNavigation))
				reconciledNavigation.AdoptReconciledOwnerIdentityFrom(previousNavigation);

			// Always diff the built views (the result of Body/Render)
			// This is especially important for Components — their Render() output needs diffing
			if (newView.BuiltView is not null && oldView.BuiltView is not null)
			{
				newView.BuiltView.Diff(oldView.BuiltView,checkRenderers);
			}

			// Reconcile an equivalent NavigationView root before the stack-owning node
			// transfers. This includes ordinary unkeyed positional re-renders, but excludes
			// explicit owner switches and non-equivalent roots.
			if (newView is NavigationView newNavigation &&
				oldView is NavigationView oldNavigation &&
				!ReferenceEquals(newNavigation, oldNavigation) &&
				NavigationStackLifecycle.DetermineOwnerTransfer(
					oldNavigation,
					oldNavigation.GetBackendNavigationStack(),
					newNavigation,
					checkRenderers) == NavigationOwnerTransferAction.PreserveStack)
				ReconcileEquivalentNavigationRoot(
					newNavigation,
					oldNavigation,
					checkRenderers);

			var oldManagedContent = HasBackendManagedContent(oldView);
			var reconcilesManagedContent = ReconcilesBackendManagedContent(oldView);
			if (newView is ContentView ncView && oldView is ContentView ocView)
			{
				if (!oldManagedContent || reconcilesManagedContent)
					ReconcileContentView(
						ncView,
						ocView,
						checkRenderers,
						oldManagedContent);
			}
			//Yes if one is IContainer, the other is too!
			else if ((!oldManagedContent || reconcilesManagedContent) &&
				newView is IContainerView newContainer &&
				oldView is IContainerView oldContainer)
			{
				// The built-in containers reject null additions, but custom IContainerView
				// implementations can surface sparse child lists. The backend child order is
				// defined by materialized (non-null) views, so normalize both sides once before
				// keyed or positional reconciliation.
				var newChildren = NonNullChildren(newContainer);
				var oldChildren = NonNullChildren(oldContainer);
				
				// Check if any children have keys — if so, use key-based reconciliation
				var hasKeys = newChildren.Any(c => !string.IsNullOrEmpty(c.GetKey()));
				
				if (hasKeys)
				{
					// Key-aware reconciliation: build a map of old children by key
					var oldByKey = new Dictionary<string, View>();
					var oldUnkeyed = new List<(int index, View view)>();
					var desiredChildren = new List<View>(newChildren.Count);
					var replacements = new Dictionary<View, View>();
					
					for (var i = 0; i < oldChildren.Count; i++)
					{
						var oldChild = oldChildren[i];
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
						var newChild = newChildren[i];
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
							var retainsLogicalContent =
								RetainsLogicalContentOnOwnerTransfer(matchedOld);
							View mergedChild;
							if (newChild is IComponentWithState || matchedOld is IComponentWithState)
							{
								mergedChild = DiffUpdate(newChild, matchedOld, checkRenderers);
								replacements[matchedOld] = mergedChild;
							}
							else
							{
								mergedChild = ReconcileKeyedPlainView(
									newChild,
									matchedOld,
									checkRenderers);
							}
							if (!ReferenceEquals(mergedChild, matchedOld))
								replacements[matchedOld] = mergedChild;

							desiredChildren.Add(mergedChild);
							DetachRetainedChild(oldContainer, newContainer, mergedChild, matchedOld);
							DetachReplacedBackendManagedChild(
								oldContainer,
								newContainer,
								mergedChild,
								matchedOld,
								retainsLogicalContent);
							continue;
						}

						desiredChildren.Add(newChild);
					}

					if (newContainer is IList<View> mutableContainer)
						ReplaceContainerChildren(mutableContainer, desiredChildren);

					if (oldView.Node is { } parentNode)
					{
						var currentChildren = oldChildren
							.Select(child => replacements.TryGetValue(child, out var replacement)
								? replacement
								: child)
							.ToList();

						for (var desiredIndex = 0; desiredIndex < desiredChildren.Count; desiredIndex++)
						{
							var desiredChild = desiredChildren[desiredIndex];
							var currentIndex = currentChildren.IndexOf(desiredChild);
							if (currentIndex < 0)
							{
								InsertBackendChild(oldView, desiredChild, desiredIndex);
								currentChildren.Insert(desiredIndex, desiredChild);
							}
							else if (currentIndex != desiredIndex)
							{
								parentNode.MoveChild(currentIndex, desiredIndex);
								currentChildren.RemoveAt(currentIndex);
								currentChildren.Insert(desiredIndex, desiredChild);
							}
						}

						for (var removeIndex = currentChildren.Count - 1;
							removeIndex >= desiredChildren.Count;
							removeIndex--)
						{
							RemoveBackendChild(
								oldView,
								oldContainer,
								newContainer,
								currentChildren[removeIndex],
								removeIndex);
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
							var retainsLogicalContent =
								RetainsLogicalContentOnOwnerTransfer(o);
							var merged = DiffUpdate(n, o, checkRenderers);
							
							// CRITICAL FIX: If DiffUpdate returned a different instance (e.g., merged component),
							// update the container to reference the merged instance
							if (merged != n && newContainer is IList<View> mutableContainer)
							{
								DetachMergedChild(oldContainer, newContainer, merged);
								mutableContainer[i] = merged;
							}
							DetachRetainedChild(oldContainer, newContainer, merged, o);
							DetachReplacedBackendManagedChild(
								oldContainer,
								newContainer,
								merged,
								o,
								retainsLogicalContent);
							continue;
						}

						if (i + 1 >= newChildren.Count || i + 1 >= oldChildren.Count)
						{
							if (i >= newChildren.Count)
							{
								for (var removeIndex = oldChildren.Count - 1; removeIndex >= i; removeIndex--)
									RemoveBackendChild(
										oldView,
										oldContainer,
										newContainer,
										oldChildren[removeIndex],
										removeIndex);
							}
							else if (i >= oldChildren.Count)
							{
								for (var insertIndex = i; insertIndex < newChildren.Count; insertIndex++)
									InsertBackendChild(
										oldView,
										newChildren[insertIndex],
										insertIndex);
							}
							else
							{
								ReplaceBackendTail(
									oldView,
									oldContainer,
									newContainer,
									oldChildren,
									newChildren,
									i);
							}
							break;
						}

						//Lets see if the next 2 match
						var o1 = oldChildren.GetViewAtIndex(i + 1);
						var n1 = newChildren.GetViewAtIndex(i + 1);
						if (n1.AreSameType(o1, checkRenderers))
						{
							Debug.WriteLine("The controls were replaced!");
							RemoveBackendChild(
								oldView,
								oldContainer,
								newContainer,
								o,
								i);
							InsertBackendChild(oldView, n, i);
							continue;
						}

						if (n.AreSameType(o1, checkRenderers))
						{
							var retainsLogicalContent =
								RetainsLogicalContentOnOwnerTransfer(o1);
							//we removed one from the old Children and use the next one

							Debug.WriteLine("One control was removed");
							var merged = DiffUpdate(n, o1, checkRenderers);
							
							// CRITICAL FIX: If DiffUpdate returned a different instance (e.g., merged component),
							// update the container to reference the merged instance
							if (merged != n && newContainer is IList<View> mutableContainer)
							{
								DetachMergedChild(oldContainer, newContainer, merged);
								mutableContainer[i] = merged;
								Debug.WriteLine($"Component merge: replaced child at index {i} with merged instance");
							}
							DetachRetainedChild(oldContainer, newContainer, merged, o1);
							DetachReplacedBackendManagedChild(
								oldContainer,
								newContainer,
								merged,
								o1,
								retainsLogicalContent);
							oldView.Node?.RemoveChildAt(i);
							DetachMergedChild(oldContainer, newContainer, o);
							o?.Dispose();
							oldChildren.RemoveAt(i);
							continue;
						}

						if (n1.AreSameType(o, checkRenderers))
						{
							var retainsLogicalContent =
								RetainsLogicalContentOnOwnerTransfer(o);
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
							DetachRetainedChild(oldContainer, newContainer, merged, o);
							DetachReplacedBackendManagedChild(
								oldContainer,
								newContainer,
								merged,
								o,
								retainsLogicalContent);
							InsertBackendChild(oldView, n, i);
							oldChildren.Insert(i, null);
							continue;
						}

						// The remaining declarations no longer have a stable positional
						// alignment. Replace the tail as one generation transition so the
						// native tree and dev registry cannot retain stale siblings while
						// the new full-screen content is already the logical owner.
						ReplaceBackendTail(
							oldView,
							oldContainer,
							newContainer,
							oldChildren,
							newChildren,
							i);
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
				// checkRenderers is the hot-reload flag: ResetView passes isHotReload into
				// Diff (View.cs), and the hot-reload replacement path passes true — every
				// other diff (ordinary Component re-render) is false. Own-content nodes use
				// it to preserve retained state on ordinary re-renders vs re-materialize on
				// a real hot reload.
				newView.UpdateFromOldView(oldView, checkRenderers);
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
