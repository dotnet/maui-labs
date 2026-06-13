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

		[Static, Export("setColor:property:argb:")]
		void SetColor(CometNode node, string property, uint argb);

		[Static, Export("setDouble:property:value:")]
		void SetDouble(CometNode node, string property, double value);

		[Static, Export("setTapHandler:handler:")]
		void SetTapHandler(CometNode node, Action handler);

		[Static, Export("insertChild:atIndex:child:")]
		void InsertChild(CometNode node, nint index, CometNode child);

		[Static, Export("removeChild:atIndex:")]
		void RemoveChild(CometNode node, nint index);

		[Static, Export("hostControllerForRoot:")]
		UIViewController HostController(CometNode root);
	}
}
