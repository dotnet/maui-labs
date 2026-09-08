namespace AndroidX.Compose;

/// <summary>
/// C# mirror of <c>androidx.compose.ui.graphics.TileMode</c> — controls
/// what a gradient does outside its declared start/end region.
/// </summary>
/// <remarks>
/// In Kotlin source <c>TileMode</c> is a <c>@JvmInline value class</c>
/// wrapping <see cref="int"/>; across JNI it travels as a bare
/// <see cref="int"/>, which is what the underlying <c>Brush</c> companion
/// factories expect. The integer values here match Kotlin's
/// <c>TileMode.Clamp.value</c> / <c>Repeated.value</c> / etc.
/// </remarks>
public enum TileMode
{
    /// <summary>
    /// Clamp to the boundary color — pixels outside the gradient region
    /// keep the endpoint color. This is Compose's default.
    /// </summary>
    Clamp = 0,

    /// <summary>
    /// Repeat the gradient pattern indefinitely outside the declared
    /// region.
    /// </summary>
    Repeated = 1,

    /// <summary>
    /// Repeat the gradient pattern with every other repetition mirrored,
    /// producing a seamless tile.
    /// </summary>
    Mirror = 2,

    /// <summary>
    /// Render anything outside the gradient region as transparent — the
    /// gradient draws only inside its declared bounds.
    /// </summary>
    Decal = 3,
}
