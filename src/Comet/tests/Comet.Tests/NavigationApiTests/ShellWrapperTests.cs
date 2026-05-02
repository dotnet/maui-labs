using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using static Comet.CometControls;

namespace Comet.Tests
{
	public class ShellWrapperTests : TestBase
	{
		const string BasicRoute = "phase5-shell-wrapper/basic";
		const string QueryRoute = "phase5-shell-wrapper/query";
		const string ExtensionRoute = "phase5-shell-wrapper/extension";

		class TrackingShellPage : View
		{
			public static TrackingShellPage LastCreated { get; private set; }

			public TrackingShellPage()
			{
				LastCreated = this;
			}

			public static void Reset()
			{
				LastCreated = null;
			}
		}

		class QueryTrackingShellPage : View, IQueryAttributable
		{
			readonly Dictionary<string, string> receivedQuery = new Dictionary<string, string>();

			public static QueryTrackingShellPage LastCreated { get; private set; }

			public IReadOnlyDictionary<string, string> ReceivedQuery => receivedQuery;

			public QueryTrackingShellPage()
			{
				LastCreated = this;
			}

			public void ApplyQueryAttributes(Dictionary<string, string> query)
			{
				receivedQuery.Clear();
				foreach (var pair in query)
					receivedQuery[pair.Key] = pair.Value;
			}

			public static void Reset()
			{
				LastCreated = null;
			}
		}

		static void ResetShellStatics(params string[] routes)
		{
			foreach (var route in routes)
				CometShell.UnregisterRoute(route);

			CometShell.Current = null;
			ModalView.ClearDelegates();
			TrackingShellPage.Reset();
			QueryTrackingShellPage.Reset();
		}

		[Fact]
		public void ShellCurrentTracksActiveShellLifetime()
		{
			ResetShellStatics(BasicRoute, QueryRoute, ExtensionRoute);

			var shell = new CometShell();
			Assert.Same(shell, CometShell.Current);

			shell.Dispose();

			Assert.Null(CometShell.Current);
		}

		[Fact]
		public void RegisterRouteRejectsTypesThatAreNotViews()
		{
			ResetShellStatics(BasicRoute);

			var exception = Assert.Throws<ArgumentException>(() => CometShell.RegisterRoute(BasicRoute, typeof(string)));

			Assert.Contains("inherit from View", exception.Message);
			Assert.False(CometShell.HasRoute(BasicRoute));
		}

		[Fact]
		public void ShellWrapperParsesRouteAndQueryParameters()
		{
			ResetShellStatics(BasicRoute);
			var shell = new CometShell();

			try
			{
				var (route, query) = shell.ParseRouteInternal("orders/details?id=42&name=Bob%20Smith");

				Assert.Equal("orders/details", route);
				Assert.Equal("42", query["id"]);
				Assert.Equal("Bob Smith", query["name"]);
			}
			finally
			{
				shell.Dispose();
				ResetShellStatics(BasicRoute);
			}
		}

		[Fact]
		public async Task ShellWrapperUsesModalFallbackWhenNavigationIsUnavailable()
		{
			ResetShellStatics(BasicRoute);
			var shell = new CometShell();

			try
			{
				View presented = null;
				ModalView.PerformPresent = view => presented = view;
				CometShell.RegisterRoute(BasicRoute, typeof(TrackingShellPage));

				await shell.GoToAsync(BasicRoute);

				var page = Assert.IsType<TrackingShellPage>(presented);
				Assert.Same(TrackingShellPage.LastCreated, page);
			}
			finally
			{
				shell.Dispose();
				ResetShellStatics(BasicRoute);
			}
		}

