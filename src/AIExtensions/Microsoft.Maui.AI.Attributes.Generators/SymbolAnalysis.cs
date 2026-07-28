using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.AI.Attributes.Generators;

internal static class SymbolAnalysis
{
    internal const string AIToolContextFullName = "Microsoft.Maui.AI.Attributes.AIToolContext";
    internal const string AIToolSourceAttributeFullName = "Microsoft.Maui.AI.Attributes.AIToolSourceAttribute";
    internal const string ExportAIFunctionAttributeFullName = "Microsoft.Maui.AI.Attributes.ExportAIFunctionAttribute";
    internal const string DescriptionAttributeFullName = "System.ComponentModel.DescriptionAttribute";
    internal const string FromServicesAttributeFullName = "Microsoft.Extensions.DependencyInjection.FromServicesAttribute";
    internal const string FromKeyedServicesAttributeFullName = "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute";
    internal const string CancellationTokenFullName = "System.Threading.CancellationToken";
    internal const string IServiceProviderFullName = "System.IServiceProvider";
    internal const string AIFunctionArgumentsFullName = "Microsoft.Extensions.AI.AIFunctionArguments";

    /// <summary>
    /// Gets the root namespace from the compilation (MSBuild property or fallback to assembly name).
    /// </summary>
    internal static string GetRootNamespace(Compilation compilation)
    {
        var namespaces = new Dictionary<string, int>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var name = ns.Name.ToString();
                var topLevel = name.Contains('.') ? name.Substring(0, name.IndexOf('.')) : name;
                namespaces.TryGetValue(topLevel, out var count);
                namespaces[topLevel] = count + 1;
            }
        }

        if (namespaces.Count > 0)
            return namespaces.OrderByDescending(kv => kv.Value).First().Key;

        // No namespaces found — emit in the global namespace.
        return "";
    }

    /// <summary>
    /// Extracts method/property info for a single [ExportAIFunction] member for the assembly-wide pipeline.
    /// </summary>
    internal static AssemblyExportedMember? GetExportedMemberInfo(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (ctx.TargetSymbol is IMethodSymbol method)
        {
            if (method.MethodKind != MethodKind.Ordinary)
                return null;
            if (method.DeclaredAccessibility != Accessibility.Public && method.DeclaredAccessibility != Accessibility.Internal)
                return null;
            if (method.ContainingType is null)
                return null;

            var methods = GetExportedMethods(method.ContainingType, diagnostics, ct);
            var match = methods.FirstOrDefault(m => m.MethodName == method.Name && !m.IsProperty);
            if (match is null)
                return null;

            return new AssemblyExportedMember(
                method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                CodeEmitter.SanitizeIdentifier(method.ContainingType.Name),
                match);
        }

        if (ctx.TargetSymbol is IPropertySymbol prop)
        {
            if (prop.ContainingType is null)
                return null;

            var methods = GetExportedMethods(prop.ContainingType, diagnostics, ct);
            var match = methods.FirstOrDefault(m => m.MethodName == prop.Name && m.IsProperty);
            if (match is null)
                return null;

            return new AssemblyExportedMember(
                prop.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                CodeEmitter.SanitizeIdentifier(prop.ContainingType.Name),
                match);
        }

        return null;
    }

    internal static ContextModel? GetContextModel(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol contextSymbol)
            return null;

        if (!InheritsFrom(contextSymbol, AIToolContextFullName))
            return null;

        var sourceTypes = new List<SourceTypeModel>();
        var diagnostics = new List<DiagnosticInfo>();

        foreach (var attr in contextSymbol.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (attr.AttributeClass?.ToDisplayString() != AIToolSourceAttributeFullName)
                continue;

            if (attr.ConstructorArguments.Length != 1)
                continue;

            if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol sourceTypeSymbol)
                continue;

            // Read IncludeTools / ExcludeTools named arguments
            ImmutableArray<string>? includeTools = null;
            ImmutableArray<string>? excludeTools = null;
            foreach (var na in attr.NamedArguments)
            {
                if (na.Key == "IncludeTools" && !na.Value.IsNull)
                {
                    includeTools = na.Value.Values
                        .Where(v => v.Value is string)
                        .Select(v => (string)v.Value!)
                        .ToImmutableArray();
                }
                else if (na.Key == "ExcludeTools" && !na.Value.IsNull)
                {
                    excludeTools = na.Value.Values
                        .Where(v => v.Value is string)
                        .Select(v => (string)v.Value!)
                        .ToImmutableArray();
                }
            }

            var methods = GetExportedMethods(sourceTypeSymbol, diagnostics, ct);

            // Apply include filter: empty or null = all tools, non-empty = only those listed
            if (includeTools is { Length: > 0 } include)
            {
                var includeSet = new HashSet<string>(include, System.StringComparer.Ordinal);
                foreach (var name in include)
                {
                    if (!methods.Any(m => m.MethodName == name))
                    {
                        diagnostics.Add(DiagnosticInfo.FilteredToolNotFound(
                            name,
                            sourceTypeSymbol.ToDisplayString(),
                            attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()));
                    }
                }
                methods = [.. methods.Where(m => includeSet.Contains(m.MethodName))];
            }
            // IncludeTools=[] or null → keep all (no filtering)

            // Apply exclude filter: removes from whatever include produced
            if (excludeTools is { Length: > 0 } exclude)
            {
                var excludeSet = new HashSet<string>(exclude, System.StringComparer.Ordinal);
                methods = [.. methods.Where(m => !excludeSet.Contains(m.MethodName))];
            }
            // ExcludeTools=[] or null → exclude nothing

            if (methods.Count == 0)
            {
                if (includeTools is null && excludeTools is null)
                {
                    // No exported methods at all — this is MAUIAI003
                    diagnostics.Add(DiagnosticInfo.NoExportableMethods(
                        sourceTypeSymbol.ToDisplayString(),
                        attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation()));
                }
                // Still add an empty source type so the context is always generated
            }

            sourceTypes.Add(new SourceTypeModel(
                sourceTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                CodeEmitter.SanitizeIdentifier(sourceTypeSymbol.Name),
                [.. methods]));
        }

        if (sourceTypes.Count == 0 && diagnostics.Count == 0)
            return null;

        var accessibility = contextSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "internal"
        };

        // Walk the containing type chain (innermost first → reversed to outermost first).
        var containingTypes = new List<ContainingTypeInfo>();
        var outer = contextSymbol.ContainingType;
        while (outer is not null)
        {
            var outerAccess = outer.DeclaredAccessibility switch
            {
                Accessibility.Public => "public",
                Accessibility.Internal => "internal",
                Accessibility.Protected => "protected",
                Accessibility.ProtectedOrInternal => "protected internal",
                Accessibility.ProtectedAndInternal => "private protected",
                Accessibility.Private => "private",
                _ => "internal"
            };
            var keyword = outer.IsRecord ? "record class" : "class";
            containingTypes.Add(new ContainingTypeInfo(keyword, outer.Name, outerAccess));
            outer = outer.ContainingType;
        }
        containingTypes.Reverse();

        return new ContextModel(
            contextSymbol.ContainingNamespace?.IsGlobalNamespace == true ? "" : contextSymbol.ContainingNamespace!.ToDisplayString(),
            contextSymbol.Name,
            contextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            accessibility,
            [.. containingTypes],
            [.. sourceTypes],
            [.. diagnostics]);
    }

    internal static List<MethodModel> GetExportedMethods(
        INamedTypeSymbol typeSymbol,
        List<DiagnosticInfo> diagnostics,
        CancellationToken ct)
    {
        var methods = new List<MethodModel>();
        var nameCollisions = new Dictionary<string, int>();

        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IMethodSymbol method)
                continue;
            if (method.MethodKind != MethodKind.Ordinary)
                continue;
            if (method.DeclaredAccessibility != Accessibility.Public && method.DeclaredAccessibility != Accessibility.Internal)
                continue;

            var exportAttr = method.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
            if (exportAttr is null)
                continue;

            var location = method.Locations.FirstOrDefault();

            if (method.IsGenericMethod)
            {
                diagnostics.Add(DiagnosticInfo.UnsupportedSignature(
                    method.ToDisplayString(),
                    "Generic methods are not supported.",
                    location));
                continue;
            }

            bool hasRefOrOut = false;
            foreach (var p in method.Parameters)
            {
                if (p.RefKind is RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnlyParameter)
                {
                    hasRefOrOut = true;
                    break;
                }
            }
            if (hasRefOrOut)
            {
                diagnostics.Add(DiagnosticInfo.UnsupportedSignature(
                    method.ToDisplayString(),
                    "Parameters with ref, out, or in modifiers are not supported.",
                    location));
                continue;
            }

            string? name = null;
            string? description = null;
            bool approvalRequired = false;

            if (exportAttr.ConstructorArguments.Length >= 1 &&
                exportAttr.ConstructorArguments[0].Value is string ctorName)
            {
                name = ctorName;
            }
            foreach (var na in exportAttr.NamedArguments)
            {
                switch (na.Key)
                {
                    case "Name": name = na.Value.Value as string; break;
                    case "Description": description = na.Value.Value as string; break;
                    case "ApprovalRequired": approvalRequired = na.Value.Value is true; break;
                }
            }

            if (description is null)
            {
                var descAttr = method.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
                if (descAttr is not null &&
                    descAttr.ConstructorArguments.Length >= 1 &&
                    descAttr.ConstructorArguments[0].Value is string d)
                {
                    description = d;
                }
            }

            var toolName = name ?? method.Name;
            var parameters = AnalyzeParameters(method, diagnostics);
            var returnInfo = AnalyzeReturnType(method.ReturnType);

            // Disambiguate same-method-name overloads in the generated class name.
            var baseClassName = $"{CodeEmitter.SanitizeIdentifier(typeSymbol.Name)}_{CodeEmitter.SanitizeIdentifier(method.Name)}_Tool";
            if (nameCollisions.TryGetValue(baseClassName, out var count))
            {
                nameCollisions[baseClassName] = count + 1;
                baseClassName = $"{baseClassName}_{count + 1}";
            }
            else
            {
                nameCollisions[baseClassName] = 0;
            }

            methods.Add(new MethodModel(
                method.Name,
                toolName,
                description,
                approvalRequired,
                method.IsStatic,
                IsProperty: false,
                IsPropertySetter: false,
                parameters,
                returnInfo,
                baseClassName));
        }

        // Also scan properties with [ExportAIFunction] — check the property itself
        // and its accessors (get/set), since C# allows attributes on accessors:
        //   public partial CartMode CartMode { [ExportAIFunction("get_cart_mode")] get; set; }
        foreach (var member in typeSymbol.GetMembers())
        {
            ct.ThrowIfCancellationRequested();

            if (member is not IPropertySymbol prop)
                continue;
            if (prop.IsIndexer)
                continue;
            if (prop.DeclaredAccessibility != Accessibility.Public && prop.DeclaredAccessibility != Accessibility.Internal)
                continue;

            // Check the property itself, then fall back to getter accessor, then setter accessor.
            var exportAttr = prop.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);

            var isGetterExport = false;
            var isSetterExport = false;

            if (exportAttr is null && prop.GetMethod is not null)
            {
                exportAttr = prop.GetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
                if (exportAttr is not null)
                    isGetterExport = true;
            }

            if (exportAttr is null && prop.SetMethod is not null)
            {
                exportAttr = prop.SetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
                if (exportAttr is not null)
                    isSetterExport = true;
            }

            // Also check if the setter has a separate [ExportAIFunction] when getter already matched.
            AttributeData? setterExportAttr = null;
            if (isGetterExport && prop.SetMethod is not null)
            {
                setterExportAttr = prop.SetMethod.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == ExportAIFunctionAttributeFullName);
            }

            if (exportAttr is null)
                continue;

            if (prop.IsWriteOnly || prop.GetMethod is null)
                continue;

            // Emit the getter tool.
            EmitPropertyTool(methods, nameCollisions, typeSymbol, prop, exportAttr, isGetterExport || !isSetterExport);

            // If the setter has its own [ExportAIFunction], emit a separate tool for it.
            if (setterExportAttr is not null)
            {
                EmitPropertyTool(methods, nameCollisions, typeSymbol, prop, setterExportAttr, false);
            }
            else if (isSetterExport)
            {
                // The only export was on the setter — already emitted above as a property read tool.
                // Re-emit as setter semantics (the tool name/description came from the setter).
            }
        }

        return methods;
    }

    private static void EmitPropertyTool(
        List<MethodModel> methods,
        Dictionary<string, int> nameCollisions,
        INamedTypeSymbol typeSymbol,
        IPropertySymbol prop,
        AttributeData exportAttr,
        bool isReadTool)
    {
        string? name = null;
        string? description = null;
        bool approvalRequired = false;

        if (exportAttr.ConstructorArguments.Length >= 1 &&
            exportAttr.ConstructorArguments[0].Value is string ctorName)
        {
            name = ctorName;
        }
        foreach (var na in exportAttr.NamedArguments)
        {
            switch (na.Key)
            {
                case "Name": name = na.Value.Value as string; break;
                case "Description": description = na.Value.Value as string; break;
                case "ApprovalRequired": approvalRequired = na.Value.Value is true; break;
            }
        }

        if (description is null)
        {
            // Check for [Description] on the accessor or property.
            var source = isReadTool ? (ISymbol?)prop.GetMethod ?? prop : (ISymbol?)prop.SetMethod ?? prop;
            var descAttr = source.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
            if (descAttr is null)
            {
                descAttr = prop.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
            }
            if (descAttr is not null &&
                descAttr.ConstructorArguments.Length >= 1 &&
                descAttr.ConstructorArguments[0].Value is string d)
            {
                description = d;
            }
        }

        var toolName = name ?? prop.Name;
        var returnInfo = AnalyzeReturnType(prop.Type);

        var baseClassName = $"{CodeEmitter.SanitizeIdentifier(typeSymbol.Name)}_{CodeEmitter.SanitizeIdentifier(prop.Name)}_{(isReadTool ? "Get" : "Set")}_Tool";
        if (nameCollisions.TryGetValue(baseClassName, out var count))
        {
            nameCollisions[baseClassName] = count + 1;
            baseClassName = $"{baseClassName}_{count + 1}";
        }
        else
        {
            nameCollisions[baseClassName] = 0;
        }

        var parameters = isReadTool || prop.SetMethod is null
            ? []
            : ImmutableArray.Create(new ParameterModel(
                "value",
                prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                prop.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsJsonBindable(prop.Type) ? ParameterKind.JsonArgument : ParameterKind.Unknown,
                KeyedServiceKey: null,
                IsNullable: prop.Type.NullableAnnotation == NullableAnnotation.Annotated || prop.Type.IsReferenceType,
                HasDefault: false,
                DefaultLiteral: null,
                Description: "The new value to assign."));

        methods.Add(new MethodModel(
            prop.Name,
            toolName,
            description,
            approvalRequired,
            prop.IsStatic,
            IsProperty: true,
            IsPropertySetter: !isReadTool,
            parameters,
            returnInfo,
            baseClassName));
    }

    private static ImmutableArray<ParameterModel> AnalyzeParameters(
        IMethodSymbol method,
        List<DiagnosticInfo> diagnostics)
    {
        var list = new List<ParameterModel>(method.Parameters.Length);
        foreach (var p in method.Parameters)
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var unannotatedTypeName = p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var kind = ClassifyParameter(p, out string? keyedServiceKey);

            // hasDefault/defaultValueLiteral
            string? defaultLiteral = null;
            bool hasDefault = p.HasExplicitDefaultValue;
            if (hasDefault)
            {
                defaultLiteral = FormatDefaultLiteral(p.ExplicitDefaultValue, p.Type);
            }

            if (kind == ParameterKind.Unknown)
            {
                diagnostics.Add(DiagnosticInfo.UnserializableParameter(
                    method.ToDisplayString(),
                    p.Name,
                    typeName,
                    p.Locations.FirstOrDefault()));
                // Fall back to JSON binding so compilation succeeds; it will fail at runtime if invoked.
                kind = ParameterKind.JsonArgument;
            }

            list.Add(new ParameterModel(
                p.Name,
                typeName,
                unannotatedTypeName,
                kind,
                keyedServiceKey,
                p.Type.NullableAnnotation == NullableAnnotation.Annotated,
                hasDefault,
                defaultLiteral,
                GetParameterDescription(p)));
        }
        return [.. list];
    }

    private static string? GetParameterDescription(IParameterSymbol p)
    {
        var attr = p.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == DescriptionAttributeFullName);
        if (attr is not null &&
            attr.ConstructorArguments.Length >= 1 &&
            attr.ConstructorArguments[0].Value is string d)
        {
            return d;
        }
        return null;
    }

    private static ParameterKind ClassifyParameter(IParameterSymbol p, out string? keyedServiceKey)
    {
        keyedServiceKey = null;
        var fullName = p.Type.ToDisplayString();

        // Explicit attributes take precedence.
        var keyedAttr = p.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == FromKeyedServicesAttributeFullName);
        if (keyedAttr is not null)
        {
            if (keyedAttr.ConstructorArguments.Length >= 1)
            {
                keyedServiceKey = FormatConstantKey(keyedAttr.ConstructorArguments[0]);
            }
            return ParameterKind.FromKeyedServices;
        }

        if (p.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == FromServicesAttributeFullName))
        {
            return ParameterKind.FromServices;
        }

        // Framework-provided types.
        if (fullName is CancellationTokenFullName or "global::" + CancellationTokenFullName)
            return ParameterKind.CancellationToken;
        if (p.Type is INamedTypeSymbol nts && nts.ToDisplayString() == CancellationTokenFullName)
            return ParameterKind.CancellationToken;
        if (p.Type.ToDisplayString() == IServiceProviderFullName)
            return ParameterKind.ServiceProvider;
        if (p.Type.ToDisplayString() == AIFunctionArgumentsFullName)
            return ParameterKind.AIFunctionArguments;

        // Otherwise, bind from the argument dictionary (JSON). No DI inference — users must
        // mark DI parameters with [FromServices] / [FromKeyedServices].
        return IsJsonBindable(p.Type) ? ParameterKind.JsonArgument : ParameterKind.Unknown;
    }

    private static bool IsJsonBindable(ITypeSymbol type)
    {
        // Conservative allowlist for the warning diagnostic only. The runtime ultimately decides
        // via JsonSerializer. We flag "clearly non-bindable" things like delegates and pointer types.
        if (type.TypeKind is TypeKind.Delegate or TypeKind.Pointer or TypeKind.FunctionPointer)
            return false;
        return true;
    }

    private static string FormatConstantKey(TypedConstant c)
    {
        if (c.Value is null) return "null";
        if (c.Value is string s) return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return c.ToCSharpString();
    }

    private static string FormatDefaultLiteral(object? value, ITypeSymbol type)
    {
        if (value is null)
        {
            return type.IsReferenceType || type.NullableAnnotation == NullableAnnotation.Annotated
                ? "null"
                : $"default({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
        }
        return value switch
        {
            string s => SymbolDisplay.FormatLiteral(s, quote: true),
            bool b => b ? "true" : "false",
            char c => SymbolDisplay.FormatLiteral(c, quote: true),
            float f => f.ToString("G9", System.Globalization.CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            _ when type.TypeKind == TypeKind.Enum =>
                $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({value})",
            _ => value.ToString()!,
        };
    }

    internal static ReturnInfo AnalyzeReturnType(ITypeSymbol returnType)
    {
        var displayName = returnType.ToDisplayString();
        if (displayName == "void")
            return new ReturnInfo(ReturnShape.Void, null);

        if (returnType is INamedTypeSymbol named)
        {
            var defn = named.ConstructedFrom.ToDisplayString();
            if (defn == "System.Threading.Tasks.Task")
                return new ReturnInfo(ReturnShape.Task, null);
            if (defn == "System.Threading.Tasks.ValueTask")
                return new ReturnInfo(ReturnShape.ValueTask, null);
            if (defn == "System.Threading.Tasks.Task<TResult>")
            {
                var t = named.TypeArguments[0];
                return new ReturnInfo(
                    ReturnShape.TaskOfT,
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
            if (defn == "System.Threading.Tasks.ValueTask<TResult>")
            {
                var t = named.TypeArguments[0];
                return new ReturnInfo(
                    ReturnShape.ValueTaskOfT,
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return new ReturnInfo(
            ReturnShape.Sync,
            returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    internal static bool InheritsFrom(INamedTypeSymbol symbol, string baseTypeFullName)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.ToDisplayString() == baseTypeFullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
