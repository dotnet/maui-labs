import UIKit
import SwiftUI

// A retained node in the SwiftUI backend tree, mirroring the Compose ComposeNode.
// C# drives these through the @objc host functions; SwiftUI observes them so a property
// or child change re-renders the narrowest scope. @Published vars are Swift-only (the C#
// side mutates them via the host functions, never directly).
@objc(CometNode) public class CometNode: NSObject, ObservableObject, Identifiable {
    @objc public let kind: String
    @Published var text: String = ""
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var padding: CGFloat = 0

    public var id: ObjectIdentifier { ObjectIdentifier(self) }

    @objc public init(kind: String) {
        self.kind = kind
        super.init()
    }
}

// The @objc surface C# binds. All parameters/returns are ObjC-representable.
@objc(CometSwiftUIHost) public class CometSwiftUIHost: NSObject {

    @objc(makeNodeWithKind:)
    public static func makeNode(_ kind: String) -> CometNode {
        return CometNode(kind: kind)
    }

    @objc(setString:property:value:)
    public static func setString(_ node: CometNode, property: String, value: String) {
        if property == "text" { node.text = value }
    }

    @objc(setColor:property:argb:)
    public static func setColor(_ node: CometNode, property: String, argb: UInt32) {
        if property == "background" { node.backgroundARGB = argb }
    }

    @objc(setDouble:property:value:)
    public static func setDouble(_ node: CometNode, property: String, value: Double) {
        if property == "padding" { node.padding = CGFloat(value) }
    }

    @objc(insertChild:atIndex:child:)
    public static func insertChild(_ node: CometNode, atIndex index: Int, child: CometNode) {
        let i = max(0, min(index, node.children.count))
        node.children.insert(child, at: i)
    }

    @objc(removeChild:atIndex:)
    public static func removeChild(_ node: CometNode, atIndex index: Int) {
        guard index >= 0 && index < node.children.count else { return }
        node.children.remove(at: index)
    }

    // Returns a UIViewController hosting the SwiftUI tree rooted at `root`.
    @objc(hostControllerForRoot:)
    public static func hostController(_ root: CometNode) -> UIViewController {
        return UIHostingController(rootView: CometNodeView(node: root))
    }
}

// Recursively renders a CometNode tree as SwiftUI, observing each node for changes.
struct CometNodeView: View {
    @ObservedObject var node: CometNode

    @ViewBuilder
    private var content: some View {
        switch node.kind {
        case "text":
            Text(node.text)
        case "hstack":
            HStack { ForEach(node.children) { CometNodeView(node: $0) } }
        case "zstack":
            ZStack { ForEach(node.children) { CometNodeView(node: $0) } }
        default: // "vstack" and unknown containers
            VStack { ForEach(node.children) { CometNodeView(node: $0) } }
        }
    }

    var body: some View {
        content
            .padding(node.padding)
            .modifier(BackgroundModifier(argb: node.backgroundARGB))
    }
}

private struct BackgroundModifier: ViewModifier {
    let argb: UInt32
    func body(content: Content) -> some View {
        if argb == 0 {
            content
        } else {
            content.background(Color(
                red:   Double((argb >> 16) & 0xFF) / 255.0,
                green: Double((argb >> 8) & 0xFF) / 255.0,
                blue:  Double(argb & 0xFF) / 255.0,
                opacity: Double((argb >> 24) & 0xFF) / 255.0))
        }
    }
}
