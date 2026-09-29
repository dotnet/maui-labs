using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace Microsoft.Maui.Essentials.AI;

internal static class AppleVisionDocumentOptions
{
	internal const string RecognitionLanguagesKey = "apple.vision.recognitionLanguages";
	internal const string CustomWordsKey = "apple.vision.customWords";
	internal const string UseLanguageCorrectionKey = "apple.vision.useLanguageCorrection";
	internal const string AutomaticallyDetectLanguageKey = "apple.vision.automaticallyDetectLanguage";
	internal const string MaximumCandidateCountKey = "apple.vision.maximumCandidateCount";
	internal const string MinimumTextHeightFractionKey = "apple.vision.minimumTextHeightFraction";
	internal const string BarcodeDetectionEnabledKey = "apple.vision.barcodeDetectionEnabled";
	internal const string BarcodeSymbologiesKey = "apple.vision.barcodeSymbologies";
	internal const string CoalesceCompositeSymbologiesKey = "apple.vision.coalesceCompositeSymbologies";
	internal const string RegionOfInterestKey = "apple.vision.regionOfInterest";
	internal const string RevisionKey = "apple.vision.revision";

	internal static DocumentExtractionOptions WithRecognitionLanguages(
		this DocumentExtractionOptions options,
		params string[] recognitionLanguages) =>
		Set(options, RecognitionLanguagesKey, recognitionLanguages);

	internal static DocumentExtractionOptions WithCustomWords(
		this DocumentExtractionOptions options,
		params string[] customWords) =>
		Set(options, CustomWordsKey, customWords);

	internal static DocumentExtractionOptions WithLanguageCorrection(
		this DocumentExtractionOptions options,
		bool enabled) =>
		Set(options, UseLanguageCorrectionKey, enabled);

	internal static DocumentExtractionOptions WithAutomaticLanguageDetection(
		this DocumentExtractionOptions options,
		bool enabled) =>
		Set(options, AutomaticallyDetectLanguageKey, enabled);

	internal static DocumentExtractionOptions WithMaximumCandidateCount(
		this DocumentExtractionOptions options,
		int count)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 10);
		return Set(options, MaximumCandidateCountKey, count);
	}

	internal static DocumentExtractionOptions WithMinimumTextHeightFraction(
		this DocumentExtractionOptions options,
		float fraction)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(fraction);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1);
		return Set(options, MinimumTextHeightFractionKey, fraction);
	}

	internal static DocumentExtractionOptions WithBarcodeDetection(
		this DocumentExtractionOptions options,
		bool enabled,
		bool? coalesceCompositeSymbologies = null,
		params string[] symbologies)
	{
		Set(options, BarcodeDetectionEnabledKey, enabled);
		if (coalesceCompositeSymbologies is { } coalesce)
		{
			Set(options, CoalesceCompositeSymbologiesKey, coalesce);
		}
		if (symbologies.Length > 0)
		{
			Set(options, BarcodeSymbologiesKey, symbologies);
		}
		return options;
	}

	internal static DocumentExtractionOptions WithRegionOfInterest(
		this DocumentExtractionOptions options,
		float x,
		float y,
		float width,
		float height)
	{
		if (x is < 0 or > 1 || y is < 0 or > 1 ||
			width is <= 0 or > 1 || height is <= 0 or > 1 ||
			x + width > 1 || y + height > 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"The region of interest must fit within normalized coordinates [0, 1].");
		}

		return Set(options, RegionOfInterestKey, new[] { x, y, width, height });
	}

	internal static DocumentExtractionOptions WithRevision(
		this DocumentExtractionOptions options,
		int revision)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
		return Set(options, RevisionKey, revision);
	}

	private static DocumentExtractionOptions Set(
		DocumentExtractionOptions options,
		string key,
		object? value)
	{
		ArgumentNullException.ThrowIfNull(options);
		(options.AdditionalProperties ??= new AdditionalPropertiesDictionary())[key] = value;
		return options;
	}
}
