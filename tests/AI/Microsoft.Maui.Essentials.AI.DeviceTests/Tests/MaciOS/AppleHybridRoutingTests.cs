#if IOS || MACCATALYST
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

public sealed class AppleHybridRoutingTests(ITestOutputHelper output)
    : HybridRoutingTestsBase<AppleIntelligenceChatClient>(output);
#endif
