using System;
using System.Reflection;
using UIKit;

namespace Microsoft.Maui.Go.CompanionApp;

public class Program
{
	static void Main(string[] args)
	{
		Console.WriteLine($"[GoCompanion] DOTNET_MODIFIABLE_ASSEMBLIES (from env) = {Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES")}");
		Console.WriteLine($"[GoCompanion] MetadataUpdater.IsSupported = {System.Reflection.Metadata.MetadataUpdater.IsSupported}");

		// Deep diagnostics: call the internal ApplyUpdateEnabled to understand WHY IsSupported=false
		try
		{
			var type = typeof(System.Reflection.Metadata.MetadataUpdater);
			var enabledMethod = type.GetMethod("ApplyUpdateEnabled", BindingFlags.Static | BindingFlags.NonPublic);
			if (enabledMethod != null)
			{
				var result = enabledMethod.Invoke(null, new object[] { 0 });
				Console.WriteLine($"[GoCompanion] ApplyUpdateEnabled(0) = '{result}' (null means not supported)");
			}
			else
			{
				Console.WriteLine("[GoCompanion] ApplyUpdateEnabled method not found");
			}

			var capMethod = type.GetMethod("GetCapabilities", BindingFlags.Static | BindingFlags.NonPublic);
			if (capMethod != null)
			{
				var caps = capMethod.Invoke(null, null);
				Console.WriteLine($"[GoCompanion] GetCapabilities = '{caps}'");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[GoCompanion] Diagnostics error: {ex.GetType().Name}: {ex.Message}");
		}

		UIApplication.Main(args, null, typeof(AppDelegate));
	}
}
