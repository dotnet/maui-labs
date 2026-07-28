using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests.Equivalence;

// ---------------------------------------------------------------------------
// Services and tool contexts used by the M.E.AI equivalence suite.
// Each [ExportAIFunction] method below corresponds to a delegate form from
// Microsoft.Extensions.AI.Tests.Functions.AIFunctionFactoryTest.
// ---------------------------------------------------------------------------

internal sealed class KeyedMyService
{
    public int Value { get; }
    public KeyedMyService(int value) => Value = value;
}

internal sealed class EquivalenceToolService
{
    // --- Parameters_MappedByName_Async ---
    [ExportAIFunction("repeat_str")]
    public string RepeatStr(string a) => a + " " + a;

    [ExportAIFunction("concat_two")]
    public string ConcatTwo(string a, string b) => b + " " + a;

    [ExportAIFunction("add_numbers")]
    public long AddNumbers(int a, long b) => a + b;

    // --- Parameters_DefaultValuesAreUsedButOverridable_Async ---
    [ExportAIFunction("repeat_with_default")]
    public string RepeatWithDefault(string a = "test") => a + " " + a;

    // --- Parameters_MissingRequiredParametersFail_Async ---
    [ExportAIFunction("missing_string")]
    public string MissingString(string theParam) => theParam + " " + theParam;

    [ExportAIFunction("missing_nullable_string")]
    public string MissingNullableString(string? theParam) => theParam + " " + theParam;

    [ExportAIFunction("missing_int")]
    public int MissingInt(int theParam) => theParam * 2;

    [ExportAIFunction("missing_nullable_int")]
    public int? MissingNullableInt(int? theParam) => theParam * 2;

    // --- Parameters_ToleratesJsonEncodedParameters ---
    [ExportAIFunction("sum_five_ints")]
    public int SumFiveInts(int x, int y, int z, int w, int u) => x + y + z + w + u;

    // --- Parameters_ToleratesJsonStringParameters / InvalidJsonStringParameters ---
    [ExportAIFunction("echo_json")]
    public System.Text.Json.JsonElement EchoJson(System.Text.Json.JsonElement param) => param;

    // --- Parameters_MappedByType_Async (CancellationToken) ---
    [ExportAIFunction("uses_cancellation")]
    public int UsesCancellation(int value1 = 1, string value2 = "2", CancellationToken cancellationToken = default)
    {
        _lastCt = cancellationToken;
        return 42;
    }
    public CancellationToken LastCancellationToken => _lastCt;
    private CancellationToken _lastCt;

    // --- Returns_AsyncReturnTypesSupported_Async ---
    [ExportAIFunction("returns_task_string")]
    public Task<string> ReturnsTaskString(string a) => Task.FromResult(a + " " + a);

    [ExportAIFunction("returns_valuetask_string")]
    public ValueTask<string> ReturnsValueTaskString(string a, string b) => new(b + " " + a);

    [ExportAIFunction("returns_task")]
    public async Task ReturnsTask(int a, long b)
    {
        LastTaskResult = a + b;
        await Task.Yield();
    }

    [ExportAIFunction("returns_valuetask")]
    public async ValueTask ReturnsValueTask(int a, long b)
    {
        LastValueTaskResult = a + b;
        await Task.Yield();
    }

    public long LastTaskResult { get; private set; }
    public long LastValueTaskResult { get; private set; }

    // --- AIFunctionArguments_SatisfiesParameters / MissingServicesMayBeOptional ---
    [ExportAIFunction("inject_sp_and_args")]
    public int InjectSpAndArgs(
        int myInteger,
        IServiceProvider services,
        AIFunctionArguments arguments)
    {
        LastServices = services;
        LastArguments = arguments;
        return myInteger;
    }

    public IServiceProvider? LastServices { get; private set; }
    public AIFunctionArguments? LastArguments { get; private set; }

    // --- FromKeyedServices_* ---
    [ExportAIFunction("from_keyed_with_key")]
    public int FromKeyedWithKey([FromKeyedServices("key")] KeyedMyService service, int myInteger)
        => service.Value + myInteger;

    [ExportAIFunction("from_keyed_optional")]
    public string FromKeyedOptional([FromKeyedServices("key")] KeyedMyService? service = null, int myInteger = 0)
        => service is null ? "null " + 1 : (service.Value + myInteger).ToString();

    // --- AIFunctionFactory_DefaultDefaultParameter ---
    [ExportAIFunction("default_struct_params")]
    public string DefaultStructParams(Guid g = default, StructWithDefaultCtor s = default)
        => g.ToString() + "," + s.Value.ToString();

    // --- AIFunctionFactory_NullableParameters ---
    [ExportAIFunction("nullable_numeric_params")]
    public int[] NullableNumericParams(int? limit = null, DateTime? from = null)
    {
        var list = new List<int>();
        var d = from ?? default;
        for (int i = 0; i < (limit ?? 4); i++) list.Add(d.Year);
        return list.ToArray();
    }

    // --- AIFunctionFactory_ReturnTypeWithDescriptionAttribute ---
    [ExportAIFunction("add_with_return_description")]
    [return: Description("The summed result")]
    public int AddWithReturnDescription(int a, int b) => a + b;

    // --- JsonSchema_NullableValueTypeParameters_AllowNull ---
    [ExportAIFunction("nullable_value_schema")]
    public void NullableValueSchema(int? nullableInt, int? nullableIntWithDefault = null) { }

    // --- JsonSchema_NullableReferenceTypeParameters_AllowNull ---
    [ExportAIFunction("nullable_ref_schema")]
    public void NullableRefSchema(
        string? nullableString,
        int? nullableInt,
        string? nullableStringWithDefault = null,
        int? nullableIntWithDefault = null)
    { }

    // --- AIContent return types (NotSerializedByDefault) ---
    [ExportAIFunction("returns_text_content")]
    public TextContent ReturnsTextContent() => new("text");

    [ExportAIFunction("returns_data_content")]
    public DataContent ReturnsDataContent() => new(new byte[] { 1, 2, 3 }, "application/octet-stream");

    [ExportAIFunction("returns_ai_content_base")]
    public AIContent ReturnsAIContentBase() => new TextContent("text");
}

public readonly struct StructWithDefaultCtor
{
    public int Value { get; }
    public StructWithDefaultCtor()
    {
        Value = 42;
    }
}

[AIToolSource(typeof(EquivalenceToolService))]
internal partial class EquivalenceToolContext : AIToolContext { }
