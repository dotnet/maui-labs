using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

/// <summary>Opt-in for the DevFlow.Sample Layout QA journey, separate from general live tests.</summary>
public sealed class LayoutQaFactAttribute : FactAttribute
{
    public LayoutQaFactAttribute() => Skip = GetSkipReason(Environment.GetEnvironmentVariable);

    internal static string? GetSkipReason(Func<string, string?> environment)
    {
        if (environment("MAUI_INSPECTOR_LAYOUT_QA_TESTS") != "1")
            return "Layout QA journey; set MAUI_INSPECTOR_LAYOUT_QA_TESTS=1 and LAYOUT_QA_AGENT_PORT to enable.";
        if (!int.TryParse(environment("LAYOUT_QA_AGENT_PORT"), out var port) || port is < 1 or > 65535)
            return "Layout QA journey requires LAYOUT_QA_AGENT_PORT between 1 and 65535.";
        return null;
    }
}
