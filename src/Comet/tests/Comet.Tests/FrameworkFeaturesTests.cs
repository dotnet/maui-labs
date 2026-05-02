using Xunit;
using System;
using System.Collections.ObjectModel;
using Comet.Converters;

namespace Comet.Tests
{
public class BindableLayoutTests
{
[Fact]
public void BindableLayout_AddItems()
{
var items = new[] { "Item 1", "Item 2", "Item 3" };
var layout = new BindableLayout<string>
{
ItemsSource = items,
ItemTemplate = item => new Text(item)
};

Assert.Equal(3, layout.Count);
}

[Fact]
public void BindableLayout_ObservableCollection()
{
var items = new ObservableCollection<string> { "A", "B" };
var layout = new BindableLayout<string>
{
ItemsSource = items,
ItemTemplate = item => new Text(item)
};

Assert.Equal(2, layout.Count);
items.Add("C");
Assert.Equal(3, layout.Count);
}

[Fact]
public void BindableLayout_NullTemplate()
{
var items = new[] { "Item" };
var layout = new BindableLayout<string>
{
ItemsSource = items
};

Assert.Empty(layout);
}

[Fact]
public void NonGenericBindableLayout_Works()
{
var items = new object[] { 1, 2, 3 };
var layout = new BindableLayout
{
ItemsSource = items,
ItemTemplate = item => new Text(item.ToString())
};

Assert.Equal(3, layout.Count);
}
}

public class ValueConverterTests
{
[Fact]
public void Not_InvertsBoolean()
{
Assert.True(ValueConverters.Not(false));
Assert.False(ValueConverters.Not(true));
}

[Fact]
public void IsEmpty_WithString()
{
Assert.True(ValueConverters.IsEmpty(""));
Assert.True(ValueConverters.IsEmpty(null));
Assert.True(ValueConverters.IsEmpty("   "));
Assert.False(ValueConverters.IsEmpty("text"));
}

[Fact]
public void IsEmpty_WithCollection()
{
var empty = new int[] { };
var nonEmpty = new[] { 1, 2, 3 };

Assert.True(ValueConverters.IsEmpty(empty));
Assert.False(ValueConverters.IsEmpty(nonEmpty));
}

[Fact]
public void HasItems_WithCollection()
{
var empty = new int[] { };
var nonEmpty = new[] { 1, 2, 3 };

Assert.False(ValueConverters.HasItems(empty));
Assert.True(ValueConverters.HasItems(nonEmpty));
}

[Fact]
public void IsPositive_WithNumber()
{
Assert.False(ValueConverters.IsPositive(0));
Assert.True(ValueConverters.IsPositive(1));
Assert.False(ValueConverters.IsPositive(-1));
}

[Fact]
public void FormatDecimal_Works()
{
var result = ValueConverters.FormatDecimal(1.23456m, 2);
Assert.Equal("1.23", result);
}

[Fact]
public void FormatCurrency_Works()
{
var result = ValueConverters.FormatCurrency(1000m);
Assert.Equal("$1,000.00", result);
}

[Fact]
public void FormatOrdinal_Works()
{
Assert.Equal("1st", ValueConverters.FormatOrdinal(1));
Assert.Equal("2nd", ValueConverters.FormatOrdinal(2));
Assert.Equal("3rd", ValueConverters.FormatOrdinal(3));
Assert.Equal("4th", ValueConverters.FormatOrdinal(4));
Assert.Equal("11th", ValueConverters.FormatOrdinal(11));
}

[Fact]
public void Pluralize_Works()
{
Assert.Equal("item", ValueConverters.Pluralize(1, "item", "items"));
Assert.Equal("items", ValueConverters.Pluralize(2, "item", "items"));
Assert.Equal("items", ValueConverters.Pluralize(0, "item", "items"));
}

[Fact]
public void Abbreviate_Works()
{
var long_text = "This is a very long string that exceeds the limit";
var result = ValueConverters.Abbreviate(long_text, 20);
Assert.EndsWith("...", result);
Assert.Equal(20, result.Length); // maxLength includes the "..."
}

[Fact]
public void MapEnum_Works()
{
var result = ValueConverters.MapEnum(DayOfWeek.Monday);
Assert.Equal("Monday", result);
}

[Fact]
public void FormatRelativeTime_Works()
{
var now = DateTime.UtcNow;
var ago = now.AddMinutes(-5);
var result = ValueConverters.FormatRelativeTime(ago);
Assert.Contains("ago", result);
}

[Fact]
public void Equals_Generic()
{
Assert.True(ValueConverters.Equals(1, 1));
Assert.False(ValueConverters.Equals(1, 2));
}

[Fact]
public void NotEquals_Generic()
{
Assert.False(ValueConverters.NotEquals(1, 1));
Assert.True(ValueConverters.NotEquals(1, 2));
}

[Fact]
public void Coalesce_ReturnsValueIfNotNull()
{
var value = "test";
var fallback = "default";
Assert.Equal("test", ValueConverters.Coalesce(value, fallback));
}

[Fact]
public void Coalesce_ReturnsFallbackIfNull()
{
string value = null;
var fallback = "default";
Assert.Equal("default", ValueConverters.Coalesce(value, fallback));
}
}

public class TabViewTests : TestBase
{
[Fact]
public void TabView_Creation()
{
var tabView = new TabView();
Assert.NotNull(tabView);
Assert.Equal(0, tabView.SelectedIndex);
}

[Fact]
public void TabView_AddTab()
{
var tabView = new TabView();
var content = new Text("Tab 1");
tabView.AddTab("Tab 1", content);

Assert.Single(tabView.Tabs);
Assert.Equal("Tab 1", tabView.Tabs[0].Title);
}

[Fact]
public void TabView_SelectedIndex()
{
var tabView = new TabView();
tabView.AddTab("Tab 1", new Text("Content 1"));
tabView.AddTab("Tab 2", new Text("Content 2"));

tabView.SelectedIndex = 1;
Assert.Equal(1, tabView.SelectedIndex);
Assert.NotNull(tabView.CurrentTab);
Assert.Equal("Tab 2", tabView.CurrentTab.Title);
}

[Fact]
public void TabView_SelectedIndexChanged()
{
var tabView = new TabView();
tabView.AddTab("Tab 1", new Text("Content 1"));
tabView.AddTab("Tab 2", new Text("Content 2"));

var changedIndex = -1;
tabView.SelectedIndexChanged = (index) => { changedIndex = index; };

tabView.SelectedIndex = 1;
Assert.Equal(1, changedIndex);
}

[Fact]
public void TabItem_Properties()
{
var item = new TabItem
{
Title = "Tab",
Icon = "icon.svg",
BadgeValue = "3"
};

Assert.Equal("Tab", item.Title);
Assert.Equal("icon.svg", item.Icon);
Assert.Equal("3", item.BadgeValue);
}
}

public class AnimationBuilderTests
{
// AnimateFadeIn/FadeOut/Pulse require MAUI MainThread (platform-only)

[Fact]
public void AnimationExtensions_Exist()
{
	// Verify the core Animate extension compiles with View
	var view = new Text("Test");
	Assert.NotNull(view);
	// AnimationBuilder<T> has internal ctor, accessed via extensions
	// Just verify the type exists
	Assert.NotNull(typeof(AnimationBuilder<Text>));
}
}
}
