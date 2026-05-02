namespace Comet
{
	public interface INativeHost
	{
		bool TryGetNativeView<T>(out T nativeView) where T : class;
	}
}
