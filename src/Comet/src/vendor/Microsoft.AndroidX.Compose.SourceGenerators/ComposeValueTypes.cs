using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace AndroidX.Compose.SourceGenerators;

/// <summary>
/// Registry of Compose <c>@JvmInline value class</c> types whose
/// parameters lower to JNI primitives. The bridge generator uses this
/// to (1) emit the right <c>JValue</c> slot, and (2) extend the
/// auto-default-mask logic so passing a non-<c>null</c> value clears
/// the corresponding <c>$default</c> bit, mirroring the existing
/// <c>IModifier?</c> / <c>IntPtr?</c> handling.
///
/// Reference-typed Compose wrappers (<c>FontWeight</c>,
/// <c>TextDecoration</c>, <c>Shape</c>) are NOT in this registry —
/// they go through the generic "reference-type → handle with null
/// check" path in
/// <see cref="ComposeBridgeGenerator"/>.<c>EmitUserArgValue</c>, which
/// already handles them.
/// </summary>
internal static class ComposeValueTypes
{
    /// <summary>
    /// Map from C# fully-qualified metadata name (without trailing
    /// <c>?</c>; the parameter is recognized only as a nullable
    /// wrapper) to a (slot, lowering-template) pair.
    /// <c>{0}</c> in the template is replaced with the C# parameter
    /// identifier. The lowering takes a <c>Nullable&lt;T&gt;</c> and
    /// returns the JNI primitive — when the value is <c>null</c> it
    /// returns the appropriate zero literal so Kotlin's default-value
    /// path wins (the auto-default-mask leaves the bit set).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (char Slot, string LowerTemplate)> Recognized =
        new Dictionary<string, (char, string)>
        {
            // androidx.compose.ui.unit.Dp → JNI float.
            ["AndroidX.Compose.Dp"] =
                ('F', "global::AndroidX.Compose.Dp.Pack({0})"),

            // androidx.compose.ui.unit.TextUnit (sp variant) → JNI long.
            ["AndroidX.Compose.Sp"] =
                ('J', "global::AndroidX.Compose.Sp.Pack({0})"),

            // androidx.compose.ui.text.style.TextOverflow → JNI int.
            // (Compose declares TextOverflow as non-nullable in
            // @Composable signatures, so it lowers as packed `I` rather
            // than the boxed `L` reference seen for nullable inline
            // classes like TextAlign / FontStyle.)
            ["AndroidX.Compose.TextOverflow"] =
                ('I', "global::AndroidX.Compose.TextOverflow.Pack({0})"),

            // androidx.compose.ui.graphics.Color is bound by
            // Xamarin.AndroidX.Compose.UI.Graphics 1.11.2.1, and the
            // managed-side `AndroidX.Compose.Color` is a value-type wrapper
            // over the same packed ULong. The Kotlin
            // `@JvmInline value class Color(val value: ULong)` surfaces
            // as a packed `long` at the JNI boundary; the implicit
            // `Color -> long` operator turns the C# struct into the
            // bridge's actual `long` JNI slot.
            ["AndroidX.Compose.Color"] =
                ('J', "(long)({0}.GetValueOrDefault())"),
        };

    /// <summary>
    /// Detect a parameter whose type is <c>Nullable&lt;T&gt;</c> where
    /// <c>T</c> is one of the recognized value types. Returns the
    /// underlying full name and the registry entry, or <c>false</c>.
    /// </summary>
    public static bool TryGet(ITypeSymbol type, out string fullName, out (char Slot, string LowerTemplate) info)
    {
        fullName = string.Empty;
        info = default;
        if (type is not INamedTypeSymbol n) return false;
        if (n.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) return false;
        if (n.TypeArguments.Length != 1) return false;
        var inner = n.TypeArguments[0];
        var ns = inner.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : (inner.ContainingNamespace?.ToDisplayString() ?? string.Empty);
        var name = ns.Length == 0 ? inner.Name : ns + "." + inner.Name;
        if (!Recognized.TryGetValue(name, out info)) return false;
        fullName = name;
        return true;
    }
}
