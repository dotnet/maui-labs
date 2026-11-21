using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Maui.Accessibility;
using WpfApplication = System.Windows.Application;
using WpfAutomationProperties = System.Windows.Automation.AutomationProperties;
using WpfGrid = System.Windows.Controls.Grid;
using WpfWindow = System.Windows.Window;

namespace Microsoft.Maui.Platforms.Windows.WPF.Essentials;

public sealed class WPFSemanticScreenReader : ISemanticScreenReader
{
	static readonly ConditionalWeakTable<WpfWindow, TextBlock> AnnouncementHosts = new();

	public void Announce(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return;

		var application = WpfApplication.Current
			?? throw new InvalidOperationException("A running WPF Application is required for semantic announcements.");

		if (application.Dispatcher.CheckAccess())
			AnnounceCore(application, text);
		else
			application.Dispatcher.Invoke(() => AnnounceCore(application, text));
	}

	static void AnnounceCore(WpfApplication application, string text)
	{
		var window = application.Windows.OfType<WpfWindow>().FirstOrDefault(candidate => candidate.IsActive)
			?? application.MainWindow
			?? throw new InvalidOperationException("A visible WPF Window is required for semantic announcements.");

		var host = AnnouncementHosts.GetValue(window, CreateAnnouncementHost);
		host.Text = text;
		WpfAutomationProperties.SetName(host, text);

		var peer = UIElementAutomationPeer.FromElement(host) ?? new TextBlockAutomationPeer(host);
		peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
	}

	static TextBlock CreateAnnouncementHost(WpfWindow window)
	{
		var host = new TextBlock
		{
			Width = 1,
			Height = 1,
			Opacity = 0,
			IsHitTestVisible = false,
			Focusable = false,
		};
		WpfAutomationProperties.SetLiveSetting(host, AutomationLiveSetting.Assertive);

		if (window.Content is Panel panel)
		{
			panel.Children.Add(host);
			return host;
		}

		var content = window.Content as UIElement;
		var container = new WpfGrid();
		window.Content = null;
		if (content is not null)
			container.Children.Add(content);
		container.Children.Add(host);
		window.Content = container;
		return host;
	}
}
