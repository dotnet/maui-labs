namespace AndroidX.Compose;

/// <summary>
/// Packed <c>androidx.compose.ui.text.style.LineBreak</c> constants —
/// the wrap-strategy presets Compose text styles carry (<c>Simple</c> =
/// greedy, <c>Heading</c> = balanced + phrase word break, <c>Paragraph</c> =
/// high-quality). <c>LineBreak</c> is a Kotlin value class, so its companion
/// getters carry a mangled JVM suffix (<c>getHeading-rAG3T2k</c>) that varies
/// by compiler version; the constants are therefore resolved once via Java
/// reflection by getter-name prefix instead of a hard-coded signature.
/// </summary>
public static class LineBreakValues
{
    static int? s_simple, s_heading, s_paragraph;

    /// <summary>Greedy wrapping — the Compose default for unstyled text.</summary>
    public static int Simple => s_simple ??= Fetch("getSimple");

    /// <summary>Balanced wrapping with phrase-based word breaks — Compose's preset for titles.</summary>
    public static int Heading => s_heading ??= Fetch("getHeading");

    /// <summary>High-quality (non-greedy) wrapping — Compose's preset for body copy.</summary>
    public static int Paragraph => s_paragraph ??= Fetch("getParagraph");

    static int Fetch(string getterPrefix)
    {
        var cls = Java.Lang.Class.ForName("androidx.compose.ui.text.style.LineBreak$Companion");
        var lineBreak = Java.Lang.Class.ForName("androidx.compose.ui.text.style.LineBreak");
        var fid = lineBreak.GetDeclaredField("Companion");
        fid.Accessible = true;
        var companion = fid.Get(null);
        foreach (var m in cls.GetDeclaredMethods())
        {
            // Match on prefix + int return: Kotlin also emits a synthetic void
            // `get*-XXXX$annotations()` holder that the prefix alone would catch.
            if (m.Name.StartsWith(getterPrefix, System.StringComparison.Ordinal)
                && m.ReturnType?.Name == "int")
            {
                m.Accessible = true;
                var boxed = m.Invoke(companion)
                    ?? throw new System.InvalidOperationException($"LineBreak.Companion.{m.Name} returned null");
                return Java.Lang.Integer.ParseInt(boxed.ToString());
            }
        }
        throw new System.MissingMethodException($"LineBreak.Companion.{getterPrefix}* not found");
    }
}
