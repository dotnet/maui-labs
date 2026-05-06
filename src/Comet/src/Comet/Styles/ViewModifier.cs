using System;

namespace Comet.Styles
{
	/// <summary>
	/// A reusable, composable set of view modifications.
	/// Analogous to SwiftUI's ViewModifier protocol.
	/// </summary>
	public abstract class ViewModifier
	{
		/// <summary>
		/// A no-op modifier that does nothing when applied.
		/// </summary>
		public static readonly ViewModifier Empty = new EmptyModifier();

		/// <summary>
		/// Apply this modifier's properties to the given view.
		/// Implementations call fluent methods on the view.
		/// </summary>
		public abstract View Apply(View view);

		sealed class EmptyModifier : ViewModifier
		{
			public override View Apply(View view) => view;
		}
	}

	/// <summary>
	/// Typed variant for control-specific modifiers.
	/// Only applies to views of type <typeparamref name="T"/>.
	/// </summary>
	public abstract class ViewModifier<T> : ViewModifier where T : View
	{
		public sealed override View Apply(View view)
		{
			if (view is T typed)
				return Apply(typed);
			return view;
		}

		/// <summary>
		/// Apply this modifier's properties to a view of type T.
		/// </summary>
		public abstract T Apply(T view);
	}

	/// <summary>
	/// A modifier that applies two modifiers in sequence.
	/// Created by <see cref="ViewModifierComposition.Then"/>.
	/// </summary>
	public sealed class ComposedModifier : ViewModifier
	{
		readonly ViewModifier _first;
		readonly ViewModifier _second;

		public ComposedModifier(ViewModifier first, ViewModifier second)
		{
			_first = first ?? throw new ArgumentNullException(nameof(first));
			_second = second ?? throw new ArgumentNullException(nameof(second));
		}

		public override View Apply(View view)
		{
			_first.Apply(view);
			_second.Apply(view);
			return view;
		}
	}

	/// <summary>
	/// Composition support for ViewModifier — creates composed chains via Then().
	/// </summary>
	public static class ViewModifierComposition
	{
		/// <summary>
		/// Creates a new modifier that applies <paramref name="first"/>, then <paramref name="second"/>.
		/// </summary>
		public static ComposedModifier Then(this ViewModifier first, ViewModifier second)
			=> new ComposedModifier(first, second);
	}

	/// <summary>
	/// Extension methods for applying ViewModifiers to views.
	/// </summary>
	public static class ViewModifierExtensions
	{
		/// <summary>
		/// Applies a ViewModifier to any view.
		/// </summary>
		public static T Modifier<T>(this T view, ViewModifier modifier) where T : View
		{
			modifier.Apply(view);
			return view;
		}

		/// <summary>
		/// Applies multiple modifiers in order (left to right).
		/// </summary>
		public static T Modifier<T>(this T view, params ViewModifier[] modifiers) where T : View
		{
			foreach (var m in modifiers)
				m.Apply(view);
			return view;
		}
	}
}
