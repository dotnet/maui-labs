#nullable enable

using System;
using System.Threading.Tasks;
using Foundation;
using ObjCRuntime;

namespace Microsoft.Maui.Essentials.AI;

// // typedef void (^AppleIntelligenceLogAction)(NSString * _Nonnull);
// [Internal] delegate void AppleIntelligenceLogAction(string message);
//
// // @interface AppleIntelligenceLogger : NSObject
// [Introduced(PlatformName.iOS, 26, 0)]
// [Introduced(PlatformName.MacCatalyst, 26, 0)]
// [Introduced(PlatformName.MacOSX, 26, 0)]
// [BaseType(typeof(NSObject))]
// [Internal]
// interface AppleIntelligenceLogger
// {
// 	// @property (class, nonatomic, copy) void (^ _Nullable)(NSString * _Nonnull) log;
// 	[Static]
// 	[NullAllowed, Export("log", ArgumentSemantic.Copy)]
// 	AppleIntelligenceLogAction Log { get; set; }
// }

// @interface AIContentNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface AIContentNative
{
}

[Internal] delegate void AIToolCompletionHandler([NullAllowed] NSString result, [NullAllowed] NSError error);

// This is essential to keep as we need to reference IAIToolNative in this file
interface IAIToolNative { }

// @protocol AIToolNative
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[Protocol, Model]
[BaseType(typeof(NSObject))]
[Internal]
interface AIToolNative
{
	// @property (nonatomic, readonly, copy) NSString * _Nonnull name;
	[Abstract]
	[Export("name")]
	string Name { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nonnull desc;
	[Abstract]
	[Export("desc")]
	string Desc { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nonnull argumentsSchema;
	[Abstract]
	[Export("argumentsSchema")]
	string ArgumentsSchema { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nonnull outputSchema;
	[Abstract]
	[Export("outputSchema")]
	string OutputSchema { get; }

	// - (void)callWithArguments:(NSString * _Nonnull)arguments completionHandler:(void (^ _Nonnull)(NSString * _Nullable, NSError * _Nullable))completionHandler;
	[Abstract]
	[Export("callWithArguments:completionHandler:")]
	void CallWithArguments(NSString arguments, AIToolCompletionHandler completionHandler);
}

// @interface CancellationTokenNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface CancellationTokenNative
{
	// - (void)cancel;
	[Export("cancel")]
	void Cancel();

	// @property (nonatomic, readonly) BOOL isCancelled;
	[Export("isCancelled")]
	bool IsCancelled { get; }
}

[Internal] delegate void OnResponseUpdate(ResponseUpdateNative update);

[Internal] delegate void OnResponseComplete([NullAllowed] ChatResponseNative response, [NullAllowed] NSError error);

// @interface ChatClientNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface ChatClientNative
{
	// - (CancellationTokenNative * _Nullable)streamResponseWithMessages:(NSArray<ChatMessageNative *> * _Nonnull)messages options:(ChatOptionsNative * _Nullable)options onUpdate:(void (^ _Nonnull)(ResponseUpdateNative * _Nonnull))onUpdate onComplete:(void (^ _Nonnull)(ChatResponseNative * _Nullable, NSError * _Nullable))onComplete SWIFT_WARN_UNUSED_RESULT;
	[Export("streamResponseWithMessages:options:onUpdate:onComplete:")]
	[return: NullAllowed]
	unsafe CancellationTokenNative StreamResponse(ChatMessageNative[] messages, [NullAllowed] ChatOptionsNative options, OnResponseUpdate onUpdate, OnResponseComplete onComplete);

	// - (CancellationTokenNative * _Nullable)getResponseWithMessages:(NSArray<ChatMessageNative *> * _Nonnull)messages options:(ChatOptionsNative * _Nullable)options onUpdate:(void (^ _Nonnull)(ResponseUpdateNative * _Nonnull))onUpdate onComplete:(void (^ _Nonnull)(ChatResponseNative * _Nullable, NSError * _Nullable))onComplete SWIFT_WARN_UNUSED_RESULT;
	[Export("getResponseWithMessages:options:onUpdate:onComplete:")]
	[return: NullAllowed]
	unsafe CancellationTokenNative GetResponse(ChatMessageNative[] messages, [NullAllowed] ChatOptionsNative options, OnResponseUpdate onUpdate, OnResponseComplete onComplete);
}

// @interface ChatMessageNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface ChatMessageNative
{
	// @property (nonatomic) enum ChatRoleNative role;
	[Export("role", ArgumentSemantic.Assign)]
	ChatRoleNative Role { get; set; }

	// @property (nonatomic, copy) NSArray<AIContentNative *> * _Nonnull contents;
	[Export("contents", ArgumentSemantic.Copy)]
	AIContentNative[] Contents { get; set; }
}

// @interface ChatOptionsNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface ChatOptionsNative
{
	// @property (nonatomic, strong) NSNumber * _Nullable topK;
	[NullAllowed, Export("topK", ArgumentSemantic.Strong)]
	NSNumber TopK { get; set; }

	// @property (nonatomic, strong) NSNumber * _Nullable seed;
	[NullAllowed, Export("seed", ArgumentSemantic.Strong)]
	NSNumber Seed { get; set; }

	// @property (nonatomic, strong) NSNumber * _Nullable temperature;
	[NullAllowed, Export("temperature", ArgumentSemantic.Strong)]
	NSNumber Temperature { get; set; }

	// @property (nonatomic, strong) NSNumber * _Nullable maxOutputTokens;
	[NullAllowed, Export("maxOutputTokens", ArgumentSemantic.Strong)]
	NSNumber MaxOutputTokens { get; set; }

	// @property (nonatomic, strong) NSString * _Nullable responseJsonSchema;
	[NullAllowed, Export("responseJsonSchema", ArgumentSemantic.Strong)]
	NSString ResponseJsonSchema { get; set; }

	// @property (nonatomic, copy) NSArray<id <AIToolNative>> * _Nullable tools;
	[NullAllowed, Export("tools", ArgumentSemantic.Copy)]
	IAIToolNative[] Tools { get; set; }
}

// @interface ChatResponseNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface ChatResponseNative
{
	// @property (nonatomic, copy) NSArray<ChatMessageNative *> * _Nonnull messages;
	[Export("messages", ArgumentSemantic.Copy)]
	ChatMessageNative[] Messages { get; set; }

	// - (nonnull instancetype)initWithMessages:(NSArray<ChatMessageNative *> * _Nonnull)messages OBJC_DESIGNATED_INITIALIZER;
	[Export("initWithMessages:")]
	[DesignatedInitializer]
	NativeHandle Constructor(ChatMessageNative[] messages);
}

// @interface FunctionCallContentNative : AIContentNative
[BaseType(typeof(AIContentNative))]
[DisableDefaultCtor]
[Internal]
interface FunctionCallContentNative
{
	// @property (nonatomic, copy) NSString * _Nonnull callId;
	[Export("callId", ArgumentSemantic.Copy)]
	string CallId { get; set; }

	// @property (nonatomic, copy) NSString * _Nonnull name;
	[Export("name", ArgumentSemantic.Copy)]
	string Name { get; set; }

	// @property (nonatomic, copy) NSString * _Nonnull arguments;
	[Export("arguments", ArgumentSemantic.Copy)]
	string Arguments { get; set; }

	// - (nonnull instancetype)initWithCallId:(NSString * _Nonnull)callId name:(NSString * _Nonnull)name arguments:(NSString * _Nonnull)arguments OBJC_DESIGNATED_INITIALIZER;
	[Export("initWithCallId:name:arguments:")]
	[DesignatedInitializer]
	NativeHandle Constructor(string callId, string name, string arguments);
}

// @interface FunctionResultContentNative : AIContentNative
[BaseType(typeof(AIContentNative))]
[DisableDefaultCtor]
[Internal]
interface FunctionResultContentNative
{
	// @property (nonatomic, copy) NSString * _Nonnull callId;
	[Export("callId", ArgumentSemantic.Copy)]
	string CallId { get; set; }

	// @property (nonatomic, copy) NSString * _Nonnull name;
	[Export("name", ArgumentSemantic.Copy)]
	string Name { get; set; }

	// @property (nonatomic, copy) NSString * _Nonnull result;
	[Export("result", ArgumentSemantic.Copy)]
	string Result { get; set; }

	// - (nonnull instancetype)initWithCallId:(NSString * _Nonnull)callId name:(NSString * _Nonnull)name result:(NSString * _Nonnull)result OBJC_DESIGNATED_INITIALIZER;
	[Export("initWithCallId:name:result:")]
	[DesignatedInitializer]
	NativeHandle Constructor(string callId, string name, string result);
}

// @interface TextContentNative : AIContentNative
[BaseType(typeof(AIContentNative))]
[DisableDefaultCtor]
[Internal]
interface TextContentNative
{
	// - (nonnull instancetype)initWithText:(NSString * _Nonnull)text OBJC_DESIGNATED_INITIALIZER;
	[Export("initWithText:")]
	[DesignatedInitializer]
	NativeHandle Constructor(string text);

	// @property (nonatomic, copy) NSString * _Nonnull text;
	[Export("text")]
	string Text { get; set; }
}

// @interface ResponseUpdateNative : NSObject
[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
// [Introduced (PlatformName.VisionOS, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface ResponseUpdateNative
{
	// @property (nonatomic, readonly) enum ResponseUpdateTypeNative updateType;
	[Export("updateType")]
	ResponseUpdateTypeNative UpdateType { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nullable text;
	[NullAllowed, Export("text")]
	string Text { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nullable toolCallId;
	[NullAllowed, Export("toolCallId")]
	string ToolCallId { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nullable toolCallName;
	[NullAllowed, Export("toolCallName")]
	string ToolCallName { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nullable toolCallArguments;
	[NullAllowed, Export("toolCallArguments")]
	string ToolCallArguments { get; }

	// @property (nonatomic, readonly, copy) NSString * _Nullable toolCallResult;
	[NullAllowed, Export("toolCallResult")]
	string ToolCallResult { get; }
}

[Internal] delegate void OnVisionDocumentComplete(
	[NullAllowed] VisionDocumentResultNative result,
	[NullAllowed] NSError error);

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface VisionDocumentCapabilitiesNative
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
[DisableDefaultCtor]
[Internal]
interface VisionDocumentNodeNative
{
	[Export("kind")]
	VisionDocumentNodeKindNative Kind { get; }

	[Export("path")]
	string Path { get; }

	[NullAllowed, Export("parentPath")]
	string ParentPath { get; }

	[NullAllowed, Export("text")]
	string Text { get; }

	[Export("polygon")]
	NSNumber[] Polygon { get; }

	[NullAllowed, Export("confidence")]
	NSNumber Confidence { get; }

	[NullAllowed, Export("rowIndex")]
	NSNumber RowIndex { get; }

	[NullAllowed, Export("columnIndex")]
	NSNumber ColumnIndex { get; }

	[NullAllowed, Export("rowSpan")]
	NSNumber RowSpan { get; }

	[NullAllowed, Export("columnSpan")]
	NSNumber ColumnSpan { get; }

	[NullAllowed, Export("itemString")]
	string ItemString { get; }

	[NullAllowed, Export("markerString")]
	string MarkerString { get; }

	[NullAllowed, Export("markerType")]
	string MarkerType { get; }

	[NullAllowed, Export("symbology")]
	string Symbology { get; }

	[NullAllowed, Export("payloadString")]
	string PayloadString { get; }

	[NullAllowed, Export("payloadData")]
	NSData PayloadData { get; }

	[NullAllowed, Export("isGS1DataCarrier")]
	NSNumber IsGs1DataCarrier { get; }

	[NullAllowed, Export("isColorInverted")]
	NSNumber IsColorInverted { get; }

	[NullAllowed, Export("supplementalPayloadString")]
	string SupplementalPayloadString { get; }

	[NullAllowed, Export("supplementalPayloadData")]
	NSData SupplementalPayloadData { get; }

	[NullAllowed, Export("supplementalCompositeType")]
	string SupplementalCompositeType { get; }

	[NullAllowed, Export("textAlignment")]
	string TextAlignment { get; }

	[NullAllowed, Export("recognitionLanguages")]
	string[] RecognitionLanguages { get; }

	[NullAllowed, Export("detectedDataJson")]
	NSData DetectedDataJson { get; }

	[NullAllowed, Export("candidatesJson")]
	NSData CandidatesJson { get; }

	[NullAllowed, Export("jsonData")]
	NSData JsonData { get; }

	[Export("boundingRegionForUtf16Location:length:")]
	[return: NullAllowed]
	NSNumber[] GetBoundingRegion(nint location, nint length);
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[DisableDefaultCtor]
[Internal]
interface VisionDocumentObservationNative
{
	[Export("uuidString")]
	string UuidString { get; }

	[Export("confidence")]
	float Confidence { get; }

	[Export("transcript")]
	string Transcript { get; }

	[Export("nodes")]
	VisionDocumentNodeNative[] Nodes { get; }

	[Export("structureTruncated")]
	bool StructureTruncated { get; }

	[Export("projectedNodeCount")]
	nint ProjectedNodeCount { get; }

	[Export("maximumTraversalDepth")]
	nint MaximumTraversalDepth { get; }

	[Export("repeatedContainerCount")]
	nint RepeatedContainerCount { get; }

	[NullAllowed, Export("firstRepeatedContainerPath")]
	string FirstRepeatedContainerPath { get; }

	[NullAllowed, Export("firstRepeatedAncestorPath")]
	string FirstRepeatedAncestorPath { get; }

	[NullAllowed, Export("jsonData")]
	NSData JsonData { get; }
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface VisionDocumentOptionsNative
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
interface VisionDocumentResultNative
{
	[Export("observations")]
	VisionDocumentObservationNative[] Observations { get; }
}

[Introduced(PlatformName.iOS, 26, 0)]
[Introduced(PlatformName.MacCatalyst, 26, 0)]
[Introduced(PlatformName.MacOSX, 26, 0)]
[BaseType(typeof(NSObject))]
[Internal]
interface VisionRecognizeDocumentsClientNative
{
	[Static]
	[Export("capabilities")]
	VisionDocumentCapabilitiesNative GetCapabilities();

	[Export("recognizeDocumentWithImageData:orientation:options:onComplete:")]
	[return: NullAllowed]
	CancellationTokenNative RecognizeDocument(
		NSData imageData,
		nint orientation,
		[NullAllowed] VisionDocumentOptionsNative options,
		OnVisionDocumentComplete onComplete);
}
