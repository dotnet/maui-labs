namespace BespokeCollections.App;

public static class PrototypeLaunch
{
    public static bool RunProof { get; set; } = Environment.GetEnvironmentVariable("BESPOKE_RUN_PROOF") == "1";
    public static bool CaptureScrollWorkload { get; set; } = Environment.GetEnvironmentVariable("BESPOKE_SCROLL_WORKLOAD") == "1";
}
