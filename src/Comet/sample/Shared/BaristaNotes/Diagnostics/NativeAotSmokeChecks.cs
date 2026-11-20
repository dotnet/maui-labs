using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Comet;
using Comet.Reflection;

namespace CometSamples.BaristaNotes.Diagnostics
{
	internal static class NativeAotSmokeChecks
	{
		const string PropsRoute = "nativeaot-smoke/props";
		const string QueryRoute = "nativeaot-smoke/query";
		const string TypeRoute = "nativeaot-smoke/type";

		internal static async Task<string> RunAsync()
		{
			var shell = new CometShell();
			var navigation = new NavigationView();
			View? navigated = null;
			navigation.SetPerformNavigate(view => navigated = view);
			shell.Navigation = navigation;

			try
			{
				CometShell.RegisterRoute<SmokePropsPage>(PropsRoute);
				CometShell.RegisterRoute<SmokeQueryPage>(QueryRoute);
				CometShell.RegisterRoute(TypeRoute, typeof(SmokeTypeRoutePage));

				var props = new SmokeProps { Id = 42, Name = "NativeAOT props" };
				await shell.GoToAsync<SmokePropsPage, SmokeProps>(props);
				Ensure(
					navigated is SmokePropsPage propsPage && ReferenceEquals(props, propsPage.Props),
					"Typed props were not assigned directly.");

				await NavigateAnonymousQueryAsync(
					shell,
					new { id = 17, name = "Native AOT query" });
				Ensure(
					navigated is SmokeQueryPage queryPage
						&& queryPage.Query.TryGetValue("id", out var id)
						&& id == "17"
						&& queryPage.Query.TryGetValue("name", out var name)
						&& name == "Native AOT query",
					"Metadata-preserved anonymous query parameters were not applied.");

				await shell.GoToAsync(TypeRoute);
				Ensure(
					navigated is SmokeTypeRoutePage,
					"The Type-based route factory did not activate its page.");

				VerifyReflectionApi();

				return $"PASS dynamicCode={RuntimeFeature.IsDynamicCodeSupported} " +
					"typedProps=true anonymousQuery=true routeFactory=true reflectionApi=true";
			}
			finally
			{
				CometShell.UnregisterRoute(PropsRoute);
				CometShell.UnregisterRoute(QueryRoute);
				CometShell.UnregisterRoute(TypeRoute);
				navigation.Dispose();
				shell.Dispose();
			}
		}

		static Task NavigateAnonymousQueryAsync<
			[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TParameters>(
			CometShell shell,
			TParameters parameters)
			=> shell.GoToAsync<SmokeQueryPage, TParameters>(parameters);

		static void VerifyReflectionApi()
		{
			var view = new SmokeMetadataView();
			Ensure(view.SetDirectPropertyValue(nameof(SmokeMetadataView.Status), "updated"),
				"The View-specific SetDirectPropertyValue API failed.");
			Ensure(view.GetDirectPropValue<string>(nameof(SmokeMetadataView.Status)) == "updated",
				"The View-specific GetDirectPropValue API failed.");
			Ensure(view.GetFieldsWithAttribute(typeof(SmokeFieldAttribute)).Count == 1,
				"The View-specific attributed-field lookup failed.");

			view.Dispose();
		}

		static void Ensure(bool condition, string message)
		{
			if (!condition)
				throw new InvalidOperationException(message);
		}

		sealed class SmokeState
		{
		}

		sealed class SmokeProps
		{
			public int Id { get; set; }
			public string Name { get; set; } = string.Empty;
			public string SerializationTrap => throw new InvalidOperationException(
				"Typed props were serialized instead of assigned directly.");
		}

		sealed class SmokePropsPage : Component<SmokeState, SmokeProps>
		{
			public SmokePropsPage()
			{
			}

			public override View Render() => new Text(Props.Name ?? string.Empty);
		}

		sealed class SmokeQueryPage : View, IQueryAttributable
		{
			readonly Dictionary<string, string> _query = new(StringComparer.Ordinal);

			public SmokeQueryPage()
			{
			}

			public IReadOnlyDictionary<string, string> Query => _query;

			public void ApplyQueryAttributes(Dictionary<string, string> query)
			{
				_query.Clear();
				foreach (var pair in query)
					_query[pair.Key] = pair.Value;
			}
		}

		sealed class SmokeTypeRoutePage : View
		{
			public SmokeTypeRoutePage()
			{
			}
		}

		[AttributeUsage(AttributeTargets.Field)]
		sealed class SmokeFieldAttribute : Attribute
		{
		}

		sealed class SmokeMetadataView : View
		{
			[SmokeField]
			public int MarkedField = 0;

			public string Status { get; set; } = string.Empty;
		}
	}
}
