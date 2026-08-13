namespace Microsoft.Maui.CopilotSdk.Tests;

/// <summary>Convenience factory for constructing a chat client backed by a fake backend.</summary>
internal static class TestChatClient
{
    public static CopilotSdkChatClient Create(
        FakeCopilotBackend backend,
        CopilotSdkConfiguration? configuration = null)
        => new(configuration ?? new CopilotSdkConfiguration(), backend, ownsBackend: true);
}
