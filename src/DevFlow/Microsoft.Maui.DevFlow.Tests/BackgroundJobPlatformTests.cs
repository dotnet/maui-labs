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
        var probe = new BackgroundJobProbe(debug, pending: true, appleVersionAtLeast27: true);

        var value = await probe.Run();

        Assert.Equal(0, probe.CallbackSubmissions);
        Assert.Equal(0, probe.SynchronousSubmissions);
        if (!debug)
        {
            Assert.Null(value);
            Assert.Equal(0, probe.PendingReads);
            Assert.Equal(0, probe.Executions);
            return;
        }
        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(true, result["success"]);
        Assert.Equal(true, result["launchRequested"]);
        Assert.Equal(false, result["executionVerified"]);
        Assert.Equal(false, result["completionVerified"]);
        Assert.False(result.ContainsKey("launched"));
        Assert.False(result.ContainsKey("consumedPendingRequest"));
        Assert.Equal(1, probe.PendingReads);
        Assert.Equal(1, probe.Executions);
        Assert.True(probe.Pending);
    }

    [Fact]
    public async Task IosRun_Apple27_WaitsForSuccessfulSubmissionCallbackBeforeLaunch()
    {
        var probe = new BackgroundJobProbe(debug: true, pending: false, appleVersionAtLeast27: true);

        var run = probe.Run();

        // The completion-handler form has been called, but the scheduler has not answered yet.
        Assert.Equal(1, probe.CallbackSubmissions);
        Assert.Equal(0, probe.SynchronousSubmissions);
        Assert.False(run.IsCompleted);
        Assert.Equal(0, probe.Executions);

        probe.CompleteSubmission(error: null);
        var value = await run;

        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(true, result["success"]);
        Assert.Equal(true, result["launchRequested"]);
        Assert.Equal("processing", result["type"]);
        Assert.Equal(1, probe.PendingReads);
        Assert.Equal(1, probe.Executions);
    }

    [Fact]
    public async Task IosRun_Apple27_DoesNotLaunchAfterSubmissionError()
    {
        var probe = new BackgroundJobProbe(debug: true, pending: false, appleVersionAtLeast27: true);

        var run = probe.Run();
        Assert.False(run.IsCompleted);

        probe.CompleteSubmission(error: "Not permitted");
        var value = await run;

        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(false, result["success"]);
        Assert.Equal("Could not schedule BGTask 'com.example.sync': Not permitted", result["error"]);
        Assert.False(result.ContainsKey("launchRequested"));
        Assert.Equal(1, probe.CallbackSubmissions);
        Assert.Equal(0, probe.SynchronousSubmissions);
        Assert.Equal(0, probe.Executions);
    }

    [Fact]
    public async Task IosRun_BeforeApple27_UsesSynchronousSubmissionThenLaunches()
    {
        var probe = new BackgroundJobProbe(debug: true, pending: false, appleVersionAtLeast27: false);

        var value = await probe.Run();

        var result = Assert.IsType<Dictionary<string, object?>>(value);
        Assert.Equal(true, result["success"]);
        Assert.Equal(0, probe.CallbackSubmissions);
        Assert.Equal(1, probe.SynchronousSubmissions);
        Assert.Equal(1, probe.Executions);
    }

    private static SyntaxTree Parse(string path, bool debug) => CSharpSyntaxTree.ParseText(
        File.ReadAllText(path), new CSharpParseOptions(preprocessorSymbols: debug ? ["IOS", "DEBUG"] : ["IOS"]));

    /// <summary>
    /// Compiles the production <c>RunPlatformJobAsync</c> body and <c>SubmitBgTaskRequestAsync</c>
    /// helper against stand-in Apple types, so the launch flow can be driven without iOS hardware.
    /// The OS version check and the scheduler's completion callback are both under test control.
    /// </summary>
    private sealed class BackgroundJobProbe
    {
        private readonly Type _probeType;
        private readonly Type _schedulerType;
        private readonly object _probe;

        public BackgroundJobProbe(bool debug, bool pending, bool appleVersionAtLeast27)
        {
            var methods = Parse(AgentPath, debug).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
            var runMethod = methods.Single(m => m.Identifier.ValueText == "RunPlatformJobAsync");
            var statements = string.Join("\n", runMethod.Body!.Statements.Select(s => s.WithoutTrivia().ToFullString()));
            var submitHelper = methods.Single(m => m.Identifier.ValueText == "SubmitBgTaskRequestAsync").WithoutTrivia().ToFullString();
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
                    public static bool Pending;
                    public Task<object?> Run() => RunPlatformJobAsync("com.example.sync");
                    protected override async Task<object?> RunPlatformJobAsync(string identifier, string? type = null)
                    {
                        {{statements}}
                    }
                    {{submitHelper}}
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
                // Shadows System.OperatingSystem for the production helper's version checks.
                public static class OperatingSystem
                {
                    public static bool AppleVersionAtLeast27;
                    public static bool IsIOSVersionAtLeast(int major) => AppleVersionAtLeast27;
                    public static bool IsMacCatalystVersionAtLeast(int major) => false;
                }
                public class NSError(string description) { public string LocalizedDescription => description; }
                public class BGTaskRequest { public object? EarliestBeginDate { get; set; } }
                public class BGProcessingTaskRequest(string id) : BGTaskRequest { }
                public class BGAppRefreshTaskRequest(string id) : BGTaskRequest { }
                public class BGTaskScheduler
                {
                    public static int SynchronousSubmissions;
                    public static int CallbackSubmissions;
                    public static Action<NSError?>? PendingCompletion;
                    public static BGTaskScheduler Shared = new();
                    public void Submit(BGTaskRequest request, out NSError? error) { SynchronousSubmissions++; error = null; }
                    public void Submit(BGTaskRequest request, Action<NSError?> completion)
                    {
                        CallbackSubmissions++;
                        PendingCompletion = completion;
                    }
                    public static void Complete(string? error) => PendingCompletion!(error is null ? null : new NSError(error));
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
            _probeType = assembly.GetType("Probe")!;
            _schedulerType = assembly.GetType("BGTaskScheduler")!;
            _probeType.GetField("Pending")!.SetValue(null, pending);
            assembly.GetType("OperatingSystem")!.GetField("AppleVersionAtLeast27")!.SetValue(null, appleVersionAtLeast27);
            _probe = Activator.CreateInstance(_probeType)!;
        }

        public int PendingReads => (int)_probeType.GetField("PendingReads")!.GetValue(null)!;
        public int Executions => (int)_probeType.GetField("Executions")!.GetValue(null)!;
        public bool Pending => (bool)_probeType.GetField("Pending")!.GetValue(null)!;
        public int SynchronousSubmissions => (int)_schedulerType.GetField("SynchronousSubmissions")!.GetValue(null)!;
        public int CallbackSubmissions => (int)_schedulerType.GetField("CallbackSubmissions")!.GetValue(null)!;

        public Task<object?> Run() => (Task<object?>)_probeType.GetMethod("Run")!.Invoke(_probe, null)!;

        public void CompleteSubmission(string? error) => _schedulerType.GetMethod("Complete")!.Invoke(null, [error]);
    }
}
