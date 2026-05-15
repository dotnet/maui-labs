// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI;

/// <summary>
/// Factory for creating <see cref="IAgentSession"/> instances bound to an <see cref="IChatClient"/>.
/// </summary>
public interface IAgentSessionFactory
{
    IAgentSession Create(IChatClient client);
}
