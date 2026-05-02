using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace Comet.Migrate
{
	internal static class Program
	{
		public static async Task<int> Main(string[] args)
		{
			if (!TryParseArgs(args, out var projectPath, out var dryRun))
			{
				PrintUsage();
				return 1;
			}

			if (!File.Exists(projectPath))
			{
				Console.Error.WriteLine($"Project file not found: {projectPath}");
				return 1;
			}

			if (!MSBuildLocator.IsRegistered)
			{
				MSBuildLocator.RegisterDefaults();
			}

			using var workspace = MSBuildWorkspace.Create();
			workspace.WorkspaceFailed += (_, e) => Console.Error.WriteLine(e.Diagnostic.Message);

			var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: CancellationToken.None);
			var solution = project.Solution;
			var reports = new List<FileReport>();

			foreach (var document in project.Documents.Where(doc => doc.FilePath != null && doc.SourceCodeKind == SourceCodeKind.Regular))
			{
				var root = await document.GetSyntaxRootAsync();
				if (root is not CompilationUnitSyntax compilationUnit)
				{
					continue;
				}

				var semanticModel = await document.GetSemanticModelAsync();
				if (semanticModel == null)
				{
					continue;
				}

				var rewriter = new MigrationRewriter(semanticModel, document.FilePath!);
				var updatedRoot = (CompilationUnitSyntax)rewriter.Visit(compilationUnit);
				if (!rewriter.HasChanges)
				{
					continue;
				}

				updatedRoot = EnsureReactiveUsing(updatedRoot);
				var updatedDocument = document.WithSyntaxRoot(updatedRoot);
				var updatedModel = await updatedDocument.GetSemanticModelAsync();
				if (updatedModel != null)
				{
					updatedRoot = RemoveCometUsingIfUnused(updatedRoot, updatedModel);
				}

				updatedDocument = updatedDocument.WithSyntaxRoot(updatedRoot);
				solution = updatedDocument.Project.Solution;
				reports.Add(new FileReport(document.FilePath!, rewriter.Changes));
			}

			var totalChanges = reports.Sum(report => report.Changes.Count);

			if (dryRun)
			{
				PrintReports("[DRY RUN] Would transform:", reports, totalChanges, "Run without --dry-run to apply.");
				return 0;
			}

			if (!workspace.TryApplyChanges(solution))
			{
				Console.Error.WriteLine("Failed to apply changes.");
				return 1;
			}

			PrintReports("Applied transformations:", reports, totalChanges, "");
			return 0;
		}

		static bool TryParseArgs(string[] args, out string projectPath, out bool dryRun)
		{
			projectPath = string.Empty;
			dryRun = false;

			for (var i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--project":
					if (i + 1 >= args.Length)
					{
						return false;
					}
					projectPath = args[++i];
					break;
					case "--dry-run":
					dryRun = true;
					break;
					default:
					return false;
				}
			}

			return !string.IsNullOrWhiteSpace(projectPath);
		}

		static void PrintUsage()
		{
			Console.WriteLine("Usage: comet-migrate --project <path> [--dry-run]");
		}

		static void PrintReports(string header, IReadOnlyList<FileReport> reports, int totalChanges, string footer)
		{
			Console.WriteLine(header);
			foreach (var report in reports)
			{
				var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), report.FilePath);
				Console.WriteLine($"  {relativePath}:");
				foreach (var change in report.Changes)
				{
					Console.WriteLine($"    Line {change.LineNumber}: {change.OldText}  →  {change.NewText}");
				}
			}

			Console.WriteLine();
			Console.WriteLine($"{totalChanges} transformations across {reports.Count} files.");
			if (!string.IsNullOrWhiteSpace(footer))
			{
				Console.WriteLine(footer);
			}
		}

		static CompilationUnitSyntax EnsureReactiveUsing(CompilationUnitSyntax root)
		{
			if (root.Usings.Any(u => u.Name?.ToString() == "Comet.Reactive"))
			{
				return root;
			}

			var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Comet.Reactive"));
			if (root.Usings.Count > 0)
			{
				var index = root.Usings.ToList().FindIndex(u => u.Name?.ToString() == "Comet");
				if (index >= 0)
				{
					return root.WithUsings(root.Usings.Insert(index + 1, usingDirective));
				}
			}

			return root.WithUsings(root.Usings.Add(usingDirective));
		}

		static CompilationUnitSyntax RemoveCometUsingIfUnused(CompilationUnitSyntax root, SemanticModel model)
		{
			var cometUsing = root.Usings.FirstOrDefault(u => u.Name?.ToString() == "Comet");
			if (cometUsing == null)
			{
				return root;
			}

			var hasCometUsage = root.DescendantNodes()
				.OfType<SimpleNameSyntax>()
				.Where(node => !node.Ancestors().OfType<UsingDirectiveSyntax>().Any())
				.Select(node => model.GetSymbolInfo(node).Symbol)
				.Any(symbol => IsCometSymbol(symbol));

			return hasCometUsage ? root : root.WithUsings(root.Usings.Remove(cometUsing));
		}

		static bool IsCometSymbol(ISymbol? symbol)
		{
			var containingNamespace = symbol?.ContainingNamespace?.ToDisplayString();
			if (string.IsNullOrWhiteSpace(containingNamespace))
			{
				return false;
			}

			if (!containingNamespace.StartsWith("Comet", StringComparison.Ordinal))
			{
				return false;
			}

			return !containingNamespace.StartsWith("Comet.Reactive", StringComparison.Ordinal);
		}
	}

	internal sealed class MigrationRewriter : CSharpSyntaxRewriter
	{
		readonly SemanticModel _semanticModel;
		readonly string _filePath;
		readonly List<ChangeEntry> _changes = new();

		public MigrationRewriter(SemanticModel semanticModel, string filePath)
		{
			_semanticModel = semanticModel;
			_filePath = filePath;
		}

		public IReadOnlyList<ChangeEntry> Changes => _changes;

		public bool HasChanges => _changes.Count > 0;

		public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
		{
			var typeSymbol = _semanticModel.GetTypeInfo(node.Declaration.Type).Type as INamedTypeSymbol;
			if (typeSymbol == null || !IsCometStateType(typeSymbol))
			{
				return base.VisitFieldDeclaration(node);
			}

			var updatedType = ReplaceStateType(node.Declaration.Type);
			var typeChanged = !updatedType.IsEquivalentTo(node.Declaration.Type);
			var updatedVariables = new List<VariableDeclaratorSyntax>();
			foreach (var variable in node.Declaration.Variables)
			{
				var updatedInitializer = RewriteInitializer(variable.Initializer);
				if (updatedInitializer != variable.Initializer || typeChanged)
				{
					var lineNumber = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
					var oldText = BuildFieldChangeText(node, variable);
					var newText = BuildFieldChangeText(node.WithDeclaration(node.Declaration.WithType(updatedType)), variable.WithInitializer(updatedInitializer));
					_changes.Add(new ChangeEntry(_filePath, lineNumber, oldText, newText));
				}
				updatedVariables.Add(variable.WithInitializer(updatedInitializer));
			}

			var updatedDeclaration = node.Declaration.WithType(updatedType).WithVariables(SyntaxFactory.SeparatedList(updatedVariables));
			return node.WithDeclaration(updatedDeclaration);
		}

		public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
		{
			var updatedNode = (ObjectCreationExpressionSyntax)base.VisitObjectCreationExpression(node)!;
			var typeSymbol = _semanticModel.GetTypeInfo(node).Type as INamedTypeSymbol;
			if (typeSymbol?.ContainingNamespace == null)
			{
				return updatedNode;
			}

			var namespaceName = typeSymbol.ContainingNamespace.ToDisplayString();
			if (!namespaceName.StartsWith("Comet", StringComparison.Ordinal) || namespaceName.StartsWith("Comet.Reactive", StringComparison.Ordinal))
			{
				return updatedNode;
			}

			if (updatedNode.ArgumentList == null)
			{
				return updatedNode;
			}

			var updatedArguments = new List<ArgumentSyntax>();
			var changed = false;
			foreach (var argument in updatedNode.ArgumentList.Arguments)
			{
				if (argument.Expression is MemberAccessExpressionSyntax memberAccess && memberAccess.Name.Identifier.Text == "Value")
				{
					var propertySymbol = _semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
					if (propertySymbol?.ContainingType is INamedTypeSymbol memberType &&
						(memberType.Name == "State" || memberType.Name == "Signal") &&
						memberType.ContainingNamespace.ToDisplayString().StartsWith("Comet", StringComparison.Ordinal))
					{
						var lambda = SyntaxFactory.ParenthesizedLambdaExpression(memberAccess.WithoutTrivia());
						lambda = lambda.WithTriviaFrom(argument.Expression);
						var updatedArgument = argument.WithExpression(lambda);
						updatedArguments.Add(updatedArgument);
						changed = true;

						var lineNumber = argument.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
						var oldText = node.ToString();
						var previewArguments = updatedArguments.Concat(updatedNode.ArgumentList.Arguments.Skip(updatedArguments.Count));
						var newText = updatedNode.WithArgumentList(updatedNode.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(previewArguments))).ToString();
						_changes.Add(new ChangeEntry(_filePath, lineNumber, oldText, newText));
						continue;
					}
				}

				updatedArguments.Add(argument);
			}

			if (!changed)
			{
				return updatedNode;
			}

			return updatedNode.WithArgumentList(updatedNode.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(updatedArguments)));
		}

		static bool IsCometStateType(INamedTypeSymbol typeSymbol)
		=> typeSymbol.Name == "State" && typeSymbol.ContainingNamespace.ToDisplayString() == "Comet";

		static TypeSyntax ReplaceStateType(TypeSyntax typeSyntax)
		{
			switch (typeSyntax)
			{
				case GenericNameSyntax generic when generic.Identifier.Text == "State":
				return generic.WithIdentifier(SyntaxFactory.Identifier("Signal"));
				case QualifiedNameSyntax qualified when qualified.Right is GenericNameSyntax generic && generic.Identifier.Text == "State":
				return qualified.WithRight(generic.WithIdentifier(SyntaxFactory.Identifier("Signal")));
				case AliasQualifiedNameSyntax alias when alias.Name is GenericNameSyntax generic && generic.Identifier.Text == "State":
				return alias.WithName(generic.WithIdentifier(SyntaxFactory.Identifier("Signal")));
				default:
				return typeSyntax;
			}
		}

		static EqualsValueClauseSyntax? RewriteInitializer(EqualsValueClauseSyntax? initializer)
		{
			if (initializer == null)
			{
				return null;
			}

			var value = initializer.Value;
			if (value is ObjectCreationExpressionSyntax objectCreation)
			{
				var typeName = objectCreation.Type switch
				{
					GenericNameSyntax generic => generic.Identifier.Text,
					QualifiedNameSyntax qualified when qualified.Right is GenericNameSyntax generic => generic.Identifier.Text,
					AliasQualifiedNameSyntax alias when alias.Name is GenericNameSyntax generic => generic.Identifier.Text,
					_ => string.Empty
				};

				if (typeName == "Signal")
				{
					return initializer;
				}

				if (typeName == "State")
				{
					var argument = objectCreation.ArgumentList?.Arguments.FirstOrDefault();
					var expression = argument?.Expression ?? SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression);
					return initializer.WithValue(CreateTargetTypedNew(expression));
				}
			}

			return initializer.WithValue(CreateTargetTypedNew(value));
		}

		static string BuildFieldChangeText(FieldDeclarationSyntax fieldDeclaration, VariableDeclaratorSyntax variable)
		{
			var modifiers = string.Join(" ", fieldDeclaration.Modifiers.Select(m => m.Text));
			var prefix = string.IsNullOrWhiteSpace(modifiers) ? string.Empty : modifiers + " ";
			return $"{prefix}{fieldDeclaration.Declaration.Type} {variable}".Trim();
		}

		static ExpressionSyntax CreateTargetTypedNew(ExpressionSyntax expression)
		{
			var parsed = SyntaxFactory.ParseExpression($"new({expression})");
			return parsed.WithTriviaFrom(expression);
		}
	}

	internal sealed record ChangeEntry(string FilePath, int LineNumber, string OldText, string NewText);

	internal sealed record FileReport(string FilePath, IReadOnlyList<ChangeEntry> Changes);
}
