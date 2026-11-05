using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace AndroidX.Compose.SourceGenerators;

/// <summary>
/// Registry of Compose <em>reference-typed</em> wrapper classes (Kotlin
/// classes — not <c>@JvmInline value class</c>) that are passed through
/// JNI as object handles. Distinct from <see cref="ComposeValueTypes"/>,
/// which lowers <c>@JvmInline</c> primitives to <c>F</c>/<c>J</c>/<c>I</c>
/// JNI slots.
///
/// Used by <see cref="ComposeFacadeGenerator"/> to recognize these
/// types as <c>OptionalValue</c> facade slots — surfaced as nullable
/// auto-properties on the generated facade class. The bridge generator's
/// existing reference-type code path (<c>x is null ? IntPtr.Zero :
/// ((Java.Lang.Object)x).Handle</c>) handles the actual JNI lowering.
///
/// Explicit registry (not heuristics) so adding a new wrapper is a
/// single-line opt-in and we don't accidentally classify unrelated
/// helper types in the <c>AndroidX.Compose</c> namespace as facade properties.
/// </summary>
internal static class ComposeReferenceTypes
{
    /// <summary>
    /// Set of fully-qualified C# metadata names (no nullable
    /// annotation, no <c>global::</c> prefix) recognized as Compose
    /// reference-type wrappers.
    /// </summary>
    public static readonly IReadOnlyCollection<string> Recognized = new HashSet<string>
    {
        "AndroidX.Compose.FontWeight",
        "AndroidX.Compose.FontFamily",
        "AndroidX.Compose.FontStyle",
        "AndroidX.Compose.TextAlign",
        "AndroidX.Compose.TextDecoration",
        "AndroidX.Compose.Shape",
        "AndroidX.Compose.Alignment",
        "AndroidX.Compose.ContentScale",
        "AndroidX.Compose.PaddingValues",
        "AndroidX.Compose.Material3.ITopAppBarScrollBehavior",
        "AndroidX.Compose.Material3.ButtonColors",
        "AndroidX.Compose.Material3.SliderColors",
        "AndroidX.Compose.Material3.CheckboxColors",
        "AndroidX.Compose.Material3.SwitchColors",
        "AndroidX.Compose.UI.Text.TextStyle",
        "AndroidX.Compose.UI.Text.Input.IVisualTransformation",
        "AndroidX.Compose.Foundation.Text.KeyboardOptions",
        "Kotlin.Ranges.IClosedFloatingPointRange",
    };

    /// <summary>
    /// True iff <paramref name="type"/> is a recognized Compose
    /// reference-type wrapper (with or without a nullable annotation).
    /// </summary>
    public static bool IsRecognized(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol n) return false;
        if (!n.IsReferenceType) return false;
        var ns = n.ContainingNamespace?.IsGlobalNamespace == true
            ? string.Empty
            : (n.ContainingNamespace?.ToDisplayString() ?? string.Empty);
        var name = ns.Length == 0 ? n.Name : ns + "." + n.Name;
        return Recognized.Contains(name);
    }
}
