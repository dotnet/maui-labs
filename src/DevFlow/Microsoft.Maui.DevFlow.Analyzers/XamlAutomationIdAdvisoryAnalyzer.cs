using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Maui.DevFlow.Analyzers;

/// <summary>
/// Advisory-only testability diagnostics over the same XAML additional files used for DevFlow
/// source maps. These diagnostics never offer a code fix and deliberately do not recommend IDs
/// for templates, styles, resource dictionaries, or repeaters.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class XamlAutomationIdAdvisoryAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor MissingAutomationId = new(
        "DFXAML001",
        "Interactive static XAML element has no AutomationId",
        "Static interactive XAML element '{0}' has no AutomationId",
        "DevFlow.Testability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A stable AutomationId can improve testability. This advisory never applies a source change.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor DuplicateAutomationId = new(
        "DFXAML002",
        "Duplicate XAML AutomationId",
        "AutomationId '{0}' is duplicated in DevFlow-mapped XAML",
        "DevFlow.Testability",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Duplicate AutomationIds are ambiguous for automation. This advisory never changes source.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public static readonly DiagnosticDescriptor TemplateAutomationId = new(
        "DFXAML003",
        "AutomationId declared in a template, style, resource, or repeater",
        "AutomationId on '{0}' is declared in a template, style, resource, or repeater scope",
        "DevFlow.Testability",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Template and repeater identities need a separate stable item-key design; DevFlow does not offer an automatic source fix.",
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    private static readonly ImmutableHashSet<string> InteractiveElements =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "Button", "ImageButton", "Entry", "Editor", "SearchBar", "CheckBox", "RadioButton", "Switch", "Slider", "Stepper");

    private static readonly ImmutableHashSet<string> UnsafeAncestors =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "DataTemplate", "ControlTemplate", "Style", "Setter", "ResourceDictionary",
            "CollectionView", "ListView", "CarouselView", "BindableLayout", "ItemsView");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingAutomationId, DuplicateAutomationId, TemplateAutomationId);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        var declarations = new List<AutomationIdDeclaration>();
        foreach (var file in context.Options.AdditionalFiles
                     .Where(static file => file.Path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var text = file.GetText(context.CancellationToken);
            if (text is null || text.Length == 0)
                continue;

            XDocument document;
            try
            {
                document = XDocument.Parse(text.ToString(), LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                // The MAUI/XAML compiler owns malformed-XAML diagnostics. This advisory does not
                // create an additional incomplete suggestion.
                continue;
            }

            foreach (var element in document.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
            {
                var unsafeScope = IsUnsafeScope(element);
                var automation = element.Attribute("AutomationId");
                if (unsafeScope)
                {
                    if (automation is not null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            TemplateAutomationId,
                            LocationFor(text, file.Path, element),
                            element.Name.LocalName));
                    }
                    continue;
                }

                if (InteractiveElements.Contains(element.Name.LocalName) &&
                    (automation is null || string.IsNullOrWhiteSpace(automation.Value)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingAutomationId,
                        LocationFor(text, file.Path, element),
                        element.Name.LocalName));
                    continue;
                }

                if (automation is not null && !string.IsNullOrWhiteSpace(automation.Value))
                {
                    declarations.Add(new AutomationIdDeclaration(
                        automation.Value,
                        text,
                        file.Path,
                        element));
                }
            }
        }

        foreach (var group in declarations
                     .GroupBy(static declaration => declaration.Value, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            foreach (var declaration in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateAutomationId,
                    LocationFor(declaration.Text, declaration.Path, declaration.Element),
                    group.Key));
            }
        }
    }

    private static bool IsUnsafeScope(XElement element)
        => element.AncestorsAndSelf().Any(ancestor =>
            UnsafeAncestors.Contains(ancestor.Name.LocalName) ||
            ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.OrdinalIgnoreCase) ||
            ancestor.Name.LocalName.EndsWith(".ItemTemplate", StringComparison.OrdinalIgnoreCase) ||
            ancestor.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "ItemsSource", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attribute.Name.LocalName, "ItemTemplate", StringComparison.OrdinalIgnoreCase)));

    private static Location LocationFor(SourceText text, string path, XElement element)
    {
        if (!(element is IXmlLineInfo info) || !info.HasLineInfo() ||
            info.LineNumber < 1 || info.LineNumber > text.Lines.Count)
        {
            return Location.None;
        }

        var line = text.Lines[info.LineNumber - 1];
        if (line.Span.Length == 0)
            return Location.None;
        var offset = Math.Max(0, Math.Min(line.Span.Length - 1, info.LinePosition - 1));
        var start = line.Start + offset;
        var length = Math.Min(Math.Max(1, element.Name.LocalName.Length), Math.Max(1, text.Length - start));
        var span = new TextSpan(start, length);
        return Location.Create(path, span, text.Lines.GetLinePositionSpan(span));
    }

    private sealed class AutomationIdDeclaration
    {
        public AutomationIdDeclaration(string value, SourceText text, string path, XElement element)
        {
            Value = value;
            Text = text;
            Path = path;
            Element = element;
        }

        public string Value { get; }
        public SourceText Text { get; }
        public string Path { get; }
        public XElement Element { get; }
    }
}
