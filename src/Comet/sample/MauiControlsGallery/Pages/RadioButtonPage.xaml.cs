namespace MauiControlsGallery.Pages;

public partial class RadioButtonPage : ContentPage
{
	public RadioButtonPage()
	{
		InitializeComponent();
	}

	void OnSizeChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value && sender is RadioButton rb)
			SelectedLabel.Text = $"Selected: {rb.Content}";
	}

	void OnColorChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value && sender is RadioButton rb)
			SelectedLabel.Text = $"Selected: {rb.Content}";
	}

	void OnPlanChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value && sender is RadioButton rb)
			SelectedLabel.Text = $"Selected: {rb.Content}";
	}
}
