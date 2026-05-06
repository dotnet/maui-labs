// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Maui.Go;

namespace Microsoft.Maui.Go.Server;

/// <summary>
/// Incremental Roslyn compiler that produces EnC (Edit and Continue) deltas.
/// Builds on the MetadataUpdaterSpike proof-of-concept.
///
/// Workflow:
///   1. CompileInitial() — full compilation, produces PE + PDB baseline
///   2. CompileDelta() — re-reads source files, diffs against baseline, produces deltas
///   3. After successful delta, the baseline advances (chained baselines)
/// </summary>
public sealed class DeltaCompiler
{
	readonly string _projectDir;
	readonly string? _singleFilePath;
	readonly List<MetadataReference> _references;

	CSharpCompilation? _currentCompilation;
	EmitBaseline? _baseline;
	int _generation;
	PEReader? _peReader;
	MetadataReaderProvider? _pdbReaderProvider;

	public string AssemblyName { get; }
	public byte[]? CurrentPe { get; private set; }
	public byte[]? CurrentPdb { get; private set; }
	public bool IsSingleFileMode => _singleFilePath is not null;

	public DeltaCompiler(string projectDir, string assemblyName)
	{
		_projectDir = projectDir;
		AssemblyName = assemblyName;
		_references = ResolveReferences();
	}

	/// <summary>
	/// Creates a DeltaCompiler in single-file mode.
	/// The assembly name is derived from the file name.
	/// </summary>
	public static DeltaCompiler ForSingleFile(string csFilePath)
	{
		var fullPath = Path.GetFullPath(csFilePath);
		var dir = Path.GetDirectoryName(fullPath)!;
		var name = Path.GetFileNameWithoutExtension(fullPath);
		return new DeltaCompiler(dir, name, fullPath);
	}

	DeltaCompiler(string projectDir, string assemblyName, string singleFilePath)
	{
		_projectDir = projectDir;
		_singleFilePath = singleFilePath;
		AssemblyName = assemblyName;
		_references = ResolveReferences();
	}

	/// <summary>
	/// Performs the initial full compilation. Must be called once before CompileDelta.
	/// </summary>
	public CompilationResult CompileInitial()
	{
		var sourceFiles = GetSourceFiles();
		var syntaxTrees = sourceFiles
			.Select(f => CSharpSyntaxTree.ParseText(
				ReadSourceText(f),
				CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
				path: f,
				encoding: System.Text.Encoding.UTF8))
			.Append(CreateImplicitUsingsSyntaxTree())
			.ToArray();

		_currentCompilation = CSharpCompilation.Create(
			AssemblyName,
			syntaxTrees,
			_references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				.WithOptimizationLevel(OptimizationLevel.Debug));

		var peStream = new MemoryStream();
		var pdbStream = new MemoryStream();

		var emitResult = _currentCompilation.Emit(peStream, pdbStream,
			options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

		if (!emitResult.Success)
		{
			return new CompilationResult
			{
				Success = false,
				Errors = emitResult.Diagnostics
					.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(FormatDiagnostic)
					.ToList()
			};
		}

		CurrentPe = peStream.ToArray();
		CurrentPdb = pdbStream.ToArray();

		// Create baseline for future deltas.
		// We must provide proper debug info and local signature providers
		// so Roslyn can correctly track local variable slots and synthesized
		// members (closures, lambdas) across delta generations.
		var moduleMetadata = ModuleMetadata.CreateFromImage(CurrentPe);

		// Build lookup tables from the PE and PDB for the baseline providers
		var peReader = new PEReader(new MemoryStream(CurrentPe));
		var metadataReader = peReader.GetMetadataReader();
		var pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(CurrentPdb));
		var pdbReader = pdbReaderProvider.GetMetadataReader();

		// Build local signature map: MethodDef handle → StandaloneSignature handle
		var localSigMap = new Dictionary<MethodDefinitionHandle, StandaloneSignatureHandle>();
		foreach (var methodHandle in metadataReader.MethodDefinitions)
		{
			var methodDef = metadataReader.GetMethodDefinition(methodHandle);
			var body = methodDef.RelativeVirtualAddress > 0
				? peReader.GetMethodBody(methodDef.RelativeVirtualAddress)
				: null;
			if (body?.LocalSignature.IsNil == false)
				localSigMap[methodHandle] = body.LocalSignature;
		}

		_baseline = EmitBaseline.CreateInitialBaseline(
			_currentCompilation,
			moduleMetadata,
			handle =>
			{
				// Read EditAndContinueMethodDebugInformation from PDB
				try
				{
					return EditAndContinueMethodDebugInfoReader.Read(pdbReader, handle);
				}
				catch
				{
					return default;
				}
			},
			handle => localSigMap.TryGetValue(handle, out var sig) ? sig : default,
			hasPortableDebugInformation: true);

		// Keep readers alive for the lifetime of the baseline
		_peReader = peReader;
		_pdbReaderProvider = pdbReaderProvider;

		_generation = 0;

		return new CompilationResult
		{
			Success = true,
			Pe = CurrentPe,
			Pdb = CurrentPdb,
		};
	}

