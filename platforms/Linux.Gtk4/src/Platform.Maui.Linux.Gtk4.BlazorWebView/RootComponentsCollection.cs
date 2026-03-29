using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.Web;

namespace Platform.Maui.Linux.Gtk4.BlazorWebView;

public class RootComponentsCollection : ObservableCollection<RootComponent>
{
	internal JSComponentConfigurationStore JSComponents { get; } = new();
}
