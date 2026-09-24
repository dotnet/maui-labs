using Microsoft.Extensions.DependencyInjection;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Application entry point for the AI playground.</summary>
public partial class App : Application
{
    /// <summary>Initializes the application.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override Window CreateWindow(IActivationState? activationState)
    {
        if (activationState is null)
            throw new InvalidOperationException("An activation state is required to create the playground window.");

        var services = activationState.Context.Services;
        var chat = services.GetRequiredService<MainPage>();
        var pages = new TabbedPage
        {
            Children =
            {
                chat,
                services.GetRequiredService<EmbeddingPage>(),
            },
        };
        pages.CurrentPageChanged += (_, _) =>
        {
            if (pages.CurrentPage != chat)
                ((ViewModels.MainViewModel)chat.BindingContext).Library.Close();
        };
        return new Window(pages);
    }
}