	/// <summary>
	/// Re-reads source files, diffs against current compilation, and emits EnC deltas.
	/// Returns null deltas if no semantic changes detected, or errors if compilation fails.
	/// </summary>
	public CompilationResult CompileDelta()
	{
		if (_currentCompilation is null || _baseline is null)
			return new CompilationResult { Success = false, Errors = ["Initial compilation required first"] };

		// Re-read all source files
		var sourceFiles = GetSourceFiles();
		var newTrees = sourceFiles
			.Select(f => CSharpSyntaxTree.ParseText(
				ReadSourceText(f),
				CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
				path: f,
				encoding: System.Text.Encoding.UTF8))
			.Append(CreateImplicitUsingsSyntaxTree())
			.ToArray();

		// Create new compilation
		var newCompilation = CSharpCompilation.Create(
			AssemblyName,
			newTrees,
			_references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
				.WithOptimizationLevel(OptimizationLevel.Debug));

		// Check for errors first
		var diagnostics = newCompilation.GetDiagnostics();
		var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
		if (errors.Length > 0)
		{
			return new CompilationResult
			{
				Success = false,
				Errors = errors.Select(FormatDiagnostic).ToList(),
				Diagnostics = errors.Select(ToStructuredDiagnostic).ToList()
			};
		}

		// Compute semantic edits between old and new compilation
		var edits = ComputeSemanticEdits(_currentCompilation, newCompilation);

		if (edits.Count == 0)
		{
			// No changes detected
			return new CompilationResult { Success = true };
		}

		// Emit delta
		var metadataStream = new MemoryStream();
		var ilStream = new MemoryStream();
		var pdbStream = new MemoryStream();

		EmitDifferenceResult emitResult;
		try
		{
			emitResult = newCompilation.EmitDifference(
				_baseline,
				edits,
				s => false,
				metadataStream,
				ilStream,
				pdbStream);
		}
		catch (Exception ex)
		{
			// EmitDifference can throw for unsupported edits
			return new CompilationResult
			{
				Success = true,
				// Signal restart required (no deltas, but compilation succeeded)
				Errors = [$"Unsupported edit for hot reload: {ex.Message}"]
			};
		}

		if (!emitResult.Success)
		{
			// Check if these are "rude edits" (unsupported by EnC)
			var rudeEdits = emitResult.Diagnostics
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.ToArray();

			if (rudeEdits.Length > 0)
			{
				return new CompilationResult
				{
					Success = true,
					Errors = rudeEdits.Select(FormatDiagnostic).ToList(),
					Diagnostics = rudeEdits.Select(ToStructuredDiagnostic).ToList()
				};
			}

			return new CompilationResult
			{
				Success = false,
				Errors = emitResult.Diagnostics
					.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(FormatDiagnostic)
					.ToList(),
				Diagnostics = emitResult.Diagnostics
					.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(ToStructuredDiagnostic)
					.ToList()
			};
		}

		// Advance the baseline
		_baseline = emitResult.Baseline;
		_currentCompilation = newCompilation;
		_generation++;

		return new CompilationResult
		{
			Success = true,
			MetadataDelta = metadataStream.ToArray(),
			ILDelta = ilStream.ToArray(),
			PdbDelta = pdbStream.ToArray(),
		};
	}

