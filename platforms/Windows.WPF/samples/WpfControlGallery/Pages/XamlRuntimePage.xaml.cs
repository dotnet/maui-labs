using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample.Pages;

public partial class XamlRuntimePage : ContentPage
{
	public XamlRuntimePage()
	{
		InitializeComponent();
		UpdateStatus("Page loaded");
	}

	void OnUpdateTimestampClicked(object? sender, EventArgs e)
	{
		UpdateStatus("Button clicked");
	}

	void UpdateStatus(string prefix)
	{
		StatusLabel.Text = $"{prefix}: {DateTime.Now:HH:mm:ss}";
	}
}
