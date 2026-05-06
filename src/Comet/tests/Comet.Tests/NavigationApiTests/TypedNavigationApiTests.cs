using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace Comet.Tests
{
	public class TypedNavigationApiTests : TestBase
	{
		const string QueryRoute = "phase5-typed-navigation/query";
		const string PropsRoute = "phase5-typed-navigation/props";

		class DetailProps
		{
			public int Id { get; set; }
			public string Name { get; set; }
		}

		class EmptyState
		{
		}

		class QueryPage : View, IQueryAttributable
		{
			readonly Dictionary<string, string> _query = new Dictionary<string, string>();

			public static QueryPage LastCreated { get; private set; }
			public IReadOnlyDictionary<string, string> Query => _query;

			public QueryPage()
			{
				LastCreated = this;
			}

			public void ApplyQueryAttributes(Dictionary<string, string> query)
			{
				_query.Clear();
				foreach (var pair in query)
					_query[pair.Key] = pair.Value;
			}

			public static void Reset() => LastCreated = null;
		}

		class PropsPage : Component<EmptyState, DetailProps>
		{
			public static PropsPage LastCreated { get; private set; }

			public PropsPage()
			{
				LastCreated = this;
			}

			public override View Render()
				=> new Text(Props.Name ?? string.Empty);

			public static void Reset() => LastCreated = null;
		}

		static MethodInfo FindGenericMethod(Type type, string name)
		{
			return type
				.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
				.FirstOrDefault(x => x.Name == name && x.IsGenericMethodDefinition && x.GetGenericArguments().Length == 1);
		}

		static void ResetNavigationStatics()
		{
			CometShell.UnregisterRoute(QueryRoute);
			CometShell.UnregisterRoute(PropsRoute);
			CometShell.Current = null;
			ModalView.ClearDelegates();
			QueryPage.Reset();
			PropsPage.Reset();
		}

		[Fact]
		public void CometShellExposesGenericRegisterRouteOverload()
		{
			var method = FindGenericMethod(typeof(CometShell), nameof(CometShell.RegisterRoute));

			Assert.NotNull(method);
			Assert.Single(method.GetParameters());
			Assert.Equal(typeof(string), method.GetParameters()[0].ParameterType);
		}

		[Fact]
		public void ShellExtensionsExposeGenericGoToAsyncOverload()
		{
			var method = FindGenericMethod(typeof(ShellExtensions), nameof(ShellExtensions.GoToAsync));

			Assert.NotNull(method);
			Assert.Equal(typeof(Task), method.ReturnType);
			Assert.Equal(typeof(View), method.GetParameters()[0].ParameterType);
		}

		[Fact]
		public void NavigationViewExposesGenericNavigateOverload()
		{
			var method = FindGenericMethod(typeof(NavigationView), nameof(NavigationView.Navigate));

			Assert.NotNull(method);
			Assert.Empty(method.GetParameters());
		}

		[Fact]
		public async Task GenericShellNavigationAppliesAnonymousObjectAsQueryParameters()
		{
			ResetNavigationStatics();
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);

				await shell.GoToAsync<QueryPage>(new { id = 7, name = "Bob Smith" });

				var page = Assert.IsType<QueryPage>(QueryPage.LastCreated);
				Assert.Equal("7", page.Query["id"]);
				Assert.Equal("Bob Smith", page.Query["name"]);
				Assert.True(CometShell.HasRoute<QueryPage>());
				Assert.Equal(QueryRoute, CometShell.GetRoute<QueryPage>());
			}
			finally
			{
				shell.Dispose();
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task GenericShellNavigationAppliesTypedPropsToComponentPages()
		{
			ResetNavigationStatics();
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				var props = new DetailProps
				{
					Id = 42,
					Name = "Amos"
				};
				CometShell.RegisterRoute<PropsPage>(PropsRoute);

				await new Text("Navigate").GoToAsync<PropsPage, DetailProps>(props);

				var page = Assert.IsType<PropsPage>(PropsPage.LastCreated);
				Assert.Same(props, page.Props);
			}
			finally
			{
				shell.Dispose();
				ResetNavigationStatics();
			}
		}

		[Fact]
		public void GenericNavigationViewNavigationCreatesTypedViewAndAppliesParameters()
		{
			QueryPage.Reset();
			PropsPage.Reset();

			View navigated = null;
			var navigationView = new NavigationView();
			navigationView.SetPerformNavigate(view => navigated = view);

			navigationView.Navigate<QueryPage>(new { id = 99, name = "Comet" });
			var queryPage = Assert.IsType<QueryPage>(navigated);
			Assert.Equal("99", queryPage.Query["id"]);
			Assert.Equal("Comet", queryPage.Query["name"]);

			var props = new DetailProps
			{
				Id = 5,
				Name = "Typed"
			};

			navigationView.Navigate<PropsPage, DetailProps>(props);
			var propsPage = Assert.IsType<PropsPage>(navigated);
			Assert.Same(props, propsPage.Props);
		}
	}
}
