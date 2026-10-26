namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public interface IProfilerCollector
{
    void Start(int intervalMs);
    void Stop();
    bool TryCollect(out ProfilerSample sample);
    ProfilerCapabilities GetCapabilities();
}
