using Microsoft.CodeAnalysis;

namespace AndroidX.Compose.SourceGenerators;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor MethodNotFound = new(
        id: "CN1001",
        title: "Compose method not found",
        messageFormat: "No static method '{0}' found on type '{1}'",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotComposable = new(
        id: "CN1002",
        title: "Method is not @Composable",
        messageFormat: "Method '{0}' has no IComposer parameter — not a @Composable signature",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MalformedAttribute = new(
        id: "CN1003",
        title: "Malformed [ComposeDefaults] attribute",
        messageFormat: "Could not read [ComposeDefaults] attribute arguments for enum '{0}'",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeMissingDefaults = new(
        id: "CN2001",
        title: "[ComposeBridge] missing or unmatched Defaults",
        messageFormat: "Could not resolve a [ComposeDefaults] attribute matching '{0}' for bridge '{1}'",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeSignatureMismatch = new(
        id: "CN2002",
        title: "[ComposeBridge] signature/Defaults mismatch",
        messageFormat: "Bridge '{0}' signature has {1} user-parameter slot(s) before composer but Defaults '{2}' declares {3}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeUnknownParameter = new(
        id: "CN2003",
        title: "[ComposeBridge] partial method parameter not in Defaults",
        messageFormat: "Bridge '{0}' has parameter '{1}' that does not match any Kotlin parameter name in Defaults '{2}'",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeMalformedSignature = new(
        id: "CN2004",
        title: "[ComposeBridge] malformed JNI signature",
        messageFormat: "Bridge '{0}' has malformed JNI signature: {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeDefaultsMismatch = new(
        id: "CN2005",
        title: "[ComposeBridge] Defaults disagrees with JNI signature",
        messageFormat: "Bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeConstructorShape = new(
        id: "CN2006",
        title: "[ComposeBridge] constructor shape requirements not met",
        messageFormat: "Constructor bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeValueTypeNeedsDefaults = new(
        id: "CN2007",
        title: "[ComposeBridge] recognized Compose value type requires a $default slot",
        messageFormat: "Bridge '{0}' parameter '{1}' is a recognized Compose value type ({2}) — those rely on the auto-default-mask logic, but this bridge has no $default slot",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeValueTypeSlotMismatch = new(
        id: "CN2008",
        title: "[ComposeBridge] value-type parameter lowers to the wrong JNI slot",
        messageFormat: "Bridge '{0}' parameter '{1}' is a recognized Compose value type ({2}) that lowers to JNI slot '{3}' but the signature has slot '{4}' at that position — declare a different parameter type or fix the signature",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeSuspendInvalid = new(
        id: "CN2009",
        title: "[ComposeBridge(Suspend = true)] configuration is invalid",
        messageFormat: "Suspend bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeWrongContainingType = new(
        id: "CN3001",
        title: "[ComposeFacade] must be applied to a method on ComposeBridges",
        messageFormat: "[ComposeFacade] on '{0}' was found on type '{1}'; facade generation only runs on methods declared in AndroidX.Compose.ComposeBridges",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeUnsupportedParameter = new(
        id: "CN3002",
        title: "[ComposeFacade] bridge has an unsupported parameter shape",
        messageFormat: "Facade for bridge '{0}' cannot be generated: parameter '{1}' has unsupported type '{2}'. Leave this bridge hand-written and remove [ComposeFacade].",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeScopeMisuse = new(
        id: "CN3003",
        title: "[ComposeFacade] Scope set without an IFunction3 content slot",
        messageFormat: "Facade for bridge '{0}' has Scope='{1}' but no IFunction3 content parameter to publish the scope from",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeMissingBridge = new(
        id: "CN3004",
        title: "[ComposeFacade] requires a sibling [ComposeBridge]",
        messageFormat: "[ComposeFacade] on '{0}' is missing the required [ComposeBridge] attribute; facade generation only wraps bridge-emitted methods",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeCallbackUnsupportedType = new(
        id: "CN3005",
        title: "[Callback] unbox type is not supported",
        messageFormat: "Facade for bridge '{0}': [Callback] on parameter '{1}' uses unsupported value type '{2}'. Supported types: bool, string, float.",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeSlotConflict = new(
        id: "CN3006",
        title: "[ComposeFacade] slot configuration is invalid",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeColorThemeBindingFailed = new(
        id: "CN3007",
        title: "[ComposeFacade(DefaultColorFromTheme=...)] could not bind to a bridge parameter",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadePainterMisuse = new(
        id: "CN3008",
        title: "[PainterResource] must annotate an 'IntPtr' bridge parameter",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeStateHolderInvalid = new(
        id: "CN3009",
        title: "[StateHolder] configuration is invalid",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeBranchInvalid = new(
        id: "CN3010",
        title: "[ComposeFacade(BranchOn=..., AlternateBridge=...)] is invalid",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeConfirmStateChangeInvalid = new(
        id: "CN3011",
        title: "[ConfirmStateChange] configuration is invalid",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor FacadeSecondaryCtorInvalid = new(
        id: "CN3012",
        title: "[ComposeFacade(SecondaryCtor=...)] is invalid",
        messageFormat: "Facade for bridge '{0}': {1}",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionNotPartial = new(
        id: "CN4001",
        title: "[ComposeCompanion] target class must be partial",
        messageFormat: "Class '{0}' carries [ComposeCompanion] but is not declared 'partial'",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionMalformedOuter = new(
        id: "CN4002",
        title: "[ComposeCompanion] outer JNI class is missing or malformed",
        messageFormat: "Class '{0}' [ComposeCompanion] requires a non-empty slash-separated outer JNI class name",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionGetterShape = new(
        id: "CN4003",
        title: "[ComposeCompanionGetter] must be on a 'public static partial T { get; }' property",
        messageFormat: "Property '{0}.{1}' carries [ComposeCompanionGetter] but is not a public static partial get-only declaration",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionMalformedGetter = new(
        id: "CN4004",
        title: "[ComposeCompanionGetter] getter name is missing or empty",
        messageFormat: "Property '{0}.{1}' [ComposeCompanionGetter] requires a non-empty Kotlin getter name",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionGetterMissingHost = new(
        id: "CN4005",
        title: "[ComposeCompanionGetter] requires the containing class to carry [ComposeCompanion]",
        messageFormat: "Property '{0}.{1}' carries [ComposeCompanionGetter] but class '{0}' has no [ComposeCompanion]",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionMissingPeerCtor = new(
        id: "CN4006",
        title: "[ComposeCompanionGetter] return type lacks an (IntPtr, JniHandleOwnership) constructor",
        messageFormat: "Property '{0}.{1}': return type '{2}' must declare an accessible '(System.IntPtr, Android.Runtime.JniHandleOwnership)' constructor so the generated body can wrap the JNI handle",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompanionInlineReturnConflict = new(
        id: "CN4007",
        title: "[ComposeCompanionGetter(ReturnDescriptor=...)] cannot be combined with [ComposeCompanion(InlineClass = true)]",
        messageFormat: "Property '{0}.{1}': inline-class companions always box through the outer type via 'box-impl(I)L<outer>;' — remove the per-property ReturnDescriptor",
        category: "AndroidX.Compose",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
