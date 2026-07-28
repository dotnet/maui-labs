namespace Microsoft.Maui.AI.Attributes.Generators.Tests;

internal static class Inputs
{
    // Keep each scenario self-contained. Inputs must reference only types available to the test
    // compilation: netcore BCL, Microsoft.Extensions.AI, Microsoft.Extensions.DependencyInjection,
    // and the Microsoft.Maui.AI.Attributes runtime library.

    public const string SimpleInstanceMethod = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class GreeterService
        {
            [ExportAIFunction]
            public string Greet(string name) => "Hello " + name;
        }

        [AIToolSource(typeof(GreeterService))]
        public partial class GreeterTools : AIToolContext { }
        """;

    public const string ExplicitToolName = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("say_hello")]
            public string Hello() => "hi";
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string DescriptionAttribute = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            [Description("Adds two numbers and returns the sum.")]
            public int Add(
                [Description("First addend")] int a,
                [Description("Second addend")] int b) => a + b;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string DefaultValues = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, int count = 3, bool verbose = false, string prefix = "hi") => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string NullableParams = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string? name, int? count) => name ?? "";
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string CancellationTokenParam = """
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public Task<string> DoAsync(string name, CancellationToken ct) => Task.FromResult(name);
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string IServiceProviderAndArgsInjection = """
        using System;
        using Microsoft.Extensions.AI;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, IServiceProvider services, AIFunctionArguments args) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string FromKeyedServicesString = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ICache { }

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, [FromKeyedServices("primary")] ICache cache) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string FromKeyedServicesNullKey = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ICache { }

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, [FromKeyedServices(null)] ICache cache) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string FromServicesOnInterface = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ICache { }

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, [FromServices] ICache cache) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string FromServicesOnConcreteClass = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class CacheImpl { }

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, [FromServices] CacheImpl cache) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ReturnTypeVoid = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public void Do(string name) { }
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ReturnTypeTask = """
        using System.Threading.Tasks;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public Task DoAsync(string name) => Task.CompletedTask;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ReturnTypeValueTask = """
        using System.Threading.Tasks;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public ValueTask DoAsync(string name) => default;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ReturnTypeTaskOfT = """
        using System.Threading.Tasks;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public Task<int> GetAsync(string name) => Task.FromResult(1);
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ReturnTypeValueTaskOfT = """
        using System.Threading.Tasks;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public ValueTask<int> GetAsync(string name) => new ValueTask<int>(1);
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string ApprovalRequired = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction(ApprovalRequired = true)]
            public string Dangerous(string name) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string MultipleAIToolSources = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class SvcA { [ExportAIFunction] public string DoA(string x) => x; }
        public class SvcB { [ExportAIFunction] public string DoB(string x) => x; }

        [AIToolSource(typeof(SvcA))]
        [AIToolSource(typeof(SvcB))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string CrossContextSameService = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Shared { [ExportAIFunction] public string Do(string x) => x; }

        [AIToolSource(typeof(Shared))]
        public partial class CtxA : AIToolContext { }

        [AIToolSource(typeof(Shared))]
        public partial class CtxB : AIToolContext { }
        """;

