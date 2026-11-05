namespace AndroidX.Compose;

/// <summary>
/// Material 3 <c>Icon</c> — renders a small image used for affordance,
/// typically inside a <see cref="Button"/>, <see cref="IconButton"/>,
/// or a <see cref="NavigationBarItem"/>.
/// <para/>
/// Three source types are supported:
/// <list type="bullet">
/// <item><description>An
/// <c>AndroidX.Compose.UI.Graphics.Vector.ImageVector</c> obtained
/// from a Compose icon library — routes through the
/// directly-bound <c>IconKt.Icon(ImageVector, …)</c> overload via the
/// generator's Phase 11 secondary-ctor dispatch.</description></item>
/// <item><description>An Android drawable resource id — resolved
/// inside <c>Render</c> via <c>painterResource(id)</c> and forwarded
/// through the <c>IconKt.Icon-ww6aTOc(Painter, …)</c> JNI
/// bridge.</description></item>
/// <item><description>A pre-resolved
/// <c>AndroidX.Compose.UI.Graphics.Painter.Painter</c> — the
/// caller-owned handle is forwarded directly through the same
/// JNI bridge.</description></item>
/// </list>
/// </summary>
/// <remarks>
/// Set the <c>Tint</c> property to a packed <see cref="Color"/>
/// (implicit-converted to <c>long</c>) to override the icon color.
/// Leave it unset (<c>null</c>) to inherit the surrounding Material
/// content color (Compose's <c>LocalContentColor</c>).
/// </remarks>
public sealed partial class Icon;
