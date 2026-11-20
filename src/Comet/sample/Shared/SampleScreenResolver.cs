using System;

namespace CometSamples;

public static class SampleScreenResolver
{
	const string SampleIdPrefix = "com.comet.sample.";

	public static string Resolve(string? explicitScreen, string? appId)
	{
		if (!string.IsNullOrEmpty(explicitScreen))
			return explicitScreen;

		if (appId?.StartsWith(SampleIdPrefix, StringComparison.Ordinal) != true)
			return "jetchat";

		var sampleId = appId.Substring(SampleIdPrefix.Length);
		var separator = sampleId.LastIndexOf('.');
		var screen = separator >= 0 ? sampleId.Substring(separator + 1) : sampleId;
		return string.IsNullOrEmpty(screen) ? "jetchat" : screen;
	}
}
