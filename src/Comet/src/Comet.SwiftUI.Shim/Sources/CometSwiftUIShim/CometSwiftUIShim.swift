import UIKit
import SwiftUI

// Phase 3 risk probe: the minimal @objc-representable surface that lets C# host a
// SwiftUI view through a UIHostingController, without any Swift-ABI interop —
// everything crosses the boundary as Objective-C-compatible types.
@objc(CometSwiftUIHost) public class CometSwiftUIHost: NSObject {

    // Returns a UIViewController hosting a SwiftUI view built from C#-supplied text.
    // C# sets this as the window's root view controller. Explicit @objc selector keeps
    // the binding stable (no Swift name mangling).
    @objc(makeHostControllerWithText:)
    public static func makeHostController(_ text: String) -> UIViewController {
        return UIHostingController(rootView: ProbeView(text: text))
    }
}

private struct ProbeView: View {
    let text: String

    var body: some View {
        VStack(spacing: 12) {
            Text(text)
                .font(.title)
                .foregroundStyle(.white)
            Text("Rendered by SwiftUI, driven from C#")
                .foregroundStyle(.white)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Color(red: 0.40, green: 0.31, blue: 0.64))
    }
}
