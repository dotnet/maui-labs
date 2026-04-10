using System;
using UIKit;

namespace Microsoft.Maui.Go.CompanionApp;

public class Program
{
	static void Main(string[] args)
	{
		Environment.SetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES", "Debug");
		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
