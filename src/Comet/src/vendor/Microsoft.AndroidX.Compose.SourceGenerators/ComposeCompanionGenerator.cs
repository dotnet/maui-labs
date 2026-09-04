using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AndroidX.Compose.SourceGenerators;

/// <summary>
/// Emits the implementing partial of <c>[ComposeCompanion]</c>-decorated
/// classes — a Roslyn-time replacement for the
/// "<c>FindClass → GetStaticFieldID(Companion) → NewGlobalRef</c>"
/// boilerplate that every Compose value/reference wrapper would
/// otherwise hand-roll. Per-property partial-property declarations
/// carrying <c>[ComposeCompanionGetter]</c> get their bodies emitted
/// here too. See <see cref="ComposeCompanionEmitter"/> for the actual
/// codegen.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComposeCompanionGenerator : IIncrementalGenerator
{
    const string CompanionAttributeMetadataName = "AndroidX.Compose.ComposeCompanionAttribute";
    const string GetterAttributeMetadataName = "AndroidX.Compose.ComposeCompanionGetterAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Attributes.cs (shared with ComposeDefaultsGenerator) emits the
        // ComposeCompanion / ComposeCompanionGetter attribute sources via
        // RegisterPostInitializationOutput. We don't re-register here —
        // doing so would collide with the defaults generator and emit
        // duplicate-class errors. Tests instantiate Attributes.Source
        // directly when running this generator in isolation.

        var classes = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is ClassDeclarationSyntax c &&
                    c.AttributeLists.Count > 0,
                transform: static (ctx, _) => ctx)
            .Where(static ctx => ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is INamedTypeSymbol);

        var combined = classes.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var (ctx, compilation) = pair;
            var companionAttr = compilation.GetTypeByMetadataName(CompanionAttributeMetadataName);
            var getterAttr = compilation.GetTypeByMetadataName(GetterAttributeMetadataName);
            if (companionAttr is null || getterAttr is null) return;

            var classSyntax = (ClassDeclarationSyntax)ctx.Node;
            var classSymbol = (INamedTypeSymbol)ctx.SemanticModel.GetDeclaredSymbol(classSyntax)!;

            var attr = classSymbol.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, companionAttr));
            if (attr is null) return;

            // Generator runs once per class-decl syntax node. If a class
            // is declared in multiple partials we may see it twice; only
            // emit when this is the syntax node that *carries* the
            // attribute, to avoid duplicate-output collisions.
            if (!CarriesAttribute(classSyntax, attr)) return;

            var result = Build(classSymbol, attr, getterAttr);
            foreach (var diag in result.Diagnostics)
                spc.ReportDiagnostic(diag);
            if (result.Source is { } source && result.HintName is { } hint)
                spc.AddSource(hint, SourceText.From(source, Encoding.UTF8));
        });

        // Second pass — flag orphaned [ComposeCompanionGetter] properties
        // whose containing class lacks [ComposeCompanion]. Without this
        // diagnostic the developer would just hit the generic
        // "partial property has no implementing declaration" error and
        // have to guess what's missing.
        var orphanProps = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) =>
                    node is PropertyDeclarationSyntax p &&
                    p.AttributeLists.Count > 0,
                transform: static (ctx, _) => ctx)
            .Where(static ctx => ctx.SemanticModel.GetDeclaredSymbol((PropertyDeclarationSyntax)ctx.Node) is IPropertySymbol);

        var orphanCombined = orphanProps.Combine(context.CompilationProvider);

        context.RegisterSourceOutput(orphanCombined, static (spc, pair) =>
        {
            var (ctx, compilation) = pair;
            var companionAttr = compilation.GetTypeByMetadataName(CompanionAttributeMetadataName);
            var getterAttr = compilation.GetTypeByMetadataName(GetterAttributeMetadataName);
            if (companionAttr is null || getterAttr is null) return;

            var propSymbol = (IPropertySymbol)ctx.SemanticModel.GetDeclaredSymbol((PropertyDeclarationSyntax)ctx.Node)!;
            var ga = propSymbol.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, getterAttr));
            if (ga is null) return;

            var containingType = propSymbol.ContainingType;
            if (containingType is null) return;

            var hostAttr = containingType.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, companionAttr));
            if (hostAttr is not null) return;

            var loc = ga.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.CompanionGetterMissingHost, loc,
                containingType.ToDisplayString(), propSymbol.Name));
        });
    }

    static bool CarriesAttribute(ClassDeclarationSyntax classSyntax, AttributeData attr)
    {
        var attrSyntax = attr.ApplicationSyntaxReference?.GetSyntax();
        if (attrSyntax is null) return true;
        // Walk up to the parent class declaration.
        for (SyntaxNode? cur = attrSyntax; cur is not null; cur = cur.Parent)
        {
            if (cur is ClassDeclarationSyntax owner)
                return owner == classSyntax;
        }
        return true;
    }

    static GenerationResult Build(INamedTypeSymbol classSymbol, AttributeData attr, INamedTypeSymbol getterAttr)
    {
        var loc = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;
        var classDisplay = classSymbol.ToDisplayString();

        // Class-level validation.
        var diags = new List<Diagnostic>();

        bool isPartial = classSymbol.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is ClassDeclarationSyntax c &&
                      c.Modifiers.Any(SyntaxKind.PartialKeyword));
        if (!isPartial)
        {
            diags.Add(Diagnostic.Create(Diagnostics.CompanionNotPartial, loc, classDisplay));
            return new GenerationResult(null, null, diags.ToArray());
        }

        string? outerJniClass = null;
        if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string s && !string.IsNullOrWhiteSpace(s))
            outerJniClass = s;
        if (outerJniClass is null)
        {
            diags.Add(Diagnostic.Create(Diagnostics.CompanionMalformedOuter, loc, classDisplay));
            return new GenerationResult(null, null, diags.ToArray());
        }

        bool inlineClass = false;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "InlineClass" && named.Value.Value is bool b)
                inlineClass = b;
        }

        // Walk all properties for [ComposeCompanionGetter] declarations.
        var getters = new List<ComposeCompanionEmitter.Getter>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            var ga = prop.GetAttributes()
                .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, getterAttr));
            if (ga is null) continue;

            var propLoc = ga.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? loc;

            // Property shape: static partial with a get-only accessor and
            // a non-void return type. Setter is forbidden (companions are
            // read-only singletons).
            if (!prop.IsStatic || !IsDeclaredPartial(prop) || prop.SetMethod is not null || prop.GetMethod is null)
            {
                diags.Add(Diagnostic.Create(Diagnostics.CompanionGetterShape, propLoc, classDisplay, prop.Name));
                continue;
            }

            if (ga.ConstructorArguments.Length < 1 || ga.ConstructorArguments[0].Value is not string getterName || string.IsNullOrWhiteSpace(getterName))
            {
                diags.Add(Diagnostic.Create(Diagnostics.CompanionMalformedGetter, propLoc, classDisplay, prop.Name));
                continue;
            }

            string? returnDescriptor = null;
            foreach (var named in ga.NamedArguments)
            {
                if (named.Key == "ReturnDescriptor" && named.Value.Value is string rd && !string.IsNullOrWhiteSpace(rd))
                    returnDescriptor = rd;
            }

            if (inlineClass && returnDescriptor is not null)
            {
                diags.Add(Diagnostic.Create(Diagnostics.CompanionInlineReturnConflict, propLoc, classDisplay, prop.Name));
                continue;
            }

            // Return type must declare an accessible (IntPtr, JniHandleOwnership)
            // constructor so the generated wrapper line compiles.
            if (!HasJniPeerCtor(prop.Type))
            {
                diags.Add(Diagnostic.Create(Diagnostics.CompanionMissingPeerCtor, propLoc, classDisplay, prop.Name, prop.Type.ToDisplayString()));
                continue;
            }

            getters.Add(new ComposeCompanionEmitter.Getter(
                propertyName: prop.Name,
                returnTypeFullyQualified: prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes)),
                getterName: getterName,
                returnDescriptor: returnDescriptor));
        }

        // Also check for orphaned [ComposeCompanionGetter] on classes
        // that don't carry [ComposeCompanion] — handled by a separate
        // generator pass below, not here. (This generator only fires for
        // classes that *do* carry [ComposeCompanion].)

        if (diags.Count > 0)
            return new GenerationResult(null, null, diags.ToArray());

        var @namespace = classSymbol.ContainingNamespace?.IsGlobalNamespace == false
            ? classSymbol.ContainingNamespace.ToDisplayString()
            : null;

        var source = ComposeCompanionEmitter.Emit(@namespace, classSymbol.Name, outerJniClass, inlineClass, getters);
        var hint = $"AndroidX.Compose.Companion.{classSymbol.Name}.g.cs";
        return new GenerationResult(source, hint, Array.Empty<Diagnostic>());
    }

    static bool IsDeclaredPartial(IPropertySymbol prop) =>
        prop.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is PropertyDeclarationSyntax p &&
                      p.Modifiers.Any(SyntaxKind.PartialKeyword));

    static bool HasJniPeerCtor(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol n) return false;
        foreach (var ctor in n.InstanceConstructors)
        {
            if (ctor.Parameters.Length != 2) continue;
            if (ctor.DeclaredAccessibility == Accessibility.Private &&
                !SymbolEqualityComparer.Default.Equals(ctor.ContainingType, type))
            {
                continue;
            }
            var p0 = ctor.Parameters[0].Type;
            var p1 = ctor.Parameters[1].Type;
            if (p0.SpecialType == SpecialType.System_IntPtr || p0.ToDisplayString() == "System.IntPtr" || p0.ToDisplayString() == "nint")
            {
                if (p1.ToDisplayString() == "Android.Runtime.JniHandleOwnership")
                    return true;
            }
        }
        return false;
    }
}
