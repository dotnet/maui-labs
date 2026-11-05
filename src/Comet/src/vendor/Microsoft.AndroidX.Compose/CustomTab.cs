namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>Tab</c> with fully custom content (the <c>Tab-bogVsAg</c>
/// overload that takes a ColumnScope-receiver content lambda — useful
/// when the text/icon slots on the regular <see cref="Tab"/> don't fit
/// your layout, e.g. multi-line labels or a leading dot indicator):
/// <code>
/// new CustomTab(selected: tab == 0, onClick: () =&gt; tab.Value = 0)
/// {
///     new Column { new Text("Inbox"), new Text("5 unread") },
/// }
/// </code>
/// Children are stacked vertically inside the tab's <c>ColumnScope</c>.
/// </summary>
public sealed partial class CustomTab;
