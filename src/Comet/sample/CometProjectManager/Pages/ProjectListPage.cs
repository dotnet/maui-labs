using CometProjectManager.Controls;
using CometProjectManager.Models;

using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;

namespace CometProjectManager.Pages;

public class ProjectListPageState { }

/// <summary>
/// Project list — matches the template's ProjectListPage exactly.
/// Uses MauiViewHost for the entire content to ensure pixel-perfect match.
/// </summary>
public class ProjectListPage : Component<ProjectListPageState>
{
[State] readonly DataStore _store = DataStore.Instance;
readonly Action? _onMenuTap;
readonly bool _wrapInNav;

public ProjectListPage(Action? onMenuTap = null, bool wrapInNav = true) { _onMenuTap = onMenuTap; _wrapInNav = wrapInNav; }

static readonly Color LightSecondaryBg = Color.FromArgb("#E0E0E0");
static readonly Color DarkOnLightBg = Color.FromArgb("#0D0D0D");
static readonly Color LightBg = Color.FromArgb("#F2F2F2");

public override View Render()
{
var projects = _store.Projects.Value ?? new List<Project>();

var rootGrid = new MauiGrid { BackgroundColor = LightBg };

var stack = new Microsoft.Maui.Controls.VerticalStackLayout
{
Padding = new Thickness(15),
Spacing = 5,
};

foreach (var project in projects)
{
var cardContent = new Microsoft.Maui.Controls.VerticalStackLayout { Padding = new Thickness(10) };
cardContent.Add(new MauiLabel
{
Text = project.Name,
FontSize = 24,
TextColor = DarkOnLightBg,
});
cardContent.Add(new MauiLabel
{
Text = project.Description,
TextColor = DarkOnLightBg,
});

var card = new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) },
Background = new SolidColorBrush(LightSecondaryBg),
StrokeThickness = 0,
Padding = new Thickness(15),
Content = cardContent,
};

var tap = new Microsoft.Maui.Controls.TapGestureRecognizer();
var p = project;
tap.Tapped += (s, e) => AppNavigation.NavigateToProject(p, Navigation);
card.GestureRecognizers.Add(tap);

stack.Add(card);
}

rootGrid.Add(new MauiScrollView { Content = stack });

// FAB
var fab = new AddButtonControl(() =>
{
var newProject = new Project
{
Name = "New Project",
Description = "Tap to edit",
Icon = "\uea28",
CategoryID = 1,
};
_store.AddProject(newProject);
AppNavigation.NavigateToProject(newProject, Navigation);
});
rootGrid.Add(fab);

if (!_wrapInNav) return new MauiViewHost(rootGrid);

var nav = NavigationView(
new MauiViewHost(rootGrid)
);
if (_onMenuTap != null) { nav.LeadingBarAction = _onMenuTap; }
return nav.Title("Projects").Background(LightBg);
}
}
