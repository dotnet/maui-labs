using Microsoft.Maui.Controls;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiBorder = Microsoft.Maui.Controls.Border;

namespace CometFeatureShowcase.Pages;

public class TabDemoPageState { }

public class TabDemoPage : Component<TabDemoPageState>
{
    int selectedTabIndex = 0;
    Microsoft.Maui.Controls.StackLayout? contentArea;

    public override Comet.View Render()
    {
        var root = new Microsoft.Maui.Controls.Grid { BackgroundColor = Color.FromArgb("#F5F5F5") };

        var content = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        // Header
        var header = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(16, 16, 16, 0),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        header.Add(new MauiLabel
        {
            Text = "TabView Demo",
            TextColor = Colors.Black,
            FontSize = 24,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
        });

        header.Add(new MauiLabel
        {
            Text = "Alternative navigation using TabView-like interface",
            TextColor = Colors.DarkGray,
            FontSize = 14,
            Margin = new Thickness(0, 4, 0, 12),
        });

        content.Add(header);

        // Tab buttons
        var tabButtons = new Microsoft.Maui.Controls.HorizontalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0),
            BackgroundColor = Colors.White,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var tabs = new[] { "Tab 1", "Tab 2", "Tab 3" };
        contentArea = new Microsoft.Maui.Controls.StackLayout();

        for (int i = 0; i < tabs.Length; i++)
        {
            int tabIndex = i;
            var tab = new Microsoft.Maui.Controls.Button
            {
                Text = tabs[i],
                BackgroundColor = i == 0 ? Color.FromArgb("#2196F3") : Colors.White,
                TextColor = i == 0 ? Colors.White : Colors.Black,
                CornerRadius = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                FontSize = 13,
            };
            tab.Clicked += (s, e) =>
            {
                SelectTab(tabIndex, tabButtons);
            };
            tabButtons.Add(tab);
        }

        content.Add(tabButtons);

        // Content area
        UpdateTabContent(0, contentArea);

        var contentBorder = new MauiBorder
        {
            StrokeThickness = 0,
            Padding = new Thickness(16),
            BackgroundColor = Color.FromArgb("#F5F5F5"),
            Content = contentArea,
        };

        content.Add(contentBorder);
        root.Add(content);

        return new MauiViewHost(root);
    }

    void SelectTab(int index, Microsoft.Maui.Controls.HorizontalStackLayout tabButtons)
    {
        if (selectedTabIndex == index || contentArea == null) return;

        // Update button styles
        var buttons = tabButtons.Children.OfType<Microsoft.Maui.Controls.Button>().ToList();
        for (int i = 0; i < buttons.Count; i++)
        {
            buttons[i].BackgroundColor = i == index ? Color.FromArgb("#2196F3") : Colors.White;
            buttons[i].TextColor = i == index ? Colors.White : Colors.Black;
        }

        selectedTabIndex = index;
        UpdateTabContent(index, contentArea);
    }

    void UpdateTabContent(int index, Microsoft.Maui.Controls.StackLayout contentArea)
    {
        contentArea.Clear();

        var stack = new Microsoft.Maui.Controls.VerticalStackLayout
        {
            Spacing = 12,
            BackgroundColor = Color.FromArgb("#F5F5F5"),
        };

        switch (index)
        {
            case 0:
                stack.Add(new MauiLabel
                {
                    Text = "Tab 1 Content",
                    TextColor = Colors.Black,
                    FontSize = 18,
                    FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
                });
                stack.Add(new MauiLabel
                {
                    Text = "This is the first tab. TabView provides an alternative way to organize content compared to Shell tabs.",
                    TextColor = Colors.DarkGray,
                    FontSize = 14,
                });
                break;
            case 1:
                stack.Add(new MauiLabel
                {
                    Text = "Tab 2 Content",
                    TextColor = Colors.Black,
                    FontSize = 18,
                    FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
                });
                stack.Add(new MauiLabel
                {
                    Text = "This is the second tab. Use SelectedIndex property to control which tab is active.",
                    TextColor = Colors.DarkGray,
                    FontSize = 14,
                });
                break;
            case 2:
                stack.Add(new MauiLabel
                {
                    Text = "Tab 3 Content",
                    TextColor = Colors.Black,
                    FontSize = 18,
                    FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
                });
                stack.Add(new MauiLabel
                {
                    Text = "This is the third tab. Perfect for organizing related content in a compact UI.",
                    TextColor = Colors.DarkGray,
                    FontSize = 14,
                });
                break;
        }

        contentArea.Add(stack);
    }
}
