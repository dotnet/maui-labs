using System;
using Comet;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;
using MauiApplication = Microsoft.Maui.Controls.Application;
using MauiContentPage = Microsoft.Maui.Controls.ContentPage;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace CometControlsGallery.Pages
{
	public class MultiWindowState
	{
		public int WindowCount { get; set; } = 1;
		public string StatusText { get; set; } = "";
		public Color StatusColor { get; set; } = Colors.Grey;
	}

	public class MultiWindowPage : Component<MultiWindowState>
	{
		public MultiWindowPage()
		{
			UpdateWindowCount();
		}

		public override View Render() => GalleryPageHelpers.Scaffold("Multi-Window",
			// Title
			Text("Multi-Window Support")
				.FontSize(24)
				.FontWeight(FontWeight.Bold),

			Text(() => $"Windows: {State.WindowCount}")
				.FontSize(16),

			// Window Styles section
			GalleryPageHelpers.Section("Window Styles",
				Button("Open New Window", OpenNewWindow),
				Button("Open Unified Window", OpenUnifiedWindow),
				Button("Open Unified Compact Window", OpenCompactWindow),
				Button("Open Expanded Window", OpenExpandedWindow)
			),

			// Sidebar Demos section
			GalleryPageHelpers.Section("Sidebar Demos",
				Text("Each opens a new window with the specified sidebar style.")
					.FontSize(13)
					.Color(Colors.Grey),
				Button("Shell -- Native Sidebar", () => OpenShellWindow(true)),
				Button("Shell -- Custom Sidebar", () => OpenShellWindow(false))
			),

			// Manage section
			GalleryPageHelpers.Section("Manage Windows",
				Button("Close This Window", CloseThisWindow)
					.Background(Colors.Red)
					.Color(Colors.White)
			),

			// Status
			Text(() => State.StatusText)
				.FontSize(14)
				.Color(() => State.StatusColor)
		);

		void UpdateWindowCount()
		{
			var count = MauiApplication.Current?.Windows?.Count ?? 0;
			SetState(s => s.WindowCount = count);
		}

		void OpenNewWindow()
		{
			var secondaryPage = new MauiContentPage
			{
				Title = "Secondary Window",
				Content = new Microsoft.Maui.Controls.VerticalStackLayout
				{
					VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					Spacing = 16,
					Children =
					{
						new Microsoft.Maui.Controls.Label
						{
							Text = "Secondary Window",
							FontSize = 24,
							FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
						},
						new Microsoft.Maui.Controls.Label
						{
							Text = "This is a new window opened via Application.OpenWindow().\nClose it with the red button or the button below.",
							MaximumWidthRequest = 400,
						},
					}
				}
			};
			AddCloseButton(secondaryPage);

			MauiApplication.Current?.OpenWindow(new MauiWindow(secondaryPage));
			UpdateWindowCount();
			SetState(s => { s.StatusText = "Opened new window"; s.StatusColor = Colors.Green; });
		}

		void OpenUnifiedWindow()
		{
			var page = CreateStyledSecondaryPage("Unified Window");
			var win = new MauiWindow(page) { Title = "Unified Window" };
			MauiApplication.Current?.OpenWindow(win);
			UpdateWindowCount();
			SetState(s => { s.StatusText = "Opened unified window"; s.StatusColor = Colors.Green; });
		}

		void OpenCompactWindow()
		{
			var page = CreateStyledSecondaryPage("Unified Compact Window");
			var win = new MauiWindow(page) { Title = "Unified Compact Window" };
			MauiApplication.Current?.OpenWindow(win);
			UpdateWindowCount();
			SetState(s => { s.StatusText = "Opened compact window"; s.StatusColor = Colors.Green; });
		}

		void OpenExpandedWindow()
		{
			var page = CreateStyledSecondaryPage("Expanded Window");
			var win = new MauiWindow(page) { Title = "Expanded Window" };
			MauiApplication.Current?.OpenWindow(win);
			UpdateWindowCount();
			SetState(s => { s.StatusText = "Opened expanded window"; s.StatusColor = Colors.Green; });
		}

		void OpenShellWindow(bool useNative)
		{
			var title = useNative ? "Shell -- Native Sidebar" : "Shell -- Custom Sidebar";
			var shell = new Microsoft.Maui.Controls.Shell
			{
				Title = title,
				FlyoutBehavior = FlyoutBehavior.Locked,
			};

			var general = new Microsoft.Maui.Controls.FlyoutItem { Title = "General" };
			general.Items.Add(new Microsoft.Maui.Controls.ShellContent
			{
				Title = "Home",
				Route = "demohome",
				ContentTemplate = new Microsoft.Maui.Controls.DataTemplate(
					() => MakeShellPage("Home", "Welcome to the Shell demo!", "#4A90E2")),
			});
			general.Items.Add(new Microsoft.Maui.Controls.ShellContent
			{
				Title = "Settings",
				Route = "demosettings",
				ContentTemplate = new Microsoft.Maui.Controls.DataTemplate(
					() => MakeShellPage("Settings", "Adjust your preferences here.", "#7B68EE")),
			});
			shell.Items.Add(general);

			var more = new Microsoft.Maui.Controls.FlyoutItem { Title = "More" };
			more.Items.Add(new Microsoft.Maui.Controls.ShellContent
			{
				Title = "About",
				Route = "demoabout",
				ContentTemplate = new Microsoft.Maui.Controls.DataTemplate(
					() => MakeShellPage("About", "MAUI macOS sidebar demo.", "#2ECC71")),
			});
			shell.Items.Add(more);

			MauiApplication.Current?.OpenWindow(new MauiWindow(shell));
			UpdateWindowCount();
			SetState(s => { s.StatusText = $"Opened {title}"; s.StatusColor = Colors.Green; });
		}

		void CloseThisWindow()
		{
			var window = MauiApplication.Current?.Windows?.Count > 0
				? MauiApplication.Current.Windows[0]
				: null;
			if (window != null)
				MauiApplication.Current?.CloseWindow(window);
		}

		static MauiContentPage CreateStyledSecondaryPage(string title)
		{
			var page = new MauiContentPage
			{
				Title = title,
				Content = new Microsoft.Maui.Controls.VerticalStackLayout
				{
					VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					Spacing = 16,
					Children =
					{
						new Microsoft.Maui.Controls.Label
						{
							Text = title,
							FontSize = 24,
							FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
						},
						new Microsoft.Maui.Controls.Label
						{
							Text = "This is a new window opened via Application.OpenWindow().\nClose it with the button below.",
							MaximumWidthRequest = 400,
						},
					}
				}
			};
			AddCloseButton(page);
			return page;
		}

		static MauiContentPage MakeShellPage(string title, string description, string accent)
		{
			return new MauiContentPage
			{
				Title = title,
				Content = new Microsoft.Maui.Controls.VerticalStackLayout
				{
					VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
					Spacing = 16,
					Children =
					{
						new Microsoft.Maui.Controls.Border
						{
							BackgroundColor = Color.FromArgb(accent),
							HeightRequest = 4,
							WidthRequest = 200,
							HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
							StrokeThickness = 0,
							StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 },
						},
						new Microsoft.Maui.Controls.Label
						{
							Text = title,
							FontSize = 28,
							FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
						},
						new Microsoft.Maui.Controls.Label
						{
							Text = description,
							FontSize = 16,
							TextColor = Colors.Grey,
						},
					}
				}
			};
		}

		static void AddCloseButton(MauiContentPage page)
		{
			var closeBtn = new Microsoft.Maui.Controls.Button { Text = "Close This Window" };
			closeBtn.Clicked += (s, e) =>
			{
				if (page.Window != null)
					MauiApplication.Current?.CloseWindow(page.Window);
			};
			((Microsoft.Maui.Controls.VerticalStackLayout)page.Content).Children.Add(closeBtn);
		}
	}
}
