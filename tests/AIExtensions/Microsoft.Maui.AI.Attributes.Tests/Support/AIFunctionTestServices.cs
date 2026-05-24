using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

internal sealed class TestToolService
{
    [Description("A test tool")]
    [ExportAIFunction("test_tool")]
    public string DoSomething([Description("input value")] string input) => $"result: {input}";

    [ExportAIFunction]
    public int GetCount() => 42;

    [Description("An async tool")]
    [ExportAIFunction("async_tool")]
    public async Task<string> DoAsyncWork([Description("input value")] string input)
    {
        await Task.Delay(1);
        return $"async: {input}";
    }

    public void InternalMethod() { }
}

internal sealed class MultiParamService
{
    [Description("A tool with multiple parameters")]
    [ExportAIFunction("multi_param")]
    public string Combine(
        [Description("first name")] string firstName,
        [Description("last name")] string lastName,
        [Description("age in years")] int age)
        => $"{firstName} {lastName}, age {age}";
}

internal sealed class DisposableToolService : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;

    [Description("Tool on a disposable service")]
    [ExportAIFunction("disposable_tool")]
    public string GetValue() => "value";
}

[Description("Service-level description")]
internal sealed class DescriptionFallbackService
{
    [ExportAIFunction("fallback_desc")]
    [Description("Method-level description from DescriptionAttribute")]
    public string Work() => "done";
}

internal sealed class NoAttributeService
{
    public string DoWork() => "no attribute";
}

internal abstract class AbstractService
{
    [ExportAIFunction("abstract_tool")]
    public string DoWork() => "abstract";
}

internal sealed class InvocationCounterService
{
    public int InvocationCount { get; private set; }

    [Description("Counts invocations")]
    [ExportAIFunction("counter_tool")]
    public int Increment()
    {
        InvocationCount++;
        return InvocationCount;
    }
}

internal sealed class CancellableToolService
{
    [Description("A cancellable tool")]
    [ExportAIFunction("cancellable_tool")]
    public async Task<string> CancellableWork(
        [Description("input value")] string input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(1, cancellationToken);
        return $"done: {input}";
    }
}

internal sealed class GenericMethodService
{
    [ExportAIFunction("bad_generic")]
    public T GenericMethod<T>() => default!;
}

internal sealed class RefParameterService
{
    [ExportAIFunction("bad_ref")]
    public void RefMethod(ref string x) { }
}

internal sealed class ApprovalMixedService
{
    [Description("A safe read-only tool")]
    [ExportAIFunction("safe_read")]
    public string ReadData() => "data";

    [Description("A dangerous write tool")]
    [ExportAIFunction("dangerous_write", ApprovalRequired = true)]
    public string WriteData([Description("data to write")] string data) => $"wrote: {data}";

    [Description("Another safe tool")]
    [ExportAIFunction("another_safe")]
    public int GetCount() => 1;
}

internal sealed class AllApprovalService
{
    [Description("Needs approval")]
    [ExportAIFunction("needs_approval", ApprovalRequired = true)]
    public string DoWork() => "done";
}

internal sealed class ComplexPlantRequest
{
    [Description("friendly nickname shown to the user")]
    public string Nickname { get; set; } = string.Empty;

    [Description("botanical species or variety")]
    public string Species { get; set; } = string.Empty;

    [Description("current location of the plant")]
    public string Location { get; set; } = string.Empty;

    [Description("whether the plant lives indoors")]
    public bool IsIndoor { get; set; }
}

internal sealed class PlantToolResult
{
    [Description("stable identifier returned to the AI")]
    public string Id { get; set; } = string.Empty;

    [Description("nickname echoed back to the AI")]
    public string Nickname { get; set; } = string.Empty;
}

internal sealed class ComplexSchemaService
{
    [Description("Creates a plant profile from structured details.")]
    [ExportAIFunction("create_plant_profile", ApprovalRequired = true)]
    public PlantToolResult CreatePlantProfile(
        [Description("structured details for the plant profile")] ComplexPlantRequest profile,
        [Description("whether to notify the user after creation")] bool notifyUser = true) =>
        new()
        {
            Id = "plant-123",
            Nickname = profile.Nickname,
        };
}

