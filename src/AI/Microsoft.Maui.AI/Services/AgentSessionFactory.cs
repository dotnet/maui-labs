// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI;

/// <summary>
/// Default implementation of <see cref="IAgentSessionFactory"/>.
/// </summary>
public class AgentSessionFactory : IAgentSessionFactory
{
    public IAgentSession Create(IChatClient client) => new AgentSession(client);
}
