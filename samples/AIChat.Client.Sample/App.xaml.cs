namespace AIChat.Client.Sample;

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
