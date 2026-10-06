using Microsoft.Maui.Essentials.AI.Internal.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionRecognizeDocumentsOptions
{
	internal const string RecognitionLanguages = "apple.vision.recognitionLanguages";
	internal const string CustomWords = "apple.vision.customWords";
	internal const string UseLanguageCorrection = "apple.vision.useLanguageCorrection";
	internal const string AutomaticallyDetectLanguage = "apple.vision.automaticallyDetectLanguage";
	internal const string MaximumCandidateCount = "apple.vision.maximumCandidateCount";
	internal const string MinimumTextHeightFraction = "apple.vision.minimumTextHeightFraction";
	internal const string BarcodeDetectionEnabled = "apple.vision.barcodeDetectionEnabled";
	internal const string BarcodeSymbologies = "apple.vision.barcodeSymbologies";
	internal const string CoalesceCompositeSymbologies = "apple.vision.coalesceCompositeSymbologies";
	internal const string RegionOfInterest = "apple.vision.regionOfInterest";
	internal const string Revision = "apple.vision.revision";

	internal static RecognizeDocumentsRequestOptions? ToRequestOptions(DocumentExtractionOptions? options)
	{
		if (options is null)
			return null;
		if (options.ModelId is not null &&
			!string.Equals(options.ModelId, "recognize-documents", StringComparison.OrdinalIgnoreCase))
			throw new NotSupportedException($"Apple Vision does not support model '{options.ModelId}'.");
		var request = new RecognizeDocumentsRequestOptions
		{
			RecognitionLanguages = Read<string[]>(options, RecognitionLanguages)?.ToArray(),
			CustomWords = Read<string[]>(options, CustomWords)?.ToArray(),
			UseLanguageCorrection = Read<bool?>(options, UseLanguageCorrection),
			AutomaticallyDetectLanguage = Read<bool?>(options, AutomaticallyDetectLanguage),
			MaximumCandidateCount = Read<int?>(options, MaximumCandidateCount),
			MinimumTextHeightFraction = Read<float?>(options, MinimumTextHeightFraction),
			BarcodeDetectionEnabled = Read<bool?>(options, BarcodeDetectionEnabled),
			BarcodeSymbologies = Read<string[]>(options, BarcodeSymbologies)?.ToArray(),
			CoalesceCompositeSymbologies = Read<bool?>(options, CoalesceCompositeSymbologies),
			RegionOfInterest = Read<float[]>(options, RegionOfInterest)?.ToArray(),
			Revision = Read<int?>(options, Revision),
		};
		if (request.MaximumCandidateCount is < 1 or > 10)
			throw new ArgumentOutOfRangeException(MaximumCandidateCount);
		if (request.Revision is < 1)
			throw new ArgumentOutOfRangeException(Revision);
		if (request.MinimumTextHeightFraction is { } fraction && (!float.IsFinite(fraction) || fraction is < 0 or > 1))
			throw new ArgumentOutOfRangeException(MinimumTextHeightFraction);
		if (request.RegionOfInterest is { } roi && (roi.Length != 4 || roi.Any(value => !float.IsFinite(value)) ||
			roi[0] is < 0 or > 1 || roi[1] is < 0 or > 1 || roi[2] is <= 0 or > 1 || roi[3] is <= 0 or > 1 ||
			roi[0] + roi[2] > 1 || roi[1] + roi[3] > 1))
			throw new ArgumentOutOfRangeException(RegionOfInterest);
		foreach (var values in new[] { request.RecognitionLanguages, request.CustomWords, request.BarcodeSymbologies })
			if (values?.Any(string.IsNullOrWhiteSpace) == true)
				throw new ArgumentException("Recognition option values must not be empty.", nameof(options));
		return request;
	}

	private static T? Read<T>(DocumentExtractionOptions options, string key)
	{
		if (options.AdditionalProperties?.TryGetValue(key, out var value) != true || value is null)
			return default;
		if (value is T typed)
			return typed;
		throw new ArgumentException($"Option '{key}' must be a {typeof(T).Name}.", nameof(options));
	}
}

internal sealed class RecognizeDocumentsRequestOptions
{
	internal string[]? RecognitionLanguages { get; init; }
	internal string[]? CustomWords { get; init; }
	internal bool? UseLanguageCorrection { get; init; }
	internal bool? AutomaticallyDetectLanguage { get; init; }
	internal int? MaximumCandidateCount { get; init; }
	internal float? MinimumTextHeightFraction { get; init; }
	internal bool? BarcodeDetectionEnabled { get; init; }
	internal string[]? BarcodeSymbologies { get; init; }
	internal bool? CoalesceCompositeSymbologies { get; init; }
	internal float[]? RegionOfInterest { get; init; }
	internal int? Revision { get; init; }
}
