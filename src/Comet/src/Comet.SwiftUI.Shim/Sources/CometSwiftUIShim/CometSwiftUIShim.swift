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
    @Published var imageUrl: String = ""
    @Published var isOn: Bool = false
    @Published var doubleValue: Double = 0
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var textColorARGB: UInt32 = 0    // 0 = inherit (default foreground)
    @Published var padding: CGFloat = 0
    @Published var cornerRadius: CGFloat = 0     // Material card rounded corners (clips content)
    @Published var elevation: CGFloat = 0        // soft drop shadow depth
    @Published var hasTapGesture: Bool = false  // view carries a Comet TapGesture
    // Yoga-computed parent-relative layout frame. hasFrame flips true once C# arranges this
    // node; until then the view uses native SwiftUI layout (so rendering is unchanged).
    @Published var frame: CGRect = .zero
    @Published var hasFrame: Bool = false
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
        case "imageurl": node.imageUrl = value
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
        switch property {
        case "background": node.backgroundARGB = argb
        case "textcolor": node.textColorARGB = argb
        default: break
        }
    }

    @objc(setDouble:property:value:)
    public static func setDouble(_ node: CometNode, property: String, value: Double) {
        switch property {
        case "padding": node.padding = CGFloat(value)
        case "value": node.doubleValue = value
        case "cornerradius": node.cornerRadius = CGFloat(value)
        case "elevation": node.elevation = CGFloat(value)
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

    @objc(setFrame:x:y:width:height:)
    public static func setFrame(_ node: CometNode, x: Double, y: Double, width: Double, height: Double) {
        node.frame = CGRect(x: x, y: y, width: width, height: height)
        node.hasFrame = true
    }

    // Measures a leaf node's intrinsic size — the one place layout crosses into native.
    // The content is constrained to the available width so multi-line text wraps (and reports
    // its wrapped height); UIHostingController.sizeThatFits alone returns the single-line ideal.
    @objc(measureNode:maxWidth:maxHeight:)
    public static func measureNode(_ node: CometNode, maxWidth: Double, maxHeight: Double) -> CGSize {
        let w = (maxWidth.isFinite && maxWidth > 0) ? maxWidth : UIScreen.main.bounds.width

        // Text: measure with TextKit, which reliably wraps to the width (UIHostingController's
        // sizeThatFits/systemLayoutSizeFitting return the single-line ideal for SwiftUI Text).
        if node.kind == "text" {
            let font = UIFont.preferredFont(forTextStyle: .body)
            let rect = (node.text as NSString).boundingRect(
                with: CGSize(width: w, height: .greatestFiniteMagnitude),
                options: [.usesLineFragmentOrigin, .usesFontLeading],
                attributes: [.font: font],
                context: nil)
            return CGSize(width: w, height: ceil(rect.height))
        }

        // Interactive controls (button/textfield/toggle/slider) don't wrap; SwiftUI sizes them.
        let host = UIHostingController(rootView: CometLeafContent(node: node))
        host.view.backgroundColor = .clear
        return host.sizeThatFits(in: CGSize(width: w, height: .greatestFiniteMagnitude))
    }

    // Returns a UIViewController hosting the SwiftUI tree rooted at `root`.
    @objc(hostControllerForRoot:)
    public static func hostController(_ root: CometNode) -> UIViewController {
        return UIHostingController(rootView: CometNodeView(node: root))
    }

    // In-app screenshot (DevFlow/ailoha style): renders the key window to a PNG, so external
    // tooling can fetch the rendered UI over the agent connection (works on a physical device
    // via USB port-forward, no Developer Disk Image needed). Must be called on the main thread.
    @objc(screenshotPNG)
    public static func screenshotPNG() -> Data? {
        guard let window = activeKeyWindow() else { return nil }
        let renderer = UIGraphicsImageRenderer(bounds: window.bounds)
        let image = renderer.image { _ in
            window.drawHierarchy(in: window.bounds, afterScreenUpdates: true)
        }
        return image.pngData()
    }

    static func activeKeyWindow() -> UIWindow? {
        for scene in UIApplication.shared.connectedScenes {
            guard let ws = scene as? UIWindowScene else { continue }
            if let kw = ws.windows.first(where: { $0.isKeyWindow }) ?? ws.windows.first {
                return kw
            }
        }
        return nil
    }
}

// The leaf control for a node (no children, no layout). Used for rendering leaves and, via
// UIHostingController.sizeThatFits, for the Yoga engine's intrinsic-size measurement.
struct CometLeafContent: View {
    @ObservedObject var node: CometNode
    @ViewBuilder
    var body: some View {
        switch node.kind {
        case "button":
            Button(action: { node.onTap?() }) { Text(node.text) }
                .modifier(ForegroundModifier(argb: node.textColorARGB))
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
        case "image":
            AsyncImage(url: URL(string: node.imageUrl)) { phase in
                if let image = phase.image {
                    image.resizable().aspectRatio(contentMode: .fill)
                } else {
                    Color.gray.opacity(0.25)
                }
            }
            .clipped()
        default: // "text" and unknown leaves
            Text(node.text)
                .modifier(ForegroundModifier(argb: node.textColorARGB))
        }
    }
}

// Applies an explicit text/foreground color when Comet set one (argb != 0); otherwise leaves
// the default so it adapts to light/dark like native SwiftUI text.
private struct ForegroundModifier: ViewModifier {
    let argb: UInt32
    func body(content: Content) -> some View {
        if argb == 0 {
            content
        } else {
            content.foregroundColor(Color(
                red:   Double((argb >> 16) & 0xFF) / 255.0,
                green: Double((argb >> 8) & 0xFF) / 255.0,
                blue:  Double(argb & 0xFF) / 255.0,
                opacity: Double((argb >> 24) & 0xFF) / 255.0))
        }
    }
}

private func isYogaContainer(_ kind: String) -> Bool {
    return kind == "vstack" || kind == "hstack" || kind == "zstack"
}

// Recursively renders a CometNode tree as SwiftUI. Once C#'s Yoga engine has arranged a flow
// container (hasFrame), its children are positioned absolutely from the computed frames;
// otherwise native SwiftUI layout is used (rendering unchanged until the engine drives it).
struct CometNodeView: View {
    @ObservedObject var node: CometNode

    var body: some View {
        // Each node sizes + positions ITSELF from its own (observed) frame, so a re-arrange
        // (reflow) re-renders just that node and the layout adapts live — the parent can't
        // observe the children's frames in its own body. Order matters: size → background
        // (so it fills the arranged frame) → offset (move the whole node into place).
        content
            .modifier(SizeModifier(node: node))
            .modifier(BackgroundModifier(argb: node.backgroundARGB))
            .modifier(SurfaceModifier(node: node)) // rounded corners + elevation (Material card)
            .modifier(TapGestureModifier(node: node))
            .modifier(OffsetModifier(node: node))
    }

    @ViewBuilder
    private var content: some View {
        if node.hasFrame && isYogaContainer(node.kind) {
            // Children self-position via their own modifiers; just overlay them.
            ZStack(alignment: .topLeading) {
                ForEach(node.children) { CometNodeView(node: $0) }
            }
        } else {
            nativeContent
        }
    }

    @ViewBuilder
    private var nativeContent: some View {
        switch node.kind {
        case "navigation":
            if let top = node.children.last { CometNodeView(node: top) } else { EmptyView() }
        case "list":
            // Full-bleed, separator-free rows so a Yoga-laid-out row (which already carries its
            // own padding) spans edge-to-edge exactly like the Compose LazyColumn.
            List {
                ForEach(node.children) { child in
                    CometNodeView(node: child)
                        .listRowInsets(EdgeInsets())
                        .listRowSeparator(.hidden)
                }
            }
            .listStyle(.plain)
        case "hstack":
            HStack { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "zstack":
            ZStack { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "vstack":
            VStack { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        default:
            CometLeafContent(node: node)
        }
    }
}

// Sizes a node to its Yoga-computed frame (observing the node, so reflow re-applies it).
private struct SizeModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        if node.hasFrame {
            content.frame(width: node.frame.width, height: node.frame.height, alignment: .topLeading)
        } else {
            content
        }
    }
}

// Positions a node at its Yoga-computed parent-relative offset (applied after background so
// the background fills the frame and moves with the node).
private struct OffsetModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        if node.hasFrame {
            content.offset(x: node.frame.minX, y: node.frame.minY)
        } else {
            content
        }
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

// Rounds a node's corners (clipping its background + content) and casts a soft drop shadow —
// the Compose `.Clip(RoundedCornerShape)` + `.Shadow(elevation)` analog, so a card looks the
// same on both backends. Applied after the background so the fill is clipped to the rounded rect.
private struct SurfaceModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        let radius = node.cornerRadius
        let elevation = node.elevation
        if radius > 0 || elevation > 0 {
            content
                .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
                .shadow(color: Color.black.opacity(elevation > 0 ? 0.18 : 0),
                        radius: elevation, x: 0, y: elevation > 0 ? elevation / 2 : 0)
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
