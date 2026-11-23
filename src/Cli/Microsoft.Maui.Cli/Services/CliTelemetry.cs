// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Utils;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Microsoft.Maui.Cli.Services;

internal sealed class CliTelemetry : IDisposable
{
	internal const string ActivitySourceName = "Microsoft.Maui.Cli";
	internal const string ConnectionStringMetadataKey = "MauiCliTelemetryConnectionString";
	internal const int ShutdownTimeoutMilliseconds = 250;

	readonly ActivitySource _source;
	readonly TracerProvider _provider;
	readonly Activity? _activity;
	readonly TextWriter _diagnostics;
	readonly bool _verbose;
	bool _completed;
	bool _disposed;

	internal CliTelemetry(ActivitySource source, TracerProvider provider, string commandName,
		TextWriter diagnostics, bool verbose)
	{
		_source = source;
		_provider = provider;
		_diagnostics = diagnostics;
		_verbose = verbose;
		// Isolate product telemetry from ambient application/agent context, including SDK enrichers.
		var previous = Activity.Current;
		try
		{
			Activity.Current = null;
			_activity = source.StartActivity("maui/cli/command", ActivityKind.Internal,
				parentContext: default,
				tags: [new("maui.cli.command.name", commandName)]);
		}
		finally
		{
			Activity.Current = previous;
		}
	}

	internal static CliTelemetry? Start(ParseResult parseResult) =>
		Start(parseResult, Environment.GetEnvironmentVariable,
			typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
				.FirstOrDefault(attribute => attribute.Key == ConnectionStringMetadataKey)?.Value,
			Path.Combine(PlatformDetector.Paths.MauiCacheDirectory, "telemetry"),
			Console.Error, Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected);

	internal static CliTelemetry? Start(ParseResult parseResult, Func<string, string?> environment,
		string? defaultConnectionString, string storageDirectory, TextWriter diagnostics, bool redirected)
	{
		var commandName = CliTelemetryPolicy.GetCommandName(parseResult);
		if (commandName is null)
			return null;

		var connectionString = CliTelemetryPolicy.ResolveConnectionString(environment, defaultConnectionString);
		if (connectionString is null)
			return null;

		var verbose = parseResult.GetValue(GlobalOptions.VerboseOption);
		var source = new ActivitySource(ActivitySourceName);
		TracerProvider? provider = null;
		try
		{
			if (CliTelemetryPolicy.ShouldShowNotice(parseResult, commandName, redirected, environment))
				ShowNotice(storageDirectory, diagnostics);

			ConfigureProcess(Environment.SetEnvironmentVariable);
			provider = CreateProviderBuilder(source.Name)
				.AddAzureMonitorTraceExporter(options => ConfigureExporter(options, connectionString, storageDirectory))
				.Build();
			return new CliTelemetry(source, provider, commandName, diagnostics, verbose);
		}
		catch (Exception exception) when (IsTelemetryFailure(exception))
		{
			provider?.Dispose();
			source.Dispose();
			WriteDiagnostic(diagnostics, verbose, $"Initialization failed ({exception.GetType().Name}); reporting is disabled for this invocation.");
			return null;
		}
	}

	internal static void ConfigureProcess(Action<string, string?> setEnvironmentVariable)
	{
		// SDK-health pipelines use their own resources, outside our command metadata allowlist.
		setEnvironmentVariable("APPLICATIONINSIGHTS_STATSBEAT_DISABLED", "true");
		setEnvironmentVariable("APPLICATIONINSIGHTS_SDKSTATS_DISABLED", "true");
	}

	internal static TracerProviderBuilder CreateProviderBuilder(string sourceName) =>
		Sdk.CreateTracerProviderBuilder()
			.AddSource(sourceName)
			.SetSampler(new AlwaysOnSampler())
			// Suppress default host attributes and Azure Monitor's Dns.GetHostName() role-instance fallback.
			.SetResourceBuilder(ResourceBuilder.CreateEmpty()
				.AddService("maui-cli", serviceVersion: UpdateCommand.CurrentVersion,
					autoGenerateServiceInstanceId: false, serviceInstanceId: "maui-cli")
				.AddAttributes(new Dictionary<string, object>
				{
					["os.type"] = PlatformDetector.IsWindows ? "windows" : PlatformDetector.IsMacOS ? "darwin" :
						PlatformDetector.IsLinux ? "linux" : "unknown",
					["os.version"] = Environment.OSVersion.Version.ToString(),
					["host.arch"] = PlatformDetector.Architecture,
					["process.runtime.version"] = Environment.Version.ToString()
				}));

	internal static void ConfigureExporter(AzureMonitorExporterOptions options, string connectionString, string storageDirectory)
	{
		options.ConnectionString = connectionString;
		options.StorageDirectory = storageDirectory;
		options.EnableLiveMetrics = false;
		options.EnableStandardMetrics = false;
		options.EnablePerformanceCounters = false;
		options.TracesPerSecond = null;
		options.SamplingRatio = 1;
		options.Retry.NetworkTimeout = TimeSpan.FromSeconds(1);
	}

	internal static void ShowNotice(string storageDirectory, TextWriter diagnostics)
	{
		var noticePath = Path.Combine(storageDirectory, "notice-v1");
		if (File.Exists(noticePath))
			return;

		Directory.CreateDirectory(storageDirectory);
		diagnostics.WriteLine("MAUI CLI collects command names, outcomes, timings, and CLI/runtime/OS versions. " +
			"No arguments, paths, or app data are collected. Set MAUI_CLI_TELEMETRY_OPTOUT=1 to opt out. " +
			"Details: https://github.com/dotnet/maui-labs/blob/main/src/Cli/README.md#telemetry");
		File.WriteAllText(noticePath, "");
	}

	internal void Complete(int exitCode, Exception? exception = null)
	{
		if (_completed)
			return;

		_completed = true;
		var cancelled = exitCode == 130 || exception is OperationCanceledException;
		_activity?.SetTag("process.exit.code", exitCode);
		_activity?.SetTag("maui.cli.command.outcome", cancelled ? "cancelled" : exitCode == 0 ? "success" : "error");
		if (exception is not null && !cancelled)
			_activity?.SetTag("error.type", exception.GetType().FullName);
		_activity?.SetStatus(cancelled ? ActivityStatusCode.Unset : exitCode == 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
		var previous = Activity.Current;
		try
		{
			_activity?.Dispose();
		}
		finally
		{
			Activity.Current = previous;
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;
		Complete(1);
		try
		{
			if (!_provider.Shutdown(ShutdownTimeoutMilliseconds))
				WriteDiagnostic(_diagnostics, _verbose, "Shutdown did not complete within its time budget.");
		}
		catch (Exception exception) when (IsTelemetryFailure(exception))
		{
			WriteDiagnostic(_diagnostics, _verbose, $"Shutdown failed ({exception.GetType().Name}).");
		}
		finally
		{
			try
			{
				_provider.Dispose();
			}
			catch (Exception exception) when (IsTelemetryFailure(exception))
			{
				WriteDiagnostic(_diagnostics, _verbose, $"Disposal failed ({exception.GetType().Name}).");
			}
			_source.Dispose();
		}
	}

	static bool IsTelemetryFailure(Exception exception) =>
		exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException;

	static void WriteDiagnostic(TextWriter diagnostics, bool verbose, string message)
	{
		if (verbose)
			diagnostics.WriteLine($"Telemetry: {message}");
	}
}
