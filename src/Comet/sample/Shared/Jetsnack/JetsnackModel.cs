#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace CometSamples.Jetsnack
{
	// Verbatim port of the gold model (model/Snack.kt, SnackCollection.kt, Filter.kt,
	// Search.kt) — same names, same shape. Image ids become bundled resource NAMES.
	// The gold assigns Random.nextLong() ids; here ids are stable sequential (the gold
	// only needs uniqueness).

	public sealed record Snack(
		long Id,
		string Name,
		string ImageRes,
		long Price,
		string Tagline = "",
		IReadOnlyCollection<string>? Tags = null);

	public sealed record SnackCollection(
		long Id,
		string Name,
		IReadOnlyList<Snack> Snacks,
		CollectionType Type = CollectionType.Normal);

	public enum CollectionType { Normal, Highlight }

	public sealed record OrderLine(Snack Snack, int Count);

	/// <summary>Filter.kt — enabled is mutable UI state (a signal here, matching the
	/// gold's mutableStateOf).</summary>
	public sealed class Filter
	{
		public Filter(string name, bool enabled = false, string? icon = null)
		{
			Name = name;
			Icon = icon;
			Enabled = new Comet.Reactive.Signal<bool>(enabled);
		}

		public string Name { get; }
		public string? Icon { get; }
		public Comet.Reactive.Signal<bool> Enabled { get; }
	}

	public sealed record SearchCategoryCollection(long Id, string Name, IReadOnlyList<SearchCategory> Categories);
	public sealed record SearchCategory(string Name, string ImageRes);
	public sealed record SearchSuggestionGroup(long Id, string Name, IReadOnlyList<string> Suggestions);

	/// <summary>The gold's single formatPrice (ui/utils/Currency.kt) — cents → "$x.yy",
	/// sign-safe (integer / and % both carry the sign in C#).</summary>
	public static class Jetsnack
	{
		public static string FormatPrice(long price)
		{
			long abs = System.Math.Abs(price);
			return $"{(price < 0 ? "-" : "")}${abs / 100}.{abs % 100:00}";
		}
	}

	public static class SnackRepo
	{
		static long _nextId = 1;
		static long NextId() => _nextId++;

		static Snack S(string name, string image, long price, string tagline = "") =>
			new(NextId(), name, image, price, tagline);

		/// <summary>model/Snack.kt `snacks` — order matters (collections slice by index).</summary>
		public static readonly IReadOnlyList<Snack> Snacks = new[]
		{
			S("Cupcake", "cupcake", 299, "A tag line"),
			S("Donut", "donut", 299, "A tag line"),
			S("Eclair", "eclair", 299, "A tag line"),
			S("Froyo", "froyo", 299, "A tag line"),
			S("Gingerbread", "gingerbread", 499, "A tag line"),
			S("Honeycomb", "honeycomb", 299, "A tag line"),
			S("Ice Cream Sandwich", "ice_cream_sandwich", 1299, "A tag line"),
			S("Jellybean", "jelly_bean", 299, "A tag line"),
			S("KitKat", "kitkat", 549, "A tag line"),
			S("Lollipop", "lollipop", 299, "A tag line"),
			S("Marshmallow", "marshmallow", 299, "A tag line"),
			S("Nougat", "nougat", 299, "A tag line"),
			S("Oreo", "oreo", 299, "A tag line"),
			S("Pie", "pie", 299, "A tag line"),
			S("Chips", "chips", 299),
			S("Pretzels", "pretzels", 299),
			S("Smoothies", "smoothies", 299),
			S("Popcorn", "popcorn", 299),
			S("Almonds", "almonds", 299),
			S("Cheese", "cheese", 299),
			S("Apples", "apples", 299, "A tag line"),
			S("Apple sauce", "apple_sauce", 299, "A tag line"),
			S("Apple chips", "apple_chips", 299, "A tag line"),
			S("Apple juice", "apple_juice", 299, "A tag line"),
			S("Apple pie", "apple_pie", 299, "A tag line"),
			S("Grapes", "grapes", 299, "A tag line"),
			S("Kiwi", "kiwi", 299, "A tag line"),
			S("Mango", "mango", 299, "A tag line"),
		};

		// SnackCollection.kt — tastyTreats/popular + renamed copies.
		static readonly SnackCollection TastyTreats = new(NextId(), "Android's picks",
			Snacks.Take(13).ToArray(), CollectionType.Highlight);
		static readonly SnackCollection Popular = new(NextId(), "Popular on Jetsnack",
			Snacks.Skip(14).Take(5).ToArray());
		static readonly SnackCollection WfhFavs = TastyTreats with { Id = NextId(), Name = "WFH favourites" };
		static readonly SnackCollection NewlyAdded = Popular with { Id = NextId(), Name = "Newly Added" };
		static readonly SnackCollection Exclusive = TastyTreats with { Id = NextId(), Name = "Only on Jetsnack" };
		static readonly SnackCollection Also = TastyTreats with { Id = NextId(), Name = "Customers also bought" };
		static readonly SnackCollection InspiredByCartCollection = TastyTreats with { Id = NextId(), Name = "Inspired by your cart" };

		public static IReadOnlyList<SnackCollection> GetSnacks() => new[]
		{
			TastyTreats, Popular, WfhFavs, NewlyAdded, Exclusive,
		};

		public static Snack GetSnack(long snackId) => Snacks.First(s => s.Id == snackId);

		public static IReadOnlyList<SnackCollection> GetRelated(long snackId) => new[]
		{
			Also with { Id = 900 }, Popular with { Id = 901 },
		};

		public static SnackCollection GetInspiredByCart() => InspiredByCartCollection;

		public static IReadOnlyList<OrderLine> GetCart() => new[]
		{
			new OrderLine(Snacks[4], 2),
			new OrderLine(Snacks[6], 3),
			new OrderLine(Snacks[8], 1),
		};

		// Filter.kt
		public static readonly IReadOnlyList<Filter> Filters = new[]
		{
			new Filter("Organic"), new Filter("Gluten-free"), new Filter("Dairy-free"),
			new Filter("Sweet"), new Filter("Savory"),
		};
		public static readonly IReadOnlyList<Filter> PriceFilters = new[]
		{
			new Filter("$"), new Filter("$$"), new Filter("$$$"), new Filter("$$$$"),
		};
		public static readonly IReadOnlyList<Filter> SortFilters = new[]
		{
			new Filter("Android's favorite (default)", icon: "android"),
			new Filter("Rating", icon: "star"),
			new Filter("Alphabetical", icon: "sort_by_alpha"),
		};
		public static readonly IReadOnlyList<Filter> CategoryFilters = new[]
		{
			new Filter("Chips & crackers"), new Filter("Fruit snacks"),
			new Filter("Desserts"), new Filter("Nuts"),
		};
		public static readonly IReadOnlyList<Filter> LifeStyleFilters = new[]
		{
			new Filter("Organic"), new Filter("Gluten-free"), new Filter("Dairy-free"),
			new Filter("Sweet"), new Filter("Savory"),
		};
		public static string SortDefault => SortFilters[0].Name;
	}

	public static class SearchRepo
	{
		public static IReadOnlyList<SearchCategoryCollection> GetCategories() => new[]
		{
			new SearchCategoryCollection(0, "Categories", new[]
			{
				new SearchCategory("Chips & crackers", "chips"),
				new SearchCategory("Fruit snacks", "fruit"),
				new SearchCategory("Desserts", "desserts"),
				new SearchCategory("Nuts", "nuts"),
			}),
			new SearchCategoryCollection(1, "Lifestyles", new[]
			{
				new SearchCategory("Organic", "organic"),
				new SearchCategory("Gluten Free", "gluten_free"),
				new SearchCategory("Paleo", "paleo"),
				new SearchCategory("Vegan", "vegan"),
				new SearchCategory("Vegetarian", "vegetarian"),
				new SearchCategory("Whole30", "whole30"),
			}),
		};

		public static IReadOnlyList<Snack> Search(string query) =>
			SnackRepo.Snacks.Where(s => s.Name.Contains(query, System.StringComparison.OrdinalIgnoreCase)).ToArray();
	}
}
