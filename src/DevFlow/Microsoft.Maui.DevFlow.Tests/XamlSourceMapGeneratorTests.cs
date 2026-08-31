using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Maui.DevFlow.Agent.Core.SourceMapping;
using Microsoft.Maui.DevFlow.Analyzers;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies the <see cref="XamlSourceMapGenerator"/> emits a correct per-assembly provider +
/// module initializer, and that the generated code — when compiled, loaded, and its module
/// initializer run — registers a map with <see cref="XamlSourceMapRegistry"/> that matches the
/// runtime parser. This is the end-to-end proof that click-to-XAML maps are real (not null).
/// </summary>
[Collection("XamlSourceMapRegistry")]
public class XamlSourceMapGeneratorTests
{
    private const string SampleXaml =
        "<ContentPage xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"\n" +
        "             xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\"\n" +
        "             x:Class=\"TestApp.MainPage\">\n" +
        "    <VerticalStackLayout>\n" +
        "        <Label Text=\"Hello\" />\n" +
        "        <Button Text=\"Click\" />\n" +
        "    </VerticalStackLayout>\n" +
        "</ContentPage>\n";

    private const string ResourceDictionaryXaml =
        "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/dotnet/2021/maui\"\n" +
        "                    xmlns:x=\"http://schemas.microsoft.com/winfx/2009/xaml\">\n" +
        "    <Color x:Key=\"Primary\">#512BD4</Color>\n" +
        "</ResourceDictionary>\n";

    [Fact]
    public void Generator_EmitsProvider_ForXamlWithClass()
    {
        var (output, diagnostics, generated) = Run(("MainPage.xaml", SampleXaml, true));

        Assert.Empty(diagnostics);
        Assert.Contains("__DevFlowXamlSourceMapProvider", generated);
        Assert.Contains("IXamlSourceMapProvider", generated);
        Assert.Contains("ModuleInitializer", generated);
        Assert.Contains("XamlSourceMapRegistry.Register", generated);
        Assert.Contains("TestApp.MainPage", generated);

        // The generated source must compile cleanly against Agent.Core.
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Empty(errors);
    }

    [Fact]
    public void Generator_Skips_WhenDevFlowXamlMetadataMissing()
    {
        // Same file, but not marked with DevFlowXaml=true → not consumed.
        var (_, _, generated) = Run(("MainPage.xaml", SampleXaml, false));
        Assert.Null(generated);
    }

    [Fact]
    public void Generator_Skips_XamlWithoutClass()
    {
        // A ResourceDictionary has no x:Class → nothing to map to a runtime type.
        var (_, _, generated) = Run(("Colors.xaml", ResourceDictionaryXaml, true));
        Assert.Null(generated);
    }

    [Fact]
    public void GeneratedProvider_EndToEnd_RegistersMapMatchingParser()
    {
        const string path = @"C:\proj\TestApp\MainPage.xaml";
        var (output, diagnostics, generated) = Run(("MainPage.xaml", SampleXaml, true, path));
        Assert.Empty(diagnostics);
        Assert.NotNull(generated);

        XamlSourceMapRegistry.Instance.Reset();
        try
        {
            // Before loading the generated assembly, the type is unmapped.
            Assert.Null(XamlSourceMapRegistry.Instance.GetMap("TestApp.MainPage"));

            LoadAndRunModuleInitializer(output);

            var map = XamlSourceMapRegistry.Instance.GetMap("TestApp.MainPage");
            Assert.NotNull(map);
            Assert.Equal(path, map!.File);

            // The generated map must equal what the runtime parser produces from the same text.
            var expected = XamlSourceMap.Parse(SampleXaml, path);
            Assert.NotNull(expected);

            AssertEntry(map, "", expected!, "ContentPage");
            AssertEntry(map, "0", expected!, "VerticalStackLayout");
            AssertEntry(map, "0/0", expected!, "Label");
            AssertEntry(map, "0/1", expected!, "Button");

            // An unrelated type stays unmapped (and is not negatively cached forever).
            Assert.Null(XamlSourceMapRegistry.Instance.GetMap("TestApp.OtherPage"));
        }
        finally
        {
            XamlSourceMapRegistry.Instance.Reset();
        }
    }

    private static void AssertEntry(XamlSourceMap actual, string childPath, XamlSourceMap expected, string expectedType)
    {
        Assert.True(actual.TryGet(childPath, out var a), $"generated map missing path '{childPath}'");
        Assert.True(expected.TryGet(childPath, out var e), $"parser map missing path '{childPath}'");
        Assert.Equal(expectedType, a.TypeName);
        Assert.Equal(e.Line, a.Line);
        Assert.Equal(e.Column, a.Column);
        Assert.Equal(e.TypeName, a.TypeName);
        Assert.Equal(e.ChildCount, a.ChildCount);
    }

    // ---- harness ----

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string? Generated) Run(
        (string Name, string Xaml, bool Marked) file)
        => Run((file.Name, file.Xaml, file.Marked, @"C:\proj\" + file.Name));

    private static (Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string? Generated) Run(
        (string Name, string Xaml, bool Marked, string Path) file)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            assemblyName: "GenTest_" + System.Guid.NewGuid().ToString("N"),
            syntaxTrees: System.Array.Empty<SyntaxTree>(),
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var additionalText = new TestAdditionalText(file.Path, file.Xaml);
        var optionsProvider = new TestOptionsProvider(additionalText, file.Marked);

        var driver = CSharpGeneratorDriver.Create(
            generators: new[] { new XamlSourceMapGenerator().AsSourceGenerator() },
            additionalTexts: ImmutableArray.Create<AdditionalText>(additionalText),
            parseOptions: parseOptions,
            optionsProvider: optionsProvider);

        var result = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var runResult = result.GetRunResult();
        var generated = runResult.GeneratedTrees.Length == 0
            ? null
            : runResult.GeneratedTrees[0].ToString();

        return (output, diagnostics, generated);
    }

    private static void LoadAndRunModuleInitializer(Compilation output)
    {
        using var ms = new System.IO.MemoryStream();
        var emit = output.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        ms.Position = 0;
        var assembly = Assembly.Load(ms.ToArray());
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
    }

    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(System.IO.Path.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries);
        var refs = trusted.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();

        void AddIfMissing(System.Type t)
        {
            var loc = t.Assembly.Location;
            if (!string.IsNullOrEmpty(loc) && !refs.Any(r => r is PortableExecutableReference pe &&
                string.Equals(pe.FilePath, loc, System.StringComparison.OrdinalIgnoreCase)))
                refs.Add(MetadataReference.CreateFromFile(loc));
        }

        AddIfMissing(typeof(object));
        AddIfMissing(typeof(XamlSourceMap)); // Agent.Core — so generated provider compiles.
        return refs.ToImmutableArray();
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _text;
        public TestAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }
        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }

    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AdditionalText _file;
        private readonly bool _marked;
        public TestOptionsProvider(AdditionalText file, bool marked) { _file = file; _marked = marked; }
        public override AnalyzerConfigOptions GlobalOptions => EmptyOptions.Instance;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => ReferenceEquals(textFile, _file) && _marked ? MarkedOptions.Instance : EmptyOptions.Instance;
    }

    private sealed class MarkedOptions : AnalyzerConfigOptions
    {
        public static readonly MarkedOptions Instance = new();
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_metadata.AdditionalFiles.DevFlowXaml") { value = "true"; return true; }
            value = null!;
            return false;
        }
    }

    private sealed class EmptyOptions : AnalyzerConfigOptions
    {
        public static readonly EmptyOptions Instance = new();
        public override bool TryGetValue(string key, out string value) { value = null!; return false; }
    }
}
