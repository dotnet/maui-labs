using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Testing.Platform.Builder;

namespace Microsoft.Maui.Testing;

public sealed class MauiTestAppBuilder
{
    private readonly MauiAppBuilder _mauiAppBuilder;
    private Action<ITestApplicationBuilder>? _configureTestApplication;
    private MauiTestApplicationRunner? _testApplicationRunner;

    internal MauiTestAppBuilder(MauiTestAppOptions options)
    {
        _mauiAppBuilder = MauiApp.CreateBuilder()
            .UseMauiApp<MauiTestApplication>();
        Options = options;
    }

    public IServiceCollection Services => _mauiAppBuilder.Services;

    internal MauiTestAppOptions Options { get; }

    public MauiTestAppBuilder ConfigureTestApplication(Action<ITestApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        EnsureTestApplicationIsNotConfigured();
        _configureTestApplication = configure;
        return this;
    }

    public MauiTestAppBuilder ConfigureTestApplication(MauiTestApplicationRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        EnsureTestApplicationIsNotConfigured();
        _testApplicationRunner = runner;
        return this;
    }

    private void EnsureTestApplicationIsNotConfigured()
    {
        if (_configureTestApplication is not null || _testApplicationRunner is not null)
        {
            throw new InvalidOperationException("The test application has already been configured.");
        }
    }

    public MauiTestApp Build()
    {
        if (_configureTestApplication is null && _testApplicationRunner is null)
        {
            throw new InvalidOperationException(
                "No test framework is configured. Call ConfigureTestApplication and register a framework such as MSTest.");
        }

        Services.AddSingleton(Options);
        return new MauiTestApp(
            _mauiAppBuilder.Build(),
            _configureTestApplication,
            _testApplicationRunner,
            Options);
    }
}

internal sealed class MauiTestApplication : Application;
