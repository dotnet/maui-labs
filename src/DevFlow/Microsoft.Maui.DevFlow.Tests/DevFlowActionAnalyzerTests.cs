using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Maui.DevFlow.Analyzers;

namespace Microsoft.Maui.DevFlow.Tests;

public class DevFlowActionAnalyzerTests
{
	// Minimal attribute definitions appended to test source so the analyzer
	// can resolve them without referencing the real Agent.Core assembly.
	private const string AttributeStubs = """

		namespace Microsoft.Maui.DevFlow.Agent.Core
		{
			[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = false)]
			public sealed class DevFlowActionAttribute : System.Attribute
			{
				public string Name { get; }
				public string? Description { get; set; }
				public DevFlowActionAttribute(string name) { Name = name; }
			}
		}
		""";

	private static CSharpAnalyzerTest<DevFlowActionAnalyzer, DefaultVerifier> CreateTest(
		string source,
		params DiagnosticResult[] expected)
	{
		var test = new CSharpAnalyzerTest<DevFlowActionAnalyzer, DefaultVerifier>
		{
			TestCode = source + AttributeStubs,
			ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
		};
		test.ExpectedDiagnostics.AddRange(expected);
		return test;
	}

	[Fact]
	public async Task ValidAction_NoDiagnostics()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("my-action")]
				public static void MyAction([Description("a param")] string name) { }
			}
			""";

		await CreateTest(source).RunAsync();
	}

	[Fact]
	public async Task DFA001_UnsupportedParamType_ReportsDiagnostic()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("bad-param")]
				public static void BadParam([Description("an object")] object {|#0:val|}) { }
			}
			""";

		var expected = new DiagnosticResult("MAUI_DFA001", DiagnosticSeverity.Error)
			.WithLocation(0)
			.WithArguments("val", "object");

		await CreateTest(source, expected).RunAsync();
	}

	[Fact]
	public async Task DFA002_PrivateMethod_ReportsDiagnostic()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("private-action")]
				private static void {|#0:PrivateAction|}() { }
			}
			""";

		var expected = new DiagnosticResult("MAUI_DFA002", DiagnosticSeverity.Error)
			.WithLocation(0)
			.WithArguments("PrivateAction");

		await CreateTest(source, expected).RunAsync();
	}

	[Fact]
	public async Task DFA002_NonStaticMethod_ReportsDiagnostic()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public class Actions
			{
				[DevFlowAction("instance-action")]
				public void {|#0:InstanceAction|}() { }
			}
			""";

		var expected = new DiagnosticResult("MAUI_DFA002", DiagnosticSeverity.Error)
			.WithLocation(0)
			.WithArguments("InstanceAction");

		await CreateTest(source, expected).RunAsync();
	}

	[Fact]
	public async Task DFA003_ComplexReturnType_ReportsWarning()
	{
		const string source = """
			using System.Collections.Generic;
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("complex-return")]
				public static List<string> {|#0:ComplexReturn|}() => new();
			}
			""";

		var expected = new DiagnosticResult("MAUI_DFA003", DiagnosticSeverity.Warning)
			.WithLocation(0)
			.WithArguments("System.Collections.Generic.List<string>");

		await CreateTest(source, expected).RunAsync();
	}

	[Fact]
	public async Task DFA004_MissingDescription_ReportsInfo()
	{
		const string source = """
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("no-desc")]
				public static void NoDesc(string {|#0:name|}) { }
			}
			""";

		var expected = new DiagnosticResult("MAUI_DFA004", DiagnosticSeverity.Info)
			.WithLocation(0)
			.WithArguments("name");

		await CreateTest(source, expected).RunAsync();
	}

	[Fact]
	public async Task DFA005_DuplicateActionName_ReportsWarning()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("do-thing")]
				public static void {|#0:DoThing|}([Description("x")] string x) { }

				[DevFlowAction("do-thing")]
				public static void {|#1:DoThingAlso|}([Description("y")] string y) { }
			}
			""";

		var expected1 = new DiagnosticResult("MAUI_DFA005", DiagnosticSeverity.Warning)
			.WithLocation(0)
			.WithArguments("do-thing", "Actions.DoThingAlso");

		var expected2 = new DiagnosticResult("MAUI_DFA005", DiagnosticSeverity.Warning)
			.WithLocation(1)
			.WithArguments("do-thing", "Actions.DoThing");

		await CreateTest(source, expected1, expected2).RunAsync();
	}

	[Fact]
	public async Task DFA005_DuplicateAcrossClasses_ReportsWarning()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class ActionsA
			{
				[DevFlowAction("shared-name")]
				public static void {|#0:DoA|}([Description("x")] string x) { }
			}

			public static class ActionsB
			{
				[DevFlowAction("shared-name")]
				public static void {|#1:DoB|}([Description("y")] string y) { }
			}
			""";

		var expected1 = new DiagnosticResult("MAUI_DFA005", DiagnosticSeverity.Warning)
			.WithLocation(0)
			.WithArguments("shared-name", "ActionsB.DoB");

		var expected2 = new DiagnosticResult("MAUI_DFA005", DiagnosticSeverity.Warning)
			.WithLocation(1)
			.WithArguments("shared-name", "ActionsA.DoA");

		await CreateTest(source, expected1, expected2).RunAsync();
	}

	[Fact]
	public async Task UniqueNames_NoDFA005()
	{
		const string source = """
			using System.ComponentModel;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("action-one")]
				public static void ActionOne([Description("x")] string x) { }

				[DevFlowAction("action-two")]
				public static void ActionTwo([Description("y")] string y) { }
			}
			""";

		await CreateTest(source).RunAsync();
	}

	[Fact]
	public async Task ValidAction_TaskReturn_NoDiagnostics()
	{
		const string source = """
			using System.ComponentModel;
			using System.Threading.Tasks;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("async-action")]
				public static Task AsyncAction([Description("x")] string x) => Task.CompletedTask;
			}
			""";

		await CreateTest(source).RunAsync();
	}

	[Fact]
	public async Task ValidAction_TaskOfStringReturn_NoDiagnostics()
	{
		const string source = """
			using System.ComponentModel;
			using System.Threading.Tasks;
			using Microsoft.Maui.DevFlow.Agent.Core;

			public static class Actions
			{
				[DevFlowAction("async-string-action")]
				public static Task<string> AsyncStringAction([Description("x")] string x) => Task.FromResult("ok");
			}
			""";

		await CreateTest(source).RunAsync();
	}
}
