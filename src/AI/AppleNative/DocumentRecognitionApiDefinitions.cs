#nullable enable

using Foundation;
using ObjCRuntime;

namespace Microsoft.Maui.Essentials.AI;

[Internal]
delegate void OnAppleVisionDocumentRecognitionComplete(
	[NullAllowed] AppleVisionDocumentRecognitionResultNative result,
	[NullAllowed] NSError error);

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface AppleVisionDocumentRecognitionCapabilitiesNative
{
	[Export("recognitionLanguages")]
	string[] RecognitionLanguages { get; }

	[Export("barcodeSymbologies")]
	string[] BarcodeSymbologies { get; }

	[Export("revisions")]
	NSNumber[] Revisions { get; }
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface AppleVisionDocumentRecognitionOptionsNative
{
	[NullAllowed, Export("recognitionLanguages", ArgumentSemantic.Copy)]
	string[] RecognitionLanguages { get; set; }

	[NullAllowed, Export("customWords", ArgumentSemantic.Copy)]
	string[] CustomWords { get; set; }

	[NullAllowed, Export("useLanguageCorrection", ArgumentSemantic.Strong)]
	NSNumber UseLanguageCorrection { get; set; }

	[NullAllowed, Export("automaticallyDetectLanguage", ArgumentSemantic.Strong)]
	NSNumber AutomaticallyDetectLanguage { get; set; }

	[NullAllowed, Export("maximumCandidateCount", ArgumentSemantic.Strong)]
	NSNumber MaximumCandidateCount { get; set; }

	[NullAllowed, Export("minimumTextHeightFraction", ArgumentSemantic.Strong)]
	NSNumber MinimumTextHeightFraction { get; set; }

	[NullAllowed, Export("barcodeDetectionEnabled", ArgumentSemantic.Strong)]
	NSNumber BarcodeDetectionEnabled { get; set; }

	[NullAllowed, Export("barcodeSymbologies", ArgumentSemantic.Copy)]
	string[] BarcodeSymbologies { get; set; }

	[NullAllowed, Export("coalesceCompositeSymbologies", ArgumentSemantic.Strong)]
	NSNumber CoalesceCompositeSymbologies { get; set; }

	[NullAllowed, Export("regionOfInterest", ArgumentSemantic.Copy)]
	NSNumber[] RegionOfInterest { get; set; }

	[NullAllowed, Export("revision", ArgumentSemantic.Strong)]
	NSNumber Revision { get; set; }
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface AppleVisionDocumentRecognitionResultNative
{
	[Export("jsonData")]
	NSData JsonData { get; }
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface AppleVisionDocumentRecognizerNative
{
	[Static]
	[Export("capabilities")]
	AppleVisionDocumentRecognitionCapabilitiesNative GetCapabilities();

	[Export("recognizeDocumentWithImageData:orientation:options:onComplete:")]
	[return: NullAllowed]
	CancellationTokenNative RecognizeDocument(
		NSData imageData,
		nint orientation,
		[NullAllowed] AppleVisionDocumentRecognitionOptionsNative options,
		OnAppleVisionDocumentRecognitionComplete onComplete);
}
