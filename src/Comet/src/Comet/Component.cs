using System;
using System.Diagnostics;
using Comet.Reactive;
using Microsoft.Maui;

namespace Comet
{
	/// <summary>
	/// Base class for MauiReactor-style components that use a Render() method
	/// instead of the [Body] attribute. Extends View so it participates in the
	/// existing Comet lifecycle (handlers, hot reload, diffing, etc.).
	/// </summary>
	public abstract class Component : View, IComponentWithState
	{
		bool _mounted;

		protected Component()
		{
			// Wire Body to call Render so the existing View pipeline
			// (GetRenderView → Body.Invoke → diff) works unchanged.
			Body = () => Render();
		}

		/// <summary>
		/// Return the view tree for this component. Called on every render cycle.
		/// </summary>
		public abstract View Render();

		/// <summary>
		/// Called once after the component's handler is first set (i.e., the
		/// component becomes part of the live visual tree).
		/// </summary>
		protected virtual void OnMounted() { }

		/// <summary>
		/// Called when the component is being disposed / removed from the tree.
		/// </summary>
		protected virtual void OnWillUnmount() { }

		protected override void OnLoaded()
		{
			base.OnLoaded();
			if (!_mounted)
			{
				_mounted = true;
				OnMounted();
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _mounted)
			{
				_mounted = false;
				OnWillUnmount();
			}
			base.Dispose(disposing);
		}

		// -- IComponentWithState (no state, just marker interface for diffing) --

		public virtual object GetStateObject() => null;

		public virtual void TransferStateFrom(IComponentWithState source)
		{
			// Base Component has no state to transfer
		}

		protected override void TransferHotReloadStateToCore(View newView)
		{
			base.TransferHotReloadStateToCore(newView);
			if (newView is IComponentWithState newComponent &&
				this is IComponentWithState currentComponent)
			{
				newComponent.TransferStateFrom(currentComponent);
			}
		}
	}

	/// <summary>
	/// Component with a typed state object. Provides SetState() for batched
	/// mutations that trigger a single re-render, similar to MauiReactor's pattern.
	/// </summary>
	/// <typeparam name="TState">
	/// A plain class with a parameterless constructor. Properties are the
	/// component's mutable data. Does NOT need to extend BindingObject.
	/// </typeparam>
	public abstract class Component<TState> : Component, IComponentWithState
		where TState : class, new()
	{
		TState _state;

		/// <summary>
		/// The component's typed state object. Initialized lazily on first access.
		/// Hides View.State (BindingState) — the typed state is the public API for Components.
		/// </summary>
		public new TState State => _state ??= new TState();

		/// <summary>
		/// Mutate state and trigger a re-render. Safe to call from any thread.
		/// Multiple mutations within a single SetState are batched into one render pass
		/// via <see cref="ReactiveScheduler"/>.
		/// </summary>
		protected void SetState(Action<TState> mutator)
		{
			if (mutator is null)
				throw new ArgumentNullException(nameof(mutator));

			// Ensure state is initialized
			var state = State;

			mutator(state);

			// Let the scheduler coalesce and dispatch the rebuild on the main thread.
			ReactiveScheduler.MarkViewDirty(this);
		}

		/// <summary>
		/// Merge state from an old Component instance during reconciliation.
		/// Called when parent re-renders and the old and new instances are the same Component type.
		/// </summary>
		internal void MergeStateFrom(Component<TState> oldComponent)
		{
			if (oldComponent?._state is not null)
			{
				_state = oldComponent._state;
			}
		}

		// -- IComponentWithState --

		public override object GetStateObject() => _state;

		public override void TransferStateFrom(IComponentWithState source)
		{
			if (source is Component<TState> typed && typed._state is not null)
			{
				_state = typed._state;
			}
		}

		// -- Hot reload integration --

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
		}
	}

	/// <summary>
	/// Component with both state and props. Props are set by the parent component
	/// and flow top-down; state is internal and managed via SetState().
	/// </summary>
	/// <typeparam name="TState">Internal mutable state class.</typeparam>
	/// <typeparam name="TProps">
	/// Parent-supplied props class. Should be treated as read-only by the component.
	/// </typeparam>
	public abstract class Component<TState, TProps> : Component<TState>, IComponentWithState
		where TState : class, new()
		where TProps : class, new()
	{
		TProps _props;

		/// <summary>
		/// Props supplied by the parent. Assigning new Props triggers a re-render.
		/// </summary>
		public TProps Props
		{
			get => _props ??= new TProps();
			set
			{
				_props = value ?? new TProps();
				// Props changed — re-render
				ThreadHelper.RunOnMainThread(() => Reload());
			}
		}

		/// <summary>
		/// Update props from a new Component instance during reconciliation.
		/// Called internally by the diff algorithm when merging same-type Components.
		/// The Component will re-render naturally as part of the diff cycle.
		/// </summary>
		internal void UpdatePropsFromDiff(TProps newProps)
		{
			if (newProps is null)
				newProps = new TProps();

			// Just update the props reference — don't trigger Reload here
			// The diff cycle is already handling the re-render
			_props = newProps;
		}

		/// <summary>
		/// Determines if the component should re-render when props change.
		/// Override to implement custom prop comparison logic for performance optimization.
		/// Default: always returns true (re-render on any props update).
		/// </summary>
		/// <param name="oldProps">Previous props (may be null if not initialized).</param>
		/// <param name="newProps">New props being applied.</param>
		/// <returns>True if component should re-render, false to skip re-render.</returns>
		protected virtual bool ShouldUpdate(TProps oldProps, TProps newProps)
		{
			// Default: always re-render when props change
			// Subclasses can override for fine-grained control
			return true;
		}

		// -- IComponentWithState (extend base to also transfer props) --

		public override void TransferStateFrom(IComponentWithState source)
		{
			base.TransferStateFrom(source);
			if (source is Component<TState, TProps> typed && typed._props is not null)
			{
				_props = typed._props;
			}
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
		}
	}
}
