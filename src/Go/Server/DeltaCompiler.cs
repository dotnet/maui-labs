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
	readonly List<MetadataReference> _references;

	CSharpCompilation? _currentCompilation;
	EmitBaseline? _baseline;
	int _generation;
	PEReader? _peReader;
	MetadataReaderProvider? _pdbReaderProvider;

	public string AssemblyName { get; }
	public byte[]? CurrentPe { get; private set; }
	public byte[]? CurrentPdb { get; private set; }

	public DeltaCompiler(string projectDir, string assemblyName)
	{
		_projectDir = projectDir;
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
				File.ReadAllText(f),
				CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
				path: f,
				encoding: System.Text.Encoding.UTF8))
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
				File.ReadAllText(f),
				CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest),
				path: f,
				encoding: System.Text.Encoding.UTF8))
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
				Errors = errors.Select(FormatDiagnostic).ToList()
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
					Errors = rudeEdits.Select(FormatDiagnostic).ToList()
				};
			}

			return new CompilationResult
			{
				Success = false,
				Errors = emitResult.Diagnostics
					.Where(d => d.Severity == DiagnosticSeverity.Error)
					.Select(FormatDiagnostic)
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
				// Note: new methods in existing types may also need SemanticEditKind.Insert
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
		}

		return edits;
	}

	static string GetMethodKey(SemanticModel model, MethodDeclarationSyntax method)
	{
		var symbol = model.GetDeclaredSymbol(method);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? method.Identifier.Text;
	}

	static string GetPropertyKey(SemanticModel model, PropertyDeclarationSyntax prop)
	{
		var symbol = model.GetDeclaredSymbol(prop);
		return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? prop.Identifier.Text;
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
		=> Directory.GetFiles(_projectDir, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
						!f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
			.ToArray();

	List<MetadataReference> ResolveReferences()
	{
		var refs = new List<MetadataReference>();
		var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Strategy: find pre-built Comet + MAUI reference assemblies from Comet.Tests output.
		// This directory contains BOTH framework types (Comet, MAUI, Extensions) AND BCL types
		// (System.*, Microsoft.*) all at the correct net11.0 version. Using these avoids
		// version mismatches with the dev server's own runtime (which may be net10.0).
		var refAssemblies = FindReferenceAssemblies();
		var hasFrameworkRefs = refAssemblies.Count > 0;

		foreach (var dll in refAssemblies)
		{
			if (loadedPaths.Add(dll))
			{
				try { refs.Add(MetadataReference.CreateFromFile(dll)); }
				catch { /* Skip assemblies that can't be loaded as metadata references */ }
			}
		}

		// Only add process runtime BCL assemblies if we don't have a complete ref set
		// (the Comet.Tests output already includes all needed BCL assemblies)
		if (!hasFrameworkRefs)
		{
			var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
			foreach (var dll in Directory.GetFiles(runtimeDir, "*.dll"))
			{
				if (loadedPaths.Add(dll))
				{
					try { refs.Add(MetadataReference.CreateFromFile(dll)); }
					catch { }
				}
			}
		}

		return refs;
	}

	/// <summary>
	/// Finds Comet + MAUI reference assemblies for compilation.
	/// Searches in priority order:
	///   1. Comet.Tests build output (has net11.0 plain TFM with all transitive refs)
	///   2. Companion app artifacts
	///   3. Comet src build output (platform-specific, may work for compilation)
	/// Also includes .NET SDK ref pack assemblies (System.Runtime, etc.)
	/// </summary>
	List<string> FindReferenceAssemblies()
	{
		var dlls = new List<string>();

		// Walk up from project dir to find the repo root
		var repoRoot = FindRepoRoot(_projectDir);
		if (repoRoot is null)
		{
			Console.Write("(no repo root found, using runtime refs only)... ");
			return dlls;
		}

		// Step 1: Find Comet + MAUI assemblies from Comet.Tests output
		var cometTestsOutput = Path.Combine(repoRoot, "src", "Comet", "tests", "Comet.Tests", "bin");
		var testDirs = new[]
		{
			Path.Combine(cometTestsOutput, "Release", "net11.0"),
			Path.Combine(cometTestsOutput, "Debug", "net11.0"),
		};

		string? foundDir = null;
		foreach (var dir in testDirs)
		{
			if (!Directory.Exists(dir)) continue;
			var cometDll = Path.Combine(dir, "Comet.dll");
			if (!File.Exists(cometDll)) continue;

			foreach (var dll in Directory.GetFiles(dir, "*.dll"))
			{
				var name = Path.GetFileNameWithoutExtension(dll);
				if (name.Contains("Tests") || name.Contains("xunit") || name.Contains("nunit") ||
					name.Contains("testhost") || name.Contains("TestPlatform") || name.Contains("CodeCoverage"))
					continue;
				dlls.Add(dll);
			}

			if (dlls.Count > 0)
			{
				foundDir = dir;
				break;
			}
		}

		if (foundDir is null)
		{
			// Try building Comet.Tests
			Console.Write("building Comet.Tests for references... ");
			var cometTestsProj = Path.Combine(repoRoot, "src", "Comet", "tests", "Comet.Tests", "Comet.Tests.csproj");
			if (File.Exists(cometTestsProj))
			{
				var psi = new ProcessStartInfo("dotnet", $"build \"{cometTestsProj}\" -c Release -f net11.0 --nologo --verbosity quiet")
				{
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
				};
				using var proc = Process.Start(psi);
				proc?.StandardOutput.ReadToEnd();
				proc?.StandardError.ReadToEnd();
				proc?.WaitForExit(180_000);

				if (proc?.ExitCode == 0)
				{
					var outputDir = Path.Combine(cometTestsOutput, "Release", "net11.0");
					if (Directory.Exists(outputDir))
					{
						foreach (var dll in Directory.GetFiles(outputDir, "*.dll"))
						{
							var name = Path.GetFileNameWithoutExtension(dll);
							if (!name.Contains("Tests") && !name.Contains("xunit") && !name.Contains("nunit") &&
								!name.Contains("testhost") && !name.Contains("TestPlatform") && !name.Contains("CodeCoverage"))
								dlls.Add(dll);
						}
					}
				}
			}
		}

		// Step 2: Add .NET SDK ref pack assemblies (System.Runtime, System.Collections, etc.)
		var sdkRefDlls = FindSdkRefPackAssemblies("net11.0");
		dlls.AddRange(sdkRefDlls);

		Console.Write($"({dlls.Count} refs)... ");
		return dlls;
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

	static string? FindRepoRoot(string startDir)
	{
		var dir = new DirectoryInfo(startDir);
		while (dir is not null)
		{
			if (File.Exists(Path.Combine(dir.FullName, "MauiLabs.slnx")) ||
				(Directory.Exists(Path.Combine(dir.FullName, ".git")) &&
				 Directory.Exists(Path.Combine(dir.FullName, "src", "Comet"))))
				return dir.FullName;
			dir = dir.Parent;
		}
		return null;
	}

	static string FormatDiagnostic(Diagnostic d)
	{
		var location = d.Location.GetLineSpan();
		var file = Path.GetFileName(location.Path);
		return $"{file}({location.StartLinePosition.Line + 1},{location.StartLinePosition.Character + 1}): {d.Id}: {d.GetMessage()}";
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
	public byte[]? Pe { get; init; }
	public byte[]? Pdb { get; init; }
	public byte[]? MetadataDelta { get; init; }
	public byte[]? ILDelta { get; init; }
	public byte[]? PdbDelta { get; init; }
}
