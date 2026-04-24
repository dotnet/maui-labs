using CometProjectManager.Controls;
using CometProjectManager.Models;
using Syncfusion.Maui.Toolkit.TextInputLayout;

using MauiGrid = Microsoft.Maui.Controls.Grid;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiButton = Microsoft.Maui.Controls.Button;
using MauiEntry = Microsoft.Maui.Controls.Entry;
using MauiPicker = Microsoft.Maui.Controls.Picker;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;
using MauiBorder = Microsoft.Maui.Controls.Border;
using MauiBoxView = Microsoft.Maui.Controls.BoxView;
using MauiImage = Microsoft.Maui.Controls.Image;
using MauiImageButton = Microsoft.Maui.Controls.ImageButton;
using MauiCheckBox = Microsoft.Maui.Controls.CheckBox;
using FontImageSource = Microsoft.Maui.Controls.FontImageSource;
using SolidColorBrush = Microsoft.Maui.Controls.SolidColorBrush;
using ColumnDefinition = Microsoft.Maui.Controls.ColumnDefinition;
using RowDefinition = Microsoft.Maui.Controls.RowDefinition;

namespace CometProjectManager.Pages;

public class ProjectDetailPageState { }

public class ProjectDetailPage : Component<ProjectDetailPageState>
{
[State] readonly DataStore _store = DataStore.Instance;
readonly Project _project;
readonly bool _wrapInNav;

static readonly Color Primary = Color.FromArgb("#512BD4");
static readonly Color LightSecondaryBg = Color.FromArgb("#E0E0E0");
static readonly Color DarkOnLightBg = Color.FromArgb("#0D0D0D");
static readonly Color LightBg = Color.FromArgb("#F2F2F2");

static readonly string[] Icons = { "\uea28", "\uf8fe", "\uf837", "\uf5a9", "\ue823", "\ue7ee", "\uea3a" };
static readonly string[] IconDescriptions = { "Balance", "Education", "Fitness", "People", "Document", "Settings", "Target" };

int _selectedIconIndex;
int _selectedCategoryIndex;

public ProjectDetailPage(Project project, bool wrapInNav = true)
{
_project = project;
_wrapInNav = wrapInNav;
var categories = DataStore.Instance.Categories.Value ?? new List<Category>();
_selectedCategoryIndex = Math.Max(0, categories.FindIndex(c => c.ID == project.CategoryID));
_selectedIconIndex = Math.Max(0, Array.IndexOf(Icons, project.Icon));
}

public override View Render()
{
var categories = _store.Categories.Value ?? new List<Category>();
var allTags = _store.Tags.Value ?? new List<Tag>();
var tasks = _project.Tasks ?? new List<ProjectTask>();

var contentStack = new Microsoft.Maui.Controls.VerticalStackLayout
{
Spacing = 5,
Padding = new Thickness(15),
};

// Name field
var nameEntry = new MauiEntry { Text = _project.Name };
contentStack.Add(new SfTextInputLayout
{
Hint = "Name",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = nameEntry,
});

// Description field
var descEntry = new MauiEntry { Text = _project.Description };
contentStack.Add(new SfTextInputLayout
{
Hint = "Description",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = descEntry,
});

// Category picker
var catPicker = new MauiPicker
{
ItemsSource = categories.Select(c => c.Title).ToList(),
SelectedIndex = _selectedCategoryIndex,
};
contentStack.Add(new SfTextInputLayout
{
Hint = "Category",
ContainerType = ContainerType.Outlined,
ContainerBackground = new SolidColorBrush(Colors.Transparent),
Content = catPicker,
});

// Icon header
contentStack.Add(new MauiLabel
{
Text = "Icon",
FontSize = 22,
FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
TextColor = DarkOnLightBg,
});

// Icon selection row
var iconStack = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 5 };
for (int i = 0; i < Icons.Length; i++)
{
var idx = i;
var isSelected = _selectedIconIndex == idx;

var iconGrid = new MauiGrid
{
RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(new GridLength(4)) },
RowSpacing = 6,
};

var iconLabel = new MauiLabel
{
Text = Icons[idx],
FontFamily = Fonts.FluentUI.FontFamily,
FontSize = 24,
VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
TextColor = DarkOnLightBg,
};
MauiGrid.SetRow(iconLabel, 0);
iconGrid.Add(iconLabel);

var indicator = new MauiBoxView
{
Color = isSelected ? Primary : Colors.Transparent,
HeightRequest = 4,
HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.Fill,
};
MauiGrid.SetRow(indicator, 1);
iconGrid.Add(indicator);

var tap = new Microsoft.Maui.Controls.TapGestureRecognizer();
tap.Tapped += (s, e) =>
{
_selectedIconIndex = idx;
_project.Icon = Icons[idx];
_store.SaveProject(_project);
};
iconGrid.GestureRecognizers.Add(tap);

iconStack.Add(iconGrid);
}
contentStack.Add(new MauiScrollView
{
Orientation = ScrollOrientation.Horizontal,
Content = iconStack,
HeightRequest = 44,
Margin = new Thickness(0, 0, 0, 15),
});

