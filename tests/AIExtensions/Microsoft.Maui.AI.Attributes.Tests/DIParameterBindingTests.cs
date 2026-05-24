using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

// Service dependencies ------------------------------------------------------

internal interface IAddressBook
{
    string Lookup(string name);
}

internal sealed class AddressBook : IAddressBook
{
    public string Lookup(string name) => $"addr:{name}";
}

internal sealed class WeatherProbe
{
    public string Read(string city) => $"weather:{city}";
}

internal abstract class TranslatorBase
{
    public abstract string Translate(string text);
}

internal sealed class EchoTranslator : TranslatorBase
{
    public override string Translate(string text) => $"echo:{text}";
}

// Services with [ExportAIFunction] ------------------------------------------

internal sealed class ContactsToolService
{
    [ExportAIFunction("from_services_tool")]
    public string Find([FromServices] IAddressBook book, string name) => book.Lookup(name);

    [ExportAIFunction("from_keyed_tool")]
    public string FindKeyed(
        [FromKeyedServices("primary")] IAddressBook book,
        string name) => book.Lookup(name);

    [ExportAIFunction("unannotated_interface_tool")]
    public string FindUnannotated(IAddressBook book, string name) => book.Lookup(name);

    [ExportAIFunction("abstract_from_services_tool")]
    public string Translate([FromServices] TranslatorBase t, string text) => t.Translate(text);
}

internal sealed class CounterState
{
    public int Count { get; set; }
}

internal enum DisplayMode
{
    Normal,
    Compact,
}

internal sealed class DisplayModeToolService
{
    public DisplayMode Mode
    {
        [ExportAIFunction("get_display_mode")]
        get;

        [ExportAIFunction("set_display_mode")]
        set;
    } = DisplayMode.Normal;
}

internal sealed class TransientCounterToolService(CounterState state)
{
    private static int _instanceNumber;
    private readonly int _instanceId = Interlocked.Increment(ref _instanceNumber);

    [ExportAIFunction("transient_increment_tool")]
    public string Increment(string name)
    {
        state.Count++;
        return $"instance:{_instanceId};count:{state.Count};name:{name}";
    }
}

[AIToolSource(typeof(ContactsToolService))]
internal partial class ContactsToolContext : AIToolContext { }

[AIToolSource(typeof(TransientCounterToolService))]
internal partial class TransientCounterToolContext : AIToolContext { }

[AIToolSource(typeof(DisplayModeToolService))]
internal partial class DisplayModeToolContext : AIToolContext { }

public class DIParameterBindingTests
{
    [Fact]
    public async Task FromServices_interface_parameter_is_excluded_from_schema()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAddressBook, AddressBook>();
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_services_tool");
        var schema = tool.JsonSchema.ToString();
        Assert.DoesNotContain("\"book\"", schema);
        Assert.Contains("\"name\"", schema);

        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "alice" }) { Services = provider });
        Assert.Equal("addr:alice", result?.ToString());
    }

    [Fact]
    public async Task FromServices_abstract_parameter_is_excluded_from_schema()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TranslatorBase, EchoTranslator>();
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "abstract_from_services_tool");
        Assert.DoesNotContain("\"t\"", tool.JsonSchema.ToString());

        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["text"] = "hi" }) { Services = provider });
        Assert.Equal("echo:hi", result?.ToString());
    }

    [Fact]
    public void Unannotated_interface_parameter_is_included_in_schema()
    {
        // No DI inference: an interface parameter without [FromServices] is treated as
        // a JSON-bound argument (same as reflection-based AIFunctionFactory).
        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "unannotated_interface_tool");
        Assert.Contains("\"book\"", tool.JsonSchema.ToString());
    }

    [Fact]
    public async Task FromKeyedServices_resolves_by_key()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IAddressBook, AddressBook>("primary");
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "carol" }) { Services = provider });
        Assert.Equal("addr:carol", result?.ToString());
    }

    [Fact]
    public async Task FromKeyedServices_with_missing_key_throws()
    {
        var services = new ServiceCollection();
        // Note: no keyed registration — only unkeyed.
        services.AddSingleton<IAddressBook, AddressBook>();
        services.AddSingleton<ContactsToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)ContactsToolContext.Default.Tools.First(t => t.Name == "from_keyed_tool");
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "x" }) { Services = provider }).AsTask());
    }

    [Fact]
    public async Task Transient_tool_source_can_write_through_to_singleton_state()
    {
        var services = new ServiceCollection();
        services.AddSingleton<CounterState>();
        services.AddTransient<TransientCounterToolService>();
        using var provider = services.BuildServiceProvider();

        var tool = (AIFunction)TransientCounterToolContext.Default.Tools.First(t => t.Name == "transient_increment_tool");

        var result1 = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "first" }) { Services = provider });
        var result2 = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["name"] = "second" }) { Services = provider });

        Assert.Contains("count:1", result1?.ToString());
        Assert.Contains("count:2", result2?.ToString());
        Assert.NotEqual(result1?.ToString(), result2?.ToString());
    }

    [Fact]
    public async Task Exported_property_setter_updates_the_service_state()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DisplayModeToolService>();
        using var provider = services.BuildServiceProvider();

        var setTool = (AIFunction)DisplayModeToolContext.Default.Tools.First(t => t.Name == "set_display_mode");
        var getTool = (AIFunction)DisplayModeToolContext.Default.Tools.First(t => t.Name == "get_display_mode");

        var result = await setTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["value"] = "compact" }) { Services = provider });
        var state = provider.GetRequiredService<DisplayModeToolService>();
        var readback = await getTool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()) { Services = provider });

        Assert.Equal(DisplayMode.Compact, state.Mode);
        Assert.Equal("Compact", result?.ToString());
        Assert.Equal("Compact", readback?.ToString());
    }
}
