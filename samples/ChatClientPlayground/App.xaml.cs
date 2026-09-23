namespace ChatClientPlayground;

/// <summary>Application entry point for the chat-client playground.</summary>
public partial class App : Application
{
    private readonly Func<MainPage> _mainPageFactory;

    /// <summary>Initializes the application with a deferred factory for its single page.</summary>
    public App(Func<MainPage> mainPageFactory)
    {
        InitializeComponent();
        _mainPageFactory = mainPageFactory;
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState) => new(_mainPageFactory());
}
