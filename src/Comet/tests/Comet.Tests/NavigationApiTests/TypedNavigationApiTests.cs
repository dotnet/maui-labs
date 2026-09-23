using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Comet.Tests
{
	public class TypedNavigationApiTests : TestBase
	{
		const string QueryRoute = "phase5-typed-navigation/query";
		const string PropsRoute = "phase5-typed-navigation/props";
		const string ConstructorRoute = "phase5-typed-navigation/constructor";
		const string TypeConstructorRoute = "phase5-typed-navigation/type-constructor";

		class DetailProps
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public string SerializationTrap => throw new InvalidOperationException(
				"Typed props must not be serialized to build a route.");
		}

		class EmptyState
		{
		}

		interface IRuntimeQuery
		{
			int id { get; }
		}

		class RuntimeQueryBase : IRuntimeQuery
		{
			public int id { get; set; }
		}

		class RuntimeQuery : RuntimeQueryBase
		{
			public string name { get; set; }
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

		class ConstructorPage : View
		{
			public static ConstructorPage LastCreated { get; private set; }

			public ConstructorPage()
			{
				LastCreated = this;
			}

			public static void Reset() => LastCreated = null;
		}

		class ParameterizedPage : View
		{
			public ParameterizedPage(string title)
			{
			}
		}

		static Task NavigateWithPreservedQueryMetadata<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			CometShell shell,
			TParameters parameters)
			=> shell.GoToAsync<QueryPage, TParameters>(parameters);

		static void NavigateWithPreservedQueryMetadata<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			NavigationView navigation,
			TParameters parameters)
			=> navigation.Navigate<QueryPage, TParameters>(parameters);

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
			CometShell.UnregisterRoute(ConstructorRoute);
			CometShell.UnregisterRoute(TypeConstructorRoute);
			CometShell.Current = null;
			ModalView.ClearDelegates();
			QueryPage.Reset();
			PropsPage.Reset();
			ConstructorPage.Reset();
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
		public async Task GenericShellNavigationPreservesAnonymousQueryMetadata()
		{
			ResetNavigationStatics();
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);

				await NavigateWithPreservedQueryMetadata(
					shell,
					new { id = 17, name = "Native AOT" });

				var page = Assert.IsType<QueryPage>(QueryPage.LastCreated);
				Assert.Equal("17", page.Query["id"]);
				Assert.Equal("Native AOT", page.Query["name"]);
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

		[Theory]
		[InlineData("object")]
		[InlineData("base")]
		[InlineData("interface")]
		public async Task GenericShellNavigation_RuntimeQueryShape_PreservesPropertiesOrRejectsUnsupportedContract(string shape)
		{
			ResetNavigationStatics();
			using var shell = new CometShell();
			var presentations = 0;
			var derived = new RuntimeQuery { id = 7, name = "Bob Smith" };
			Func<Task> navigate = shape switch
			{
				"object" => () => shell.GoToAsync<QueryPage, object>(new { id = 7, name = "Bob Smith" }),
				"base" => () => shell.GoToAsync<QueryPage, RuntimeQueryBase>(derived),
				"interface" => () => shell.GoToAsync<QueryPage, IRuntimeQuery>(derived),
				_ => throw new ArgumentOutOfRangeException(nameof(shape)),
			};

			try
			{
				ModalView.PerformPresent = _ => presentations++;
				CometShell.RegisterRoute<QueryPage>(QueryRoute);

				if (!RuntimeFeature.IsDynamicCodeSupported)
				{
					var error = await Assert.ThrowsAsync<NotSupportedException>(navigate);
					Assert.Contains("concrete", error.Message);
					Assert.Contains("dictionary", error.Message);
					Assert.Equal(0, presentations);
					Assert.Empty(QueryPage.LastCreated.Query);
					return;
				}

				await navigate();
				Assert.Equal("7", QueryPage.LastCreated.Query["id"]);
				Assert.Equal("Bob Smith", QueryPage.LastCreated.Query["name"]);
				Assert.Equal(2, QueryPage.LastCreated.Query.Count);
				Assert.Equal(1, presentations);
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Theory]
		[InlineData("object")]
		[InlineData("base")]
		[InlineData("interface")]
		public void GenericNavigationView_RuntimeQueryShape_PreservesPropertiesOrRejectsUnsupportedContract(string shape)
		{
			QueryPage.Reset();
			using var navigation = new NavigationView();
			View navigated = null;
			navigation.SetPerformNavigate(view => navigated = view);
			var derived = new RuntimeQuery { id = 7, name = "Bob Smith" };
			Action navigate = shape switch
			{
				"object" => () => navigation.Navigate<QueryPage, object>(new { id = 7, name = "Bob Smith" }),
				"base" => () => navigation.Navigate<QueryPage, RuntimeQueryBase>(derived),
				"interface" => () => navigation.Navigate<QueryPage, IRuntimeQuery>(derived),
				_ => throw new ArgumentOutOfRangeException(nameof(shape)),
			};

			if (!RuntimeFeature.IsDynamicCodeSupported)
			{
				var error = Assert.Throws<NotSupportedException>(navigate);
				Assert.Contains("concrete", error.Message);
				Assert.Contains("dictionary", error.Message);
				Assert.Null(navigated);
				Assert.Empty(navigation.GetBackendNavigationStack());
				Assert.Empty(QueryPage.LastCreated.Query);
				return;
			}

			navigate();
			var page = Assert.IsType<QueryPage>(navigated);
			Assert.Equal("7", page.Query["id"]);
			Assert.Equal("Bob Smith", page.Query["name"]);
			Assert.Equal(2, page.Query.Count);
		}

		[Fact]
		public void GenericNavigationView_ExactAnonymousType_PreservesQueryMetadata()
		{
			using var navigation = new NavigationView();
			View navigated = null;
			navigation.SetPerformNavigate(view => navigated = view);

			NavigateWithPreservedQueryMetadata(navigation, new { id = 17, name = "Native AOT" });

			var page = Assert.IsType<QueryPage>(navigated);
			Assert.Equal("17", page.Query["id"]);
			Assert.Equal("Native AOT", page.Query["name"]);
		}

		[Theory]
		[InlineData(true, true)]
		[InlineData(true, false)]
		[InlineData(false, true)]
		[InlineData(false, false)]
		public async Task GenericNavigation_ObjectTypedDictionary_DoesNotRequireRuntimePropertyReflection(
			bool useShell, bool stringValues)
		{
			ResetNavigationStatics();
			using var shell = new CometShell();
			using var navigation = new NavigationView();
			View navigated = null;
			object parameters = stringValues
				? new Dictionary<string, string> { ["id"] = "7", ["name"] = "Bob Smith" }
				: new Dictionary<string, object> { ["id"] = 7, ["name"] = "Bob Smith" };

			try
			{
				ModalView.PerformPresent = view => navigated = view;
				navigation.SetPerformNavigate(view => navigated = view);
				CometShell.RegisterRoute<QueryPage>(QueryRoute);
				if (useShell)
					await shell.GoToAsync<QueryPage, object>(parameters);
				else
					navigation.Navigate<QueryPage, object>(parameters);

				var page = Assert.IsType<QueryPage>(navigated);
				Assert.Equal("7", page.Query["id"]);
				Assert.Equal("Bob Smith", page.Query["name"]);
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Theory]
		[InlineData(true)]
		[InlineData(false)]
		public async Task GenericNavigation_ObjectTypedProps_DoesNotSerializeQueryProperties(bool useShell)
		{
			ResetNavigationStatics();
			using var shell = new CometShell();
			using var navigation = new NavigationView();
			View navigated = null;
			object props = new DetailProps { Id = 42, Name = "Unserialized" };

			try
			{
				ModalView.PerformPresent = view => navigated = view;
				navigation.SetPerformNavigate(view => navigated = view);
				CometShell.RegisterRoute<PropsPage>(PropsRoute);
				if (useShell)
					await shell.GoToAsync<PropsPage, object>(props);
				else
					navigation.Navigate<PropsPage, object>(props);

				Assert.Same(props, Assert.IsType<PropsPage>(navigated).Props);
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Theory]
		[InlineData(typeof(CometShell), nameof(CometShell.GoToAsync))]
		[InlineData(typeof(NavigationView), nameof(NavigationView.Navigate))]
		[InlineData(typeof(ShellExtensions), nameof(ShellExtensions.GoToAsync))]
		public void GenericNavigation_ExactTypeContract_RetainsMetadataWithoutBlanketTrimWarning(Type type, string name)
		{
			var method = type.GetMethods().Single(method =>
				method.Name == name && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2);
			Assert.Null(method.GetCustomAttribute<RequiresUnreferencedCodeAttribute>());
			Assert.Equal(DynamicallyAccessedMemberTypes.PublicProperties,
				method.GetGenericArguments()[1].GetCustomAttribute<DynamicallyAccessedMembersAttribute>().MemberTypes);
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

		[Fact]
		public async Task GenericRouteRegistrationPreservesPublicParameterlessConstructor()
		{
			ResetNavigationStatics();
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<ConstructorPage>(ConstructorRoute);

				await shell.GoToAsync<ConstructorPage>();

				Assert.IsType<ConstructorPage>(ConstructorPage.LastCreated);
			}
			finally
			{
				shell.Dispose();
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task TypeRouteRegistrationPreservesPublicParameterlessConstructor()
		{
			ResetNavigationStatics();
			var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute(TypeConstructorRoute, typeof(ConstructorPage));

				await shell.GoToAsync(TypeConstructorRoute);

				Assert.IsType<ConstructorPage>(ConstructorPage.LastCreated);
			}
			finally
			{
				shell.Dispose();
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task RegisterRoute_InvalidConstructor_DoesNotRegisterRouteOrFactory()
		{
			ResetNavigationStatics();
			using var shell = new CometShell();

			try
			{
				Assert.Throws<ArgumentException>(() => CometShell.RegisterRoute<ParameterizedPage>(QueryRoute));

				Assert.False(CometShell.HasRoute(QueryRoute));
				Assert.False(CometShell.HasRoute<ParameterizedPage>());
				Assert.Throws<InvalidOperationException>(() => CometShell.GetRoute<ParameterizedPage>());
				await Assert.ThrowsAsync<InvalidOperationException>(() => shell.GoToAsync(QueryRoute));
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task RegisterRoute_InvalidReplacement_PreservesOriginalMappingsAndFactory()
		{
			ResetNavigationStatics();
			using var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);

				Assert.Throws<ArgumentException>(() =>
					CometShell.RegisterRoute(QueryRoute, typeof(ParameterizedPage)));

				Assert.True(CometShell.HasRoute(QueryRoute));
				Assert.True(CometShell.HasRoute<QueryPage>());
				Assert.Equal(QueryRoute, CometShell.GetRoute<QueryPage>());
				Assert.False(CometShell.HasRoute<ParameterizedPage>());
				await shell.GoToAsync<QueryPage>();
				Assert.IsType<QueryPage>(QueryPage.LastCreated);
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task RegisterRoute_ValidReplacement_RemovesStaleMappingAndReplacesFactory()
		{
			ResetNavigationStatics();
			using var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);
				CometShell.RegisterRoute<ConstructorPage>(QueryRoute);

				Assert.True(CometShell.HasRoute(QueryRoute));
				Assert.False(CometShell.HasRoute<QueryPage>());
				Assert.Throws<InvalidOperationException>(() => CometShell.GetRoute<QueryPage>());
				Assert.Equal(QueryRoute, CometShell.GetRoute<ConstructorPage>());
				await shell.GoToAsync<ConstructorPage>();
				Assert.IsType<ConstructorPage>(ConstructorPage.LastCreated);
				Assert.Null(QueryPage.LastCreated);

				CometShell.UnregisterRoute(QueryRoute);
				Assert.False(CometShell.HasRoute<ConstructorPage>());
				Assert.False(CometShell.HasRoute(QueryRoute));
				await Assert.ThrowsAsync<InvalidOperationException>(() => shell.GoToAsync(QueryRoute));
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task RegisterRoute_ReplacingOlderAlias_PreservesLatestTypedAlias()
		{
			ResetNavigationStatics();
			using var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);
				CometShell.RegisterRoute<QueryPage>(ConstructorRoute);
				CometShell.RegisterRoute<ConstructorPage>(QueryRoute);

				Assert.Equal(ConstructorRoute, CometShell.GetRoute<QueryPage>());
				Assert.Equal(QueryRoute, CometShell.GetRoute<ConstructorPage>());
				await shell.GoToAsync<QueryPage>();
				Assert.IsType<QueryPage>(QueryPage.LastCreated);
				await shell.GoToAsync(QueryRoute);
				Assert.IsType<ConstructorPage>(ConstructorPage.LastCreated);

				CometShell.UnregisterRoute(QueryRoute);
				Assert.Equal(ConstructorRoute, CometShell.GetRoute<QueryPage>());
			}
			finally
			{
				ResetNavigationStatics();
			}
		}

		[Fact]
		public async Task RegisterRoute_ReplacingLatestAlias_DoesNotRestoreOlderTypedAlias()
		{
			ResetNavigationStatics();
			using var shell = new CometShell();

			try
			{
				ModalView.PerformPresent = _ => { };
				CometShell.RegisterRoute<QueryPage>(QueryRoute);
				CometShell.RegisterRoute<QueryPage>(ConstructorRoute);
				CometShell.RegisterRoute<ConstructorPage>(ConstructorRoute);

				Assert.True(CometShell.HasRoute(QueryRoute));
				Assert.False(CometShell.HasRoute<QueryPage>());
				Assert.Equal(ConstructorRoute, CometShell.GetRoute<ConstructorPage>());
				await shell.GoToAsync(QueryRoute);
				Assert.IsType<QueryPage>(QueryPage.LastCreated);
				await shell.GoToAsync<ConstructorPage>();
				Assert.IsType<ConstructorPage>(ConstructorPage.LastCreated);

				CometShell.RegisterRoute<QueryPage>(QueryRoute);
				Assert.Equal(QueryRoute, CometShell.GetRoute<QueryPage>());
			}
			finally
			{
				ResetNavigationStatics();
			}
		}
	}
}
