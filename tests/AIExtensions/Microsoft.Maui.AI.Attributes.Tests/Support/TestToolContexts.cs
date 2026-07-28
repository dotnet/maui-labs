using Microsoft.Maui.AI.Attributes;

namespace Microsoft.Maui.AI.Attributes.Tests;

[AIToolSource(typeof(TestToolService))]
internal partial class TestToolContext : AIToolContext { }

[AIToolSource(typeof(InvocationCounterService))]
internal partial class InvocationCounterToolContext : AIToolContext { }

[AIToolSource(typeof(MultiParamService))]
internal partial class MultiParamToolContext : AIToolContext { }

[AIToolSource(typeof(DisposableToolService))]
internal partial class DisposableToolContext : AIToolContext { }

[AIToolSource(typeof(DescriptionFallbackService))]
internal partial class DescriptionFallbackToolContext : AIToolContext { }

[AIToolSource(typeof(CancellableToolService))]
internal partial class CancellableToolContext : AIToolContext { }

[AIToolSource(typeof(ComplexSchemaService))]
internal partial class ComplexSchemaToolContext : AIToolContext { }

[AIToolSource(typeof(AllApprovalService))]
internal partial class AllApprovalToolContext : AIToolContext { }

[AIToolSource(typeof(ApprovalMixedService))]
internal partial class ApprovalMixedToolContext : AIToolContext { }

[AIToolSource(typeof(TestToolService))]
[AIToolSource(typeof(MultiParamService))]
internal partial class CompositeToolContext : AIToolContext { }

[AIToolSource(typeof(TestToolService))]
[AIToolSource(typeof(DisposableToolService))]
[AIToolSource(typeof(DescriptionFallbackService))]
internal partial class RegistrationTestToolContext : AIToolContext { }

// --- Static class tool context ---

[AIToolSource(typeof(StaticMathService))]
internal partial class StaticMathToolContext : AIToolContext { }

// --- Mixed static + instance tool context ---

[AIToolSource(typeof(MixedStaticInstanceService))]
internal partial class MixedStaticInstanceToolContext : AIToolContext { }

// --- Interface tool contexts ---

[AIToolSource(typeof(IOrderArchiveService))]
internal partial class OrderArchiveToolContext : AIToolContext { }

[AIToolSource(typeof(IBarService))]
internal partial class BarToolContext : AIToolContext { }

[AIToolSource(typeof(ICatalogService))]
internal partial class CatalogToolContext : AIToolContext { }

[AIToolSource(typeof(IDangerService))]
internal partial class DangerToolContext : AIToolContext { }

// --- Enum parameter tool context ---

[AIToolSource(typeof(EnumParamService))]
internal partial class EnumParamToolContext : AIToolContext { }

// --- Collection parameter tool context ---

[AIToolSource(typeof(CollectionParamService))]
internal partial class CollectionParamToolContext : AIToolContext { }

// --- Nested class tool context ---

internal partial class OuterClass
{
    [AIToolSource(typeof(TestToolService))]
    internal partial class NestedToolContext : AIToolContext { }
}

// --- Deeply nested class tool context ---

internal partial class TopLevel
{
    internal partial class MidLevel
    {
        [AIToolSource(typeof(StaticMathService))]
        private partial class DeepNestedToolContext : AIToolContext { }

        // Expose for testing since the context itself is private
        public static AIToolContext CreateDeep() => DeepNestedToolContext.Default;
    }
}