	/// <summary>
	/// Computes semantic edits by comparing symbols between old and new compilations.
	/// Finds methods, properties, and types that changed.
	/// </summary>
	List<SemanticEdit> ComputeSemanticEdits(CSharpCompilation oldCompilation, CSharpCompilation newCompilation)
	{
		var edits = new List<SemanticEdit>();

		// Build a map of old symbols by fully qualified name
		var oldSymbols = new Dictionary<string, ISymbol>();
		CollectSymbols(oldCompilation, oldSymbols);

		// Compare new symbols
		foreach (var newTree in newCompilation.SyntaxTrees)
		{
			var newModel = newCompilation.GetSemanticModel(newTree);
			var newRoot = newTree.GetRoot();

			// Find corresponding old tree (by file path)
			var oldTree = oldCompilation.SyntaxTrees
				.FirstOrDefault(t => t.FilePath == newTree.FilePath);

			if (oldTree is null)
			{
				// New file — all types are additions
				foreach (var typeDecl in newRoot.DescendantNodes().OfType<TypeDeclarationSyntax>())
				{
					var newSymbol = newModel.GetDeclaredSymbol(typeDecl);
					if (newSymbol is not null)
						edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, newSymbol));
				}
				continue;
			}

			var oldModel = oldCompilation.GetSemanticModel(oldTree);
			var oldRoot = oldTree.GetRoot();

			// Compare method bodies
			var newMethods = newRoot.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
			var oldMethods = oldRoot.DescendantNodes().OfType<MethodDeclarationSyntax>()
				.ToDictionary(m => GetMethodKey(oldModel, m));

