using System;
using System.Collections.Generic;
using Comet.Reactive;

namespace Comet
{
/// <summary>
/// Table view for form-style layouts with sections.
/// </summary>
public class TableView : View
{
public List<TableSection> Root { get; set; } = new List<TableSection>();

public TableView() { }

public TableView(params TableSection[] sections)
{
Root.AddRange(sections);
}
}

/// <summary>
/// A section within a TableView containing cells.
/// </summary>
public class TableSection
{
public string Title { get; set; }
public List<View> Cells { get; set; } = new List<View>();

public TableSection() { }

public TableSection(string title, params View[] cells)
{
Title = title;
Cells.AddRange(cells);
}
}

/// <summary>
/// A text cell for use within TableSection.
/// </summary>
public class TextCell : View
{
private PropertySubscription<string> _text;
public PropertySubscription<string> CellText
{
get => _text;
set => this.SetPropertySubscription(ref _text, value);
}

private PropertySubscription<string> _detail;
public PropertySubscription<string> Detail
{
get => _detail;
set => this.SetPropertySubscription(ref _detail, value);
}

public Action OnTapped { get; set; }
}

/// <summary>
/// A switch cell for toggle items within TableSection.
/// </summary>
public class SwitchCell : View
{
private PropertySubscription<string> _text;
public PropertySubscription<string> CellText
{
get => _text;
set => this.SetPropertySubscription(ref _text, value);
}

private PropertySubscription<bool> _on;
public PropertySubscription<bool> On
{
get => _on;
set => this.SetPropertySubscription(ref _on, value);
}
}

/// <summary>
/// An entry cell for text input within TableSection.
/// </summary>
public class EntryCell : View
{
private PropertySubscription<string> _label;
public PropertySubscription<string> Label
{
get => _label;
set => this.SetPropertySubscription(ref _label, value);
}

private PropertySubscription<string> _text;
public PropertySubscription<string> CellText
{
get => _text;
set => this.SetPropertySubscription(ref _text, value);
}

private PropertySubscription<string> _placeholder;
public PropertySubscription<string> Placeholder
{
get => _placeholder;
set => this.SetPropertySubscription(ref _placeholder, value);
}
}

/// <summary>
/// A cell displaying an image with text and detail text, for use within TableSection.
/// </summary>
public class ImageCell : View
{
private PropertySubscription<string> _imageSource;
public PropertySubscription<string> ImageSource
{
get => _imageSource;
set => this.SetPropertySubscription(ref _imageSource, value);
}

private PropertySubscription<string> _text;
public PropertySubscription<string> CellText
{
get => _text;
set => this.SetPropertySubscription(ref _text, value);
}

private PropertySubscription<string> _detail;
public PropertySubscription<string> Detail
{
get => _detail;
set => this.SetPropertySubscription(ref _detail, value);
}
}

/// <summary>
/// A cell containing a custom view, for use within TableSection.
/// </summary>
public class ViewCell : View
{
public View Content { get; set; }

public ViewCell() { }

public ViewCell(View content)
{
Content = content;
}
}
}
