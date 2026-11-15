// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Maui.Cli.Services;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class CliTelemetryTests : IDisposable
{
	readonly string _directory = Path.Combine(AppContext.BaseDirectory, "cli-telemetry-tests", Guid.NewGuid().ToString("N"));

	[Theory]
	[InlineData(0, "success", ActivityStatusCode.Ok)]
	[InlineData(1, "error", ActivityStatusCode.Error)]
	[InlineData(130, "cancelled", ActivityStatusCode.Unset)]
	public void Complete_ReportsCommandDurationAndOutcome(int exitCode, string outcome, ActivityStatusCode status)
	{
		using var telemetry = Create(out var exporter, out _, out _);
		telemetry.Complete(exitCode);

		var activity = Assert.Single(exporter.Activities);
		Assert.Equal("maui/cli/command", activity.OperationName);
		Assert.Equal("project check", activity.GetTagItem("maui.cli.command.name"));
		Assert.Equal(exitCode, activity.GetTagItem("process.exit.code"));
		Assert.Equal(outcome, activity.GetTagItem("maui.cli.command.outcome"));
		Assert.Equal(status, activity.Status);
		Assert.True(activity.Duration > TimeSpan.Zero);
		Assert.Equal(3, activity.TagObjects.Count());
		Assert.Empty(activity.Events);
		Assert.Empty(activity.Baggage);
	}

	[Fact]
	public void Complete_AmbientActivity_DoesNotInheritPrivateBaggageOrParentMetadata()
	{
		using var parent = new Activity("private-parent")
			.AddBaggage("token", "private-token")
			.SetTag("microsoft.gen_ai.main_agent.name", "private-agent")
			.Start();
		using var telemetry = Create(out var exporter, out _, out _);
		telemetry.Complete(0);

		var activity = Assert.Single(exporter.Activities);
		Assert.Null(activity.Parent);
		Assert.NotEqual(parent.TraceId, activity.TraceId);
		Assert.Empty(activity.Baggage);
		Assert.Null(activity.GetTagItem("microsoft.gen_ai.main_agent.name"));
		Assert.Same(parent, Activity.Current);
	}

	[Fact]
	public void Complete_Exception_ReportsTypeWithoutMessageStackOrData()
	{
		using var telemetry = Create(out var exporter, out _, out _);
		var exception = new InvalidOperationException(@"secret-token C:\private\App.csproj");
		exception.Data["private"] = "private-app-data";
		telemetry.Complete(1, exception);

		var activity = Assert.Single(exporter.Activities);
		Assert.Equal(typeof(InvalidOperationException).FullName, activity.GetTagItem("error.type"));
		Assert.Equal(4, activity.TagObjects.Count());
		Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains("secret", StringComparison.Ordinal) == true);
		Assert.DoesNotContain(activity.TagObjects, tag => tag.Value?.ToString()?.Contains("private", StringComparison.Ordinal) == true);
		Assert.Null(activity.StatusDescription);
		Assert.Empty(activity.Events);
	}

	[Fact]
	public void Complete_Cancellation_DoesNotReportAnError()
	{
		using var telemetry = Create(out var exporter, out _, out _);
		telemetry.Complete(130, new OperationCanceledException("private cancellation details"));

		var activity = Assert.Single(exporter.Activities);
		Assert.Equal("cancelled", activity.GetTagItem("maui.cli.command.outcome"));
		Assert.Null(activity.GetTagItem("error.type"));
		Assert.Equal(ActivityStatusCode.Unset, activity.Status);
	}

	[Fact]
	public void Complete_CalledTwice_ExportsOnlyTheFinalCommandResultOnce()
	{
		using var telemetry = Create(out var exporter, out _, out _);
		telemetry.Complete(1);
		telemetry.Complete(0);

		Assert.Equal(1, Assert.Single(exporter.Activities).GetTagItem("process.exit.code"));
	}

	[Fact]
	public void Dispose_WithoutCompletion_DoesNotReportSuccess()
	{
		var telemetry = Create(out var exporter, out _, out _);
		telemetry.Dispose();
		telemetry.Dispose();

		var activity = Assert.Single(exporter.Activities);
		Assert.Equal("error", activity.GetTagItem("maui.cli.command.outcome"));
		Assert.Equal(1, activity.GetTagItem("process.exit.code"));
		Assert.Equal(1, exporter.DisposeCount);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Dispose_FailedShutdown_IsBoundedAndOnlyDiagnosedInVerboseMode(bool verbose)
	{
		var telemetry = Create(out var exporter, out _, out var diagnostics, verbose, shutdownSucceeds: false);
		telemetry.Complete(0);
		telemetry.Dispose();

		Assert.InRange(exporter.ShutdownTimeout, 0, CliTelemetry.ShutdownTimeoutMilliseconds);
		Assert.Equal(verbose, diagnostics.ToString().Contains("Shutdown did not complete", StringComparison.Ordinal));
		Assert.Equal(1, exporter.DisposeCount);
		Assert.Equal(0, Assert.Single(exporter.Activities).GetTagItem("process.exit.code"));
	}

	[Fact]
	public void CreateProviderBuilder_ResourceContainsOnlyAllowlistedMetadata()
	{
		using var telemetry = Create(out _, out var resource, out _);

		Assert.Equal("maui-cli", resource.Attributes.Single(attribute => attribute.Key == "service.name").Value);
		Assert.Equal(new[]
		{
			"host.arch", "os.type", "os.version", "process.runtime.version", "service.instance.id", "service.name", "service.version"
		}, resource.Attributes.Select(attribute => attribute.Key).Order(StringComparer.Ordinal));
		Assert.Equal("maui-cli", resource.Attributes.Single(attribute => attribute.Key == "service.instance.id").Value);
	}

	[Fact]
	public async Task AzureExporter_SerializedPayload_DoesNotAddMachineIdentifiers()
	{
		using var parent = new Activity("private-parent")
			.AddBaggage("token", "private-token")
			.SetTag("microsoft.gen_ai.main_agent.name", "private-agent")
			.Start();
		using var handler = new CaptureHandler(HttpStatusCode.OK);
		using var client = new HttpClient(handler);
		using var telemetry = CreateAzure(client, disableOfflineStorage: true, out var provider);
		telemetry.Complete(0);
		Assert.True(provider.ForceFlush(5_000));
		var payload = await handler.Payload.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.DoesNotContain("private-token", payload);
		Assert.DoesNotContain("private-agent", payload);
		using var document = JsonDocument.Parse("[" + string.Join(",",
			payload.Split('\n', StringSplitOptions.RemoveEmptyEntries)) + "]");
		var envelope = Assert.Single(document.RootElement.EnumerateArray(), item =>
			item.GetProperty("data").GetProperty("baseData").TryGetProperty("name", out var name) &&
			name.GetString() == "maui/cli/command");
		var data = envelope.GetProperty("data").GetProperty("baseData");
		Assert.Equal("maui/cli/command", data.GetProperty("name").GetString());
		Assert.Equal("project check", data.GetProperty("properties").GetProperty("maui.cli.command.name").GetString());
		Assert.Equal("success", data.GetProperty("properties").GetProperty("maui.cli.command.outcome").GetString());
		Assert.Equal("maui-cli", envelope.GetProperty("tags").GetProperty("ai.cloud.roleInstance").GetString());
		Assert.False(envelope.GetProperty("tags").TryGetProperty("ai.user.id", out _));
		Assert.False(envelope.GetProperty("tags").TryGetProperty("ai.device.id", out _));
	}

	[Fact]
	public void AzureExporter_ShutdownWithUnavailableDestination_PersistsCommandBeforeReturning()
	{
		using var handler = new CaptureHandler(HttpStatusCode.ServiceUnavailable);
		using var client = new HttpClient(handler);
		var telemetry = CreateAzure(client, disableOfflineStorage: false, out _);
		telemetry.Complete(0);
		telemetry.Dispose();

		Assert.True(Directory.Exists(_directory));
		Assert.Contains(Directory.GetFiles(_directory, "*", SearchOption.AllDirectories),
			path => File.ReadAllText(path).Contains("project check", StringComparison.Ordinal));
	}

	[Fact]
	public void ConfigureProcess_DisablesUnfilteredExporterHealthTelemetry()
	{
		var settings = new Dictionary<string, string?>();
		CliTelemetry.ConfigureProcess((name, value) => settings.Add(name, value));

		Assert.Equal(2, settings.Count);
		Assert.Equal("true", settings["APPLICATIONINSIGHTS_STATSBEAT_DISABLED"]);
		Assert.Equal("true", settings["APPLICATIONINSIGHTS_SDKSTATS_DISABLED"]);
	}

	[Fact]
	public void ConfigureExporter_DisablesAutomaticCollectionAndBoundsNetworkTime()
	{
		var options = new AzureMonitorExporterOptions();
		CliTelemetry.ConfigureExporter(options, "maui-destination", _directory);

		Assert.Equal("maui-destination", options.ConnectionString);
		Assert.Equal(_directory, options.StorageDirectory);
		Assert.False(options.EnableLiveMetrics);
		Assert.False(options.EnableStandardMetrics);
		Assert.False(options.EnablePerformanceCounters);
		Assert.False(options.DisableOfflineStorage);
		Assert.Null(options.TracesPerSecond);
		Assert.Equal(1f, options.SamplingRatio);
		Assert.Equal(TimeSpan.FromSeconds(1), options.Retry.NetworkTimeout);
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("MAUI_CLI_TELEMETRY_OPTOUT", "true")]
	[InlineData("DOTNET_CLI_TELEMETRY_OPTOUT", "1")]
	[InlineData("OTEL_SDK_DISABLED", "true")]
	public void Start_Disabled_DoesNotCreateStorageOrWriteOutput(string? optOut, string? value)
	{
		using var diagnostics = new StringWriter();
		var parse = CliTelemetryPolicyTests.CreateRoot().Parse("project check --verbose");
		using var telemetry = CliTelemetry.Start(parse, key => key == optOut ? value : null,
			optOut is null ? null : "invalid-destination", _directory, diagnostics, redirected: false);

		Assert.Null(telemetry);
		Assert.False(Directory.Exists(_directory));
		Assert.Equal("", diagnostics.ToString());
	}

	[Fact]
	public void Start_InvalidDestination_IsNonFatalAndDoesNotPrintTheConnectionString()
	{
		using var diagnostics = new StringWriter();
		var parse = CliTelemetryPolicyTests.CreateRoot().Parse("project check --verbose");
		using var telemetry = CliTelemetry.Start(parse, _ => null, "invalid-private-destination",
			_directory, diagnostics, redirected: true);

		Assert.Null(telemetry);
		Assert.Contains("Initialization failed", diagnostics.ToString());
		Assert.DoesNotContain("invalid-private-destination", diagnostics.ToString());
	}

	[Fact]
	public void Start_NoticeStorageUnavailable_DisablesReportingWithoutFailingTheCommand()
	{
		Directory.CreateDirectory(_directory);
		var blocked = Path.Combine(_directory, "blocked");
		File.WriteAllText(blocked, "");
		using var diagnostics = new StringWriter();
		var parse = CliTelemetryPolicyTests.CreateRoot().Parse("project check --verbose");
		using var telemetry = CliTelemetry.Start(parse, _ => null, "unused-destination",
			blocked, diagnostics, redirected: false);

		Assert.Null(telemetry);
		Assert.Contains("Initialization failed", diagnostics.ToString());
		Assert.DoesNotContain(blocked, diagnostics.ToString());
	}

	[Fact]
	public void ShowNotice_OnlyWritesOnceAndIncludesOptOutInstructions()
	{
		using var diagnostics = new StringWriter();
		CliTelemetry.ShowNotice(_directory, diagnostics);
		var first = diagnostics.ToString();
		CliTelemetry.ShowNotice(_directory, diagnostics);

		Assert.Equal(first, diagnostics.ToString());
		Assert.Contains("MAUI_CLI_TELEMETRY_OPTOUT=1", first);
		Assert.Contains("README.md#telemetry", first);
		Assert.True(File.Exists(Path.Combine(_directory, "notice-v1")));
	}

	static CliTelemetry Create(out RecordingExporter exporter, out Resource resource, out StringWriter diagnostics,
		bool verbose = false, bool shutdownSucceeds = true)
	{
		var source = new ActivitySource($"{CliTelemetry.ActivitySourceName}.Tests.{Guid.NewGuid():N}");
		exporter = new RecordingExporter { ShutdownSucceeds = shutdownSucceeds };
		var provider = CliTelemetry.CreateProviderBuilder(source.Name)
			.AddProcessor(new SimpleActivityExportProcessor(exporter))
			.Build();
		resource = provider.GetResource();
		diagnostics = new StringWriter();
		return new CliTelemetry(source, provider, "project check", diagnostics, verbose);
	}

	CliTelemetry CreateAzure(HttpClient client, bool disableOfflineStorage, out TracerProvider provider)
	{
		var source = new ActivitySource($"{CliTelemetry.ActivitySourceName}.Tests.{Guid.NewGuid():N}");
		provider = CliTelemetry.CreateProviderBuilder(source.Name)
			.AddAzureMonitorTraceExporter(options =>
			{
				CliTelemetry.ConfigureExporter(options,
					"InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://telemetry.example.test/",
					_directory);
				options.Transport = new HttpClientTransport(client);
				options.DisableOfflineStorage = disableOfflineStorage;
				options.Retry.MaxRetries = 0;
			})
			.Build();
		return new CliTelemetry(source, provider, "project check", TextWriter.Null, verbose: false);
	}

	public void Dispose()
	{
		if (Directory.Exists(_directory))
			Directory.Delete(_directory, recursive: true);
	}

	sealed class RecordingExporter : BaseExporter<Activity>
	{
		internal List<Activity> Activities { get; } = [];
		internal bool ShutdownSucceeds { get; init; }
		internal int ShutdownTimeout { get; private set; }
		internal int DisposeCount { get; private set; }

		public override ExportResult Export(in Batch<Activity> batch)
		{
			foreach (var activity in batch)
				Activities.Add(activity);
			return ExportResult.Success;
		}

		protected override bool OnShutdown(int timeoutMilliseconds)
		{
			ShutdownTimeout = timeoutMilliseconds;
			return ShutdownSucceeds;
		}

		protected override void Dispose(bool disposing)
		{
			DisposeCount++;
			base.Dispose(disposing);
		}
	}

	sealed class CaptureHandler(HttpStatusCode statusCode) : HttpMessageHandler
	{
		internal TaskCompletionSource<string> Payload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			if (request.Content is not null)
			{
				using var content = new MemoryStream(await request.Content.ReadAsByteArrayAsync(cancellationToken));
				using var decoded = request.Content.Headers.ContentEncoding.Contains("gzip")
					? new GZipStream(content, CompressionMode.Decompress)
					: (Stream)content;
				using var reader = new StreamReader(decoded);
				var payload = await reader.ReadToEndAsync(cancellationToken);
				if (payload.Contains("maui/cli/command", StringComparison.Ordinal))
					Payload.TrySetResult(payload);
			}
			return new HttpResponseMessage(statusCode)
			{
				Content = new StringContent("""{"itemsReceived":1,"itemsAccepted":1,"errors":[]}""", Encoding.UTF8, "application/json")
			};
		}
	}
}
