namespace ControlGallery.Pages.Features;

public partial class AppThemeViewModel : ObservableObject
{

	public AppTheme CurrentTheme
	{
		get {
			return Application.Current.UserAppTheme;
		}
		set
		{
            if (App.Current.UserAppTheme == value)
                return;

            App.Current.UserAppTheme = value;
            //App.Current.PlatformAppTheme = AppTheme.Unspecified;
	        //App.Current.RequestedTheme
            OnPropertyChanged(nameof(CurrentTheme));
        }
	}

	[RelayCommand]
	void ThemeChanged(AppTheme theme)
	{

        if (App.Current.UserAppTheme == theme)
            return;

        App.Current.UserAppTheme = theme;
		OnPropertyChanged(nameof(CurrentTheme));
    }

    public AppThemeViewModel()
    {
        App.Current.RequestedThemeChanged += Current_RequestedThemeChanged;
    }

    private void Current_RequestedThemeChanged(object sender, AppThemeChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentTheme));
    }
}

