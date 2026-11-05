namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>BadgedBox</c>. Wraps a <c>Content</c> node
/// (typically an <see cref="Icon"/>) and overlays a <c>Badge</c>
/// on its top-end corner:
/// <code>
/// new BadgedBox
/// {
///     Badge   = new Badge { new Text("3") },
///     Content = new Icon(Resource.Drawable.ic_inbox, "Inbox"),
/// }
/// </code>
/// </summary>
public sealed partial class BadgedBox;
