using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Comet
{
	public class CometShell : View
	{
		private static CometShell _current;
		private static readonly Dictionary<string, Type> _routes = new Dictionary<string, Type>(StringComparer.Ordinal);
		private static readonly Dictionary<Type, string> _typedRoutes = new Dictionary<Type, string>();
		private readonly Stack<string> _navigationStack = new Stack<string>();

		public static CometShell Current
		{
			get => _current;
			set => _current = value;
		}

		public List<ShellItem> Items { get; set; } = new List<ShellItem>();
		public ShellItem CurrentItem { get; set; }
		public ShellItem FlyoutHeader { get; set; }
		public bool FlyoutIsPresented { get; set; }
		public SearchHandler SearchHandler { get; set; }

		public CometShell(params ShellItem[] items)
		{
			_current = this;
			if (items is null)
				return;

			foreach (var item in items)
			{
				AddItem(item);
			}
		}

		/// <summary>
		/// Sets the search handler for the current Shell instance.
		/// </summary>
		public static void SetSearchHandler(SearchHandler handler)
		{
			if (Current is not null)
				Current.SearchHandler = handler;
		}

		/// <summary>
		/// Gets the search handler from the current Shell instance.
		/// </summary>
		public static SearchHandler GetSearchHandler() => Current?.SearchHandler;

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_current == this)
					_current = null;
				_navigationStack.Clear();
			}
			base.Dispose(disposing);
		}

		public static void RegisterRoute(string route, Type type)
		{
			if (string.IsNullOrWhiteSpace(route))
				throw new ArgumentException("Route cannot be null or empty.", nameof(route));

			if (type is null)
				throw new ArgumentNullException(nameof(type));

			if (!typeof(View).IsAssignableFrom(type))
				throw new ArgumentException($"Route type must inherit from View. Type: {type.Name}");

			_routes[route] = type;
			_typedRoutes[type] = route;
		}

		public static void RegisterRoute<TView>(string route) where TView : View
			=> RegisterRoute(route, typeof(TView));

		public static void UnregisterRoute(string route)
		{
			if (string.IsNullOrWhiteSpace(route))
				return;

			if (_routes.TryGetValue(route, out var type))
			{
				_routes.Remove(route);
				if (_typedRoutes.TryGetValue(type, out var registeredRoute) && string.Equals(registeredRoute, route, StringComparison.Ordinal))
					_typedRoutes.Remove(type);
			}
		}

		public static bool HasRoute(string route) => _routes.ContainsKey(route);
		public static bool HasRoute<TView>() where TView : View => _typedRoutes.ContainsKey(typeof(TView));
		public static string GetRoute<TView>() where TView : View => ResolveRoute(typeof(TView));

		public static Dictionary<string, string> ParseQueryString(string route)
		{
			var parts = route.Split('?');
			var queryParams = new Dictionary<string, string>();
			if (parts.Length > 1)
			{
				foreach (var param in parts[1].Split('&'))
				{
					var keyValue = param.Split('=');
					if (keyValue.Length == 2)
						queryParams[Uri.UnescapeDataString(keyValue[0])] = Uri.UnescapeDataString(keyValue[1]);
				}
			}
			return queryParams;
		}

		internal static bool TryGetRoute(Type type, out string route)
			=> _typedRoutes.TryGetValue(type, out route);

		static string ResolveRoute(Type type)
		{
			if (type is null)
				throw new ArgumentNullException(nameof(type));

			if (_typedRoutes.TryGetValue(type, out var route))
				return route;

			throw new InvalidOperationException($"Route for '{type.Name}' is not registered. Use CometShell.RegisterRoute<{type.Name}>(\"route\") first.");
		}

		public CometShell AddItem(ShellItem item)
		{
			if (item is null)
				return this;

			Items.Add(item);
			CurrentItem ??= item;
			return this;
		}

		public CometShell AddItem(string title, Action<ShellItem> configure)
		{
			var item = new ShellItem(title);
			configure?.Invoke(item);
			return AddItem(item);
		}

		public CometShell WithCurrentItem(ShellItem item)
		{
			CurrentItem = item;
			return this;
		}

		public CometShell WithFlyoutHeader(ShellItem header)
		{
			FlyoutHeader = header;
			return this;
		}

		public CometShell WithSearchHandler(SearchHandler handler)
		{
			SearchHandler = handler;
			return this;
		}

		public CometShell ShowFlyout(bool isPresented = true)
		{
			FlyoutIsPresented = isPresented;
			return this;
		}

		public Task GoToAsync<TView>() where TView : View
			=> GoToAsync<TView>(parameters: null);

		public Task GoToAsync<TView>(object parameters) where TView : View
		{
			var route = NavigationParameterHelper.BuildRoute(ResolveRoute(typeof(TView)), parameters);
			return GoToAsync(route, parameters);
		}

		public Task GoToAsync<TView, TParameters>(TParameters parameters) where TView : View
			=> GoToAsync<TView>((object)parameters);

		public Task GoToAsync(string route)
			=> GoToAsync(route, parameters: null);

		async Task GoToAsync(string route, object parameters)
		{
			// Handle back navigation
			if (route == "..")
			{
				if (_navigationStack.Count > 0)
				{
					_navigationStack.Pop();
					// Trigger navigation back
					await NavigateBack();
				}
				return;
			}

			// Parse query parameters
			var (routePath, queryParams) = ParseRouteInternal(route);

			if (!_routes.TryGetValue(routePath, out var pageType))
			{
				throw new InvalidOperationException($"Route '{routePath}' is not registered. Use CometShell.RegisterRoute() to register it.");
			}

			var page = CreateView(pageType, queryParams, parameters);

			_navigationStack.Push(routePath);

			// Perform the actual navigation
			await NavigateTo(page);
		}

		internal (string route, Dictionary<string, string> queryParams) ParseRouteInternal(string route)
		{
			var parts = route.Split('?');
			var routePath = parts[0];
			var queryParams = new Dictionary<string, string>();

			if (parts.Length > 1)
			{
				var query = parts[1];
				foreach (var param in query.Split('&'))
				{
					var keyValue = param.Split('=');
					if (keyValue.Length == 2)
					{
						queryParams[Uri.UnescapeDataString(keyValue[0])] = Uri.UnescapeDataString(keyValue[1]);
					}
				}
			}

			return (routePath, queryParams);
		}

		static View CreateView(Type pageType, Dictionary<string, string> queryParams, object parameters)
		{
			var page = Activator.CreateInstance(pageType) as View;
			if (page is null)
				throw new InvalidOperationException($"Failed to create instance of {pageType.Name}");

			NavigationParameterHelper.Apply(page, parameters, queryParams);
			return page;
		}

		private async Task NavigateTo(View page)
		{
			// Use existing navigation infrastructure
			if (Navigation is not null)
			{
				Navigation.Navigate(page);
			}
			else
			{
				// Fallback to modal presentation
				ModalView.Present(page);
			}

			await Task.CompletedTask;
		}

		private async Task NavigateBack()
		{
			if (Navigation is not null)
			{
				Navigation.Pop();
			}
			else
			{
				ModalView.Dismiss();
			}

			await Task.CompletedTask;
		}
	}

	public interface IQueryAttributable
	{
		void ApplyQueryAttributes(Dictionary<string, string> query);
	}

	public class ShellItem
	{
		public ShellItem()
		{
		}

		public ShellItem(string title, params ShellSection[] sections)
			: this(title, null, sections)
		{
		}

		public ShellItem(string title, string route, params ShellSection[] sections)
		{
			Title = title;
			Route = route;
			if (sections is null)
				return;

			foreach (var section in sections)
			{
				AddSection(section);
			}
		}

		public string Title { get; set; }
		public string Route { get; set; }
		public List<ShellSection> Items { get; set; } = new List<ShellSection>();
		public View Icon { get; set; }

		public ShellItem WithRoute(string route)
		{
			Route = route;
			return this;
		}

		public ShellItem WithIcon(View icon)
		{
			Icon = icon;
			return this;
		}

		public ShellItem AddSection(ShellSection section)
		{
			if (section is not null)
				Items.Add(section);
			return this;
		}

		public ShellItem AddSection(string title, Action<ShellSection> configure)
		{
			var section = new ShellSection(title);
			configure?.Invoke(section);
			return AddSection(section);
		}
	}

	public class ShellSection
	{
		public ShellSection()
		{
		}

		public ShellSection(string title, params ShellContent[] content)
			: this(title, null, content)
		{
		}

		public ShellSection(string title, string route, params ShellContent[] content)
		{
			Title = title;
			Route = route;
			if (content is null)
				return;

			foreach (var item in content)
			{
				AddContent(item);
			}
		}

		public string Title { get; set; }
		public string Route { get; set; }
		public List<ShellContent> Items { get; set; } = new List<ShellContent>();
		public View Icon { get; set; }

		public ShellSection WithRoute(string route)
		{
			Route = route;
			return this;
		}

		public ShellSection WithIcon(View icon)
		{
			Icon = icon;
			return this;
		}

		public ShellSection AddContent(ShellContent content)
		{
			if (content is not null)
				Items.Add(content);
			return this;
		}

		public ShellSection AddContent(string title, Action<ShellContent> configure)
		{
			var content = new ShellContent(title);
			configure?.Invoke(content);
			return AddContent(content);
		}

		public ShellSection AddContent<TView>(string title = null, string route = null) where TView : View, new()
			=> AddContent(ShellContent.Create<TView>(title, route));
	}

	public class ShellContent
	{
		public ShellContent()
		{
		}

		public ShellContent(string title)
		{
			Title = title;
		}

		public ShellContent(string title, View content)
			: this(title, null, content)
		{
		}

		public ShellContent(string title, Func<View> contentTemplate)
			: this(title, null, contentTemplate)
		{
		}

		public ShellContent(string title, string route, View content)
			: this(title)
		{
			Route = route;
			WithContent(content);
		}

		public ShellContent(string title, string route, Func<View> contentTemplate)
			: this(title)
		{
			Route = route;
			WithContent(contentTemplate);
		}

		public string Title { get; set; }
		public string Route { get; set; }
		public View Content { get; set; }
		public Func<View> ContentTemplate { get; set; }
		public View Icon { get; set; }

		public ShellContent WithRoute(string route)
		{
			Route = route;
			return this;
		}

		public ShellContent WithIcon(View icon)
		{
			Icon = icon;
			return this;
		}

		public ShellContent WithContent(View content)
		{
			Content = content;
			ContentTemplate = null;
			return this;
		}

		public ShellContent WithContent(Func<View> contentTemplate)
		{
			ContentTemplate = contentTemplate;
			Content = null;
			return this;
		}

		public ShellContent WithContent<TView>() where TView : View, new()
		{
			if (string.IsNullOrWhiteSpace(Route) && CometShell.TryGetRoute(typeof(TView), out var route))
				Route = route;

			return WithContent(() => new TView());
		}

		public static ShellContent Create<TView>(string title = null, string route = null) where TView : View, new()
		{
			var content = new ShellContent(title ?? typeof(TView).Name, route, () => new TView());
			if (string.IsNullOrWhiteSpace(content.Route) && CometShell.TryGetRoute(typeof(TView), out var registeredRoute))
				content.Route = registeredRoute;
			return content;
		}

		public View GetContent()
		{
			return Content ?? ContentTemplate?.Invoke();
		}
	}

	public static class ShellExtensions
	{
		static CometShell GetCurrentShell()
		{
			if (CometShell.Current is not null)
				return CometShell.Current;

			throw new InvalidOperationException("No Shell instance is currently active. Ensure a CometShell is set as Current.");
		}

		public static Task GoToAsync(this View view, string route)
			=> GetCurrentShell().GoToAsync(route);

		public static Task GoToAsync<TView>(this View view) where TView : View
			=> GetCurrentShell().GoToAsync<TView>();

		public static Task GoToAsync<TView>(this View view, object parameters) where TView : View
			=> GetCurrentShell().GoToAsync<TView>(parameters);

		public static Task GoToAsync<TView, TParameters>(this View view, TParameters parameters) where TView : View
			=> GetCurrentShell().GoToAsync<TView, TParameters>(parameters);

		public static Task GoBackAsync(this View view)
			=> GetCurrentShell().GoToAsync("..");

		/// <summary>
		/// Sets the back button behavior for a view in Shell navigation.
		/// </summary>
		public static T BackButtonBehavior<T>(this T view, BackButtonBehavior behavior) where T : View
		{
			view.SetEnvironment(nameof(BackButtonBehavior), behavior, false);
			return view;
		}

		/// <summary>
		/// Gets the back button behavior for a view.
		/// </summary>
		public static BackButtonBehavior GetBackButtonBehavior(this View view) =>
			view.GetEnvironment<BackButtonBehavior>(nameof(BackButtonBehavior), false);
	}
}
