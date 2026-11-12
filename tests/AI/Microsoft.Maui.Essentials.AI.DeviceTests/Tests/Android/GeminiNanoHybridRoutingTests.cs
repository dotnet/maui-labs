#if ANDROID
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Essentials.AI.DeviceTests;

[Collection(nameof(GeminiNanoCollection))]
public sealed class GeminiNanoHybridRoutingTests(ITestOutputHelper output)
    : HybridRoutingTestsBase<GeminiNanoChatClient>(output);
#endif
