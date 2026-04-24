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

        var indent = GetLineIndentation(text, returnStatement.SpanStart);
        var missingCalls = new List<string>();
        if (!hasAgentRegistration)
            missingCalls.Add($"{builderName}.AddMauiDevFlowAgent();");
        if (includeBlazor && !hasBlazorRegistration)
            missingCalls.Add($"{builderName}.AddMauiBlazorDevFlowTools();");

        var insertion = BuildRegistrationBlock(indent, missingCalls);
        var updated = text.Insert(returnStatement.SpanStart, insertion);
        var updatedRoot = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        updated = EnsureUsing(updated, updatedRoot, agentNamespace);
        if (includeBlazor)
        {
            updatedRoot = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
            updated = EnsureUsing(updated, updatedRoot, blazorNamespace);
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

    static string EnsureUsing(string text, CompilationUnitSyntax root, string namespaceName)
    {
        if (root.Usings.Any(usingDirective => usingDirective.Name?.ToString() == namespaceName))
            return text;

        if (root.Usings.Count > 0)
        {
            var position = root.Usings.Last().FullSpan.End;
            return text.Insert(position, $"{Environment.NewLine}using {namespaceName};");
        }

        return $"using {namespaceName};{Environment.NewLine}{text}";
    }

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

    static string GetLineIndentation(string text, int position)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;

        var end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            end++;

        return text[lineStart..end];
    }

    static string BuildRegistrationBlock(string indent, IReadOnlyList<string> calls)
    {
        var block = new List<string>
        {
            $"{indent}#if DEBUG"
        };
        block.AddRange(calls.Select(call => $"{indent}{call}"));
        block.Add($"{indent}#endif");
        block.Add(string.Empty);
        return string.Join(Environment.NewLine, block);
    }
}