			foreach (var newMethod in newMethods)
			{
				var newSymbol = newModel.GetDeclaredSymbol(newMethod);
				if (newSymbol is null) continue;

				var key = GetMethodKey(newModel, newMethod);

				if (oldMethods.TryGetValue(key, out var oldMethod))
				{
					// Check if body changed
					var oldBody = oldMethod.Body?.ToFullString() ?? oldMethod.ExpressionBody?.ToFullString() ?? "";
					var newBody = newMethod.Body?.ToFullString() ?? newMethod.ExpressionBody?.ToFullString() ?? "";

					if (oldBody != newBody)
					{
						var oldSymbol = oldModel.GetDeclaredSymbol(oldMethod);
						edits.Add(new SemanticEdit(SemanticEditKind.Update, oldSymbol, newSymbol));
					}
				}
				else
				{
					// New method in existing type — this IS an edit
					edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, newSymbol));
				}
			}

			// Compare constructors
			var newCtors = newRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>().ToArray();
			var oldCtors = oldRoot.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
				.ToDictionary(c => GetConstructorKey(oldModel, c));

			foreach (var newCtor in newCtors)
			{
				var newSymbol = newModel.GetDeclaredSymbol(newCtor);
				if (newSymbol is null) continue;

				var key = GetConstructorKey(newModel, newCtor);

				if (oldCtors.TryGetValue(key, out var oldCtor))
				{
					var oldBody = oldCtor.Body?.ToFullString() ?? oldCtor.ExpressionBody?.ToFullString() ?? "";
					var newBody = newCtor.Body?.ToFullString() ?? newCtor.ExpressionBody?.ToFullString() ?? "";

					if (oldBody != newBody)
					{
						var oldSymbol = oldModel.GetDeclaredSymbol(oldCtor);
						edits.Add(new SemanticEdit(SemanticEditKind.Update, oldSymbol, newSymbol));
					}
				}
				else
				{
					edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, newSymbol));
				}
			}

			// Compare properties (auto-properties with expression bodies)
			var newProps = newRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>().ToArray();
			var oldProps = oldRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>()
				.ToDictionary(p => GetPropertyKey(oldModel, p));

			foreach (var newProp in newProps)
			{
				var newSymbol = newModel.GetDeclaredSymbol(newProp);
				if (newSymbol is null) continue;

				var key = GetPropertyKey(newModel, newProp);

				if (oldProps.TryGetValue(key, out var oldProp))
				{
					var oldText = oldProp.ToFullString();
					var newText = newProp.ToFullString();

					if (oldText != newText)
					{
						var oldSymbol = oldModel.GetDeclaredSymbol(oldProp);
						edits.Add(new SemanticEdit(SemanticEditKind.Update, oldSymbol, newSymbol));
					}
				}
			}

			// Compare field initializers
			var newFields = newRoot.DescendantNodes().OfType<FieldDeclarationSyntax>().ToArray();
			var oldFields = oldRoot.DescendantNodes().OfType<FieldDeclarationSyntax>()
				.ToDictionary(f => GetFieldKey(oldModel, f));

			foreach (var newField in newFields)
			{
				var key = GetFieldKey(newModel, newField);

				if (oldFields.TryGetValue(key, out var oldField))
				{
					var oldText = oldField.ToFullString();
					var newText = newField.ToFullString();

					if (oldText != newText)
					{
						// Field initializer changed — update the containing type's constructor
						var containingType = newField.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
						if (containingType is not null)
						{
							var typeSymbol = newModel.GetDeclaredSymbol(containingType);
							if (typeSymbol is INamedTypeSymbol namedType)
							{
								// Find the instance constructor (or static constructor for static fields)
								var isStatic = newField.Modifiers.Any(SyntaxKind.StaticKeyword);
								var ctorSymbol = namedType.Constructors
									.FirstOrDefault(c => c.IsStatic == isStatic);
								if (ctorSymbol is not null)
								{
									var oldTypeSymbol = oldSymbols.TryGetValue(
										typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), out var ots)
										? ots as INamedTypeSymbol : null;
									var oldCtorSymbol = oldTypeSymbol?.Constructors
										.FirstOrDefault(c => c.IsStatic == isStatic);
									edits.Add(new SemanticEdit(SemanticEditKind.Update, oldCtorSymbol, ctorSymbol));
								}
							}
						}
					}
				}
			}
		}

		return edits;
	}

	static string GetMethodKey(SemanticModel model, MethodDeclarationSyntax method)
	{
		var symbol = model.GetDeclaredSymbol(method);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? method.Identifier.Text;
	}

	static string GetConstructorKey(SemanticModel model, ConstructorDeclarationSyntax ctor)
	{
		var symbol = model.GetDeclaredSymbol(ctor);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? ctor.Identifier.Text;
	}

	static string GetPropertyKey(SemanticModel model, PropertyDeclarationSyntax prop)
	{
		var symbol = model.GetDeclaredSymbol(prop);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? prop.Identifier.Text;
	}

	static string GetFieldKey(SemanticModel model, FieldDeclarationSyntax field)
	{
		var variable = field.Declaration.Variables.FirstOrDefault();
		if (variable is null) return field.ToFullString();
		var symbol = model.GetDeclaredSymbol(variable);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? variable.Identifier.Text;
	}

	static SyntaxTree CreateImplicitUsingsSyntaxTree()
	{
		return CSharpSyntaxTree.ParseText(
			"global using System;\n" +
			"global using System.Linq;\n" +
			"global using System.Collections.Generic;\n",
			CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
			path: "__implicit_usings.cs",
			encoding: System.Text.Encoding.UTF8);
	}

	static void CollectSymbols(CSharpCompilation compilation, Dictionary<string, ISymbol> symbols)
	{
		foreach (var tree in compilation.SyntaxTrees)
		{
			var model = compilation.GetSemanticModel(tree);
			var root = tree.GetRoot();

			foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
			{
				var symbol = model.GetDeclaredSymbol(member);
				if (symbol is not null)
				{
					var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
					if (!symbols.ContainsKey(key))
						symbols[key] = symbol;
				}
			}
		}
	}

	string[] GetSourceFiles()
	{
		if (_singleFilePath is not null)
			return [_singleFilePath];

		return Directory.GetFiles(_projectDir, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
						!f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
			.ToArray();
	}

	/// <summary>
	/// Reads source text from a file, replacing file-based app directives (#: and #!)
	/// with blank lines to preserve line number mapping for diagnostics.
	/// </summary>
	static string ReadSourceText(string filePath)
	{
		var lines = File.ReadAllLines(filePath);
		var inDirectiveBlock = true;
		for (var i = 0; i < lines.Length; i++)
		{
			if (!inDirectiveBlock) break;

			var trimmed = lines[i].TrimStart();
			if (trimmed.StartsWith("#:") || trimmed.StartsWith("#!"))
				lines[i] = ""; // blank line preserves line numbers
			else if (trimmed.Length == 0)
				continue; // blank lines between directives are fine
			else
				inDirectiveBlock = false; // first real code line ends directive block
		}
		return string.Join(Environment.NewLine, lines);
	}

	List<MetadataReference> ResolveReferences()
	{
		var refs = new List<MetadataReference>();
		var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Step 1: Resolve Comet + MAUI from NuGet package cache
		var nugetDlls = FindNuGetReferenceAssemblies();

		foreach (var dll in nugetDlls)
		{
			if (loadedPaths.Add(dll))
			{
				try { refs.Add(MetadataReference.CreateFromFile(dll)); }
				catch { }
			}
		}

		// Step 2: Add .NET SDK ref pack assemblies (System.Runtime, System.Collections, etc.)
		var sdkDlls = FindSdkRefPackAssemblies("net11.0");
		foreach (var dll in sdkDlls)
		{
			if (loadedPaths.Add(dll))
			{
				try { refs.Add(MetadataReference.CreateFromFile(dll)); }
				catch { }
			}
		}

		Console.Write($"({refs.Count} refs)... ");
		return refs;
	}

	/// <summary>
	/// Resolves Comet and MAUI reference assemblies from the NuGet package cache.
	/// Looks for packages in ~/.nuget/packages/ by name, picking the best available
	/// TFM (prefer net11.0, fall back to any platform TFM like net11.0-maccatalyst).
	/// </summary>
	List<string> FindNuGetReferenceAssemblies()
	{
		var dlls = new List<string>();
		var nugetRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			".nuget", "packages");

		if (!Directory.Exists(nugetRoot))
			return dlls;

		// Parse #:package directives from source file (if single-file mode)
		var requestedPackages = new List<(string Name, string? Version)>();
		if (_singleFilePath is not null && File.Exists(_singleFilePath))
		{
			foreach (var line in File.ReadLines(_singleFilePath))
			{
				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("#:package "))
				{
					var spec = trimmed["#:package ".Length..].Trim();
					var parts = spec.Split('@', 2);
					requestedPackages.Add((parts[0], parts.Length > 1 ? parts[1] : null));
				}
				else if (!trimmed.StartsWith("#:") && !trimmed.StartsWith("#!") && trimmed.Length > 0)
					break; // past directive block
			}
		}

		// Always include Comet and its MAUI dependencies
		string[] requiredPackages =
		[
			"Comet",
			"Microsoft.Maui.Core",
			"Microsoft.Maui.Controls",
			"Microsoft.Maui.Graphics",
			"Microsoft.Maui.Essentials",
		];

		foreach (var pkgName in requiredPackages)
		{
			var pkgDir = Path.Combine(nugetRoot, pkgName.ToLowerInvariant());
			if (!Directory.Exists(pkgDir)) continue;

			// Find the requested version or latest available
			var requestedVersion = requestedPackages
				.FirstOrDefault(p => string.Equals(p.Name, pkgName, StringComparison.OrdinalIgnoreCase))
				.Version;

			var dll = FindBestDllFromPackage(pkgDir, requestedVersion);
			if (dll is not null)
				dlls.Add(dll);
		}

		return dlls;
	}

	/// <summary>
	/// Finds the best DLL from a NuGet package directory.
	/// Prefers: requested version > latest preview.3 > latest version.
	/// For TFM: prefers net11.0 > net11.0-maccatalyst > any net11.0-* > net10.0-*.
	/// </summary>
	static string? FindBestDllFromPackage(string packageDir, string? requestedVersion)
	{
		// Find the version directory
		string? versionDir = null;

		if (requestedVersion is not null)
		{
			var candidate = Path.Combine(packageDir, requestedVersion);
			if (Directory.Exists(candidate))
				versionDir = candidate;
		}

		if (versionDir is null)
		{
			// Pick the latest version, preferring preview.3 for .NET 11
			var versions = Directory.GetDirectories(packageDir)
				.Select(Path.GetFileName)
				.Where(v => v is not null)
				.OrderByDescending(v => v!.Contains("preview.3") ? 1 : 0)
				.ThenByDescending(v => v)
				.ToArray();

			foreach (var ver in versions)
			{
				var candidate = Path.Combine(packageDir, ver!);
				if (Directory.Exists(Path.Combine(candidate, "lib")))
				{
					versionDir = candidate;
					break;
				}
			}
		}

		if (versionDir is null) return null;

		var libDir = Path.Combine(versionDir, "lib");
		if (!Directory.Exists(libDir)) return null;

		// Find the best TFM directory
		var tfmDirs = Directory.GetDirectories(libDir)
			.Select(Path.GetFileName)
			.Where(d => d is not null)
			.ToArray();

		// Priority: net11.0 (plain) > net11.0-ios* > net11.0-maccatalyst* > any net11.0-* > net10.0-* > netstandard*
		// Prefer ios since the companion app typically runs on iOS simulator
		// Only include TFMs that actually exist in the package
		var tfmPriority = new List<string>();
		if (tfmDirs.Contains("net11.0")) tfmPriority.Add("net11.0");
		tfmPriority.AddRange(tfmDirs.Where(d => d!.StartsWith("net11.0-ios")).OrderByDescending(d => d)!);
		tfmPriority.AddRange(tfmDirs.Where(d => d!.StartsWith("net11.0-maccatalyst")).OrderByDescending(d => d)!);
		tfmPriority.AddRange(tfmDirs.Where(d => d!.StartsWith("net11.0-") && !d!.StartsWith("net11.0-ios") && !d!.StartsWith("net11.0-maccatalyst")).OrderByDescending(d => d)!);
		tfmPriority.AddRange(tfmDirs.Where(d => d!.StartsWith("net10.0")).OrderByDescending(d => d)!);
		tfmPriority.AddRange(tfmDirs.Where(d => d!.StartsWith("netstandard")).OrderByDescending(d => d)!);

		foreach (var tfm in tfmPriority)
		{
			var tfmDir = Path.Combine(libDir, tfm!);
			if (!Directory.Exists(tfmDir)) continue;
			var dllFiles = Directory.GetFiles(tfmDir, "*.dll");
			if (dllFiles.Length > 0)
				return dllFiles[0];
		}

		return null;
	}

	/// <summary>
	/// Finds .NET SDK reference pack assemblies for the given TFM.
	/// These contain System.Runtime.dll, System.Collections.dll, etc.
	/// Located at: /usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/
	/// </summary>
	static List<string> FindSdkRefPackAssemblies(string tfm)
	{
		var dlls = new List<string>();

		var dotnetRoots = new[]
		{
			"/usr/local/share/dotnet",
			"/usr/share/dotnet",
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
		};

		foreach (var dotnetRoot in dotnetRoots)
		{
			var packsDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
			if (!Directory.Exists(packsDir)) continue;

			// Find the latest version that matches our TFM
			var tfmShort = tfm.Replace(".", ""); // "net110"
			var versionDirs = Directory.GetDirectories(packsDir)
				.Where(d =>
				{
					var verName = Path.GetFileName(d);
					// For net11.0, match versions starting with "11."
					return verName.StartsWith(tfm.Replace("net", "") + ".");
				})
				.OrderByDescending(d => d)
				.ToList();

			foreach (var versionDir in versionDirs)
			{
				var refDir = Path.Combine(versionDir, "ref", tfm);
				if (!Directory.Exists(refDir)) continue;

				dlls.AddRange(Directory.GetFiles(refDir, "*.dll"));
				return dlls; // Found it
			}
		}

		return dlls;
	}

	static string FormatDiagnostic(Diagnostic d)
	{
		var location = d.Location.GetLineSpan();
		var file = Path.GetFileName(location.Path);
		var formatted = $"{file}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1}): {d.Id}: {d.GetMessage()}";
		return formatted + AddHint(d);
	}

	/// <summary>
	/// Post-processes common Roslyn diagnostics to add actionable hints for Comet users.
	/// </summary>
	static string AddHint(Diagnostic d)
	{
		var msg = d.GetMessage();
		return d.Id switch
		{
			"CS0121" when msg.Contains("VStack") || msg.Contains("HStack")
				=> "\nHint: Use named arg, e.g. VStack(spacing: 0f, ...)",
			"CS0117" when msg.Contains("FontWeight")
				=> "\nHint: Available values: Bold, Regular, Light, Medium, Heavy, Thin",
			"CS1503" when msg.Contains("method group") && msg.Contains("Action")
				=> "\nHint: Wrap in lambda, e.g. () => Method()",
			"CS0246" when msg.Contains("Action") || msg.Contains("Func")
				=> "\nHint: Add 'using System;'",
			_ => ""
		};
	}

	/// <summary>
	/// Extracts structured diagnostic fields from a Roslyn diagnostic.
	/// </summary>
	internal static CompilationDiagnostic ToStructuredDiagnostic(Diagnostic d)
	{
		var location = d.Location.GetLineSpan();
		return new CompilationDiagnostic
		{
			Id = d.Id,
			Message = FormatDiagnostic(d),
			FilePath = location.Path ?? "",
			Line = location.StartLinePosition.Line + 1,
			Column = location.StartLinePosition.Character + 1,
			Severity = d.Severity.ToString(),
		};
	}
}