// --- Static class with static methods (no DI needed) ---

internal static class StaticMathService
{
    [ExportAIFunction("add_numbers")]
    [Description("Adds two integers.")]
    public static int Add([Description("first")] int a, [Description("second")] int b) => a + b;

    [ExportAIFunction("negate_number")]
    public static int Negate(int value) => -value;
}

// --- Non-static class with both static and instance methods ---

internal sealed class MixedStaticInstanceService
{
    public int CallCount { get; private set; }

    [ExportAIFunction("static_echo")]
    public static string StaticEcho(string message) => $"static:{message}";

    [ExportAIFunction("instance_echo")]
    public string InstanceEcho(string message)
    {
        CallCount++;
        return $"instance:{message}";
    }
}

// --- Interface with [ExportAIFunction] on the interface ---

internal interface IOrderArchiveService
{
    [ExportAIFunction("list_orders")]
    [Description("Lists all past orders.")]
    IReadOnlyList<string> ListOrders();

    [ExportAIFunction("find_order")]
    string FindOrder([Description("order ID")] string orderId);
}

internal sealed class InMemoryOrderArchiveService : IOrderArchiveService
{
    private readonly List<string> _orders = new() { "order-1", "order-2", "order-3" };

    public IReadOnlyList<string> ListOrders() => _orders;

    public string FindOrder(string orderId) =>
        _orders.FirstOrDefault(o => o == orderId) ?? "not found";
}

// --- Interface with [FromServices] ---

internal interface IFoo { string Name { get; } }
internal sealed class FooImpl : IFoo { public string Name => "foo-impl"; }

internal interface IBarService
{
    [ExportAIFunction("bar_action")]
    string DoBar(string input, [FromServices] IFoo foo);
}

internal sealed class BarServiceImpl : IBarService
{
    public string DoBar(string input, IFoo foo) => $"{input}:{foo.Name}";
}

// --- Interface with property ---

internal interface ICatalogService
{
    [ExportAIFunction("all_products")]
    [Description("All products.")]
    IReadOnlyList<string> Products { get; }
}

internal sealed class CatalogServiceImpl : ICatalogService
{
    public IReadOnlyList<string> Products { get; } = new[] { "apple", "banana", "cherry" };
}

// --- Interface with ApprovalRequired ---

internal interface IDangerService
{
    [ExportAIFunction("safe_op")]
    string SafeOp();

    [ExportAIFunction("danger_op", ApprovalRequired = true)]
    string DangerOp();
}

internal sealed class DangerServiceImpl : IDangerService
{
    public string SafeOp() => "safe";
    public string DangerOp() => "danger";
}

// --- Enum parameter service ---
internal enum Priority { Low, Medium, High }

internal sealed class EnumParamService
{
    [ExportAIFunction("set_priority")]
    [Description("Sets the priority level.")]
    public string SetPriority([Description("priority level")] Priority level) => $"set to {level}";

    [ExportAIFunction("get_priority")]
    public Priority GetPriority() => Priority.Medium;
}

// --- Collection parameter service ---
internal sealed class CollectionParamService
{
    [ExportAIFunction("process_tags")]
    [Description("Processes a list of tags.")]
    public int ProcessTags([Description("tags to process")] List<string> tags) => tags.Count;

    [ExportAIFunction("merge_maps")]
    public Dictionary<string, int> MergeMaps(
        [Description("first map")] Dictionary<string, int> a,
        [Description("second map")] Dictionary<string, int> b)
    {
        var result = new Dictionary<string, int>(a);
        foreach (var kv in b) result[kv.Key] = kv.Value;
        return result;
    }

    [ExportAIFunction("find_items")]
    public List<string> FindItems(string? query = null)
        => query is null ? new() { "all" } : new() { query };
}
