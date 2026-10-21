using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Comet.SwiftUI.Interop
{
	// An opaque handle to a Swift CometNode (a node in the SwiftUI backend tree).
	[BaseType(typeof(NSObject), Name = "CometNode")]
	interface CometNode
	{
		[Export("kind")]
		string Kind { get; }
	}

	// The @objc surface that drives the SwiftUI backend tree from C#.
	[BaseType(typeof(NSObject), Name = "CometSwiftUIHost")]
	interface CometSwiftUIHost
	{
		[Static, Export("makeNodeWithKind:")]
		CometNode MakeNode(string kind);

		[Static, Export("setString:property:value:")]
		void SetString(CometNode node, string property, string value);

		[Static, Export("setBool:property:value:")]
		void SetBool(CometNode node, string property, bool value);

		[Static, Export("setColor:property:argb:")]
		void SetColor(CometNode node, string property, uint argb);

		[Static, Export("setDouble:property:value:")]
		void SetDouble(CometNode node, string property, double value);

		[Static, Export("setData:data:")]
		void SetData(CometNode node, NSData data);

		[Static, Export("scrollNodeToBottom:")]
		void ScrollToBottom(CometNode node);

		[Static, Export("scrollNode:toIndex:position:")]
		void Scroll(CometNode node, nint index, nint position);

		[Static, Export("scrollNode:toIndex:position:animated:")]
		void ScrollAnimated(CometNode node, nint index, nint position, bool animated);

		[Static, Export("markListContentReady:")]
		void MarkListContentReady(CometNode node);

		[Static, Export("resetListScrollReplay:")]
		void ResetListScrollReplay(CometNode node);

		[Static, Export("clearGradientStops:")]
		void ClearGradientStops(CometNode node);

		[Static, Export("clearBorderGradientStops:")]
		void ClearBorderGradientStops(CometNode node);

		[Static, Export("addBorderGradientStop:argb:")]
		void AddBorderGradientStop(CometNode node, uint argb);

		[Static, Export("addGradientStop:argb:")]
		void AddGradientStop(CometNode node, uint argb);

		[Static, Export("clearTextRuns:")]
		void ClearTextRuns(CometNode node);

		[Static, Export("addTextRun:text:colorArgb:hasColor:mono:bgArgb:hasBg:underline:")]
		void AddTextRun(CometNode node, string text, uint colorArgb, bool hasColor, bool mono, uint bgArgb, bool hasBg, bool underline);

		[Static, Export("setTapHandler:handler:")]
		void SetTapHandler(CometNode node, Action handler);

		[Static, Export("setTapGestureHandler:handler:")]
		void SetTapGestureHandler(CometNode node, Action handler);

		[Static, Export("setStringChangeHandler:handler:")]
		void SetStringChangeHandler(CometNode node, Action<string> handler);

		[Static, Export("setBoolChangeHandler:handler:")]
		void SetBoolChangeHandler(CometNode node, Action<bool> handler);

		[Static, Export("setDoubleChangeHandler:handler:")]
		void SetDoubleChangeHandler(CometNode node, Action<double> handler);

		[Static, Export("setDialogDismissHandler:handler:")]
		void SetDialogDismissHandler(CometNode node, Action handler);

		[Static, Export("setDialogConfirmHandler:handler:")]
		void SetDialogConfirmHandler(CometNode node, Action handler);

		[Static, Export("setDialogDismissActionHandler:handler:")]
		void SetDialogDismissActionHandler(CometNode node, Action handler);

		[Static, Export("setFocusHandler:handler:")]
		void SetFocusHandler(CometNode node, Action handler);

		[Static, Export("setCompletedHandler:handler:")]
		void SetCompletedHandler(CometNode node, Action handler);

		[Static, Export("setScrollHandler:handler:")]
		void SetScrollHandler(CometNode node, Action<double> handler);

		[Static, Export("setScrollTopHandler:handler:")]
		void SetScrollTopHandler(CometNode node, Action<double> handler);

		[Static, Export("setLongPressGestureHandler:handler:")]
		void SetLongPressGestureHandler(CometNode node, Action handler);

		[Static, Export("setRecordGestureHandler:handler:")]
		void SetRecordGestureHandler(CometNode node, Action<double, double, double> handler);

		[Static, Export("setRowVisibilityHandler:handler:")]
		void SetRowVisibilityHandler(CometNode node, Action<double, double> handler);

		[Static, Export("setSelectionChangedHandler:handler:")]
		void SetSelectionChangedHandler(CometNode node, Action<double> handler);

		[Static, Export("setDateChangedHandler:handler:")]
		void SetDateChangedHandler(CometNode node, Action<double> handler);

		[Static, Export("setRefreshHandler:handler:")]
		void SetRefreshHandler(CometNode node, Action handler);

		[Static, Export("setRefreshEndedHandler:handler:")]
		void SetRefreshEndedHandler(CometNode node, Action handler);

		[Static, Export("setBackRequestHandler:handler:")]
		void SetBackRequestHandler(CometNode node, Action handler);

		[Static, Export("setSystemThemeChangedHandler:handler:")]
		void SetSystemThemeChangedHandler(CometNode node, Action handler);

		[Static, Export("insertChild:atIndex:child:")]
		void InsertChild(CometNode node, nint index, CometNode child);

		[Static, Export("removeChild:atIndex:")]
		void RemoveChild(CometNode node, nint index);

		[Static, Export("clearChildren:")]
		void ClearChildren(CometNode node);

		[Static, Export("setFrame:x:y:width:height:")]
		void SetFrame(CometNode node, double x, double y, double width, double height);

		[Static, Export("measureNode:maxWidth:maxHeight:")]
		CoreGraphics.CGSize MeasureNode(CometNode node, double maxWidth, double maxHeight);

		[Static, Export("screenshotPNG")]
		[return: NullAllowed]
		Foundation.NSData ScreenshotPng();

		[Static, Export("hostControllerForRoot:")]
		UIViewController HostController(CometNode root);
	}
}
