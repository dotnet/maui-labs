// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Comet;
using Comet.Styles;
using Microsoft.Maui.Hosting;
using ZXing.Net.Maui.Controls;

namespace Microsoft.Maui.Go.CompanionApp;

/// <summary>
/// Comet Go companion app entry point.
/// Sets up Comet with the GoMainPage as root view.
/// </summary>
public class GoApp : CometApp
{
	public GoApp()
	{
		Body = () => new GoMainPage();
	}

	public static MauiApp CreateMauiApp()
	{
		// CRITICAL: Set this as early as possible.
		// MetadataUpdater.IsSupported checks this env var, and it must
		// be set BEFORE the runtime initializes the update pipeline.
		Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "Debug");

		// Global exception handlers — log but don't crash for user code errors
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
		{
			var ex = e.ExceptionObject as Exception;
			Console.WriteLine($"[GoApp] Unhandled exception: {ex?.GetBaseException()?.Message ?? ex?.Message ?? "unknown"}");
		};

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Console.WriteLine($"[GoApp] Unobserved task: {e.Exception.GetBaseException().Message}");
			e.SetObserved();
		};

		var builder = MauiApp.CreateBuilder();
		builder.UseCometApp<GoApp>();
		builder.UseBarcodeReader();

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
		});

		ThemeManager.SetTheme(Defaults.Light);

		return builder.Build();
	}
}
