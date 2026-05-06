using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.ApplicationModel;

namespace Microsoft.Maui.WPF
{
	internal static partial class MauiContextExtensions
	{
		public static IAnimationManager GetAnimationManager(this IMauiContext mauiContext) =>
			mauiContext.Services.GetRequiredService<IAnimationManager>();

		public static IDispatcher GetDispatcher(this IMauiContext mauiContext) =>
			mauiContext.Services.GetRequiredService<IDispatcher>();

		public static IDispatcher? GetOptionalDispatcher(this IMauiContext mauiContext) =>
			mauiContext.Services.GetService<IDispatcher>();

		public static IMauiContext MakeApplicationScope(this IMauiContext mauiContext, System.Windows.Application platformApplication)
		{
			var scopedContext = new WPFMauiContext(mauiContext.Services);

			scopedContext.AddSpecific(platformApplication);

			scopedContext.InitializeScopedServices();

			return scopedContext;
		}

		public static IMauiContext MakeWindowScope(this IMauiContext mauiContext, System.Windows.Window platformWindow, out IServiceScope scope)
		{
			scope = mauiContext.Services.CreateScope();

			var scopedContext = new WPFMauiContext(scope.ServiceProvider);

			scopedContext.AddWeakSpecific(platformWindow);

			return scopedContext;
		}

		public static void InitializeScopedServices(this IMauiContext scopedContext)
		{
			var scopedServices = scopedContext.Services.GetServices<IMauiInitializeScopedService>();

			foreach (var service in scopedServices)
				service.Initialize(scopedContext.Services);
		}

		public static FlowDirection GetFlowDirection(this IMauiContext mauiContext)
		{
			var appInfo = AppInfo.Current;

			if (appInfo.RequestedLayoutDirection == LayoutDirection.RightToLeft)
				return FlowDirection.RightToLeft;

			return FlowDirection.LeftToRight;
		}
	}
}
