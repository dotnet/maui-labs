namespace ControlGallery.Pages;

public partial class ControlsPage : ContentPage
{
	public ControlsPage()
	{
		InitializeComponent();

		//InitMenu();
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
		// MenuItem.SetAccelerator(exitItem, Accelerator.FromString("cmd+q"));
		exitItem.KeyboardAccelerators.Add(new KeyboardAccelerator
		{
			Modifiers = KeyboardAcceleratorModifiers.Cmd,
			Key = "Q"
		});
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
		// MenuItem.SetKeyboardAccelerator(refreshItem, KeyboardAccelerator.FromString("cmd+r"));
		refreshItem.KeyboardAccelerators.Add(new KeyboardAccelerator
		{
			Modifiers = KeyboardAcceleratorModifiers.Cmd,
			Key = "R"
		});
		locationMenu.Add(refreshItem);

		this.MenuBarItems.Add(fileMenu);
		this.MenuBarItems.Add(locationMenu);
	}

	void ToolbarItem_Clicked(System.Object sender, System.EventArgs e)
	{
		ControlsList.ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical) { HorizontalItemSpacing = 8, VerticalItemSpacing = 8 };
	}

	void ToolbarItem_Clicked_1(System.Object sender, System.EventArgs e)
	{
		ControlsList.ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical);
	}
}

public class DisplayTemplateSelector : DataTemplateSelector
{
	public DataTemplate GridTemplate { get; set; }
	public DataTemplate ListTemplate { get; set; }

	protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
	{
		CollectionView colV = (CollectionView)container;
		return colV.ItemsLayout.GetType() == typeof(GridItemsLayout) ? GridTemplate : ListTemplate;
	}
}