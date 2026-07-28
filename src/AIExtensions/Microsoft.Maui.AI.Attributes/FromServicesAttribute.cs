using System;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Marks a parameter of an <c>[ExportAIFunction]</c>-decorated method as resolved from the
/// <see cref="IServiceProvider"/> passed via <c>AIFunctionArguments.Services</c> at invocation
/// time, rather than from the AI function's argument dictionary. Parameters marked with this
/// attribute are excluded from the tool's JSON schema.
/// </summary>
/// <remarks>
/// <para>
/// This attribute lives in the <c>Microsoft.Extensions.DependencyInjection</c> namespace — the
/// same namespace as <see cref="FromKeyedServicesAttribute"/> — for discoverability.
/// </para>
/// <para>
/// For keyed services, use <see cref="FromKeyedServicesAttribute"/> instead.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute : Attribute
{
}