    public const string NoNamespace = """
        using Microsoft.Maui.AI.Attributes;

        public class Svc { [ExportAIFunction] public string Do(string x) => x; }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string NestedNamespace = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample.Inner.Deeper;

        public class Svc { [ExportAIFunction] public string Do(string x) => x; }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string EmptyToolSource = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc { public string Do(string x) => x; }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string UnsupportedDelegateParam = """
        using System;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string name, Func<int, int> fn) => name;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string GenericMethod = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public T Do<T>(T value) => value;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string RefParam = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public void Do(ref int value) { value++; }
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string OutParam = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public void Do(out int value) { value = 1; }
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string InParam = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public int Do(in int value) => value + 1;
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string StaticProperty = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public static class Catalog
        {
            [ExportAIFunction("list_all")]
            [Description("Returns every item in the catalog.")]
            public static IReadOnlyList<string> All { get; } = new[] { "apple", "banana" };
        }

        [AIToolSource(typeof(Catalog))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string InstanceProperty = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("current_count")]
            [Description("Gets the current count.")]
            public int Count { get; set; }
        }

        [AIToolSource(typeof(Svc))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string MixedMethodsAndProperties = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public static class Store
        {
            [ExportAIFunction("list_items")]
            [Description("All items.")]
            public static IReadOnlyList<string> Items { get; } = new[] { "x" };

            [ExportAIFunction("find_item")]
            [Description("Find by name.")]
            public static string? Find(string name) => name;
        }

        [AIToolSource(typeof(Store))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    // --- Static class as source type (static class with static methods) ---

    public const string StaticClassWithStaticMethods = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public static class MathHelper
        {
            [ExportAIFunction("add")]
            [Description("Adds two integers.")]
            public static int Add([Description("first")] int a, [Description("second")] int b) => a + b;

            [ExportAIFunction("negate")]
            public static int Negate(int value) => -value;
        }

        [AIToolSource(typeof(MathHelper))]
        public partial class MathTools : AIToolContext { }
        """;

    // --- Static method on a non-static class ---

    public const string StaticMethodOnNonStaticClass = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Utility
        {
            [ExportAIFunction("echo_static")]
            public static string Echo(string message) => message;

            [ExportAIFunction("echo_instance")]
            public string EchoInstance(string message) => message;
        }

        [AIToolSource(typeof(Utility))]
        public partial class UtilityTools : AIToolContext { }
        """;

    // --- Static method with [FromServices] ---

    public const string StaticMethodWithFromServices = """
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ILogger { }

        public class Worker
        {
            [ExportAIFunction("do_work")]
            public static string DoWork(string input, [FromServices] ILogger logger) => input;
        }

        [AIToolSource(typeof(Worker))]
        public partial class WorkerTools : AIToolContext { }
        """;

    // --- Static method with no DI (no RequireServices needed) ---

    public const string StaticMethodNoDI = """
        using System.Linq;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public static class PureFunctions
        {
            [ExportAIFunction("reverse")]
            public static string Reverse(string input) => new string(input.Reverse().ToArray());
        }

        [AIToolSource(typeof(PureFunctions))]
        public partial class PureTools : AIToolContext { }
        """;

    // --- Interface as source type (attributes on interface, resolved via DI) ---

    public const string InterfaceAsSourceType = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface IOrderService
        {
            [ExportAIFunction("place_order")]
            [Description("Places an order.")]
            string PlaceOrder([Description("item name")] string item, [Description("quantity")] int qty);
        }

        [AIToolSource(typeof(IOrderService))]
        public partial class OrderTools : AIToolContext { }
        """;

    // --- Interface as source type with [FromServices] ---

    public const string InterfaceWithFromServices = """
        using System.ComponentModel;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ICart { }

        public interface IArchive
        {
            [ExportAIFunction("checkout")]
            [Description("Checks out the cart.")]
            string Checkout([FromServices] ICart cart);
        }

        [AIToolSource(typeof(IArchive))]
        public partial class ArchiveTools : AIToolContext { }
        """;

    // --- Interface with property ---

    public const string InterfaceWithProperty = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface ICatalog
        {
            [ExportAIFunction("all_items")]
            [Description("Gets all items.")]
            IReadOnlyList<string> Items { get; }
        }

        [AIToolSource(typeof(ICatalog))]
        public partial class CatalogTools : AIToolContext { }
        """;

    // --- Nested class context (context nested inside another class) ---

    public const string NestedClassContext = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string x) => x;
        }

        public partial class OuterViewModel
        {
            [AIToolSource(typeof(Svc))]
            private partial class InnerTools : AIToolContext { }
        }
        """;

    // --- Deeply nested class context (multi-level nesting) ---

    public const string DeeplyNestedClassContext = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string x) => x;
        }

        public partial class LevelOne
        {
            public partial class LevelTwo
            {
                [AIToolSource(typeof(Svc))]
                private partial class DeepTools : AIToolContext { }
            }
        }
        """;

    // --- Nested class context with no namespace ---

    public const string NestedClassNoNamespace = """
        using Microsoft.Maui.AI.Attributes;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string x) => x;
        }

        public partial class Outer
        {
            [AIToolSource(typeof(Svc))]
            internal partial class NestedTools : AIToolContext { }
        }
        """;

    // --- Internal context class ---

    public const string InternalContextClass = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string Do(string x) => x;
        }

        [AIToolSource(typeof(Svc))]
        internal partial class InternalTools : AIToolContext { }
        """;

    // --- Static class with mixed static members (property + method) and FromServices ---

    public const string StaticClassWithFromServicesAndProperty = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface IDb { }

        public static class DataAccess
        {
            [ExportAIFunction("all_records")]
            [Description("All records.")]
            public static IReadOnlyList<string> Records { get; } = new[] { "a", "b" };

            [ExportAIFunction("query_records")]
            [Description("Queries records.")]
            public static string Query(string filter, [FromServices] IDb db) => filter;
        }

        [AIToolSource(typeof(DataAccess))]
        public partial class DataTools : AIToolContext { }
        """;

    // --- Interface with ApprovalRequired ---

    public const string InterfaceWithApproval = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public interface IDangerousService
        {
            [ExportAIFunction("safe_read")]
            [Description("Safe operation.")]
            string Read();

            [ExportAIFunction("dangerous_write", ApprovalRequired = true)]
            [Description("Dangerous write operation.")]
            void Write([Description("data")] string data);
        }

        [AIToolSource(typeof(IDangerousService))]
        public partial class DangerousTools : AIToolContext { }
        """;

    public const string AccessorLevelExportAIFunction = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class DisplaySettings
        {
            public string Mode
            {
                [ExportAIFunction("get_display_mode")]
                [Description("Gets the current display mode.")]
                get;

                [ExportAIFunction("set_display_mode")]
                [Description("Sets the display mode.")]
                set;
            } = "normal";
        }

        [AIToolSource(typeof(DisplaySettings))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string EnumParameter = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public enum Severity { Low, Medium, High }

        public class AlertService
        {
            [ExportAIFunction("set_alert")]
            public string SetAlert([Description("severity level")] Severity level) => level.ToString();
        }

        [AIToolSource(typeof(AlertService))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public const string CollectionParameter = """
        using System.Collections.Generic;
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class TagService
        {
            [ExportAIFunction("process_tags")]
            public int ProcessTags([Description("tags")] List<string> tags) => tags.Count;

            [ExportAIFunction("merge")]
            public Dictionary<string, int> Merge(Dictionary<string, int> a, Dictionary<string, int> b)
            {
                var r = new Dictionary<string, int>(a);
                foreach (var kv in b) r[kv.Key] = kv.Value;
                return r;
            }
        }

        [AIToolSource(typeof(TagService))]
        public partial class ToolsCtx : AIToolContext { }
        """;

    public static string Get(string name) => name switch
    {
        nameof(SimpleInstanceMethod) => SimpleInstanceMethod,
        nameof(ExplicitToolName) => ExplicitToolName,
        nameof(DescriptionAttribute) => DescriptionAttribute,
        nameof(DefaultValues) => DefaultValues,
        nameof(NullableParams) => NullableParams,
        nameof(CancellationTokenParam) => CancellationTokenParam,
        nameof(IServiceProviderAndArgsInjection) => IServiceProviderAndArgsInjection,
        nameof(FromKeyedServicesString) => FromKeyedServicesString,
        nameof(FromKeyedServicesNullKey) => FromKeyedServicesNullKey,
        nameof(FromServicesOnInterface) => FromServicesOnInterface,
        nameof(FromServicesOnConcreteClass) => FromServicesOnConcreteClass,
        nameof(ReturnTypeVoid) => ReturnTypeVoid,
        nameof(ReturnTypeTask) => ReturnTypeTask,
        nameof(ReturnTypeValueTask) => ReturnTypeValueTask,
        nameof(ReturnTypeTaskOfT) => ReturnTypeTaskOfT,
        nameof(ReturnTypeValueTaskOfT) => ReturnTypeValueTaskOfT,
        nameof(ApprovalRequired) => ApprovalRequired,
        nameof(MultipleAIToolSources) => MultipleAIToolSources,
        nameof(CrossContextSameService) => CrossContextSameService,
        nameof(NoNamespace) => NoNamespace,
        nameof(NestedNamespace) => NestedNamespace,
        nameof(EmptyToolSource) => EmptyToolSource,
        nameof(UnsupportedDelegateParam) => UnsupportedDelegateParam,
        nameof(GenericMethod) => GenericMethod,
        nameof(RefParam) => RefParam,
        nameof(OutParam) => OutParam,
        nameof(InParam) => InParam,
        nameof(StaticProperty) => StaticProperty,
        nameof(InstanceProperty) => InstanceProperty,
        nameof(MixedMethodsAndProperties) => MixedMethodsAndProperties,
        nameof(StaticClassWithStaticMethods) => StaticClassWithStaticMethods,
        nameof(StaticMethodOnNonStaticClass) => StaticMethodOnNonStaticClass,
        nameof(StaticMethodWithFromServices) => StaticMethodWithFromServices,
        nameof(StaticMethodNoDI) => StaticMethodNoDI,
        nameof(InterfaceAsSourceType) => InterfaceAsSourceType,
        nameof(InterfaceWithFromServices) => InterfaceWithFromServices,
        nameof(InterfaceWithProperty) => InterfaceWithProperty,
        nameof(NestedClassContext) => NestedClassContext,
        nameof(DeeplyNestedClassContext) => DeeplyNestedClassContext,
        nameof(NestedClassNoNamespace) => NestedClassNoNamespace,
        nameof(InternalContextClass) => InternalContextClass,
        nameof(StaticClassWithFromServicesAndProperty) => StaticClassWithFromServicesAndProperty,
        nameof(InterfaceWithApproval) => InterfaceWithApproval,
        nameof(AccessorLevelExportAIFunction) => AccessorLevelExportAIFunction,
        nameof(IncludeToolsFilter) => IncludeToolsFilter,
        nameof(ExcludeToolsFilter) => ExcludeToolsFilter,
        nameof(IncludeAndExcludeBothSet) => IncludeAndExcludeBothSet,
        nameof(IncludeToolsNonexistentMethod) => IncludeToolsNonexistentMethod,
        nameof(IncludeToolsEmpty) => IncludeToolsEmpty,
        nameof(ExcludeToolsEmpty) => ExcludeToolsEmpty,
        nameof(IncludeThenExcludeSame) => IncludeThenExcludeSame,
        nameof(ExcludeToolsAll) => ExcludeToolsAll,
        nameof(IncludeToolsSingleMethod) => IncludeToolsSingleMethod,
        nameof(ExcludeToolsSingleMethod) => ExcludeToolsSingleMethod,
        nameof(IncludeToolsWithProperty) => IncludeToolsWithProperty,
        nameof(TwoContextsDifferentFilters) => TwoContextsDifferentFilters,
        nameof(EnumParameter) => EnumParameter,
        nameof(CollectionParameter) => CollectionParameter,
        _ => throw new KeyNotFoundException(name),
    };

    // ── IncludeTools / ExcludeTools scenarios ───────────────────────

    public const string IncludeToolsFilter = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class PollService
        {
            [ExportAIFunction("get_poll")]
            public string GetPollResults() => "results";

            [ExportAIFunction("get_all_polls")]
            public string GetAllPollResults() => "all";

            [ExportAIFunction("create_poll")]
            public string CreatePoll(string question) => question;
        }

        [AIToolSource(typeof(PollService), IncludeTools = [nameof(PollService.GetPollResults), nameof(PollService.GetAllPollResults)])]
        public partial class ReadOnlyPollContext : AIToolContext { }
        """;

    public const string ExcludeToolsFilter = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class PollService
        {
            [ExportAIFunction("get_poll")]
            public string GetPollResults() => "results";

            [ExportAIFunction("get_all_polls")]
            public string GetAllPollResults() => "all";

            [ExportAIFunction("create_poll")]
            public string CreatePoll(string question) => question;
        }

        [AIToolSource(typeof(PollService), ExcludeTools = [nameof(PollService.CreatePoll)])]
        public partial class ReadOnlyPollContext : AIToolContext { }
        """;

    public const string IncludeAndExcludeBothSet = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
            [ExportAIFunction("tool_c")]
            public string C() => "c";
        }

        [AIToolSource(typeof(Svc), IncludeTools = ["A", "B"], ExcludeTools = ["B"])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string IncludeToolsNonexistentMethod = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string A() => "a";
        }

        [AIToolSource(typeof(Svc), IncludeTools = ["NonExistent"])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string IncludeToolsEmpty = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
        }

        [AIToolSource(typeof(Svc), IncludeTools = [])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string ExcludeToolsEmpty = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
        }

        [AIToolSource(typeof(Svc), ExcludeTools = [])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string IncludeThenExcludeSame = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
        }

        [AIToolSource(typeof(Svc), IncludeTools = ["A"], ExcludeTools = ["A"])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string ExcludeToolsAll = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction]
            public string A() => "a";
        }

        [AIToolSource(typeof(Svc), ExcludeTools = ["A"])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string IncludeToolsSingleMethod = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
            [ExportAIFunction("tool_c")]
            public string C() => "c";
        }

        [AIToolSource(typeof(Svc), IncludeTools = [nameof(Svc.B)])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string ExcludeToolsSingleMethod = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
            [ExportAIFunction("tool_c")]
            public string C() => "c";
        }

        [AIToolSource(typeof(Svc), ExcludeTools = [nameof(Svc.B)])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string IncludeToolsWithProperty = """
        using System.ComponentModel;
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("get_value")]
            [Description("Gets the value")]
            public string Value { get; set; } = "default";

            [ExportAIFunction("do_action")]
            public string DoAction() => "done";
        }

        [AIToolSource(typeof(Svc), IncludeTools = [nameof(Svc.Value)])]
        public partial class Ctx : AIToolContext { }
        """;

    public const string TwoContextsDifferentFilters = """
        using Microsoft.Maui.AI.Attributes;

        namespace Sample;

        public class Svc
        {
            [ExportAIFunction("tool_a")]
            public string A() => "a";
            [ExportAIFunction("tool_b")]
            public string B() => "b";
            [ExportAIFunction("tool_c")]
            public string C() => "c";
        }

        [AIToolSource(typeof(Svc), IncludeTools = [nameof(Svc.A)])]
        public partial class ContextA : AIToolContext { }

        [AIToolSource(typeof(Svc), ExcludeTools = [nameof(Svc.A)])]
        public partial class ContextBC : AIToolContext { }
        """;
}
