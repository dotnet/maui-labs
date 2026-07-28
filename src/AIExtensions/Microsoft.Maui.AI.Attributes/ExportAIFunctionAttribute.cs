using System;

namespace Microsoft.Maui.AI.Attributes;

/// <summary>
/// Marks a method to be exported as an AI tool function.
/// Methods with this attribute are discovered by the source generator on any
/// <see cref="AIToolContext"/> that lists the declaring type via
/// <see cref="AIToolSourceAttribute"/>, and surfaced through
/// <c>TContext.Default.Tools</c>.
/// </summary>
/// <remarks>
/// The method may be either a public instance method on a type that is
/// registered in DI, or a public <c>static</c> method (in which case no DI
/// resolution happens for the host service).
/// Method and parameter descriptions should preferably use
/// <see cref="System.ComponentModel.DescriptionAttribute"/>.
/// Return values are automatically serialized by Microsoft.Extensions.AI.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ExportAIFunctionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with default name (derived from method name).
    /// </summary>
    public ExportAIFunctionAttribute() { }

    /// <summary>
    /// Initializes a new instance with an explicit tool name.
    /// </summary>
    /// <param name="name">The name of the tool as exposed to the AI model.</param>
    public ExportAIFunctionAttribute(string name) => Name = name;

    /// <summary>
    /// The tool name exposed to the AI model (e.g. "get_plants").
    /// If not set, defaults to the method name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Description shown to the AI model explaining what this tool does.
    /// If this property is not set, the method-level description is used.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When true, the tool will require user approval before execution.
    /// The function is wrapped in <c>ApprovalRequiredAIFunction</c> so that
    /// <c>FunctionInvokingChatClient</c> yields a <c>ToolApprovalRequestContent</c>
    /// instead of auto-invoking.
    /// </summary>
    public bool ApprovalRequired { get; set; }
}
