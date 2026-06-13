using System;
using Foundation;
using ObjCRuntime;
using UIKit;

namespace Comet.SwiftUI.Interop
{
	// Binds the @objc surface of CometSwiftUIShim (a Swift shim wrapping SwiftUI).
	[BaseType(typeof(NSObject), Name = "CometSwiftUIHost")]
	interface CometSwiftUIHost
	{
		// + (UIViewController *)makeHostControllerWithText:(NSString *)text;
		[Static]
		[Export("makeHostControllerWithText:")]
		UIViewController MakeHostController(string text);
	}
}
