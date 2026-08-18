// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.AI.Chat.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ToolBlockGenerator : IIncrementalGenerator
{
    private const string ToolBlockAttribute =
        "Microsoft.Maui.AI.Chat.ToolBlockAttribute";
    private const string ToolParameterAttribute =
        "Microsoft.Maui.AI.Chat.ToolParameterAttribute";
    private const string ToolResultAttribute =
        "Microsoft.Maui.AI.Chat.ToolResultAttribute";
    private const string FunctionBlockType =
        "Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock";

    private static readonly SymbolDisplayFormat EscapedNamespaceFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle:
            SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            ToolBlockAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, cancellationToken) =>
                Parse(attributeContext, cancellationToken));

        context.RegisterSourceOutput(results, static (productionContext, result) =>
        {
            foreach (var diagnostic in result.Diagnostics)
            {
                productionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ById(diagnostic.DescriptorId),
                    diagnostic.Location.ToLocation(),
                    diagnostic.Arguments.Cast<object?>().ToArray()));
            }

            if (result.Model is not null)
                EmitHandler(productionContext, result.Model);
        });

        context.RegisterSourceOutput(
            results
                .Where(static result => result.Model is not null)
                .Select(static (result, _) => result.Model!)
                .Collect(),
            static (productionContext, models) =>
                EmitRegistration(productionContext, models));
    }

    private static ParseResult Parse(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        if (context.TargetSymbol is not INamedTypeSymbol type
            || context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return new(null, diagnostics.ToImmutable());
        }

        var location = SourceLocation.From(declaration.Identifier.GetLocation());
        var name = type.Name;

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            AddDiagnostic(DiagnosticDescriptors.NotPartial, location, name);
        if (type.ContainingType is not null)
            AddDiagnostic(DiagnosticDescriptors.NestedType, location, name);
        if (type.IsAbstract)
            AddDiagnostic(DiagnosticDescriptors.IsAbstract, location, name);
        if (type.IsGenericType)
            AddDiagnostic(DiagnosticDescriptors.IsGeneric, location, name);
        if (!ExtendsFunctionBlock(type))
            AddDiagnostic(DiagnosticDescriptors.WrongBaseClass, location, name);
        if (!HasPublicParameterlessConstructor(type))
            AddDiagnostic(DiagnosticDescriptors.MissingConstructor, location, name);

        var toolName = context.Attributes
            .SelectMany(attribute => attribute.ConstructorArguments)
            .Select(argument => argument.Value as string)
            .FirstOrDefault(value => value is not null);
        if (string.IsNullOrWhiteSpace(toolName))
            AddDiagnostic(DiagnosticDescriptors.EmptyToolName, location, name);

        var parameters = ParseProperties(
            type,
            ToolParameterAttribute,
            DiagnosticDescriptors.DuplicateArgumentKey,
            diagnostics,
            cancellationToken);
        var results = ParseProperties(
            type,
            ToolResultAttribute,
            DiagnosticDescriptors.DuplicateResultKey,
            diagnostics,
            cancellationToken);

        if (diagnostics.Any(diagnostic =>
                DiagnosticDescriptors.ById(diagnostic.DescriptorId).DefaultSeverity
                    == DiagnosticSeverity.Error))
        {
            return new(null, diagnostics.ToImmutable());
        }

        var ns = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString(EscapedNamespaceFormat);
        return new(
            new ToolBlockModel(
                ns,
                type.Name,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                toolName!,
                parameters,
                results,
                location),
            diagnostics.ToImmutable());

        void AddDiagnostic(
            DiagnosticDescriptor descriptor,
            SourceLocation diagnosticLocation,
            params string[] arguments)
        {
            diagnostics.Add(new(
                descriptor.Id,
                diagnosticLocation,
                arguments.ToImmutableArray()));
        }
    }

    private static ImmutableArray<ToolPropertyModel> ParseProperties(
        INamedTypeSymbol type,
        string attributeName,
        DiagnosticDescriptor duplicateDescriptor,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var properties = ImmutableArray.CreateBuilder<ToolPropertyModel>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attribute = property.GetAttributes().FirstOrDefault(candidate =>
                candidate.AttributeClass?.ToDisplayString() == attributeName);
            if (attribute is null)
                continue;

            var propertyLocation = SourceLocation.From(
                property.Locations.FirstOrDefault() ?? Location.None);
            if (property.SetMethod is null
                || property.SetMethod.DeclaredAccessibility is not (
                    Accessibility.Public
                    or Accessibility.Internal
                    or Accessibility.ProtectedOrInternal))
            {
                diagnostics.Add(new(
                    DiagnosticDescriptors.PropertySetterUnavailable.Id,
                    propertyLocation,
                    ImmutableArray.Create(property.Name)));
                continue;
            }

            var key = property.Name;
            var hasExplicitName = false;
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "Name"
                    && argument.Value.Value is string overrideName
                    && !string.IsNullOrWhiteSpace(overrideName))
                {
                    key = overrideName;
                    hasExplicitName = true;
                    break;
                }
            }

            if (!seenKeys.Add(key))
            {
                diagnostics.Add(new(
                    duplicateDescriptor.Id,
                    propertyLocation,
                    ImmutableArray.Create(key)));
                continue;
            }

            properties.Add(new(
                property.Name,
                key,
                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                hasExplicitName));
        }

        return properties.ToImmutable();
    }

    private static bool ExtendsFunctionBlock(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString() == FunctionBlockType)
                return true;
        }
        return false;
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type) =>
        type.InstanceConstructors.Any(constructor =>
            constructor.Parameters.Length == 0
            && constructor.DeclaredAccessibility == Accessibility.Public);

    private static void EmitHandler(
        SourceProductionContext context,
        ToolBlockModel model)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using global::System.Text.Json;");
        builder.AppendLine();
        if (!string.IsNullOrEmpty(model.Namespace))
        {
            builder.Append("namespace ").Append(model.Namespace).AppendLine(";");
            builder.AppendLine();
        }

        var handlerName = EscapeIdentifier(model.ClassName) + "GeneratedHandler";
        builder.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        builder.Append("internal sealed class ").Append(handlerName).AppendLine();
        builder.Append("    : global::Microsoft.Maui.AI.Chat.ContentBlockHandler<")
            .Append(model.FullyQualifiedType).AppendLine(">");
        builder.AppendLine("{");
        builder.Append("    public override global::Microsoft.Maui.AI.Chat.BlockMappingResult<")
            .Append(model.FullyQualifiedType).AppendLine("> Handle(");
        builder.AppendLine(
            "        global::Microsoft.Maui.AI.Chat.BlockMappingContext context,");
        builder.Append("        ").Append(model.FullyQualifiedType).AppendLine(" state)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (state.Call is null)");
        builder.AppendLine("        {");
        builder.AppendLine(
            "            global::Microsoft.Extensions.AI.FunctionCallContent? call = null;");
        builder.AppendLine("            foreach (var content in context.UnhandledContents)");
        builder.AppendLine("            {");
        builder.AppendLine(
            "                if (content is global::Microsoft.Extensions.AI.FunctionCallContent candidate");
        builder.AppendLine("                    && !candidate.InformationalOnly");
        builder.Append("                    && candidate.Name == \"")
            .Append(EscapeString(model.ToolName)).AppendLine("\")");
        builder.AppendLine("                {");
        builder.AppendLine("                    call = candidate;");
        builder.AppendLine("                    break;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (call is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                context.MarkHandled(call);");
        builder.AppendLine("                state.Call = call;");
        EmitCallProperties(builder, model.Parameters);
        builder.AppendLine("                foreach (var content in context.UnhandledContents)");
        builder.AppendLine("                {");
        builder.AppendLine(
            "                    if (content is global::Microsoft.Extensions.AI.FunctionResultContent result");
        builder.AppendLine("                        && result.CallId == call.CallId)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        context.MarkHandled(result);");
        builder.AppendLine("                        ApplyFunctionResult(state, result);");
        builder.AppendLine("                        break;");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.Append("                return global::Microsoft.Maui.AI.Chat.BlockMappingResult<")
            .Append(model.FullyQualifiedType).AppendLine(">.Emit(state, state);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (state.Call is not null)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (var content in context.UnhandledContents)");
        builder.AppendLine("            {");
        builder.AppendLine(
            "                if (content is global::Microsoft.Extensions.AI.FunctionResultContent result");
        builder.AppendLine("                    && result.CallId == state.Call.CallId)");
        builder.AppendLine("                {");
        builder.AppendLine("                    context.MarkHandled(result);");
        builder.AppendLine("                    ApplyFunctionResult(state, result);");
        builder.Append("                    return global::Microsoft.Maui.AI.Chat.BlockMappingResult<")
            .Append(model.FullyQualifiedType).AppendLine(">.Complete();");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.Append("        return global::Microsoft.Maui.AI.Chat.BlockMappingResult<")
            .Append(model.FullyQualifiedType).AppendLine(">.Pass();");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.Append("    protected override bool ApplyFunctionResult(")
            .Append(model.FullyQualifiedType).AppendLine(" state,");
        builder.AppendLine(
            "        global::Microsoft.Extensions.AI.FunctionResultContent result)");
        builder.AppendLine("    {");
        builder.AppendLine(
            "        if (state.Call is null || result.CallId != state.Call.CallId)");
        builder.AppendLine("            return false;");
        builder.AppendLine();
        builder.AppendLine("        state.Result = result;");
        EmitResultProperties(builder, model.Results);
        builder.AppendLine("        return true;");
        builder.AppendLine("    }");
        EmitConversionHelper(builder);
        builder.AppendLine("}");

        context.AddSource(GetHintName(model), builder.ToString());
    }

    private static void EmitCallProperties(
        StringBuilder builder,
        ImmutableArray<ToolPropertyModel> properties)
    {
        if (properties.IsEmpty)
            return;

        builder.AppendLine("                if (call.Arguments is { } arguments)");
        builder.AppendLine("                {");
        foreach (var property in properties)
        {
            var local = "__" + SanitizeIdentifier(property.PropertyName);
            builder.Append("                    if (arguments.TryGetValue(\"")
                .Append(EscapeString(property.Key)).Append("\", out var ")
                .Append(local).Append(") && ").Append(local).AppendLine(" is not null)");
            builder.AppendLine("                    {");
            builder.AppendLine("                        try");
            builder.AppendLine("                        {");
            builder.Append("                            state.")
                .Append(EscapeIdentifier(property.PropertyName))
                .Append(" = ConvertValue<").Append(property.TypeName).Append(">(")
                .Append(local).AppendLine(");");
            builder.AppendLine("                        }");
            builder.AppendLine("                        catch (global::System.Exception) { }");
            builder.AppendLine("                    }");
        }
        builder.AppendLine("                }");
        builder.AppendLine();
    }

    private static void EmitResultProperties(
        StringBuilder builder,
        ImmutableArray<ToolPropertyModel> properties)
    {
        if (properties.IsEmpty)
            return;

        builder.AppendLine("                    if (result.Result is not null)");
        builder.AppendLine("                    {");
        if (properties.Length == 1)
        {
            var property = properties[0];
            if (property.HasExplicitName)
            {
                var local = "__" + SanitizeIdentifier(property.PropertyName);
                builder.AppendLine(
                    "                        var resultObject = result.Result is global::System.Text.Json.JsonElement element");
                builder.AppendLine(
                    "                            ? element");
                builder.AppendLine(
                    "                            : global::System.Text.Json.JsonSerializer.SerializeToElement(result.Result, JsonOptions);");
                builder.AppendLine(
                    "                        if (resultObject.ValueKind == global::System.Text.Json.JsonValueKind.Object");
                builder.Append("                            && resultObject.TryGetProperty(\"")
                    .Append(EscapeString(property.Key)).Append("\", out var ")
                    .Append(local)
                    .Append(") && ").Append(local)
                    .AppendLine(".ValueKind is not global::System.Text.Json.JsonValueKind.Null and not global::System.Text.Json.JsonValueKind.Undefined)");
                builder.AppendLine("                        {");
                builder.AppendLine("                            try");
                builder.AppendLine("                            {");
                builder.Append("                                state.")
                    .Append(EscapeIdentifier(property.PropertyName))
                    .Append(" = ConvertValue<").Append(property.TypeName).Append(">(")
                    .Append(local).AppendLine(");");
                builder.AppendLine("                            }");
                builder.AppendLine("                            catch (global::System.Exception) { }");
                builder.AppendLine("                        }");
            }
            else
            {
                builder.AppendLine("                        try");
                builder.AppendLine("                        {");
                builder.Append("                            state.")
                    .Append(EscapeIdentifier(property.PropertyName))
                    .Append(" = ConvertValue<").Append(property.TypeName)
                    .AppendLine(">(result.Result);");
                builder.AppendLine("                        }");
                builder.AppendLine("                        catch (global::System.Exception) { }");
            }
        }
        else
        {
            builder.AppendLine(
                "                        var resultObject = result.Result is global::System.Text.Json.JsonElement element");
            builder.AppendLine(
                "                            ? element");
            builder.AppendLine(
                "                            : global::System.Text.Json.JsonSerializer.SerializeToElement(result.Result, JsonOptions);");
            builder.AppendLine(
                "                        if (resultObject.ValueKind == global::System.Text.Json.JsonValueKind.Object)");
            builder.AppendLine("                        {");
            foreach (var property in properties)
            {
                var local = "__" + SanitizeIdentifier(property.PropertyName);
                builder.Append("                            if (resultObject.TryGetProperty(\"")
                    .Append(EscapeString(property.Key)).Append("\", out var ")
                    .Append(local)
                    .Append(") && ").Append(local)
                    .AppendLine(".ValueKind is not global::System.Text.Json.JsonValueKind.Null and not global::System.Text.Json.JsonValueKind.Undefined)");
                builder.AppendLine("                            {");
                builder.AppendLine("                                try");
                builder.AppendLine("                                {");
                builder.Append("                                    state.")
                    .Append(EscapeIdentifier(property.PropertyName))
                    .Append(" = ConvertValue<").Append(property.TypeName).Append(">(")
                    .Append(local).AppendLine(");");
                builder.AppendLine("                                }");
                builder.AppendLine("                                catch (global::System.Exception) { }");
                builder.AppendLine("                            }");
            }
            builder.AppendLine("                        }");
        }
        builder.AppendLine("                    }");
    }

    private static void EmitConversionHelper(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine(
            "    private static readonly global::System.Text.Json.JsonSerializerOptions JsonOptions =");
        builder.AppendLine(
            "        new() { PropertyNameCaseInsensitive = true };");
        builder.AppendLine();
        builder.AppendLine("    private static T ConvertValue<T>(object? value)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (value is null)");
        builder.AppendLine("            return default!;");
        builder.AppendLine("        if (value is T typed)");
        builder.AppendLine("            return typed;");
        builder.AppendLine(
            "        if (value is global::System.Text.Json.JsonElement element)");
        builder.AppendLine(
            "            return element.Deserialize<T>(JsonOptions)!;");
        builder.AppendLine(
            "        if (value is string json && typeof(T) != typeof(string))");
        builder.AppendLine("        {");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine(
            "                return global::System.Text.Json.JsonSerializer.Deserialize<T>(json, JsonOptions)!;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Text.Json.JsonException) { }");
        builder.AppendLine("        }");
        builder.AppendLine(
            "        var serialized = global::System.Text.Json.JsonSerializer.SerializeToElement(value, JsonOptions);");
        builder.AppendLine("        return serialized.Deserialize<T>(JsonOptions)!;");
        builder.AppendLine("    }");
    }

    private static void EmitRegistration(
        SourceProductionContext context,
        ImmutableArray<ToolBlockModel> models)
    {
        if (models.IsEmpty)
            return;

        var unique = new Dictionary<string, ToolBlockModel>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (unique.TryGetValue(model.ToolName, out var existing))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateToolName,
                    model.Location.ToLocation(),
                    model.ToolName,
                    existing.ClassName,
                    model.ClassName));
            }
            else
            {
                unique.Add(model.ToolName, model);
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace Microsoft.Maui.AI.Chat;");
        builder.AppendLine();
        builder.AppendLine(
            "[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        builder.AppendLine("internal static class GeneratedToolBlockRegistrations");
        builder.AppendLine("{");
        builder.AppendLine(
            "    internal static void AddGeneratedToolBlocks(this global::Microsoft.Maui.AI.Chat.UIAgentOptions options)");
        builder.AppendLine("    {");
        foreach (var model in unique.Values)
        {
            var handler = string.IsNullOrEmpty(model.Namespace)
                ? "global::" + EscapeIdentifier(model.ClassName) + "GeneratedHandler"
                : "global::" + model.Namespace + "." + EscapeIdentifier(model.ClassName)
                    + "GeneratedHandler";
            builder.Append("        options.AddBlockHandler(new ")
                .Append(handler).AppendLine("());");
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        context.AddSource("GeneratedToolBlockRegistrations.g.cs", builder.ToString());
    }

    private static string GetHintName(ToolBlockModel model) =>
        string.IsNullOrEmpty(model.Namespace)
            ? model.ClassName + "GeneratedHandler.g.cs"
            : SanitizeHint(model.Namespace) + "." + model.ClassName
                + "GeneratedHandler.g.cs";

    private static string SanitizeHint(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) || character is '.' or '_'
                    ? character
                    : '_');
        }
        return builder.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        return builder.ToString();
    }

    private static string EscapeIdentifier(string value) =>
        SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
            || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None
            ? "@" + value
            : value;

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
