// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli;

/// <summary>
/// Configures dependency injection for the application.
/// </summary>
public static class ServiceConfiguration
{
	/// <summary>
	/// Creates and configures the service provider.
	/// </summary>
	public static IServiceProvider CreateServiceProvider()
	{
		var services = new ServiceCollection();
		ConfigureServices(services);
		return services.BuildServiceProvider();
	}

	/// <summary>
	/// Configures services for dependency injection.
	/// </summary>
	public static void ConfigureServices(IServiceCollection services)
	{
		// Android providers
		services.AddSingleton<IJdkManager, JdkManager>();
		services.AddSingleton<IAndroidProvider, AndroidProvider>();

		// Apple providers
		services.AddSingleton<IAppleProvider, AppleProvider>();

		// Core services
		services.AddSingleton<IDoctorService, DoctorService>();
		services.AddSingleton<IDeviceManager, DeviceManager>();
		services.AddSingleton<HttpClient>();
		services.AddSingleton<IMauiVersionFeedService, MauiVersionFeedService>();
		services.AddSingleton<IMauiProjectVersionService, MauiProjectVersionService>();

		// DevFlow output
		services.AddSingleton<IDevFlowOutputWriter, DevFlowOutputWriter>();
		AddFlowExecutionServices(services);

		// Output formatters (transient - created per request with specific config)
		services.AddTransient<JsonOutputFormatter>();
		services.AddTransient<SpectreOutputFormatter>();
	}

	/// <summary>
	/// Creates services for testing with custom implementations.
	/// </summary>
	public static IServiceProvider CreateTestServiceProvider(
		IAndroidProvider? androidProvider = null,
		IAppleProvider? appleProvider = null,
		IJdkManager? jdkManager = null,
		IDoctorService? doctorService = null,
		IDeviceManager? deviceManager = null,
		IDevFlowOutputWriter? devFlowOutputWriter = null,
		IMauiVersionFeedService? mauiVersionFeedService = null,
		IMauiProjectVersionService? mauiProjectVersionService = null)
	{
		var services = new ServiceCollection();

		// Use provided mocks or create real implementations
		if (jdkManager != null)
			services.AddSingleton(jdkManager);
		else
			services.AddSingleton<IJdkManager, JdkManager>();

		if (androidProvider != null)
			services.AddSingleton(androidProvider);
		else
			services.AddSingleton<IAndroidProvider, AndroidProvider>();

		if (appleProvider != null)
			services.AddSingleton(appleProvider);
		else
			services.AddSingleton<IAppleProvider, AppleProvider>();

		if (doctorService != null)
			services.AddSingleton(doctorService);
		else
			services.AddSingleton<IDoctorService, DoctorService>();

		if (deviceManager != null)
			services.AddSingleton(deviceManager);
		else
			services.AddSingleton<IDeviceManager, DeviceManager>();

		if (devFlowOutputWriter != null)
			services.AddSingleton(devFlowOutputWriter);
		else
			services.AddSingleton<IDevFlowOutputWriter, DevFlowOutputWriter>();

		services.AddSingleton<HttpClient>();
		AddFlowExecutionServices(services);

		if (mauiVersionFeedService != null)
			services.AddSingleton(mauiVersionFeedService);
		else
			services.AddSingleton<IMauiVersionFeedService, MauiVersionFeedService>();

		if (mauiProjectVersionService != null)
			services.AddSingleton(mauiProjectVersionService);
		else
			services.AddSingleton<IMauiProjectVersionService, MauiProjectVersionService>();

		return services.BuildServiceProvider();
	}

