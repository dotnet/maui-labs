using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

internal static class TestSetup
{
    // Bindings need a dispatcher. Without a running app, give every test thread one that runs work inline.
#pragma warning disable CA2255 // The test assembly is the only consumer of this initializer.
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize() => ImmediateDispatcher.Install();
}
