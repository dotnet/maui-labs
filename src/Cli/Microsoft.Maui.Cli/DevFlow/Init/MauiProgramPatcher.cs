using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.Cli.DevFlow.Init;

internal static class MauiProgramPatcher
{
    public static DevFlowInitOperationResult EnsureRegistration(string filePath, bool includeBlazor, bool isGtk, bool dryRun)
    {
        if (!File.Exists(filePath))
        {
            return new DevFlowInitOperationResult
            {
                Name = "Patch MauiProgram.cs",
                Status = DevFlowInitStatus.ManualRequired,
                Detail = "Could not find MauiProgram.cs.",
                ManualSteps = [$"Add builder.AddMauiDevFlowAgent() manually in {Path.GetFileName(filePath)}."]
            };
        }

        var text = File.ReadAllText(filePath);
        var hasAgentRegistration = text.Contains("AddMauiDevFlowAgent", StringComparison.Ordinal);
        var hasBlazorRegistration = text.Contains("AddMauiBlazorDevFlowTools", StringComparison.Ordinal);
        if (hasAgentRegistration && (!includeBlazor || hasBlazorRegistration))
        {
            return new DevFlowInitOperationResult
            {
                Name = "Patch MauiProgram.cs",
                Status = DevFlowInitStatus.AlreadyPresent,
                Detail = "MauiProgram.cs already contains the required DevFlow registration."
            };
        }

        var agentNamespace = isGtk ? "Microsoft.Maui.DevFlow.Agent.Gtk" : "Microsoft.Maui.DevFlow.Agent";
        var blazorNamespace = isGtk ? "Microsoft.Maui.DevFlow.Blazor.Gtk" : "Microsoft.Maui.DevFlow.Blazor";

        var tree = CSharpSyntaxTree.ParseText(text);
        var root = tree.GetCompilationUnitRoot();
        var builderName = FindBuilderVariableName(root);
        var returnStatement = FindBuilderReturnStatement(root, builderName);
        if (builderName == null || returnStatement == null)
        {
            return new DevFlowInitOperationResult
            {
                Name = "Patch MauiProgram.cs",
                Status = DevFlowInitStatus.ManualRequired,
                Detail = "Could not confidently locate the MAUI app builder or return statement.",
                ManualSteps =
                [
                    $"Add `using {agentNamespace};`.",
                    "Add `builder.AddMauiDevFlowAgent();` inside `#if DEBUG` before `return builder.Build();`."
                ]
            };
        }

        var newline = DetectNewline(text);
        var (lineStart, indent) = GetLineStartAndIndentation(text, returnStatement.SpanStart);
        var missingCalls = new List<string>();
        if (!hasAgentRegistration)
            missingCalls.Add($"{builderName}.AddMauiDevFlowAgent();");
        if (includeBlazor && !hasBlazorRegistration)
            missingCalls.Add($"{builderName}.AddMauiBlazorDevFlowTools();");

        var insertion = BuildRegistrationBlock(indent, newline, missingCalls);
        var updated = text.Insert(lineStart, insertion);
        var updatedRoot = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        updated = EnsureUsing(updated, updatedRoot, agentNamespace, newline);
        if (includeBlazor)
        {
            updatedRoot = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
            updated = EnsureUsing(updated, updatedRoot, blazorNamespace, newline);
        }

        if (!dryRun)
            File.WriteAllText(filePath, updated);

        return new DevFlowInitOperationResult
        {
            Name = "Patch MauiProgram.cs",
            Status = DevFlowInitStatus.Success,
            Detail = "Added DevFlow registration to MauiProgram.cs.",
            FilesChanged = [filePath]
        };
    }

    static string EnsureUsing(string text, CompilationUnitSyntax root, string namespaceName, string newline)
    {
        if (root.Usings.Any(usingDirective => usingDirective.Name?.ToString() == namespaceName))
            return text;

        if (root.Usings.Count > 0)
        {
            // Insert after the semicolon of the last using (Span.End, not FullSpan.End)
            // so that any trailing trivia (blank lines before namespace) is preserved.
            var position = root.Usings.Last().Span.End;
            return text.Insert(position, $"{newline}using {namespaceName};");
        }

        return $"using {namespaceName};{newline}{newline}{text}";
    }

    /// <summary>
    /// Detects the dominant line ending in <paramref name="text"/>.
    /// </summary>
    static string DetectNewline(string text)
        => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    static string? FindBuilderVariableName(CompilationUnitSyntax root)
    {
        foreach (var declaration in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "CreateBuilder" &&
                memberAccess.Expression.ToString() == "MauiApp")
            {
                return declaration.Identifier.Text;
            }
        }

        return null;
    }

    static ReturnStatementSyntax? FindBuilderReturnStatement(CompilationUnitSyntax root, string? builderName)
    {
        if (builderName == null)
            return null;

        return root.DescendantNodes().OfType<ReturnStatementSyntax>()
            .FirstOrDefault(returnStatement =>
                returnStatement.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "Build" &&
                memberAccess.Expression.ToString() == builderName);
    }

    /// <summary>
    /// Returns the position of the first character on the line containing <paramref name="position"/>
    /// and the whitespace indent string.
    /// </summary>
    static (int lineStart, string indent) GetLineStartAndIndentation(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        // Skip past a \r that might follow the \n (rare but possible in mixed files)
        if (lineStart < text.Length && text[lineStart] == '\r')
            lineStart++;

        var end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            end++;

        return (lineStart, text[lineStart..end]);
    }

    static string BuildRegistrationBlock(string indent, string newline, IReadOnlyList<string> calls)
    {
        var block = new List<string>
        {
            "#if DEBUG"
        };
        block.AddRange(calls.Select(call => $"{indent}{call}"));
        block.Add("#endif");
        block.Add(string.Empty);
        return string.Join(newline, block);
    }
}
