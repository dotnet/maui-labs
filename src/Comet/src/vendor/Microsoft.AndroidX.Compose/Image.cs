namespace AndroidX.Compose;

/// <summary>
/// Foundation <c>Image</c> composable. Two ctor shapes are exposed:
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="Image(int, string?)"/> — pass an Android drawable
///       resource id; the bridge resolves it via
///       <c>painterResource(id, composer)</c> inside the composition
///       and forwards the <c>Painter</c> to
///       <c>ImageKt.Image(Painter, ...)</c>. This is the common case.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="Image(AndroidX.Compose.UI.Graphics.Painter.Painter, string?)"/>
///       — pass a pre-resolved Compose <c>Painter</c>. Useful when the
///       image isn't packaged as a drawable resource: e.g. a
///       <c>BitmapPainter</c> wrapping an arbitrary
///       <c>android.graphics.Bitmap</c>, or a Painter materialized
///       from an asynchronous load.
///     </description>
///   </item>
/// </list>
/// The <c>Painter</c> type itself is bound by
/// <c>Xamarin.AndroidX.Compose.UI.Graphics</c>; only the
/// <c>ImageKt.Image-...</c> overloads (mangled by an inline-class
/// param) need the JNI bridge for direct invocation.
/// </summary>
public sealed partial class Image
{
    /// <summary>Render an Android drawable resource with no content description (decorative).</summary>
    public Image(int drawableResourceId) : this(drawableResourceId, null) { }

    /// <summary>Render a pre-resolved Compose <c>Painter</c> with no content description (decorative).</summary>
    public Image(global::AndroidX.Compose.UI.Graphics.Painter.Painter painter)
        : this(painter, null) { }
}