/// <summary>
/// Reads EditAndContinueMethodDebugInformation from a Portable PDB.
/// EnC-specific debug info is stored in custom debug info entries:
///   - EncLocalSlotMap  (GUID: EE813940-...) — maps local variable slots across edits
///   - EncLambdaAndClosureMap (GUID: A643004F-...) — tracks lambda ordinals and closure scopes
/// </summary>
static class EditAndContinueMethodDebugInfoReader
{
	// Portable PDB custom debug info GUIDs for EnC
	static readonly Guid EncLocalSlotMapGuid = new("C6B3C19F-4B94-45F3-9D7A-F9EB21C54D13");
	static readonly Guid EncLambdaAndClosureMapGuid = new("A643004F-0170-4B5E-B5AA-1C5763BD9B08");

	public static EditAndContinueMethodDebugInformation Read(
		MetadataReader pdbReader, MethodDefinitionHandle handle)
	{
		var localSlotMap = ImmutableArray<byte>.Empty;
		var lambdaMap = ImmutableArray<byte>.Empty;

		foreach (var cdiHandle in pdbReader.GetCustomDebugInformation(handle))
		{
			var cdi = pdbReader.GetCustomDebugInformation(cdiHandle);
			var guid = pdbReader.GetGuid(cdi.Kind);
			var blob = pdbReader.GetBlobContent(cdi.Value);

			if (guid == EncLocalSlotMapGuid)
				localSlotMap = blob;
			else if (guid == EncLambdaAndClosureMapGuid)
				lambdaMap = blob;
		}

		return EditAndContinueMethodDebugInformation.Create(localSlotMap, lambdaMap);
	}
}
public sealed class CompilationResult
{
	public bool Success { get; init; }
	public List<string> Errors { get; init; } = [];
	/// <summary>Structured diagnostics from Roslyn (populated for compilation errors).</summary>
	public List<CompilationDiagnostic> Diagnostics { get; init; } = [];
	public byte[]? Pe { get; init; }
	public byte[]? Pdb { get; init; }
	public byte[]? MetadataDelta { get; init; }
	public byte[]? ILDelta { get; init; }
	public byte[]? PdbDelta { get; init; }
}
