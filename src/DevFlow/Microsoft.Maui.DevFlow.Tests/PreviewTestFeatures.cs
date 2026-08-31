using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Tests;

internal static class PreviewTestFeatures
{
    internal static MauiPreviewFeatureFlags AllEnabled() => new()
    {
        WorkbenchEnabled = true,
        AgentAuthoringEnabled = true,
        RepairProposalsEnabled = true,
        SourceProposalsEnabled = true,
        TraceImportExportEnabled = true,
    };
}
