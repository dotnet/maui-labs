using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.AI.Chat.Generators.Tests;

public class ToolBlockGeneratorTests
{
    [Fact]
    public void ValidBlock_GeneratesCompilableHandlerAndRegistration()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;

            namespace TestApp;

            [ToolBlock("get_weather")]
            public sealed partial class WeatherBlock : FunctionInvocationContentBlock
            {
                [ToolParameter(Name = "city")]
                public string City { get; set; } = "";

                [ToolParameter]
                public int Days { get; set; }

                [ToolResult]
                public Weather? Weather { get; set; }
            }

            public sealed class Weather
            {
                public int Temperature { get; set; }
            }

            public static class Setup
            {
                public static void Configure(UIAgentOptions options) =>
                    options.AddGeneratedToolBlocks();
            }
            """;

        var driver = GeneratorTestHarness.Run(
            source,
            out var compilation,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        var generated = string.Join(
            "\n",
            driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
        Assert.Contains("WeatherBlockGeneratedHandler", generated);
        Assert.Contains("state.Call = call", generated);
        Assert.DoesNotContain("state.Id = call.CallId", generated);
        Assert.DoesNotContain("InformationalOnly", generated);
        Assert.Contains("arguments.TryGetValue(\"city\"", generated);
        Assert.Contains("state.Weather = ConvertValue", generated);
        Assert.Contains("result.CallId == call.CallId", generated);
        Assert.Contains("AddGeneratedToolBlocks", generated);
    }

    [Fact]
    public void SingleNamedResult_GeneratesNamedPropertyExtraction()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;

            [ToolBlock("get_title")]
            public sealed partial class TitleBlock : FunctionInvocationContentBlock
            {
                [ToolResult(Name = "title")]
                public string Title { get; set; } = "";
            }
            """;

        var driver = GeneratorTestHarness.Run(
            source,
            out var compilation,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        var generated = string.Join(
            "\n",
            driver.GetRunResult().GeneratedTrees.Select(
                tree => tree.GetText().ToString()));
        Assert.Contains(
            "resultObject.TryGetProperty(\"title\"",
            generated);
    }

