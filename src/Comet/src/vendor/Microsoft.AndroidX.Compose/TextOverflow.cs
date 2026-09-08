namespace AndroidX.Compose;

/// <summary>
/// C# mirror of Kotlin's <c>androidx.compose.ui.text.style.TextOverflow</c>
/// — a <c>@JvmInline value class</c> wrapping an <c>Int</c>. The bridge
/// generator lowers <c>TextOverflow?</c> to the underlying <c>int</c>
/// JNI slot.
///
/// Values mirror the Kotlin <c>TextOverflow.Companion</c> constants:
/// <list type="bullet">
///   <item><see cref="Clip"/> = 1 — truncate at the container edge.</item>
///   <item><see cref="Ellipsis"/> = 2 — replace overflow with <c>"…"</c>.</item>
///   <item><see cref="Visible"/> = 3 — render past the container bounds.</item>
///   <item><see cref="StartEllipsis"/> = 4 — leading ellipsis.</item>
///   <item><see cref="MiddleEllipsis"/> = 5 — middle ellipsis.</item>
/// </list>
/// </summary>
public readonly record struct TextOverflow(int Value)
{
    /// <summary>Truncate the text at the edge of the container.</summary>
    public static TextOverflow Clip => new(1);

    /// <summary>Replace the overflowing text with an ellipsis (default for single-line).</summary>
    public static TextOverflow Ellipsis => new(2);

    /// <summary>Render the text outside the container bounds (no clipping).</summary>
    public static TextOverflow Visible => new(3);

    /// <summary>Place the ellipsis at the start of the text.</summary>
    public static TextOverflow StartEllipsis => new(4);

    /// <summary>Place the ellipsis in the middle of the text.</summary>
    public static TextOverflow MiddleEllipsis => new(5);

    /// <summary>
    /// Pack a nullable <see cref="TextOverflow"/> into the raw
    /// <c>int</c> the JNI slot expects. <c>null</c> → <c>0</c>; the
    /// auto-mask leaves the <c>$default</c> bit set so Kotlin's real
    /// default applies.
    /// </summary>
    public static int Pack(TextOverflow? value) => value?.Value ?? 0;
}
