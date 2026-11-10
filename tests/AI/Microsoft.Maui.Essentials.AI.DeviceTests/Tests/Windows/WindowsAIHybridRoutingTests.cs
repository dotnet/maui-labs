#if WINDOWS
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public sealed class WindowsAIHybridRoutingTests(ITestOutputHelper output)
    : HybridRoutingTestsBase<WindowsAIChatClient>(output);
#endif