    [Theory]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("x")]
        public class Invalid : FunctionInvocationContentBlock { }
        """,
        "MAUIAI101")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("x")]
        public partial class Invalid { }
        """,
        "MAUIAI102")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("x")]
        public abstract partial class Invalid : FunctionInvocationContentBlock { }
        """,
        "MAUIAI103")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("x")]
        public partial class Invalid<T> : FunctionInvocationContentBlock { }
        """,
        "MAUIAI104")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("")]
        public partial class Invalid : FunctionInvocationContentBlock { }
        """,
        "MAUIAI105")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        public class Outer {
            [ToolBlock("x")]
            public partial class Invalid : FunctionInvocationContentBlock { }
        }
        """,
        "MAUIAI109")]
    [InlineData(
        """
        using Microsoft.Maui.AI.Chat;
        [ToolBlock("x")]
        public partial class Invalid : FunctionInvocationContentBlock {
            public Invalid(string value) { }
        }
        """,
        "MAUIAI110")]
    public void InvalidDeclaration_ReportsExpectedDiagnostic(
        string source,
        string expectedId)
    {
        var driver = GeneratorTestHarness.Run(
            source,
            out _,
            out _);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            diagnostic => diagnostic.Id == expectedId);
    }

    [Fact]
    public void DuplicatePropertyKeys_ReportDiagnostics()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("x")]
            public partial class Invalid : FunctionInvocationContentBlock {
                [ToolParameter(Name = "same")] public string First { get; set; } = "";
                [ToolParameter(Name = "same")] public string Second { get; set; } = "";
                [ToolResult(Name = "result")] public string A { get; set; } = "";
                [ToolResult(Name = "result")] public string B { get; set; } = "";
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out _, out _);
        var diagnostics = driver.GetRunResult().Diagnostics;

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MAUIAI106");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MAUIAI111");
    }

    [Fact]
    public void PrivateSetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("x")]
            public partial class Invalid : FunctionInvocationContentBlock {
                [ToolParameter] public string Value { get; private set; } = "";
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out _, out _);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            diagnostic => diagnostic.Id == "MAUIAI107");
    }

    [Fact]
    public void InitOnlySetter_ReportsDiagnostic()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("x")]
            public partial class Invalid : FunctionInvocationContentBlock {
                [ToolParameter] public string Value { get; init; } = "";
                [ToolResult] public string Result { get; init; } = "";
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out _);

        Assert.Equal(
            2,
            driver.GetRunResult().Diagnostics.Count(diagnostic => diagnostic.Id == "MAUIAI107"));
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
    }

    [Fact]
    public void PrimitiveProperties_UseConversionsThatAvoidReflection()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("configure")]
            public partial class ConfigureBlock : FunctionInvocationContentBlock {
                [ToolParameter] public int Count { get; set; }
                [ToolParameter] public long Total { get; set; }
                [ToolParameter] public double Ratio { get; set; }
                [ToolParameter] public float Scale { get; set; }
                [ToolParameter] public decimal? Price { get; set; }
                [ToolParameter] public bool Enabled { get; set; }
                [ToolParameter] public string Label { get; set; } = "";
                [ToolResult] public int Answer { get; set; }
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        var generated = GetGenerated(driver);
        Assert.Contains("element.GetInt32()", generated);
        Assert.Contains("element.GetInt64()", generated);
        Assert.Contains("element.GetDouble()", generated);
        Assert.Contains("element.GetSingle()", generated);
        Assert.Contains("element.GetDecimal()", generated);
        Assert.Contains("element.GetBoolean()", generated);
        Assert.Contains("element.GetString()", generated);
        Assert.Contains("CultureInfo.InvariantCulture", generated);
        Assert.DoesNotContain("JsonSerializer", generated);
        Assert.DoesNotContain("ConvertValue", generated);
    }

    [Fact]
    public void MultipleResultProperties_ReadJsonObjectsAndJsonStringsWithoutReflection()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("get_weather")]
            public partial class WeatherBlock : FunctionInvocationContentBlock {
                [ToolResult] public int Temperature { get; set; }
                [ToolResult] public string? Conditions { get; set; }
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        var generated = GetGenerated(driver);
        Assert.Contains("JsonDocument.Parse(json)", generated);
        Assert.DoesNotContain("JsonSerializer", generated);
        Assert.DoesNotContain("JsonOptions", generated);
        Assert.DoesNotContain("ConvertValue", generated);
    }

    [Theory]
    [InlineData("""
        [ToolResult] public int Temperature { get; set; }
        [ToolResult] public string? Conditions { get; set; }
        """)]
    [InlineData("""
        [ToolResult(Name = "title")] public string Title { get; set; } = "";
        """)]
    [InlineData("""
        [ToolParameter] public bool Metric { get; set; }
        [ToolResult(Name = "high")] public double High { get; set; }
        [ToolResult(Name = "low")] public decimal? Low { get; set; }
        """)]
    public void PrimitiveResultObjects_CallNothingThatTrimmingOrNativeAotCanBreak(string members)
    {
        var source = $$"""
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("get_weather")]
            public partial class WeatherBlock : FunctionInvocationContentBlock {
                {{members}}
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        Assert.Contains("GetResultObject", GetGenerated(driver));
        Assert.Empty(GetCallsThatRequireUnreferencedOrDynamicCode(driver, compilation));
        var generatedPaths = driver.GetRunResult().GeneratedTrees
            .Select(tree => tree.FilePath)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity >= DiagnosticSeverity.Warning
            && diagnostic.Location.SourceTree is { } tree
            && generatedPaths.Contains(tree.FilePath)));
    }

    [Fact]
    public void ComplexResultProperty_ReadsClrResultObjectsWithReflection()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("get_weather")]
            public partial class WeatherBlock : FunctionInvocationContentBlock {
                [ToolResult] public int Temperature { get; set; }
                [ToolResult] public Forecast? Forecast { get; set; }
            }
            public sealed class Forecast { public string? Summary { get; set; } }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        Assert.Contains("JsonSerializer.SerializeToElement(value, JsonOptions)", GetGenerated(driver));
        Assert.Contains(
            GetCallsThatRequireUnreferencedOrDynamicCode(driver, compilation),
            call => call.Contains("SerializeToElement", StringComparison.Ordinal));
    }

    [Fact]
    public void BlocksWithSameNameInDifferentNamespaces_GetUniqueCompilableHandlers()
    {
        const string source = """
            namespace First
            {
                [Microsoft.Maui.AI.Chat.ToolBlock("first")]
                public partial class StatusBlock : Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock { }
            }

            namespace Second
            {
                [Microsoft.Maui.AI.Chat.ToolBlock("second")]
                public partial class StatusBlock : Microsoft.Maui.AI.Chat.FunctionInvocationContentBlock { }
            }
            """;

        var driver = GeneratorTestHarness.Run(source, out var compilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
        var handlerHintNames = driver.GetRunResult().GeneratedTrees
            .Select(tree => Path.GetFileName(tree.FilePath))
            .Where(name => name.EndsWith("StatusBlockGeneratedHandler.g.cs", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, handlerHintNames.Count);
        Assert.Equal(2, handlerHintNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SourceWithoutToolBlocks_DoesNotGenerateRegistration()
    {
        var driver = GeneratorTestHarness.Run(
            "public sealed class OtherType { }",
            out var compilation,
            out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(driver.GetRunResult().GeneratedTrees);
        Assert.Empty(compilation.GetDiagnostics().Where(IsError));
    }

    [Fact]
    public void DuplicateToolNames_ReportDiagnostic()
    {
        const string source = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("same")]
            public partial class First : FunctionInvocationContentBlock { }
            [ToolBlock("same")]
            public partial class Second : FunctionInvocationContentBlock { }
            """;

        var driver = GeneratorTestHarness.Run(source, out _, out _);

        Assert.Contains(
            driver.GetRunResult().Diagnostics,
            diagnostic => diagnostic.Id == "MAUIAI108");
    }

    [Fact]
    public void UnrelatedEdit_ReusesStructurallyEqualPipelineOutputs()
    {
        const string original = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("weather")]
            public partial class WeatherBlock : FunctionInvocationContentBlock {
                [ToolParameter] public string City { get; set; } = "";
            }
            public class Unrelated { public int Value => 1; }
            """;
        const string edited = """
            using Microsoft.Maui.AI.Chat;
            [ToolBlock("weather")]
            public partial class WeatherBlock : FunctionInvocationContentBlock {
                [ToolParameter] public string City { get; set; } = "";
            }
            public class Unrelated { public int Value => 2; }
            """;

        var driver = GeneratorTestHarness.CreateDriver(trackSteps: true);
        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation(original));
        driver = driver.RunGenerators(GeneratorTestHarness.CreateCompilation(edited));

        var reasons = driver.GetRunResult().Results.Single()
            .TrackedOutputSteps
            .SelectMany(pair => pair.Value)
            .SelectMany(step => step.Outputs)
            .Select(output => output.Reason)
            .ToArray();

        Assert.NotEmpty(reasons);
        Assert.DoesNotContain(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains(
            reasons,
            reason => reason is IncrementalStepRunReason.Cached
                or IncrementalStepRunReason.Unchanged);
    }

    private static bool IsError(Diagnostic diagnostic) =>
        diagnostic.Severity == DiagnosticSeverity.Error;

    /// <summary>
    /// Gets the generated calls to members that trimming or NativeAOT can break, which the trim and AOT
    /// analyzers report as IL2026 and IL3050 in the consuming app.
    /// </summary>
    private static List<string> GetCallsThatRequireUnreferencedOrDynamicCode(
        GeneratorDriver driver,
        Compilation compilation)
    {
        var generatedPaths = driver.GetRunResult().GeneratedTrees
            .Select(tree => tree.FilePath)
            .ToHashSet(StringComparer.Ordinal);
        var calls = new List<string>();
        foreach (var tree in compilation.SyntaxTrees.Where(tree => generatedPaths.Contains(tree.FilePath)))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is not (InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax))
                    continue;

                if (model.GetSymbolInfo(node).Symbol is IMethodSymbol method
                    && method.GetAttributes().Any(attribute => attribute.AttributeClass?.Name
                        is "RequiresUnreferencedCodeAttribute" or "RequiresDynamicCodeAttribute"))
                {
                    calls.Add(method.ToDisplayString());
                }
            }
        }

        return calls;
    }

    private static string GetGenerated(GeneratorDriver driver) =>
        string.Join(
            "\n",
            driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
}
