using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.Maui.DevFlow.Tests;

public class BackgroundJobPlatformTests
{
    private static readonly string AgentPath = Path.Combine(TestRepo.Root,
        "src", "DevFlow", "Microsoft.Maui.DevFlow.Agent", "DevFlowAgentService.cs");
    private static readonly string SamplePath = Path.Combine(TestRepo.Root,
        "samples", "DevFlow.Sample", "Platforms", "iOS", "SampleBackgroundTask.cs");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrivateSelector_IsCompiledOnlyInDebug(bool debug)
    {
        foreach (var path in new[] { AgentPath, SamplePath })
        {
            var root = Parse(path, debug).GetRoot();
            var hasSelectorLiteral = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Any(literal => literal.Token.ValueText.Contains("_simulateLaunchForTaskWithIdentifier:", StringComparison.Ordinal));
            var hasPInvoke = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Any(method => method.Identifier.ValueText == "SendSimulateLaunch");
            Assert.Equal(debug, hasSelectorLiteral);
            Assert.Equal(debug, hasPInvoke);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IosRun_DebugAcknowledgesLaunch_ReleaseDoesNotSchedule(bool debug)
    {
        // Compile the actual platform method against a scheduler that immediately reschedules
        // from its handler, so this regression runs without physical iOS hardware.
        var method = Parse(AgentPath, debug).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == "RunPlatformJobAsync");
        var statements = string.Join("\n", method.Body!.Statements.Select(s => s.WithoutTrivia().ToFullString()));
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class Base
            {
                protected virtual Task<object?> RunPlatformJobAsync(string identifier, string? type = null)
                    => Task.FromResult<object?>(null);
            }
            public class Probe : Base
            {
                public static int PendingReads;
                public static int Executions;
                public static bool Pending = true;
                public Task<object?> Run() => RunPlatformJobAsync("com.example.sync");
                protected override async Task<object?> RunPlatformJobAsync(string identifier, string? type = null)
                {
                    {{statements}}
                }
                private static Task<string> ResolveBgTaskRequestTypeAsync(string identifier, string? type)
                    => Task.FromResult("processing");
                private static Task<bool> IsBgTaskPendingAsync(string identifier)
                {
                    PendingReads++;
                    return Task.FromResult(Pending);
                }
                private static bool TrySimulateBgTaskLaunch(string identifier, out string? error)
                {
                    Executions++;
                    Pending = true;
                    error = null;
                    return true;
                }
                private static string DescribeBgTaskSchedulerError(NSError error, string identifier) => "";
            }
            public class NSError { public string LocalizedDescription => "error"; }
            public class BGTaskRequest { public object? EarliestBeginDate { get; set; } }
            public class BGProcessingTaskRequest(string id) : BGTaskRequest { }
            public class BGAppRefreshTaskRequest(string id) : BGTaskRequest { }
            public class BGTaskScheduler
            {
                public static int Submissions;
                public static BGTaskScheduler Shared = new();
                public void Submit(BGTaskRequest request, out NSError? error) { Submissions++; error = null; }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("BackgroundJobProbe_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var assemblyStream = new MemoryStream();
        var emitted = compilation.Emit(assemblyStream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

        var assembly = Assembly.Load(assemblyStream.ToArray());
        var type = assembly.GetType("Probe")!;
        var probe = Activator.CreateInstance(type)!;
        var value = await (Task<object?>)type.GetMethod("Run")!.Invoke(probe, null)!;
        Assert.Equal(0, assembly.GetType("BGTaskScheduler")!.GetField("Submissions")!.GetValue(null));
        if (!debug)
        {
            Assert.Null(value);
            Assert.Equal(0, type.GetField("PendingReads")!.GetValue(null));
            Assert.Equal(0, type.GetField("Executions")!.GetValue(null));
            return;
        }
        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(true, result["success"]);
        Assert.Equal(true, result["launchRequested"]);
        Assert.Equal(false, result["executionVerified"]);
        Assert.Equal(false, result["completionVerified"]);
        Assert.False(result.ContainsKey("launched"));
        Assert.False(result.ContainsKey("consumedPendingRequest"));
        Assert.Equal(1, type.GetField("PendingReads")!.GetValue(null));
        Assert.Equal(1, type.GetField("Executions")!.GetValue(null));
        Assert.Equal(true, type.GetField("Pending")!.GetValue(null));
    }

    private static SyntaxTree Parse(string path, bool debug) => CSharpSyntaxTree.ParseText(
        File.ReadAllText(path), new CSharpParseOptions(preprocessorSymbols: debug ? ["IOS", "DEBUG"] : ["IOS"]));
}
