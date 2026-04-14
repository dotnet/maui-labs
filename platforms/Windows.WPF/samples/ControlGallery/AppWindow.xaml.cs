namespace ControlGallery;

public partial class AppWindow : Window
{
	public AppWindow()
	{
		InitializeComponent();

		var currentTheme = Application.Current!.UserAppTheme;		
		ThemeSegmentedControl.SelectedIndex = currentTheme == AppTheme.Light ? 0 : 1;
	}

	private void SfSegmentedControl_SelectionChanged(object sender, Syncfusion.Maui.Toolkit.SegmentedControl.SelectionChangedEventArgs e)
    {
		Application.Current!.UserAppTheme = e.NewIndex == 0 ? AppTheme.Light : AppTheme.Dark;
    }
}