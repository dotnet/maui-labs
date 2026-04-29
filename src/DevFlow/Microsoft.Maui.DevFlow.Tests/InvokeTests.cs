using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.DevFlow.Tests;

public class InvokeTests
{
	[Fact]
	public async Task ListActions_DiscoversMethods_WithDevFlowActionAttribute()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var actions = await harness.Client.ListActionsAsync();

		Assert.Equal(JsonValueKind.Object, actions.ValueKind);
		var actionsArray = actions.GetProperty("actions");
		Assert.Equal(JsonValueKind.Array, actionsArray.ValueKind);

		var testAction = actionsArray.EnumerateArray()
			.FirstOrDefault(a => a.GetProperty("name").GetString() == "test-greet");

		Assert.NotEqual(default, testAction);
		Assert.Equal("Returns a greeting for the given name", testAction.GetProperty("description").GetString());

		var nameParam = testAction.GetProperty("parameters").EnumerateArray().First();
		Assert.Equal("name", nameParam.GetProperty("name").GetString());
		Assert.Equal("string", nameParam.GetProperty("type").GetString());
		Assert.Equal("The name to greet", nameParam.GetProperty("description").GetString());
		Assert.Equal("Friend", nameParam.GetProperty("defaultValue").GetString());
		Assert.False(nameParam.GetProperty("isRequired").GetBoolean());
	}

	[Fact]
	public async Task InvokeAction_CallsRegisteredAction_ReturnsResult()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("test-greet",
			JsonArray(JsonElement("World")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("Hello, World!", result.ReturnValue);
		Assert.Equal("test-greet", result.Action);
	}

	[Fact]
	public async Task InvokeAction_WithDefaultParameters_UsesDefaults()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("test-greet");

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("Hello, Friend!", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_NotFound_ReturnsError()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync("nonexistent-action");

		Assert.NotNull(result);
		Assert.False(result.Success);
		Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task InvokeAction_CallsAsyncMethod_AwaitsResult()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-async",
			JsonArray(JsonElement("test-value")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("async:test-value", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_CallsValueTaskMethod_AwaitsResult()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-value-task",
			JsonArray(JsonElement("test-value")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("value-task:test-value", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_CallsVoidMethod_ReturnsVoid()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		TestInvokeHelpers.LastSideEffect = null;
		var result = await harness.Client.InvokeActionAsync(
			"test-side-effect",
			JsonArray(JsonElement("done")));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("done", TestInvokeHelpers.LastSideEffect);
		Assert.Equal("void", result.ReturnType);
	}

	[Fact]
	public async Task InvokeAction_WithBoolParameter_ConvertsCorrectly()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-bool",
			JsonArray(JsonElement(true)));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("True", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithArrayParameter_ConvertsJsonArray()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-join-numbers",
			JsonArray(JsonElement(new[] { 1, 2, 3 })));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("1,2,3", result.ReturnValue);
	}

	[Theory]
	[InlineData("High", "High")]
	[InlineData("medium", "Medium")]
	public async Task InvokeAction_WithEnumParameter_ConvertsStringToEnum(string input, string expected)
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-priority",
			JsonArray(JsonElement(input)));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal(expected, result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithNullableParameter_PassesValue()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-nullable",
			JsonArray(JsonElement(42)));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("42", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithNullableParameter_PassesNull()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.InvokeActionAsync(
			"test-nullable",
			JsonArray(JsonElement<int?>(null)));

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("null", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_DispatchesInvocationToUiThread()
	{
		using var harness = await InvokeTestHarness.CreateWithDispatcherAsync(new DispatchRequiredDispatcher());

		TestInvokeHelpers.ResetDispatchState();
		var result = await harness.Client.InvokeActionAsync("test-dispatch-state");

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("dispatched", result.ReturnValue);
		Assert.True(TestInvokeHelpers.DispatchCallCount > 0);
	}

	[Fact]
	public async Task InvokeAction_WithMcpStyleJsonArgs_ParsesAndInvokes()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var args = ParseMcpArgsJson("[\"World\"]");
		var result = await harness.Client.InvokeActionAsync("test-greet", args);

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("Hello, World!", result.ReturnValue);
	}

	[Fact]
	public async Task InvokeAction_WithMcpStyleMixedTypeArgs_ParsesCorrectly()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var args = ParseMcpArgsJson("[10, 20]");
		var result = await harness.Client.InvokeActionAsync("test-add", args);

		Assert.NotNull(result);
		Assert.True(result.Success, result.Error);
		Assert.Equal("30", result.ReturnValue);
	}

	[Fact]
	public async Task Batch_WithInvokeActionFailure_StopsAndReportsFailure()
	{
		using var harness = await InvokeTestHarness.CreateAsync();
		TestInvokeHelpers.LastSideEffect = null;

		var result = await harness.Client.BatchAsync(
			[
				new JsonObject
				{
					["action"] = "invoke-action",
					["name"] = "nonexistent-action"
				},
				new JsonObject
				{
					["action"] = "invoke-action",
					["name"] = "test-side-effect",
					["args"] = JsonArray(JsonElement("should-not-run"))
				}
			],
			continueOnError: false);

		Assert.False(result.GetProperty("success").GetBoolean());
		var onlyResult = Assert.Single(result.GetProperty("results").EnumerateArray());
		Assert.False(onlyResult.GetProperty("success").GetBoolean());
		Assert.Equal(400, onlyResult.GetProperty("statusCode").GetInt32());
		Assert.Null(TestInvokeHelpers.LastSideEffect);
	}

	[Fact]
	public async Task Batch_WithGenericInvoke_ReturnsUnsupportedAction()
	{
		using var harness = await InvokeTestHarness.CreateAsync();

		var result = await harness.Client.BatchAsync(
			[
				new JsonObject
				{
					["action"] = "invoke",
					["typeName"] = typeof(TestInvokeHelpers).FullName,
					["methodName"] = "Greet"
				}
			],
			continueOnError: false);

		Assert.False(result.GetProperty("success").GetBoolean());
		var onlyResult = Assert.Single(result.GetProperty("results").EnumerateArray());
		Assert.False(onlyResult.GetProperty("success").GetBoolean());
		Assert.Contains("Unsupported batch action", onlyResult.GetProperty("response").GetString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void InvalidateActionCache_ReplacesCachedActionList()
	{
		var cacheField = typeof(DevFlowAgentService).GetField("s_cachedActions", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(cacheField);
		var before = cacheField.GetValue(null);

		var invalidateMethod = typeof(DevFlowAgentService).GetMethod("InvalidateActionCache", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(invalidateMethod);
		invalidateMethod.Invoke(null, null);

		var after = cacheField.GetValue(null);
		Assert.NotSame(before, after);
	}

	#region Helpers

	private static JsonArray JsonArray(params JsonElement[] elements)
	{
		var arr = new JsonArray();
		foreach (var e in elements)
			arr.Add(JsonNode.Parse(e.GetRawText()));
		return arr;
	}

	private static JsonElement JsonElement(object value)
	{
		var json = JsonSerializer.Serialize(value);
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	private static JsonElement JsonElement<T>(T value)
	{
		var json = JsonSerializer.Serialize(value);
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	private static JsonArray? ParseMcpArgsJson(string? argsJson)
	{
		if (string.IsNullOrWhiteSpace(argsJson))
			return null;
		return JsonNode.Parse(argsJson) as JsonArray;
	}

	#endregion

	#region Test Harness

	private sealed class InvokeTestHarness : IDisposable
	{
		private readonly DevFlowAgentService _service;
		public AgentClient Client { get; }

		private InvokeTestHarness(DevFlowAgentService service, AgentClient client)
		{
			_service = service;
			Client = client;
		}

		public static async Task<InvokeTestHarness> CreateAsync()
			=> await CreateWithDispatcherAsync(new ImmediateDispatcher());

		public static async Task<InvokeTestHarness> CreateWithDispatcherAsync(IDispatcher dispatcher)
		{
			var app = new TestApplication();
			var service = new DevFlowAgentService(new AgentOptions { Port = GetFreePort() });
			var client = new AgentClient("localhost", service.Port);

			service.StartServerOnly(dispatcher);
			service.BindApp(app);

			for (var i = 0; i < 10; i++)
			{
				var status = await client.GetStatusAsync();
				if (status != null)
					return new InvokeTestHarness(service, client);
				await Task.Delay(100);
			}

			throw new InvalidOperationException("Agent did not start in time");
		}

		public void Dispose()
		{
			Client.Dispose();
			_service.Dispose();
		}

		private static int GetFreePort()
		{
			using var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			return ((IPEndPoint)listener.LocalEndpoint).Port;
		}
	}

	private sealed class ImmediateDispatcher : IDispatcher
	{
		public bool IsDispatchRequired => false;
		public bool Dispatch(Action action) { action(); return true; }
		public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
		public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
	}

	private sealed class DispatchRequiredDispatcher : IDispatcher
	{
		public bool IsDispatchRequired => true;

		public bool Dispatch(Action action)
		{
			TestInvokeHelpers.DispatchCallCount++;
			var wasDispatched = TestInvokeHelpers.IsDispatched;
			TestInvokeHelpers.IsDispatched = true;
			try
			{
				action();
			}
			finally
			{
				TestInvokeHelpers.IsDispatched = wasDispatched;
			}
			return true;
		}

		public bool DispatchDelayed(TimeSpan delay, Action action) => Dispatch(action);
		public IDispatcherTimer CreateTimer() => new ImmediateDispatcherTimer();
	}

	private sealed class ImmediateDispatcherTimer : IDispatcherTimer
	{
		public bool IsRepeating { get; set; }
		public TimeSpan Interval { get; set; }
		public bool IsRunning { get; private set; }
		public event EventHandler? Tick { add { } remove { } }
		public void Start() => IsRunning = true;
		public void Stop() => IsRunning = false;
	}

	private sealed class TestApplication : Application
	{
	}

	#endregion
}

#region Test Fixture Classes

public static class TestInvokeHelpers
{
	[ThreadStatic]
	public static bool IsDispatched;

	public static int DispatchCallCount { get; set; }
	public static string? LastSideEffect { get; set; }

	public static void ResetDispatchState()
	{
		IsDispatched = false;
		DispatchCallCount = 0;
	}

	[DevFlowAction("test-greet", Description = "Returns a greeting for the given name")]
	public static string Greet(
		[System.ComponentModel.Description("The name to greet")] string name = "Friend")
		=> $"Hello, {name}!";

	[DevFlowAction("test-add", Description = "Adds two numbers")]
	public static int Add(
		[System.ComponentModel.Description("First number")] int a,
		[System.ComponentModel.Description("Second number")] int b)
		=> a + b;

	[DevFlowAction("test-dispatch-state", Description = "Returns whether invocation is dispatched")]
	public static string GetActionDispatchState()
		=> IsDispatched ? "dispatched" : "not-dispatched";

	[DevFlowAction("test-async", Description = "Returns an async value")]
	public static Task<string> GetValueAsync(
		[System.ComponentModel.Description("Value key")] string key)
		=> Task.FromResult($"async:{key}");

	[DevFlowAction("test-value-task", Description = "Returns a ValueTask value")]
	public static async ValueTask<string> GetValueTaskAsync(
		[System.ComponentModel.Description("Value key")] string key)
	{
		await Task.Yield();
		return $"value-task:{key}";
	}

	[DevFlowAction("test-side-effect", Description = "Records a side effect")]
	public static void DoSideEffect(
		[System.ComponentModel.Description("Value to record")] string value)
		=> LastSideEffect = value;

	[DevFlowAction("test-bool", Description = "Formats a boolean value")]
	public static string IsEnabled(
		[System.ComponentModel.Description("Whether the feature is enabled")] bool enabled)
		=> enabled.ToString();

	[DevFlowAction("test-join-numbers", Description = "Joins numbers")]
	public static string JoinNumbers(
		[System.ComponentModel.Description("Numbers to join")] int[] numbers)
		=> string.Join(",", numbers);

	[DevFlowAction("test-priority", Description = "Formats priority")]
	public static string GetPriority(
		[System.ComponentModel.Description("Priority value")] Priority p)
		=> p.ToString();

	[DevFlowAction("test-nullable", Description = "Formats a nullable integer")]
	public static string FormatNullable(
		[System.ComponentModel.Description("Nullable value")] int? value)
		=> value.HasValue ? value.Value.ToString() : "null";
}

public enum Priority
{
	Low,
	Medium,
	High
}

#endregion