		[Fact]
		public async Task ShellWrapperAppliesQueryAttributesBeforePresentingPage()
		{
			ResetShellStatics(QueryRoute);
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute(QueryRoute, typeof(QueryTrackingShellPage));

				await shell.GoToAsync($"{QueryRoute}?id=7&name=Bob%20Smith");

				var page = Assert.IsType<QueryTrackingShellPage>(QueryTrackingShellPage.LastCreated);
				Assert.Equal("7", page.ReceivedQuery["id"]);
				Assert.Equal("Bob Smith", page.ReceivedQuery["name"]);
			}
			finally
			{
				shell.Dispose();
				ResetShellStatics(QueryRoute);
			}
		}

		[Fact]
		public async Task ShellWrapperGoBackDismissesModalFallbackWhenStackHasEntries()
		{
			ResetShellStatics(BasicRoute);
			var shell = new CometShell();

			try
			{
				var dismissCalled = false;
				ModalView.PerformPresent = _ => { };
				ModalView.PerformDismiss = () => dismissCalled = true;
				CometShell.RegisterRoute(BasicRoute, typeof(TrackingShellPage));

				await shell.GoToAsync(BasicRoute);
				await shell.GoToAsync("..");

				Assert.True(dismissCalled);
			}
			finally
			{
				shell.Dispose();
				ResetShellStatics(BasicRoute);
			}
		}

		[Fact]
		public async Task ShellExtensionsDelegateToCurrentShell()
		{
			ResetShellStatics(ExtensionRoute);
			var shell = new CometShell();

			try
			{
				View presented = null;
				ModalView.PerformPresent = view => presented = view;
				CometShell.RegisterRoute(ExtensionRoute, typeof(TrackingShellPage));

				await new Text("Navigate").GoToAsync(ExtensionRoute);

				Assert.IsType<TrackingShellPage>(presented);
			}
			finally
			{
				shell.Dispose();
				ResetShellStatics(ExtensionRoute);
			}
		}

		[Fact]
		public async Task ShellExtensionsThrowWhenNoCurrentShellExists()
		{
			ResetShellStatics(BasicRoute);

			var view = new Text("Detached");
			var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => view.GoToAsync(BasicRoute));

			Assert.Contains("No Shell instance", exception.Message);
		}

		[Fact]
		public void BackButtonBehaviorRoundTripsThroughEnvironment()
		{
			var behavior = new BackButtonBehavior
			{
				IsVisible = false,
				Title = "Back",
			};

			var view = new Text("Detail").BackButtonBehavior(behavior);

			Assert.Same(behavior, view.GetBackButtonBehavior());
		}

		[Fact]
		public void ShellWrapperFluentApiBuildsHierarchy()
		{
			var shell = new CometShell()
				.WithSearchHandler(new SearchHandler { Placeholder = "Search..." })
				.WithFlyoutHeader(new ShellItem("Header").WithRoute("//header"))
				.ShowFlyout()
				.AddItem("Main", item => item
					.WithRoute("//main")
					.AddSection("Home", section => section
						.WithRoute("//main/home")
						.AddContent<TrackingShellPage>("Dashboard", BasicRoute)));

			Assert.True(shell.FlyoutIsPresented);
			Assert.Equal("Search...", shell.SearchHandler.Placeholder);
			Assert.Equal("//header", shell.FlyoutHeader.Route);
			Assert.Single(shell.Items);
			Assert.Equal("//main", shell.Items[0].Route);
			Assert.Single(shell.Items[0].Items);
			Assert.Single(shell.Items[0].Items[0].Items);
			Assert.Equal(BasicRoute, shell.Items[0].Items[0].Items[0].Route);
		}

		[Fact]
		public void ShellFactoriesCreateWrapperTypes()
		{
			var shell = CometShell(
				ShellItem("Main",
					ShellSection("Home",
						ShellContent<TrackingShellPage>("Dashboard", BasicRoute))));

			Assert.Single(shell.Items);
			Assert.Equal("Main", shell.Items[0].Title);
			Assert.Equal("Home", shell.Items[0].Items[0].Title);
			Assert.Equal("Dashboard", shell.Items[0].Items[0].Items[0].Title);
			Assert.Equal(BasicRoute, shell.Items[0].Items[0].Items[0].Route);
		}
	}
}
