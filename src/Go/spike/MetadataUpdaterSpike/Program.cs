// MetadataUpdater Feasibility Spike — MAUI Go Phase 0
//
// Proves whether MetadataUpdater.ApplyUpdate() can be called
// programmatically (no debugger) with Roslyn-generated deltas.
//
// MUST be run with: DOTNET_MODIFIABLE_ASSEMBLIES=debug

using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace MetadataUpdaterSpike;

class Program
{
	// v1: the original source
	const string SourceV1 = """
		namespace SpikeTarget;

		public static class Greeter
		{
			public static string GetMessage() => "v1";
		}
		""";

	// v2: method body change only (the sweet spot for hot reload)
	const string SourceV2 = """
		namespace SpikeTarget;

		public static class Greeter
		{
			public static string GetMessage() => "v2";
		}
		""";

	static int Main()
	{
		Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
		Console.WriteLine("║  MetadataUpdater Feasibility Spike — MAUI Go Phase 0   ║");
		Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
		Console.WriteLine();

		// Step 0: Check environment
		var envVar = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES");
		Console.WriteLine($"[env] DOTNET_MODIFIABLE_ASSEMBLIES = \"{envVar}\"");

		if (!string.Equals(envVar, "debug", StringComparison.OrdinalIgnoreCase))
		{
			Console.WriteLine("[FATAL] DOTNET_MODIFIABLE_ASSEMBLIES must be set to 'debug' BEFORE process start.");
			Console.WriteLine("        Run: DOTNET_MODIFIABLE_ASSEMBLIES=debug dotnet run --project ...");
			return 1;
		}

		// Step 1: Check MetadataUpdater.IsSupported
		Console.WriteLine();
		Console.WriteLine("── Step 1: MetadataUpdater.IsSupported ──");
		bool isSupported = MetadataUpdater.IsSupported;
		Console.WriteLine($"  MetadataUpdater.IsSupported = {isSupported}");

		if (!isSupported)
		{
			Console.WriteLine("[FATAL] MetadataUpdater.IsSupported returned false.");
			Console.WriteLine("  This means the runtime does NOT support programmatic updates");
			Console.WriteLine("  without a debugger attached. MAUI Go's hot-reload-over-network");
			Console.WriteLine("  approach is NOT feasible with this runtime.");
			return 2;
		}

		Console.WriteLine("  ✓ Runtime supports metadata updates without debugger!");
		Console.WriteLine();

		// Step 2: Compile v1 assembly with Roslyn
		Console.WriteLine("── Step 2: Compile v1 assembly with Roslyn ──");
		var (peBytes, pdbBytes, v1Compilation) = CompileAssembly("SpikeTarget", SourceV1);
		Console.WriteLine($"  ✓ v1 compiled: {peBytes.Length} bytes PE, {pdbBytes.Length} bytes PDB");

		// Step 3: Load v1 into default AssemblyLoadContext
		Console.WriteLine();
		Console.WriteLine("── Step 3: Load v1 into default ALC ──");
		var assembly = System.Runtime.Loader.AssemblyLoadContext.Default
			.LoadFromStream(new MemoryStream(peBytes));
		Console.WriteLine($"  ✓ Loaded: {assembly.FullName}");

		// Verify v1 works
		var greeterType = assembly.GetType("SpikeTarget.Greeter")!;
		var getMessageMethod = greeterType.GetMethod("GetMessage")!;
		var v1Result = (string)getMessageMethod.Invoke(null, null)!;
		Console.WriteLine($"  ✓ Greeter.GetMessage() = \"{v1Result}\"");

		if (v1Result != "v1")
		{
			Console.WriteLine($"[FATAL] Expected \"v1\" but got \"{v1Result}\"");
			return 3;
		}

		// Step 4: Generate deltas using Roslyn EmitDifference
		Console.WriteLine();
		Console.WriteLine("── Step 4: Generate EnC deltas (v1 → v2) ──");
		var (metadataDelta, ilDelta, updatedPdbDelta) = GenerateDeltas(
			peBytes, pdbBytes, v1Compilation, SourceV2);
		Console.WriteLine($"  ✓ Metadata delta: {metadataDelta.Length} bytes");
		Console.WriteLine($"  ✓ IL delta: {ilDelta.Length} bytes");
		Console.WriteLine($"  ✓ PDB delta: {updatedPdbDelta.Length} bytes");

		// Step 5: Apply the update via MetadataUpdater
		Console.WriteLine();
		Console.WriteLine("── Step 5: MetadataUpdater.ApplyUpdate() ──");
		try
		{
			MetadataUpdater.ApplyUpdate(
				assembly,
				metadataDelta,
				ilDelta,
				ReadOnlySpan<byte>.Empty);  // PDB delta not needed for behavior change

			Console.WriteLine("  ✓ ApplyUpdate() succeeded — no exception!");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"  ✗ ApplyUpdate() threw: {ex.GetType().Name}");
			Console.WriteLine($"    {ex.Message}");
			if (ex.InnerException != null)
				Console.WriteLine($"    Inner: {ex.InnerException.Message}");
			Console.WriteLine();
			Console.WriteLine("[RESULT] ApplyUpdate FAILED. Constraints:");
			Console.WriteLine("  - The runtime may require a debugger agent for ApplyUpdate");
			Console.WriteLine("  - Or the delta format may be incorrect");
			Console.WriteLine("  - Or the assembly may not be marked as modifiable");
			return 4;
		}

		// Step 6: Verify the method now returns "v2"
		Console.WriteLine();
		Console.WriteLine("── Step 6: Verify updated method ──");
		var v2Result = (string)getMessageMethod.Invoke(null, null)!;
		Console.WriteLine($"  Greeter.GetMessage() = \"{v2Result}\"");

