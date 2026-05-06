namespace Comet.Reactive;

public interface IReactiveSubscriber
{
	void OnDependencyChanged(IReactiveSource source);
}
