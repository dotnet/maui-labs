using ChatControls.Sample;

namespace ChatControls.BlazorHybrid.Sample;

public partial class MainPage : ContentPage
{
    public MainPage()
        : this(new TeamChatViewModel())
    {
    }

    public MainPage(TeamChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
