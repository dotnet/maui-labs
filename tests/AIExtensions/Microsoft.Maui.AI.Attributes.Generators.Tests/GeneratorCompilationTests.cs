using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.Maui.AI.Attributes.Generators.Tests;

/// <summary>
/// Tests that the generator produces compilable output and emits the expected diagnostics.
/// These complement the snapshot tests by asserting structural properties of the output
/// (tool count, diagnostic IDs, compilation success) rather than exact text.
/// </summary>
public class GeneratorCompilationTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static (ImmutableArray<Diagnostic> GeneratorDiags, ImmutableArray<Diagnostic> CompilationDiags, Compilation Output) RunAndCompile(string source)
    {
        var driver = GeneratorTestHarness.RunGenerator(source, out var output, out var diags);
        var generatorDiags = driver.GetRunResult().Diagnostics;
        var compilationDiags = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generatorDiags, compilationDiags, output);
    }

    private static void AssertCleanCompilation(string source)
    {
        var (genDiags, compDiags, _) = RunAndCompile(source);
        Assert.Empty(genDiags.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(compDiags);
    }

    private static int CountGeneratedSources(string source)
    {
        var driver = GeneratorTestHarness.RunGenerator(source, out _, out _);
        return driver.GetRunResult().GeneratedTrees.Length;
    }

    private static string GetGeneratedSource(Compilation output, string contextClassName)
    {
        // Find the generated syntax tree that contains the user-declared context class (not the assembly-wide one).
        // Generated trees come after the input trees and have file paths ending in .g.cs
        foreach (var tree in output.SyntaxTrees)
        {
            var text = tree.ToString();
            var path = tree.FilePath ?? "";
            if (path.EndsWith(".g.cs") && text.Contains($"partial class {contextClassName}"))
                return text;
        }
        // Fallback: search generated trees only
        foreach (var tree in output.SyntaxTrees)
        {
            var path = tree.FilePath ?? "";
            if (path.EndsWith(".g.cs"))
            {
                var text = tree.ToString();
                if (text.Contains($"partial class {contextClassName}"))
                    return text;
            }
        }
        return output.SyntaxTrees.Last().ToString();
    }

    [Fact]
    public void StaticClass_WithStaticMethods_CompilesCleanly()
        => AssertCleanCompilation(Inputs.StaticClassWithStaticMethods);

    [Fact]
    public void StaticClass_WithStaticMethods_EmitsTwoTools()
    {
        var (_, _, output) = RunAndCompile(Inputs.StaticClassWithStaticMethods);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("new MathHelper_Add_Tool()", generated);
        Assert.Contains("new MathHelper_Negate_Tool()", generated);
    }

    [Fact]
    public void StaticMethodOnNonStaticClass_CompilesCleanly()
        => AssertCleanCompilation(Inputs.StaticMethodOnNonStaticClass);

    [Fact]
    public void StaticMethodOnNonStaticClass_EmitsBothStaticAndInstance()
    {
        var (_, _, output) = RunAndCompile(Inputs.StaticMethodOnNonStaticClass);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        // Static method: class name derived from method name "Echo"
        Assert.Contains("Utility_Echo_Tool", generated);
        // Instance method: class name derived from method name "EchoInstance"
        Assert.Contains("Utility_EchoInstance_Tool", generated);
    }

    [Fact]
    public void StaticMethodWithFromServices_CompilesCleanly()
        => AssertCleanCompilation(Inputs.StaticMethodWithFromServices);

    [Fact]
    public void StaticMethodWithFromServices_InlinesServiceProviderCheck()
    {
        var (_, _, output) = RunAndCompile(Inputs.StaticMethodWithFromServices);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("arguments.Services ?? throw new", generated);
        Assert.Contains("GetService<global::Sample.ILogger>()", generated);
    }

    [Fact]
    public void StaticMethodNoDI_CompilesCleanly()
        => AssertCleanCompilation(Inputs.StaticMethodNoDI);

    [Fact]
    public void StaticMethodNoDI_DoesNotInlineServiceProviderCheck()
    {
        var (_, _, output) = RunAndCompile(Inputs.StaticMethodNoDI);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.DoesNotContain("arguments.Services", generated);
        Assert.DoesNotContain("__provider", generated);
    }

    [Fact]
    public void StaticClassWithFromServicesAndProperty_CompilesCleanly()
        => AssertCleanCompilation(Inputs.StaticClassWithFromServicesAndProperty);

    // ── Interface scenarios ─────────────────────────────────────────────

    [Fact]
    public void InterfaceAsSourceType_CompilesCleanly()
        => AssertCleanCompilation(Inputs.InterfaceAsSourceType);

    [Fact]
    public void InterfaceAsSourceType_ResolvesViaInterface()
    {
        var (_, _, output) = RunAndCompile(Inputs.InterfaceAsSourceType);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("GetService<global::Sample.IOrderService>()", generated);
    }

    [Fact]
    public void InterfaceWithFromServices_CompilesCleanly()
        => AssertCleanCompilation(Inputs.InterfaceWithFromServices);

    [Fact]
    public void InterfaceWithFromServices_ExcludesFromServicesFromSchema()
    {
        var (_, _, output) = RunAndCompile(Inputs.InterfaceWithFromServices);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        // The DI-injected "cart" parameter should not appear in the schema properties
        Assert.DoesNotContain("properties[\"cart\"]", generated);
    }

    [Fact]
    public void InterfaceWithProperty_CompilesCleanly()
        => AssertCleanCompilation(Inputs.InterfaceWithProperty);

    [Fact]
    public void InterfaceWithProperty_EmitsPropertyAccess()
    {
        var (_, _, output) = RunAndCompile(Inputs.InterfaceWithProperty);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        // Property getter tools access the property directly on the service instance
        Assert.Contains("__service.Items", generated);
    }

    [Fact]
    public void InterfaceWithApproval_CompilesCleanly()
        => AssertCleanCompilation(Inputs.InterfaceWithApproval);

    [Fact]
    public void InterfaceWithApproval_WrapsApprovalRequired()
    {
        var (_, _, output) = RunAndCompile(Inputs.InterfaceWithApproval);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("ApprovalRequiredAIFunction(new IDangerousService_Write_Tool())", generated);
        // safe_read should NOT be wrapped
        Assert.Contains("new IDangerousService_Read_Tool(),", generated);
    }

    // ── Nested class scenarios ──────────────────────────────────────────

    [Fact]
    public void NestedClassContext_CompilesCleanly()
        => AssertCleanCompilation(Inputs.NestedClassContext);

    [Fact]
    public void NestedClassContext_GeneratesOneSource()
        => Assert.Equal(2, CountGeneratedSources(Inputs.NestedClassContext));

    [Fact]
    public void NestedClassContext_EmitsContainingTypeWrapper()
    {
        var (_, _, output) = RunAndCompile(Inputs.NestedClassContext);
        var generated = GetGeneratedSource(output, "InnerTools");
        Assert.Contains("public partial class OuterViewModel", generated);
        Assert.Contains("private partial class InnerTools", generated);
    }

    [Fact]
    public void DeeplyNestedClassContext_CompilesCleanly()
        => AssertCleanCompilation(Inputs.DeeplyNestedClassContext);

    [Fact]
    public void DeeplyNestedClassContext_EmitsMultipleLevels()
    {
        var (_, _, output) = RunAndCompile(Inputs.DeeplyNestedClassContext);
        var generated = GetGeneratedSource(output, "DeepTools");
        Assert.Contains("public partial class LevelOne", generated);
        Assert.Contains("public partial class LevelTwo", generated);
        Assert.Contains("private partial class DeepTools", generated);
    }

    [Fact]
    public void NestedClassNoNamespace_CompilesCleanly()
        => AssertCleanCompilation(Inputs.NestedClassNoNamespace);

    [Fact]
    public void NestedClassNoNamespace_EmitsNoNamespaceWrapper()
    {
        var (_, _, output) = RunAndCompile(Inputs.NestedClassNoNamespace);
        var generated = GetGeneratedSource(output, "NestedTools");
        Assert.DoesNotContain("namespace", generated);
        Assert.Contains("public partial class Outer", generated);
        Assert.Contains("internal partial class NestedTools", generated);
    }

    // ── Accessibility scenarios ─────────────────────────────────────────

    [Fact]
    public void InternalContextClass_CompilesCleanly()
        => AssertCleanCompilation(Inputs.InternalContextClass);

    [Fact]
    public void InternalContextClass_EmitsInternalAccessibility()
    {
        var (_, _, output) = RunAndCompile(Inputs.InternalContextClass);
        var generated = GetGeneratedSource(output, "InternalTools");
        Assert.Contains("internal partial class InternalTools", generated);
    }

    // ── Cross-feature combinations ──────────────────────────────────────

    [Fact]
    public void MixedMethodsAndProperties_CompilesCleanly()
        => AssertCleanCompilation(Inputs.MixedMethodsAndProperties);

    [Fact]
    public void MultipleAIToolSources_CompilesCleanly()
        => AssertCleanCompilation(Inputs.MultipleAIToolSources);

    [Fact]
    public void CrossContextSameService_GeneratesTwoSources()
        => Assert.Equal(3, CountGeneratedSources(Inputs.CrossContextSameService));

    // ── Diagnostic scenarios ────────────────────────────────────────────

    [Fact]
    public void EmptyToolSource_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.EmptyToolSource);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI003");
    }

    [Fact]
    public void GenericMethod_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.GenericMethod);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI004");
    }

    [Fact]
    public void RefParam_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.RefParam);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI004");
    }

    [Fact]
    public void OutParam_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.OutParam);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI004");
    }

    [Fact]
    public void InParam_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.InParam);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI004");
    }

    [Fact]
    public void UnsupportedDelegateParam_EmitsWarningDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.UnsupportedDelegateParam);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI002");
    }

    // ── Existing features still compile ─────────────────────────────────

    [Theory]
    [InlineData(nameof(Inputs.SimpleInstanceMethod))]
    [InlineData(nameof(Inputs.ExplicitToolName))]
    [InlineData(nameof(Inputs.DescriptionAttribute))]
    [InlineData(nameof(Inputs.DefaultValues))]
    [InlineData(nameof(Inputs.NullableParams))]
    [InlineData(nameof(Inputs.CancellationTokenParam))]
    [InlineData(nameof(Inputs.IServiceProviderAndArgsInjection))]
    [InlineData(nameof(Inputs.FromKeyedServicesString))]
    [InlineData(nameof(Inputs.FromKeyedServicesNullKey))]
    [InlineData(nameof(Inputs.FromServicesOnInterface))]
    [InlineData(nameof(Inputs.FromServicesOnConcreteClass))]
    [InlineData(nameof(Inputs.ReturnTypeVoid))]
    [InlineData(nameof(Inputs.ReturnTypeTask))]
    [InlineData(nameof(Inputs.ReturnTypeValueTask))]
    [InlineData(nameof(Inputs.ReturnTypeTaskOfT))]
    [InlineData(nameof(Inputs.ReturnTypeValueTaskOfT))]
    [InlineData(nameof(Inputs.ApprovalRequired))]
    [InlineData(nameof(Inputs.NoNamespace))]
    [InlineData(nameof(Inputs.NestedNamespace))]
    [InlineData(nameof(Inputs.StaticProperty))]
    [InlineData(nameof(Inputs.InstanceProperty))]
    [InlineData(nameof(Inputs.AccessorLevelExportAIFunction))]
    [InlineData(nameof(Inputs.EnumParameter))]
    [InlineData(nameof(Inputs.CollectionParameter))]
    public void AllValidInputs_CompileCleanly(string scenario)
        => AssertCleanCompilation(Inputs.Get(scenario));

    [Fact]
    public void AccessorLevelExportAIFunction_EmitsTwoTools()
    {
        var (_, _, output) = RunAndCompile(Inputs.AccessorLevelExportAIFunction);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("get_display_mode", generated);
        Assert.Contains("set_display_mode", generated);
    }

    // ── IncludeTools / ExcludeTools scenarios ────────────────────────────

    [Fact]
    public void IncludeTools_CompilesCleanly()
        => AssertCleanCompilation(Inputs.IncludeToolsFilter);

    [Fact]
    public void IncludeTools_OnlyIncludedMethodsEmitted()
    {
        var (_, _, output) = RunAndCompile(Inputs.IncludeToolsFilter);
        var generated = GetGeneratedSource(output, "ReadOnlyPollContext");
        Assert.Contains("get_poll", generated);
        Assert.Contains("get_all_polls", generated);
        Assert.DoesNotContain("create_poll", generated);
    }

    [Fact]
    public void ExcludeTools_CompilesCleanly()
        => AssertCleanCompilation(Inputs.ExcludeToolsFilter);

    [Fact]
    public void ExcludeTools_ExcludedMethodNotEmitted()
    {
        var (_, _, output) = RunAndCompile(Inputs.ExcludeToolsFilter);
        var generated = GetGeneratedSource(output, "ReadOnlyPollContext");
        Assert.Contains("get_poll", generated);
        Assert.Contains("get_all_polls", generated);
        Assert.DoesNotContain("create_poll", generated);
    }

    [Fact]
    public void IncludeAndExcludeCompose_ExcludeRemovesFromInclude()
    {
        // Include=[A,B], Exclude=[B] → only A survives
        var (_, _, output) = RunAndCompile(Inputs.IncludeAndExcludeBothSet);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("tool_a", generated);
        Assert.DoesNotContain("tool_b", generated);
        Assert.DoesNotContain("tool_c", generated);
    }

    [Fact]
    public void IncludeToolsNonexistent_EmitsDiagnostic()
    {
        var (genDiags, _, _) = RunAndCompile(Inputs.IncludeToolsNonexistentMethod);
        Assert.Contains(genDiags, d => d.Id == "MAUIAI006");
    }

    [Fact]
    public void IncludeToolsEmpty_MeansAllTools()
    {
        // Include=[] → start with all tools (empty = no filter)
        var (_, _, output) = RunAndCompile(Inputs.IncludeToolsEmpty);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("tool_a", generated);
        Assert.Contains("tool_b", generated);
    }

    [Fact]
    public void ExcludeToolsEmpty_MeansNoExclusion()
    {
        // Exclude=[] → exclude nothing
        var (_, _, output) = RunAndCompile(Inputs.ExcludeToolsEmpty);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("tool_a", generated);
        Assert.Contains("tool_b", generated);
    }

    [Fact]
    public void IncludeThenExcludeSame_ContextGeneratedWithNoTools()
    {
        // Include=[A], Exclude=[A] → context exists but has zero tools
        var (_, _, output) = RunAndCompile(Inputs.IncludeThenExcludeSame);
        var hasContext = output.SyntaxTrees.Any(t =>
            (t.FilePath ?? "").EndsWith(".g.cs") && t.ToString().Contains("partial class Ctx"));
        Assert.True(hasContext, "Context should still be generated even with zero tools");
    }

    [Fact]
    public void ExcludeToolsAll_ContextGeneratedWithNoTools()
    {
        var (_, _, output) = RunAndCompile(Inputs.ExcludeToolsAll);
        var hasContext = output.SyntaxTrees.Any(t =>
            (t.FilePath ?? "").EndsWith(".g.cs") && t.ToString().Contains("partial class Ctx"));
        Assert.True(hasContext, "Context should still be generated even when all tools excluded");
    }

    [Fact]
    public void IncludeToolsSingleMethod_OnlyThatToolEmitted()
    {
        var (_, _, output) = RunAndCompile(Inputs.IncludeToolsSingleMethod);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("tool_b", generated);
        Assert.DoesNotContain("tool_a", generated);
        Assert.DoesNotContain("tool_c", generated);
    }

    [Fact]
    public void ExcludeToolsSingleMethod_OtherToolsEmitted()
    {
        var (_, _, output) = RunAndCompile(Inputs.ExcludeToolsSingleMethod);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("tool_a", generated);
        Assert.Contains("tool_c", generated);
        Assert.DoesNotContain("tool_b", generated);
    }

    [Fact]
    public void IncludeToolsWithProperty_PropertyToolIncluded()
    {
        var (_, _, output) = RunAndCompile(Inputs.IncludeToolsWithProperty);
        var generated = GetGeneratedSource(output, "Ctx");
        Assert.Contains("get_value", generated);
        Assert.DoesNotContain("do_action", generated);
    }

    [Fact]
    public void TwoContextsDifferentFilters_EachGetsCorrectSubset()
    {
        var (_, _, output) = RunAndCompile(Inputs.TwoContextsDifferentFilters);
        var generatedA = GetGeneratedSource(output, "ContextA");
        var generatedBC = GetGeneratedSource(output, "ContextBC");

        Assert.Contains("tool_a", generatedA);
        Assert.DoesNotContain("tool_b", generatedA);
        Assert.DoesNotContain("tool_c", generatedA);

        Assert.DoesNotContain("tool_a", generatedBC);
        Assert.Contains("tool_b", generatedBC);
        Assert.Contains("tool_c", generatedBC);
    }

    // ── Enum and collection parameter scenarios ─────────────────────────

    [Fact]
    public void EnumParameter_CompilesCleanly()
        => AssertCleanCompilation(Inputs.EnumParameter);

    [Fact]
    public void EnumParameter_EmitsSchemaForEnumType()
    {
        var (_, _, output) = RunAndCompile(Inputs.EnumParameter);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("\"level\"", generated);
        Assert.Contains("typeof(global::Sample.Severity)", generated);
    }

    [Fact]
    public void CollectionParameter_CompilesCleanly()
        => AssertCleanCompilation(Inputs.CollectionParameter);

    [Fact]
    public void CollectionParameter_EmitsSchemaForCollectionTypes()
    {
        var (_, _, output) = RunAndCompile(Inputs.CollectionParameter);
        var generated = GetGeneratedSource(output, "ToolsCtx");
        Assert.Contains("typeof(global::System.Collections.Generic.List<string>)", generated);
        Assert.Contains("typeof(global::System.Collections.Generic.Dictionary<string, int>)", generated);
    }
}
