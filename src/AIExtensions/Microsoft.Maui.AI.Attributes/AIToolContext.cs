using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Attributes;

/// <summary>
/// Base class for source-generated AI tool contexts. Subclasses decorated with
/// <see cref="AIToolSourceAttribute"/> have their <see cref="Tools"/> override implemented
/// by the source generator at compile time.
/// </summary>
/// <remarks>
/// This follows the same pattern as <c>System.Text.Json.Serialization.JsonSerializerContext</c>:
/// declare a partial class, decorate it with attributes, and the source generator fills in the
/// implementation &#8212; no runtime reflection for discovery.
/// </remarks>
public abstract class AIToolContext
{
    /// <summary>
    /// Gets the AI tools defined by this context. The returned list is cached and the same
    /// instance is returned on every access. Tools do not capture any <see cref="IServiceProvider"/>;
    /// tools whose backing method requires services read them from
    /// <see cref="AIFunctionArguments.Services"/> at invocation time.
    /// </summary>
    /// <remarks>
    /// If any tool in this context binds to an instance method or a <c>[FromServices]</c>
    /// parameter, callers must set <see cref="AIFunctionArguments.Services"/> before invoking
    /// the tool. <see cref="Microsoft.Extensions.AI.ChatClientBuilderChatClientExtensions.UseFunctionInvocation"/>
    /// combined with <c>ChatClientBuilder.Build(IServiceProvider)</c> does this automatically.
    /// </remarks>
    public abstract IReadOnlyList<AITool> Tools { get; }
}
