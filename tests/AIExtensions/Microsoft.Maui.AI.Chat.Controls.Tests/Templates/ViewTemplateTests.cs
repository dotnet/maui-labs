using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// <see cref="ViewTemplate"/> takes its services from <see cref="IPlatformApplication.Current"/>, which these tests set,
/// so they restore it afterwards.
/// </summary>
public sealed class ViewTemplateTests : IDisposable
{
    private readonly IPlatformApplication? _previous = IPlatformApplication.Current;

    public ViewTemplateTests() => IPlatformApplication.Current = null;

    public void Dispose() => IPlatformApplication.Current = _previous;

    [Fact]
    public void WithoutAnApp_CreatesTheViewWithItsParameterlessConstructor()
    {
        var template = new ViewTemplate(typeof(RawTextLabel));

        var first = template.CreateContent();
        var second = template.CreateContent();

        Assert.IsType<RawTextLabel>(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void WithARegisteredView_ResolvesItFromTheAppServices()
    {
        var greeting = new GreetingService();
        UseServices(new ServiceCollection()
            .AddSingleton(greeting)
            .AddTransient(services => new ServiceBackedView(services.GetRequiredService<GreetingService>()) { AutomationId = "registered" }));

        var view = Assert.IsType<ServiceBackedView>(new ViewTemplate(typeof(ServiceBackedView)).CreateContent());

        Assert.Equal("registered", view.AutomationId);
        Assert.Same(greeting, view.Service);
    }

    [Fact]
    public void WithAnUnregisteredView_ResolvesItsConstructorDependencies()
    {
        var greeting = new GreetingService();
        UseServices(new ServiceCollection().AddSingleton(greeting));

        var view = Assert.IsType<ServiceBackedView>(new ViewTemplate(typeof(ServiceBackedView)).CreateContent());

        Assert.Same(greeting, view.Service);
    }

    [Fact]
    public void WithServicesMissingADependency_ExplainsWhy()
    {
        UseServices(new ServiceCollection());

        var error = Assert.Throws<InvalidOperationException>(() => new ViewTemplate(typeof(ServiceBackedView)).CreateContent());

        Assert.Contains("Register", error.Message);
        Assert.Contains(nameof(ServiceBackedView), error.Message);
    }

    [Fact]
    public void WithoutAnAppOrAParameterlessConstructor_ExplainsWhy()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ViewTemplate(typeof(ServiceBackedView)).CreateContent());

        Assert.Contains("parameterless constructor", error.Message);
    }

    [Fact]
    public void ATypeThatIsNotAView_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ViewTemplate(null!));
        Assert.Throws<ArgumentException>(() => new ViewTemplate(typeof(Span)));
    }

    private static void UseServices(IServiceCollection services) =>
        IPlatformApplication.Current = new TestApplication(services.BuildServiceProvider());

    private sealed class TestApplication(IServiceProvider services) : IPlatformApplication
    {
        public IServiceProvider Services { get; } = services;

        public IApplication Application => throw new NotSupportedException();
    }
}
