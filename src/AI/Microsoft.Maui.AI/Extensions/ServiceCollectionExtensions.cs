// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentSession(this IServiceCollection services)
    {
        services.AddTransient<IAgentSessionFactory, AgentSessionFactory>();
        return services;
    }
}
