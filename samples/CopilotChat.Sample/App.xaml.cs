namespace CopilotChat.Sample;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    private readonly IServiceProvider _services;

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_services.GetRequiredService<MainPage>()) { Title = "Copilot Chat" };
    }
}