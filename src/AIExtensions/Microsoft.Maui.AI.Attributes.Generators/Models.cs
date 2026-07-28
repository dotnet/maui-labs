using System.Collections.Immutable;

namespace Microsoft.Maui.AI.Attributes.Generators;

internal enum ParameterKind
{
    JsonArgument,
    CancellationToken,
    ServiceProvider,
    AIFunctionArguments,
    FromServices,
    FromKeyedServices,
    Unknown,
}

internal enum ReturnShape
{
    Void,
    Sync,
    Task,
    TaskOfT,
    ValueTask,
    ValueTaskOfT,
}

internal sealed record ReturnInfo(ReturnShape Shape, string? TypeName);

internal sealed record ParameterModel(
    string Name,
    string TypeName,
    string UnannotatedTypeName,
    ParameterKind Kind,
    string? KeyedServiceKey,
    bool IsNullable,
    bool HasDefault,
    string? DefaultLiteral,
    string? Description);

internal sealed record MethodModel(
    string MethodName,
    string ToolName,
    string? Description,
    bool ApprovalRequired,
    bool IsStatic,
    bool IsProperty,
    bool IsPropertySetter,
    ImmutableArray<ParameterModel> Parameters,
    ReturnInfo ReturnInfo,
    string GeneratedClassName);

internal sealed record SourceTypeModel(
    string FullyQualifiedName,
    string SimpleName,
    ImmutableArray<MethodModel> Methods);

internal sealed record ContainingTypeInfo(
    string Keyword,
    string Name,
    string Accessibility);

internal sealed record ContextModel(
    string Namespace,
    string ClassName,
    string FullyQualifiedName,
    string Accessibility,
    ImmutableArray<ContainingTypeInfo> ContainingTypes,
    ImmutableArray<SourceTypeModel> SourceTypes,
    ImmutableArray<DiagnosticInfo> Diagnostics,
    bool EmitBaseClass = false)
{
    public ContextModel WithAdditionalSourceTypes(ImmutableArray<SourceTypeModel> newTypes)
    {
        var existing = SourceTypes.ToList();
        foreach (var nt in newTypes)
        {
            if (!existing.Any(e => e.FullyQualifiedName == nt.FullyQualifiedName))
                existing.Add(nt);
        }
        return this with { SourceTypes = [.. existing] };
    }
}

internal sealed record AssemblyExportedMember(
    string ContainingTypeFQN,
    string ContainingTypeSimpleName,
    MethodModel Method);
