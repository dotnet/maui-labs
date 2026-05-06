using MauiSwipeView = Microsoft.Maui.Controls.SwipeView;
using MauiSwipeItem = Microsoft.Maui.Controls.SwipeItem;
using MauiSwipeItems = Microsoft.Maui.Controls.SwipeItems;
using MauiLabel = Microsoft.Maui.Controls.Label;
using MauiScrollView = Microsoft.Maui.Controls.ScrollView;

namespace CometStressTest.Pages;

public class SwipeTestPageState
{
public int RemainingCount { get; set; } = 20;
}

public class SwipeTestPage : Component<SwipeTestPageState>
{
record EmailItem(string From, string Subject, string Preview, string Date);

readonly List<EmailItem> emails;

public SwipeTestPage()
{
emails = Enumerable.Range(1, 20).Select(i => new EmailItem(
$"sender{i}@example.com",
$"Subject line {i}",
$"This is a preview of email message number {i}...",
DateTime.Today.AddDays(-i).ToString("MMM dd")
)).ToList();
}

public override View Render()
{
var root = new Microsoft.Maui.Controls.VerticalStackLayout { Spacing = 0 };

// Header
root.Add(new MauiLabel
{
Text = $"Swipe Test — {State.RemainingCount} items remaining",
FontSize = 18,
FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold,
Padding = new Thickness(12),
BackgroundColor = Colors.LightGray,
});

for (int i = 0; i < emails.Count; i++)
{
root.Add(BuildSwipeRow(emails[i]));
}

var scroll = new MauiScrollView { Content = root };
return new MauiViewHost(scroll);
}

Microsoft.Maui.Controls.View BuildSwipeRow(EmailItem email)
{
var swipeView = new MauiSwipeView();

// Left swipe items: Archive and Flag
var leftItems = new MauiSwipeItems();
var archiveItem = new MauiSwipeItem
{
Text = "Archive",
BackgroundColor = Colors.Blue,
IconImageSource = null,
};
archiveItem.Invoked += (s, e) => RemoveEmail(email, swipeView);
leftItems.Add(archiveItem);

var flagItem = new MauiSwipeItem
{
Text = "Flag",
BackgroundColor = Colors.Orange,
};
leftItems.Add(flagItem);
swipeView.LeftItems = leftItems;

// Right swipe items: Delete
var rightItems = new MauiSwipeItems();
var deleteItem = new MauiSwipeItem
{
Text = "Delete",
BackgroundColor = Colors.Red,
};
deleteItem.Invoked += (s, e) => RemoveEmail(email, swipeView);
rightItems.Add(deleteItem);
swipeView.RightItems = rightItems;

// Row content
var row = new Microsoft.Maui.Controls.VerticalStackLayout
{
Padding = new Thickness(12, 8),
Spacing = 2,
};
var headerGrid = new Microsoft.Maui.Controls.Grid
{
ColumnDefinitions =
{
new Microsoft.Maui.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
new Microsoft.Maui.Controls.ColumnDefinition { Width = GridLength.Auto },
}
};
var fromLabel = new MauiLabel { Text = email.From, FontSize = 15, FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold };
var dateLabel = new MauiLabel { Text = email.Date, FontSize = 12, TextColor = Colors.Gray, VerticalTextAlignment = TextAlignment.Center };
Microsoft.Maui.Controls.Grid.SetColumn(dateLabel, 1);
headerGrid.Add(fromLabel);
headerGrid.Add(dateLabel);
row.Add(headerGrid);
row.Add(new MauiLabel { Text = email.Subject, FontSize = 14 });
row.Add(new MauiLabel { Text = email.Preview, FontSize = 12, TextColor = Colors.Gray, MaxLines = 1 });

// Separator
var container = new Microsoft.Maui.Controls.VerticalStackLayout();
swipeView.Content = row;
container.Add(swipeView);
container.Add(new Microsoft.Maui.Controls.BoxView { HeightRequest = 1, Color = Colors.LightGray });

return container;
}

void RemoveEmail(EmailItem email, MauiSwipeView swipeView)
{
emails.Remove(email);
SetState(s => s.RemainingCount = emails.Count);

// Remove the swipe row container from the layout
if (swipeView.Parent is Microsoft.Maui.Controls.Layout container
&& container.Parent is Microsoft.Maui.Controls.Layout parentLayout)
{
parentLayout.Remove(container);
}
}
}
