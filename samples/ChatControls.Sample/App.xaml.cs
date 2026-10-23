using Microsoft.Extensions.DependencyInjection;

namespace ChatControls.Sample;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<MainPage>());
}
