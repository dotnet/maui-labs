namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public interface IMarkerPublisher
{
    void Publish(ProfilerMarker marker);
}
