namespace CometBaristaNotes.Services;

public class NavigationRegistry : INavigationRegistry
{
	private readonly List<RouteInfo> _routes;

	public NavigationRegistry()
	{
		_routes = new List<RouteInfo>
		{
			new RouteInfo
			{
				Route = "activity",
				DisplayName = "Activity Feed",
				Description = "View recent brewing activity and shot history",
				Keywords = new[] { "activity", "feed", "recent", "history", "log" }
			},
			new RouteInfo
			{
				Route = "beans",
				DisplayName = "Bean Management",
				Description = "Browse and manage your coffee bean collection",
				Keywords = new[] { "beans", "coffee", "roast", "collection", "manage" }
			},
			new RouteInfo
			{
				Route = "bean-detail",
				DisplayName = "Bean Detail",
				Description = "View details for a specific coffee bean",
				Keywords = new[] { "bean", "detail", "info", "roast" },
				RequiresParameter = true,
				ParameterName = "beanId"
			},
			new RouteInfo
			{
				Route = "bag-detail",
				DisplayName = "Bag Detail",
				Description = "View details for a specific bag of coffee",
				Keywords = new[] { "bag", "detail", "purchase" },
				RequiresParameter = true,
				ParameterName = "bagId"
			},
			new RouteInfo
			{
				Route = "newshot",
				DisplayName = "Log New Shot",
				Description = "Log a new espresso shot",
				Keywords = new[] { "shot", "log", "new", "espresso", "brew", "pull" }
			},
			new RouteInfo
			{
				Route = "shot-detail",
				DisplayName = "Shot Detail",
				Description = "View details for a specific shot",
				Keywords = new[] { "shot", "detail", "view" },
				RequiresParameter = true,
				ParameterName = "shotId"
			},
			new RouteInfo
			{
				Route = "shot-edit",
				DisplayName = "Edit Shot",
				Description = "Edit an existing shot record",
				Keywords = new[] { "shot", "edit", "modify", "update" },
				RequiresParameter = true,
				ParameterName = "shotId"
			},
			new RouteInfo
			{
				Route = "equipment",
				DisplayName = "Equipment Management",
				Description = "Browse and manage your coffee equipment",
				Keywords = new[] { "equipment", "gear", "grinder", "machine", "tools" }
			},
			new RouteInfo
			{
				Route = "equipment-detail",
				DisplayName = "Equipment Detail",
				Description = "View details for a specific piece of equipment",
				Keywords = new[] { "equipment", "detail", "gear" },
				RequiresParameter = true,
				ParameterName = "equipmentId"
			},
			new RouteInfo
			{
				Route = "settings",
				DisplayName = "Settings",
				Description = "App settings and preferences",
				Keywords = new[] { "settings", "preferences", "options", "config" }
			},
			new RouteInfo
			{
				Route = "profiles",
				DisplayName = "User Profiles",
				Description = "Manage user profiles",
				Keywords = new[] { "profiles", "users", "people", "account" }
			},
			new RouteInfo
			{
				Route = "profile-form",
				DisplayName = "Profile Form",
				Description = "Create or edit a user profile",
				Keywords = new[] { "profile", "form", "create", "edit", "user" },
				RequiresParameter = true,
				ParameterName = "profileId"
			}
		};
	}

	public IReadOnlyList<RouteInfo> GetAllRoutes() => _routes.AsReadOnly();

	public RouteInfo? FindRoute(string keyword)
	{
		if (string.IsNullOrWhiteSpace(keyword))
			return null;

		var lower = keyword.ToLowerInvariant();

		// Exact route match first
		var exact = _routes.FirstOrDefault(r =>
			r.Route.Equals(lower, StringComparison.OrdinalIgnoreCase));
		if (exact != null)
			return exact;

		// Keyword contains match
		return _routes.FirstOrDefault(r =>
			r.Keywords.Any(k => k.Contains(lower, StringComparison.OrdinalIgnoreCase)
				|| lower.Contains(k, StringComparison.OrdinalIgnoreCase)));
	}

	public async Task NavigateToRoute(string route, Dictionary<string, object>? parameters = null)
	{
		// Navigation via route strings is not supported in pure CometApp mode.
		// Voice command navigation will be implemented using direct view navigation.
		await Task.CompletedTask;
	}
}
