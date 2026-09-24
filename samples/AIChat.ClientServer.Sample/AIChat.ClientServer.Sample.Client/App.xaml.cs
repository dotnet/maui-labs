namespace AIChat.ClientServer.Sample.Client;

public partial class App : Application
{
    public App(MainPage page)
    {
        InitializeComponent();
        _page = page;
    }

    private readonly MainPage _page;

    protected override Window CreateWindow(IActivationState? activationState) => new(_page);
}
