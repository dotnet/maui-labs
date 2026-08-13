using Microsoft.CodeAnalysis;

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
        Assert.Contains("arguments.TryGetValue(\"city\"", generated);
        Assert.Contains("state.Weather = ConvertValue", generated);
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
}
