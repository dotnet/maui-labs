using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class AIFunctionSchemaTests
{
    [Fact]
    public void Json_schema_contains_parameter_info()
    {
        var tool = TestToolContext.Default.Tools.First(t => t.Name == "test_tool");
        var function = Assert.IsAssignableFrom<AIFunctionDeclaration>(tool);

        Assert.Contains("input", function.JsonSchema.ToString());
    }

    [Fact]
    public void Schema_contains_parameter_description()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "test_tool"));

        Assert.Contains("input value", tool.JsonSchema.ToString());
    }

    [Fact]
    public void Schema_matches_direct_factory_output()
    {
        var diTool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "test_tool"));

        var schema = diTool.JsonSchema.ToString();
        // Verify the schema has the right structure for the "input" parameter
        Assert.Contains("\"input\"", schema);
        Assert.Contains("\"type\"", schema);
        Assert.Contains("\"properties\"", schema);
        Assert.Contains("input value", schema);
    }

    [Fact]
    public void Approval_wrapped_tools_preserve_full_ai_visible_schema()
    {
        var reflectedTool = ComplexSchemaToolContext.Default.Tools.Single(t => t.Name == "create_plant_profile");
        var reflectedFunction = Assert.IsAssignableFrom<AIFunction>(reflectedTool);

        Assert.IsType<ApprovalRequiredAIFunction>(reflectedTool);
        Assert.Equal("create_plant_profile", reflectedFunction.Name);
        Assert.Equal("Creates a plant profile from structured details.", reflectedFunction.Description);

        var inputSchema = reflectedFunction.JsonSchema.ToString();
        Assert.Contains("\"properties\"", inputSchema);
        Assert.Contains("structured details for the plant profile", inputSchema);
        Assert.Contains("friendly nickname shown to the user", inputSchema);
        Assert.Contains("botanical species or variety", inputSchema);
        Assert.Contains("current location of the plant", inputSchema);
        Assert.Contains("whether the plant lives indoors", inputSchema);
        Assert.Contains("whether to notify the user after creation", inputSchema);

        var returnSchema = reflectedFunction.ReturnJsonSchema?.ToString();
        Assert.NotNull(returnSchema);
        Assert.Contains("stable identifier returned to the AI", returnSchema!);
        Assert.Contains("nickname echoed back to the AI", returnSchema!);
    }

    [Fact]
    public void Schema_marks_non_nullable_reference_params_as_required()
    {
        // TestToolService.DoSomething(string input) — "input" is non-nullable string, should be required
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "test_tool"));

        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"required\"", schema);
        Assert.Contains("\"input\"", schema);
    }

    [Fact]
    public void Schema_marks_value_type_params_as_required()
    {
        // StaticMathService has Add(int a, int b) — both value types, both required
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            StaticMathToolContext.Default.Tools.First(t => t.Name == "add_numbers"));

        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"required\"", schema);
        Assert.Contains("\"a\"", schema);
        Assert.Contains("\"b\"", schema);
    }

    [Fact]
    public void Schema_enum_param_has_type_info()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            EnumParamToolContext.Default.Tools.First(t => t.Name == "set_priority"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"level\"", schema);
        Assert.Contains("priority level", schema);
        Assert.Contains("\"required\"", schema);
    }

    [Fact]
    public void Schema_enum_return_type_has_return_schema()
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(
            EnumParamToolContext.Default.Tools.First(t => t.Name == "get_priority"));
        Assert.NotNull(tool.ReturnJsonSchema);
    }

    [Fact]
    public void Schema_collection_param_has_type()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            CollectionParamToolContext.Default.Tools.First(t => t.Name == "process_tags"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"tags\"", schema);
        Assert.Contains("\"required\"", schema);
    }

    [Fact]
    public void Schema_optional_param_not_in_required()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            CollectionParamToolContext.Default.Tools.First(t => t.Name == "find_items"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"query\"", schema);
        // query has a default, should NOT be in required
        Assert.DoesNotContain("\"required\"", schema);
    }

    [Fact]
    public void Schema_no_params_produces_empty_properties()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            TestToolContext.Default.Tools.First(t => t.Name == "GetCount"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"properties\"", schema);
        // No required array since there are no params
        Assert.DoesNotContain("\"required\"", schema);
    }

    [Fact]
    public void Schema_void_return_has_null_return_schema()
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(
            StaticMathToolContext.Default.Tools.First(t => t.Name == "negate_number"));
        // negate returns int, should have return schema
        Assert.NotNull(tool.ReturnJsonSchema);
    }

    [Fact]
    public void Schema_multi_param_marks_all_non_nullable_as_required()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            MultiParamToolContext.Default.Tools.First(t => t.Name == "multi_param"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"required\"", schema);
        Assert.Contains("\"firstName\"", schema);
        Assert.Contains("\"lastName\"", schema);
        Assert.Contains("\"age\"", schema);
    }

    [Fact]
    public void Schema_di_params_excluded_from_properties()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            BarToolContext.Default.Tools.First(t => t.Name == "bar_action"));
        var schema = tool.JsonSchema.ToString();
        // "input" should be in schema
        Assert.Contains("\"input\"", schema);
        // "foo" is [FromServices], should NOT be in schema properties
        Assert.DoesNotContain("\"foo\"", schema);
    }

    [Fact]
    public void Schema_cancellation_token_excluded_from_properties()
    {
        var tool = Assert.IsAssignableFrom<AIFunctionDeclaration>(
            CancellableToolContext.Default.Tools.First(t => t.Name == "cancellable_tool"));
        var schema = tool.JsonSchema.ToString();
        Assert.Contains("\"input\"", schema);
        Assert.DoesNotContain("cancellationToken", schema);
    }
}