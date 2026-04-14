namespace ControlGallery.Pages;

public partial class ContextMenuPage : ContentPage
{
    int count = 0;

    public ContextMenuPage()
    {
        InitializeComponent();

        BindingContext = this;
    }

    private void OnCounterClicked(object sender, EventArgs e)
    {
        count++;
        OnPropertyChanged(nameof(CounterValue));

    }

    public string CounterValue => count.ToString("N0");

    void OnIncrementMenuItemClicked(object sender, EventArgs e)
    {
        var menuItem = (MenuFlyoutItem)sender;
        var incrementAmount = int.Parse((string)menuItem.CommandParameter);
        count += incrementAmount;
        OnPropertyChanged(nameof(CounterValue));
    }
}