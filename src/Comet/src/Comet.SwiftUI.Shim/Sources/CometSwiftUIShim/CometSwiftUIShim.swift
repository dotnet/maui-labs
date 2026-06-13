import UIKit
import SwiftUI

// A retained node in the SwiftUI backend tree, mirroring the Compose ComposeNode.
// C# drives these through the @objc host functions; SwiftUI observes them so a property
// or child change re-renders the narrowest scope. @Published vars are Swift-only (the C#
// side mutates them via the host functions, never directly).
@objc(CometNode) public class CometNode: NSObject, ObservableObject, Identifiable {
    @objc public let kind: String
    @Published var text: String = ""
    @Published var placeholder: String = ""
    @Published var isOn: Bool = false
    @Published var doubleValue: Double = 0
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var padding: CGFloat = 0
    @Published var hasTapGesture: Bool = false  // view carries a Comet TapGesture
    // Callbacks into C# (set via host fns). ObjC blocks so .NET binds them as Actions.
    var onTap: (() -> Void)?            // Button action (-> Clicked)
    var onTapGesture: (() -> Void)?     // arbitrary-view tap gesture (-> OnGesture(Tap))
    var onChangeString: ((String) -> Void)?
    var onChangeBool: ((Bool) -> Void)?
    var onChangeDouble: ((Double) -> Void)?

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
        switch property {
        case "text": node.text = value
        case "placeholder": node.placeholder = value
        default: break
        }
    }

    @objc(setBool:property:value:)
    public static func setBool(_ node: CometNode, property: String, value: Bool) {
        switch property {
        case "ison": node.isOn = value
        case "hastapgesture": node.hasTapGesture = value
        default: break
        }
    }

    @objc(setColor:property:argb:)
    public static func setColor(_ node: CometNode, property: String, argb: UInt32) {
        if property == "background" { node.backgroundARGB = argb }
    }

    @objc(setDouble:property:value:)
    public static func setDouble(_ node: CometNode, property: String, value: Double) {
        switch property {
        case "padding": node.padding = CGFloat(value)
        case "value": node.doubleValue = value
        default: break
        }
    }

    @objc(setTapHandler:handler:)
    public static func setTapHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onTap = handler
    }

    @objc(setTapGestureHandler:handler:)
    public static func setTapGestureHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onTapGesture = handler
    }

    @objc(setStringChangeHandler:handler:)
    public static func setStringChangeHandler(_ node: CometNode, handler: @escaping @convention(block) (String) -> Void) {
        node.onChangeString = handler
    }

    @objc(setBoolChangeHandler:handler:)
    public static func setBoolChangeHandler(_ node: CometNode, handler: @escaping @convention(block) (Bool) -> Void) {
        node.onChangeBool = handler
    }

    @objc(setDoubleChangeHandler:handler:)
    public static func setDoubleChangeHandler(_ node: CometNode, handler: @escaping @convention(block) (Double) -> Void) {
        node.onChangeDouble = handler
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

    @objc(clearChildren:)
    public static func clearChildren(_ node: CometNode) {
        node.children.removeAll()
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
        case "button":
            Button(action: { node.onTap?() }) { Text(node.text) }
        case "textfield":
            TextField(node.placeholder, text: Binding(
                get: { node.text },
                set: { node.text = $0; node.onChangeString?($0) }))
                .textFieldStyle(.roundedBorder)
        case "toggle":
            Toggle("", isOn: Binding(
                get: { node.isOn },
                set: { node.isOn = $0; node.onChangeBool?($0) }))
                .labelsHidden()
        case "slider":
            Slider(value: Binding(
                get: { node.doubleValue },
                set: { node.doubleValue = $0; node.onChangeDouble?($0) }))
        case "navigation":
            // Imperative nav: the C# side keeps the stack and sets the single top screen
            // as this node's child (mirrors the Compose nav node).
            if let top = node.children.last { CometNodeView(node: top) } else { EmptyView() }
        case "list":
            List { ForEach(node.children) { CometNodeView(node: $0) } }
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
            .modifier(TapGestureModifier(node: node))
    }
}

// Routes a SwiftUI tap on an arbitrary view to the node's gesture callback (the iOS
// counterpart of Compose's Modifier.Clickable). contentShape makes the whole padded
// frame — including transparent areas of a stack — hittable.
private struct TapGestureModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        if node.hasTapGesture {
            content
                .contentShape(Rectangle())
                .onTapGesture { node.onTapGesture?() }
        } else {
            content
        }
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