	static void AddFlowExecutionServices(IServiceCollection services)
	{
		services.AddSingleton<IExecutionProcessRunner, ExecutionProcessRunner>();
		services.AddSingleton<IExecutionStandardInputProcessRunner, ExecutionStandardInputProcessRunner>();
		services.AddSingleton<IAppSourceIdentityProvider, GitAppSourceIdentityProvider>();
		services.AddSingleton<IFlowExecutionHostEnvironment, SystemFlowExecutionHostEnvironment>();
		services.AddSingleton<IFlowExecutionProcessController, SystemFlowExecutionProcessController>();
		services.AddSingleton<IWindowsDesktopSessionAdmissionProbe, WindowsDesktopSessionAdmissionProbe>();
		services.AddSingleton<IWindowsAppProjectInspector, WindowsAppProjectInspector>();
		services.AddSingleton<IAppleAppBundleInspector, AppleAppBundleInspector>();
		services.AddSingleton<IAppleSimulatorAppInspector, AppleSimulatorAppInspector>();
		services.AddSingleton<IAppArtifactResolver, MsBuildAppArtifactResolver>();
		services.AddSingleton<IAndroidAppDeployment, AndroidAppDeployment>();
		services.AddSingleton<IAndroidFlowPortManager, AndroidFlowPortManager>();
		services.AddSingleton<AndroidFlowExecutionAdapter>();
		services.AddSingleton<WindowsFlowExecutionAdapter>();
		services.AddSingleton<WpfFlowExecutionAdapter>();
		services.AddSingleton<IosSimulatorFlowExecutionAdapter>();
		services.AddSingleton<MacCatalystFlowExecutionAdapter>();
		services.AddSingleton<AppKitFlowExecutionAdapter>();
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<AndroidFlowExecutionAdapter>());
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<WindowsFlowExecutionAdapter>());
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<WpfFlowExecutionAdapter>());
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<IosSimulatorFlowExecutionAdapter>());
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<MacCatalystFlowExecutionAdapter>());
		services.AddSingleton<IFlowExecutionPlatformAdapter>(
			static provider => provider.GetRequiredService<AppKitFlowExecutionAdapter>());
		services.AddSingleton<AndroidAppStorageEvidenceProvider>();
		services.AddSingleton<IFlowStateEvidenceProvider>(
			static provider => provider.GetRequiredService<AndroidAppStorageEvidenceProvider>());
		services.AddSingleton<IAttachedRunOracleEvaluator>(
			static provider => provider.GetRequiredService<AndroidAppStorageEvidenceProvider>());
		services.AddSingleton<IFlowStateEvidenceProviderRegistry>(
			static provider => new FlowStateEvidenceProviderRegistry(
				provider.GetServices<IFlowStateEvidenceProvider>()));
		services.AddSingleton<CommittedFlowBundleLoader>();
		services.AddSingleton(
			static _ => new ExactAgentBindingResolver(BrokerClient.ListAgentsAsync));
		services.AddSingleton<FlowRunReportWriter>();
		services.AddSingleton<JUnitFlowExecutionWriter>();
		services.AddSingleton<ExecutionManifestWriter>();
		services.AddSingleton<ImmutableExecutionOutputWriter>();
		services.AddSingleton<ArtifactTrustImportService>();
		services.AddSingleton<IArtifactTrustImporter>(
			static provider => provider.GetRequiredService<ArtifactTrustImportService>());
		services.AddSingleton<IFlowExecutionCoordinator>(static provider =>
			new FlowExecutionCoordinator(
				provider.GetRequiredService<CommittedFlowBundleLoader>(),
				provider.GetRequiredService<IAppArtifactResolver>(),
				provider.GetServices<IFlowExecutionPlatformAdapter>(),
				provider.GetRequiredService<IFlowStateEvidenceProviderRegistry>(),
				provider.GetRequiredService<ExactAgentBindingResolver>(),
				provider.GetRequiredService<FlowRunReportWriter>(),
				provider.GetRequiredService<JUnitFlowExecutionWriter>(),
				provider.GetRequiredService<ExecutionManifestWriter>(),
				provider.GetRequiredService<ImmutableExecutionOutputWriter>(),
				BrokerClient.EnsureBrokerRunningAsync,
				appSourceIdentityProvider: provider.GetRequiredService<IAppSourceIdentityProvider>()));
		services.AddSingleton<IFlowReproductionCoordinator, FlowReproductionCoordinator>();
		services.AddSingleton<IFlowTriageCoordinator, FlowTriageCoordinator>();
	}
}
