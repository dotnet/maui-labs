namespace CometBaristaNotes.Services;

public class RouteInfo
{
	public string Route { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Description { get; set; } = "";
	public string[] Keywords { get; set; } = Array.Empty<string>();
	public bool RequiresParameter { get; set; }
	public string? ParameterName { get; set; }
}

public interface INavigationRegistry
{
	IReadOnlyList<RouteInfo> GetAllRoutes();
	RouteInfo? FindRoute(string keyword);
	Task NavigateToRoute(string route, Dictionary<string, object>? parameters = null);
}
