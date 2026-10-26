using Microsoft.Extensions.DependencyInjection;
using Microsoft.Testing.Extensions;
using Microsoft.Testing.Platform.Builder;
using Microsoft.Testing.Platform.Extensions;

namespace Microsoft.Maui.Testing;

public sealed class MauiTestApp : IDisposable
{
    private readonly Action<ITestApplicationBuilder>? _configureTestApplication;
    private readonly MauiTestApplicationRunner? _testApplicationRunner;
    private readonly MauiTestAppOptions _options;
    private bool _disposed;

    internal MauiTestApp(
        MauiApp mauiApp,
        Action<ITestApplicationBuilder>? configureTestApplication,
        MauiTestApplicationRunner? testApplicationRunner,
        MauiTestAppOptions options)
    {
        MauiApp = mauiApp;
        _configureTestApplication = configureTestApplication;
        _testApplicationRunner = testApplicationRunner;
        _options = options;
    }

    public MauiApp MauiApp { get; }

    public IServiceProvider Services => MauiApp.Services;

    public static MauiTestAppBuilder CreateBuilder() => new(new MauiTestAppOptions());

    public static MauiTestAppBuilder CreateBuilder(MauiTestAppOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(options);
    }

    internal async Task<MauiTestRunResult> RunAsync(
        string[] args,
        string defaultResultsDirectory,
        MauiTestResultConsumer consumer,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultResultsDirectory);
        ArgumentNullException.ThrowIfNull(consumer);

        Directory.CreateDirectory(defaultResultsDirectory);

        var effectiveArgs = CreateArguments(args, defaultResultsDirectory, _options.GenerateTrxReport);
        if (_testApplicationRunner is not null)
        {
            var exitCode = await _testApplicationRunner(
                effectiveArgs,
                (builder, _) => ConfigureMauiTesting(builder, consumer));
            cancellationToken.ThrowIfCancellationRequested();
            return consumer.CreateResult(exitCode);
        }

        var builder = await TestApplication.CreateBuilderAsync(effectiveArgs);
        _configureTestApplication!(builder);
        ConfigureMauiTesting(builder, consumer);

        using ITestApplication application = await builder.BuildAsync();
        var result = await application.RunAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return consumer.CreateResult(result);
    }

    private void ConfigureMauiTesting(
        ITestApplicationBuilder builder,
        MauiTestResultConsumer consumer)
    {
        builder.TestHost.AddDataConsumer(_ => consumer);
        if (_options.GenerateTrxReport)
        {
            builder.AddTrxReportProvider();
        }
    }

    internal static string[] CreateArguments(
        IEnumerable<string> args,
        string defaultResultsDirectory,
        bool generateTrxReport)
    {
        var result = args.ToList();
        if (!ContainsOption(result, "--results-directory"))
        {
            result.Add("--results-directory");
            result.Add(defaultResultsDirectory);
        }

        if (generateTrxReport &&
            !ContainsOption(result, "--report-trx"))
        {
            result.Add("--report-trx");
        }

        return [.. result];
    }

    private static bool ContainsOption(IEnumerable<string> args, string option) =>
        args.Any(argument =>
            argument.Equals(option, StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith($"{option}=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith($"{option}:", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        MauiApp.Dispose();
    }
}
