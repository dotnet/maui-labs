namespace Comet.Reactive;

public interface IReactiveSource
{
	void Subscribe(IReactiveSubscriber subscriber);
	void Unsubscribe(IReactiveSubscriber subscriber);
	uint Version { get; }
}
