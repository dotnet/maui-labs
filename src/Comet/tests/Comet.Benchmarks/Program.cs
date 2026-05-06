using BenchmarkDotNet.Running;

namespace Comet.Benchmarks
{
	public class Program
	{
		public static void Main(string[] args)
		{
			BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
		}
	}
}