		if (v2Result == "v2")
		{
			Console.WriteLine("  ✓ Method body successfully updated from \"v1\" to \"v2\"!");
			Console.WriteLine();
			Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
			Console.WriteLine("║  RESULT: FEASIBLE ✓                                     ║");
			Console.WriteLine("║                                                          ║");
			Console.WriteLine("║  MetadataUpdater.ApplyUpdate() works without a debugger. ║");
			Console.WriteLine("║  Deltas can be generated programmatically with Roslyn    ║");
			Console.WriteLine("║  and applied at runtime. Network delivery is viable.     ║");
			Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
			return 0;
		}
		else
		{
			Console.WriteLine($"  ✗ Expected \"v2\" but got \"{v2Result}\"");
			Console.WriteLine("    ApplyUpdate succeeded but the change is not visible.");
			Console.WriteLine("    This may mean currently-executing methods keep old IL.");
			return 5;
		}
	}

	/// <summary>
	/// Compiles C# source into a PE + PDB byte array pair using Roslyn.
	/// Returns the compilation object for later delta generation.
	/// </summary>
	static (byte[] pe, byte[] pdb, CSharpCompilation compilation) CompileAssembly(
		string assemblyName, string source)
	{
		var syntaxTree = CSharpSyntaxTree.ParseText(source);

		// Reference the same runtime assemblies this process uses
		var references = AppDomain.CurrentDomain.GetAssemblies()
			.Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
			.Select(a => MetadataReference.CreateFromFile(a.Location))
			.Cast<MetadataReference>()
			.ToList();

		// Ensure System.Runtime is referenced
		var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
		var systemRuntime = Path.Combine(runtimeDir, "System.Runtime.dll");
		if (File.Exists(systemRuntime))
			references.Add(MetadataReference.CreateFromFile(systemRuntime));

		var compilation = CSharpCompilation.Create(
			assemblyName,
			new[] { syntaxTree },
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		var peStream = new MemoryStream();
		var pdbStream = new MemoryStream();

		var result = compilation.Emit(peStream, pdbStream,
			options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

		if (!result.Success)
		{
			var errors = result.Diagnostics
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.Select(d => d.ToString());
			throw new InvalidOperationException(
				$"Compilation failed:\n{string.Join("\n", errors)}");
		}

		return (peStream.ToArray(), pdbStream.ToArray(), compilation);
	}

	/// <summary>
	/// Generates EnC metadata + IL deltas between v1 (already compiled) and v2 source.
	/// Uses Roslyn's EmitDifference API — the same mechanism dotnet-watch uses.
	/// </summary>
	static (byte[] metadataDelta, byte[] ilDelta, byte[] pdbDelta) GenerateDeltas(
		byte[] v1PeBytes, byte[] v1PdbBytes,
		CSharpCompilation v1Compilation, string v2Source)
	{
		// Create the baseline from v1 compilation + module metadata
		var moduleMetadata = ModuleMetadata.CreateFromImage(v1PeBytes);
		var baseline = EmitBaseline.CreateInitialBaseline(
			v1Compilation,
			moduleMetadata,
			handle => default,                 // debug info provider
			handle => default,                 // local signature provider
			hasPortableDebugInformation: true);

		// Parse v2 source
		var v2SyntaxTree = CSharpSyntaxTree.ParseText(v2Source);

		// Create v2 compilation with same references and options
		var v2Compilation = v1Compilation
			.RemoveAllSyntaxTrees()
			.AddSyntaxTrees(v2SyntaxTree);

		// Find the method that changed (GetMessage)
		var v1Tree = v1Compilation.SyntaxTrees.Single();
		var v2Tree = v2Compilation.SyntaxTrees.Single();

		var v1Model = v1Compilation.GetSemanticModel(v1Tree);
		var v2Model = v2Compilation.GetSemanticModel(v2Tree);

		// Find GetMessage method in both trees
		var v1Method = FindMethodSymbol(v1Model, v1Tree, "GetMessage");
		var v2Method = FindMethodSymbol(v2Model, v2Tree, "GetMessage");

		// Create the semantic edit: method body update
		var edits = new[]
		{
			new SemanticEdit(
				SemanticEditKind.Update,
				v1Method,
				v2Method)
		};

		// Generate the deltas
		var metadataStream = new MemoryStream();
		var ilStream = new MemoryStream();
		var pdbStream = new MemoryStream();
		var updatedMethods = new List<System.Reflection.Metadata.MethodDefinitionHandle>();

		var emitResult = v2Compilation.EmitDifference(
			baseline,
			edits,
			s => false,   // isAddedSymbol: no symbols added, only updated
			metadataStream,
			ilStream,
			pdbStream);

		if (!emitResult.Success)
		{
			var errors = emitResult.Diagnostics
				.Where(d => d.Severity == DiagnosticSeverity.Error)
				.Select(d => d.ToString());
			throw new InvalidOperationException(
				$"EmitDifference failed:\n{string.Join("\n", errors)}");
		}

		Console.WriteLine($"  Updated methods: {emitResult.UpdatedMethods.Length}");

		return (metadataStream.ToArray(), ilStream.ToArray(), pdbStream.ToArray());
	}

	/// <summary>
	/// Finds a method symbol by name in a syntax tree, walking the semantic model.
	/// </summary>
	static IMethodSymbol FindMethodSymbol(
		SemanticModel model, SyntaxTree tree, string methodName)
	{
		var root = tree.GetRoot();
		var methodDecl = root.DescendantNodes()
			.OfType<MethodDeclarationSyntax>()
			.Single(m => m.Identifier.Text == methodName);

		return (IMethodSymbol)model.GetDeclaredSymbol(methodDecl)!;
	}
}
