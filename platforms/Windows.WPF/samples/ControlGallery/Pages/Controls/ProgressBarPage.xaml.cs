using Microsoft.Maui.Dispatching;

namespace ControlGallery.Pages;
public partial class ProgressBarPage : ContentPage
{
    private bool isActiveWindow;

    public ProgressBarPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isActiveWindow = true;
        this.Dispatcher.StartTimer(TimeSpan.FromSeconds(0.05), TimerCallback);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        isActiveWindow = false;
    }

    bool TimerCallback()
    {
        progressBar.Progress += 0.01;
        if(isActiveWindow && progressBar.Progress == 1)
            progressBar.Progress = 0;
        return isActiveWindow || progressBar.Progress == 1;
    }
}