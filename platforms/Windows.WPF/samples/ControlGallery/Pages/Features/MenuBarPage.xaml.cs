namespace ControlGallery.Pages;

public partial class MenuBarPage : ContentPage
{
    public MenuBarPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        InitMenu();
    }

    void InitMenu()
    {
        var fileMenu = new MenuBarItem { Text = "File" };
        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new Command((obj) =>
            {
                Application.Current.Quit();
            })
        };
        exitItem.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = "Q" });
        fileMenu.Add(exitItem);

        var locationMenu = new MenuBarItem { Text = "Location" };
        var changeItem = new MenuFlyoutItem
        {
            Text = "Change",
            Command = new Command((obj) =>
            {
                Navigation.PushAsync(new ButtonPage());
            })
        };
        locationMenu.Add(changeItem);

        var refreshItem = new MenuFlyoutItem
        {
            Text = "Refresh"
        };
        refreshItem.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = KeyboardAcceleratorModifiers.Cmd, Key = "R" });
        locationMenu.Add(refreshItem);

        this.MenuBarItems.Add(fileMenu);
        this.MenuBarItems.Add(locationMenu);
    }

}