// Tags header
contentStack.Add(new MauiLabel
{
Text = "Tags",
FontSize = 22,
FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
TextColor = DarkOnLightBg,
});

// Tag chips
var tagStack = new Microsoft.Maui.Controls.HorizontalStackLayout { Spacing = 5 };
foreach (var tag in allTags)
{
var isSelected = _project.Tags.Any(t => t.ID == tag.ID);
var chip = new MauiBorder
{
StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(22) },
HeightRequest = 44,
StrokeThickness = 0,
Background = new SolidColorBrush(isSelected ? tag.DisplayColor : LightSecondaryBg),
Padding = new Thickness(18, 0, 18, 8),
Content = new MauiLabel
{
Text = tag.Title,
TextColor = isSelected ? LightBg : DarkOnLightBg,
FontSize = 16,
VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
},
};

var tagTap = new Microsoft.Maui.Controls.TapGestureRecognizer();
var currentTag = tag;
tagTap.Tapped += (s, e) =>
{
if (_project.Tags.Any(t => t.ID == currentTag.ID))
_project.Tags.RemoveAll(t => t.ID == currentTag.ID);
else
_project.Tags.Add(new Tag { ID = currentTag.ID, Title = currentTag.Title, ColorHex = currentTag.ColorHex });
_store.SaveProject(_project);
};
chip.GestureRecognizers.Add(tagTap);
tagStack.Add(chip);
}
contentStack.Add(new MauiScrollView
{
Orientation = ScrollOrientation.Horizontal,
Content = tagStack,
HeightRequest = 44,
Margin = new Thickness(0, 0, 0, 15),
});

// Save button
var saveBtn = new MauiButton { Text = "Save", HeightRequest = 44 };
saveBtn.Clicked += (s, e) =>
{
_project.Name = nameEntry.Text?.Trim() ?? "";
_project.Description = descEntry.Text?.Trim() ?? "";
var catIdx = catPicker.SelectedIndex;
if (catIdx >= 0 && catIdx < categories.Count)
_project.CategoryID = categories[catIdx].ID;
if (_selectedIconIndex >= 0 && _selectedIconIndex < Icons.Length)
_project.Icon = Icons[_selectedIconIndex];
_store.SaveProject(_project);
AppNavigation.GoBack(this);
_ = AppNavigation.ShowToastAsync("Project saved");
};
contentStack.Add(saveBtn);

// Delete button (only for existing projects)
if (_project.ID > 0)
{
var deleteBtn = new MauiButton
{
Text = "Delete",
HeightRequest = 44,
BackgroundColor = Color.FromArgb("#FF3300"),
TextColor = Colors.White,
};
deleteBtn.Clicked += (s, e) =>
{
_store.DeleteProject(_project.ID);
AppNavigation.GoBack(this);
_ = AppNavigation.ShowToastAsync("Project deleted");
};
contentStack.Add(deleteBtn);
}

// Tasks header with clean button
var tasksHeader = new MauiGrid { HeightRequest = 44 };
tasksHeader.Add(new MauiLabel
{
Text = "Tasks",
FontSize = 22,
FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
TextColor = DarkOnLightBg,
VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
});

if (tasks.Any(t => t.IsCompleted))
{
var cleanBtn = new MauiImageButton
{
Source = new FontImageSource
{
Glyph = Fonts.FluentUI.broom_32_regular,
FontFamily = Fonts.FluentUI.FontFamily,
Color = DarkOnLightBg,
Size = 24,
},
HorizontalOptions = Microsoft.Maui.Controls.LayoutOptions.End,
VerticalOptions = Microsoft.Maui.Controls.LayoutOptions.Center,
HeightRequest = 44,
WidthRequest = 44,
BackgroundColor = Colors.Transparent,
BorderWidth = 0,
Aspect = Aspect.Center,
};
cleanBtn.Clicked += (s, e) =>
{
_store.CleanCompletedTasks();
_ = AppNavigation.ShowToastAsync("All cleaned up!");
};
tasksHeader.Add(cleanBtn);
}
contentStack.Add(tasksHeader);

// Task rows
var taskRowsStack = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 5 };
foreach (var task in tasks)
{
taskRowsStack.Add(new TaskViewControl(
task.Title,
task.IsCompleted,
isChecked => _store.ToggleTaskComplete(task.ID),
() => AppNavigation.NavigateToTask(task, _project.ID, Navigation)
));
}
contentStack.Add(taskRowsStack);

// Root grid with scroll + FAB
var rootGrid = new MauiGrid { BackgroundColor = LightBg };
rootGrid.Add(new MauiScrollView { Content = contentStack });
rootGrid.Add(new AddButtonControl(() =>
{
AppNavigation.NavigateToTask(null, _project.ID, Navigation);
}));

if (!_wrapInNav) return new MauiViewHost(rootGrid);

return NavigationView(
new MauiViewHost(rootGrid)
)
.Title("Project")
.Background(LightBg);
}
}
