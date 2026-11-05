using Microsoft.Maui.DevFlow.Agent.Core;
using GtkNativeElementDiagnosticsBridge =
    Microsoft.Maui.Platforms.Linux.Gtk4.Platform.NativeElementDiagnosticsBridge;
using MacOSNativeElementDiagnosticsBridge =
    Microsoft.Maui.Platforms.MacOS.Platform.NativeElementDiagnosticsBridge;
using WpfNativeElementDiagnosticsBridge =
    Microsoft.Maui.Platforms.Windows.WPF.NativeElementDiagnosticsBridge;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers the cross-assembly contract between a platform's native-element diagnostic
/// <em>publisher</em> (one per non-MAUI backend: macOS AppKit, GTK4, WPF) and the single
/// <em>consumer</em> in <c>Agent.Core</c>, <see cref="MauiNativeElementDiagnosticSubscriber"/>.
/// </summary>
/// <remarks>
/// <para>
/// The publisher and consumer live in different assemblies and only agree by convention: a
/// <see cref="System.Diagnostics.DiagnosticListener"/> name, a handful of event-name strings, and
/// a payload shape (an <c>object?[]</c> whose first element is a contract version). Nothing in the
/// type system enforces that agreement — it is entirely string- and array-shape-based — so it has
/// to be tested as a contract, not as an implementation detail of either side.
/// </para>
/// <para>
/// <see cref="RegisteredNativeElementRegistryTests"/> already covers
/// <see cref="MauiNativeElementDiagnosticSubscriber"/> in isolation, but it does so entirely
/// through <see cref="MauiNativeElementDiagnosticSubscriber"/>'s own constants
/// (<c>MauiNativeElementDiagnosticSubscriber.ListenerName</c>, etc.) — that only proves the
/// subscriber agrees with itself. This class instead (1) asserts the subscriber's constants equal
/// canonical string/int literals written independently here, so a silent rename on the consumer
/// side would be caught, and (2) drives the registration through the actual production
/// <see cref="NativeElementDiagnosticsBridge"/> publisher — linked in from the macOS platform
/// project (see the .csproj) — rather than hand-rolling a <c>DiagnosticListener.Write</c> call, so
/// a change to the real publisher's payload shape fails a test instead of only failing at runtime
/// on a device.
/// </para>
/// </remarks>
[Collection(NativeElementDiagnosticsCollection.Name)]
public class NativeElementDiagnosticContractTests
{
    // Canonical values, written independently of MauiNativeElementDiagnosticSubscriber's own
    // constants. If the consumer's constants above ever drift from what every publisher actually
    // writes, these literals — not the consumer — are the source of truth to fix.
    private const string CanonicalListenerName = "Microsoft.Maui.NativeElements";
    private const int CanonicalContractVersion = 1;
    private const string CanonicalRegisteredEventName = "Microsoft.Maui.NativeElements.Registered.v1";
    private const string CanonicalUnregisteredEventName = "Microsoft.Maui.NativeElements.Unregistered.v1";
    private const string CanonicalLegacyRegisteredEventName = "Microsoft.Maui.NativeElements.Registered";
    private const string CanonicalLegacyUnregisteredEventName = "Microsoft.Maui.NativeElements.Unregistered";

    [Fact]
    public void Subscriber_ConstantsMatchTheCanonicalWireContract()
    {
        // Not a test of the subscriber against itself: every value on the right is a literal typed
        // independently above, standing in for what a platform publisher is documented to write.
        Assert.Equal(CanonicalListenerName, MauiNativeElementDiagnosticSubscriber.ListenerName);
        Assert.Equal(CanonicalContractVersion, MauiNativeElementDiagnosticSubscriber.ContractVersion);
        Assert.Equal(CanonicalRegisteredEventName, MauiNativeElementDiagnosticSubscriber.RegisteredEventName);
        Assert.Equal(CanonicalUnregisteredEventName, MauiNativeElementDiagnosticSubscriber.UnregisteredEventName);
        Assert.Equal(CanonicalLegacyRegisteredEventName, MauiNativeElementDiagnosticSubscriber.LegacyRegisteredEventName);
        Assert.Equal(CanonicalLegacyUnregisteredEventName, MauiNativeElementDiagnosticSubscriber.LegacyUnregisteredEventName);
    }

    [Fact]
    public void ProductionBridges_RegisterThroughToTheRegistry()
    {
        AssertBridge(
            (owner, nativeElement, role) =>
                MacOSNativeElementDiagnosticsBridge.Register(
                    owner,
                    nativeElement,
                    role,
                    "RealizedView"),
            MacOSNativeElementDiagnosticsBridge.Unregister,
            "RealizedView");
        AssertBridge(
            GtkNativeElementDiagnosticsBridge.Register,
            GtkNativeElementDiagnosticsBridge.Unregister,
            expectedDiscriminator: null);
        AssertBridge(
            WpfNativeElementDiagnosticsBridge.Register,
            WpfNativeElementDiagnosticsBridge.Unregister,
            expectedDiscriminator: null);
    }

    private static void AssertBridge(
        Action<object, object, string> register,
        Action<object> unregister,
        string? expectedDiscriminator)
    {
        var registry = new RegisteredNativeElementRegistry();
        using var subscriber = new MauiNativeElementDiagnosticSubscriber(registry);
        var owner = new object();
        var nativeElement = new object();

        register(owner, nativeElement, "ToolbarItem");

        var registration = Assert.Single(registry.GetSnapshot());
        Assert.Same(owner, registration.Owner);
        Assert.Same(nativeElement, registration.NativeElement);
        Assert.Equal("ToolbarItem", registration.Role);
        Assert.Equal(expectedDiscriminator, registration.Discriminator);

        unregister(nativeElement);

        Assert.Empty(registry.GetSnapshot());
    }
}

[CollectionDefinition(Name)]
public sealed class NativeElementDiagnosticsCollection
{
    public const string Name = "Native element diagnostics";
}
