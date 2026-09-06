using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>
/// Factory for creating AgentContext instances with configured clients.
/// </summary>
internal static class SessionFactory
{
    public static AgentContext Create(string response = "Hello!")
    {
        var client = new TestChatClient(response);
        return Create(client);
    }

    public static AgentContext Create(TestChatClient client)
    {
        var agent = new UIAgent(client);
        return new AgentContext(agent);
    }
}
