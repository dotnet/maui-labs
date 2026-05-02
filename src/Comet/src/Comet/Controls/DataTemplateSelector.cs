using System;

namespace Comet
{
	/// <summary>
	/// Base class for selecting different view templates based on item data.
	/// Subclass this to create reusable template selectors.
	/// </summary>
	public abstract class DataTemplateSelector
	{
		/// <summary>
		/// Selects a view template for the given item.
		/// </summary>
		public View SelectTemplate(object item) => OnSelectTemplate(item);

		/// <summary>
		/// Override to provide custom template selection logic.
		/// </summary>
		protected abstract View OnSelectTemplate(object item);
	}

	/// <summary>
	/// Strongly-typed template selector for use with ListView&lt;T&gt; and CollectionView&lt;T&gt;.
	/// </summary>
	/// <example>
	/// <code>
	/// public class AnimalTemplateSelector : DataTemplateSelector&lt;Animal&gt;
	/// {
	///     protected override View OnSelectTemplate(Animal item)
	///     {
	///         return item switch {
	///             Dog d => new DogView(d),
	///             Cat c => new CatView(c),
	///             _ => new DefaultAnimalView(item)
	///         };
	///     }
	/// }
	/// </code>
	/// </example>
	public abstract class DataTemplateSelector<T> : DataTemplateSelector
	{
		protected sealed override View OnSelectTemplate(object item)
		{
			if (item is T typed)
				return OnSelectTemplate(typed);
			throw new ArgumentException($"DataTemplateSelector<{typeof(T).Name}> received item of type {item?.GetType().Name ?? "null"}");
		}

		/// <summary>
		/// Override to provide custom template selection logic for items of type <typeparamref name="T"/>.
		/// </summary>
		protected abstract View OnSelectTemplate(T item);

		/// <summary>
		/// Converts this selector to a <see cref="Func{T, View}"/> for use with ViewFor.
		/// </summary>
		public Func<T, View> AsFunc() => item => OnSelectTemplate(item);

		public static implicit operator Func<T, View>(DataTemplateSelector<T> selector) =>
			selector.AsFunc();
	}

	public static class DataTemplateSelectorExtensions
	{
		/// <summary>
		/// Sets the item template using a <see cref="DataTemplateSelector{T}"/>.
		/// </summary>
		public static TListView ItemTemplateSelector<TListView, T>(this TListView listView, DataTemplateSelector<T> selector)
			where TListView : ListView<T>
		{
			listView.ViewFor = selector.AsFunc();
			return listView;
		}

		/// <summary>
		/// Sets the item template using a selector function.
		/// This is the functional MVU-style alternative to subclassing DataTemplateSelector.
		/// </summary>
		/// <example>
		/// <code>
		/// new CollectionView&lt;Animal&gt;(animals)
		///     .ItemTemplate(animal => animal switch {
		///         Dog d => new DogView(d),
		///         Cat c => new CatView(c),
		///         _ => new DefaultAnimalView(animal)
		///     });
		/// </code>
		/// </example>
		public static TListView ItemTemplate<TListView, T>(this TListView listView, Func<T, View> template)
			where TListView : ListView<T>
		{
			listView.ViewFor = template;
			return listView;
		}
	}
}
