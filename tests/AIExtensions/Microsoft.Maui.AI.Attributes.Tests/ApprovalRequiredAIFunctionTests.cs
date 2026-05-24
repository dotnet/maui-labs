using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.AI.Attributes.Tests;

public class ApprovalRequiredAIFunctionTests
{
    [Fact]
    public void Approval_required_true_wraps_in_approval_required_ai_function()
    {
        var tools = AllApprovalToolContext.Default.Tools;

        Assert.Single(tools);
        Assert.IsType<ApprovalRequiredAIFunction>(tools[0]);
        Assert.Equal("needs_approval", tools[0].Name);
    }

    [Fact]
    public void Approval_required_false_does_not_wrap()
    {
        foreach (var tool in TestToolContext.Default.Tools)
        {
            Assert.IsNotType<ApprovalRequiredAIFunction>(tool);
        }
    }

    [Fact]
    public void Mixed_service_wraps_only_flagged_methods()
    {
        var tools = ApprovalMixedToolContext.Default.Tools;

        Assert.Equal(3, tools.Count);
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == "safe_read"));
        Assert.IsType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == "dangerous_write"));
        Assert.IsNotType<ApprovalRequiredAIFunction>(tools.Single(t => t.Name == "another_safe"));
    }

    [Fact]
    public void Approval_required_preserves_tool_name_and_description()
    {
        var wrapped = ApprovalMixedToolContext.Default.Tools.Single(t => t.Name == "dangerous_write");

        Assert.IsType<ApprovalRequiredAIFunction>(wrapped);
        Assert.Equal("dangerous_write", wrapped.Name);
        Assert.Equal("A dangerous write tool", wrapped.Description);
    }
}
