namespace Comet.Benchmarks
{
	/// <summary>
	/// Helper to create VStack/HStack from View arrays since they use collection initializers.
	/// </summary>
	public static class LayoutHelper
	{
		public static VStack ToVStack(Comet.View[] children)
		{
			var stack = new VStack();
			foreach (var child in children)
				stack.Add(child);
			return stack;
		}

		public static HStack ToHStack(Comet.View[] children)
		{
			var stack = new HStack();
			foreach (var child in children)
				stack.Add(child);
			return stack;
		}
	}
}
