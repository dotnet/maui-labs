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
    @Published var iconName: String = ""        // cross-platform symbol name → SF Symbol
    @Published var isOn: Bool = false
    @Published var doubleValue: Double = 0
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var textColorARGB: UInt32 = 0    // 0 = inherit (default foreground)
    @Published var fontSize: CGFloat = 0        // 0 = default (body)
    @Published var fontWeight: Int = 0          // 0 = default; otherwise Maui FontWeight (100–900)
    @Published var fontFamily: String = ""      // custom font family (e.g. "Montserrat"); "" = system
    @Published var padding: CGFloat = 0
    // Per-corner radii (top-left, top-right, bottom-right, bottom-left); clips content.
    @Published var cornerTL: CGFloat = 0
    @Published var cornerTR: CGFloat = 0
    @Published var cornerBR: CGFloat = 0
    @Published var cornerBL: CGFloat = 0
    @Published var elevation: CGFloat = 0        // soft drop shadow depth
    @Published var borderWidth: CGFloat = 0      // stroke width (e.g. avatar ring)
    @Published var borderColorARGB: UInt32 = 0   // stroke color
    @Published var hasTapGesture: Bool = false  // view carries a Comet TapGesture
    @Published var drawerOpen: Bool = false     // Drawer: side panel shown
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
        case "icon": node.iconName = value
        case "fontfamily": node.fontFamily = value
        default: break
        }
    }

    @objc(setBool:property:value:)
    public static func setBool(_ node: CometNode, property: String, value: Bool) {
        switch property {
        case "ison": node.isOn = value
        case "hastapgesture": node.hasTapGesture = value
        case "draweropen": node.drawerOpen = value
        default: break
        }
    }

    @objc(setColor:property:argb:)
    public static func setColor(_ node: CometNode, property: String, argb: UInt32) {
        switch property {
        case "background": node.backgroundARGB = argb
        case "textcolor": node.textColorARGB = argb
        case "bordercolor": node.borderColorARGB = argb
        default: break
        }
    }

    @objc(setDouble:property:value:)
    public static func setDouble(_ node: CometNode, property: String, value: Double) {
        switch property {
        case "padding": node.padding = CGFloat(value)
        case "value": node.doubleValue = value
        case "corner.tl": node.cornerTL = CGFloat(value)
        case "corner.tr": node.cornerTR = CGFloat(value)
        case "corner.br": node.cornerBR = CGFloat(value)
        case "corner.bl": node.cornerBL = CGFloat(value)
        case "elevation": node.elevation = CGFloat(value)
        case "fontsize": node.fontSize = CGFloat(value)
        case "fontweight": node.fontWeight = Int(value)
        case "borderwidth": node.borderWidth = CGFloat(value)
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
            let size = node.fontSize > 0 ? node.fontSize : UIFont.preferredFont(forTextStyle: .body).pointSize
            var font: UIFont = node.fontSize > 0
                ? UIFont.systemFont(ofSize: node.fontSize, weight: uiFontWeight(node.fontWeight))
                : UIFont.preferredFont(forTextStyle: .body)
            if !node.fontFamily.isEmpty, let custom = customUIFont(node.fontFamily, size, node.fontWeight) {
                font = custom
            }
            let rect = (node.text as NSString).boundingRect(
                with: CGSize(width: w, height: .greatestFiniteMagnitude),
                options: [.usesLineFragmentOrigin, .usesFontLeading],
                attributes: [.font: font],
                context: nil)
            // Report the ACTUAL used width (≤ constraint) so a short label hugs and the flex row
            // packs it tight; the wrapped height drives multi-line bubbles.
            return CGSize(width: min(ceil(rect.width), w), height: ceil(rect.height))
        }

        // Icon: a square box at the symbol's point size.
        if node.kind == "icon" {
            let s = node.fontSize > 0 ? node.fontSize : 24
            return CGSize(width: s, height: s)
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
                .modifier(FontModifier(node: node))
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
        case "icon":
            // Real SF Symbol, tinted + sized — the iOS counterpart of Compose's Material Icon.
            Image(systemName: sfSymbol(node.iconName))
                .font(.system(size: node.fontSize > 0 ? node.fontSize : 24))
                .modifier(ForegroundModifier(argb: node.textColorARGB))
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
                .modifier(FontModifier(node: node))
        }
    }
}

// Cross-platform symbol name → SF Symbol (the iOS counterpart of the Compose Icons mapping).
private func sfSymbol(_ name: String) -> String {
    switch name {
    case "search": return "magnifyingglass"
    case "info": return "info.circle"
    case "menu": return "line.3.horizontal"
    case "send": return "paperplane.fill"
    case "place", "location": return "mappin"
    case "person": return "person.crop.circle"
    case "people": return "person.2.fill"
    case "jetchat": return "bubble.left.and.bubble.right.fill"   // logo stand-in (no bundled vector on iOS yet)
    case "account": return "person.crop.circle.fill"
    case "call", "phone": return "phone.fill"
    case "email", "mail": return "envelope"
    case "close": return "xmark"
    case "settings": return "gearshape"
    case "share": return "square.and.arrow.up"
    case "back": return "chevron.left"
    case "add": return "plus"
    case "edit": return "pencil"
    case "mood", "emoji": return "face.smiling"
    case "at": return "at"
    case "photo", "image": return "photo"
    case "video", "duo": return "video"
    default: return "star"
    }
}

// SwiftUI Font.Weight for a Maui FontWeight numeric (100–900).
private func swiftFontWeight(_ w: Int) -> Font.Weight {
    switch w {
    case 1..<200: return .thin
    case 200..<300: return .ultraLight
    case 300..<400: return .light
    case 400..<500: return .regular
    case 500..<600: return .medium
    case 600..<700: return .semibold
    case 700..<800: return .bold
    case 800..<900: return .heavy
    case 900...: return .black
    default: return .regular
    }
}

private func uiFontWeight(_ w: Int) -> UIFont.Weight {
    switch w {
    case 1..<200: return .thin
    case 200..<300: return .ultraLight
    case 300..<400: return .light
    case 400..<500: return .regular
    case 500..<600: return .medium
    case 600..<700: return .semibold
    case 700..<800: return .bold
    case 800..<900: return .heavy
    case 900...: return .black
    default: return .regular
    }
}

// A registered custom font (e.g. Montserrat/Karla, bundled + listed in UIAppFonts) at the given
// weight, or nil if not available. Derives the weight on a variable font via the descriptor.
private func customUIFont(_ family: String, _ size: CGFloat, _ weight: Int) -> UIFont? {
    guard let base = UIFont(name: family, size: size) else { return nil }
    let traits: [UIFontDescriptor.TraitKey: Any] = [.weight: uiFontWeight(weight)]
    let descriptor = base.fontDescriptor.addingAttributes([.traits: traits])
    return UIFont(descriptor: descriptor, size: size)
}

// Applies an explicit font (custom family if set, else system) at the Comet size/weight.
private struct FontModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        let size = node.fontSize > 0 ? node.fontSize : UIFont.preferredFont(forTextStyle: .body).pointSize
        if !node.fontFamily.isEmpty, let f = customUIFont(node.fontFamily, size, node.fontWeight) {
            return AnyView(content.font(Font(f)))
        }
        if node.fontSize > 0 {
            return AnyView(content.font(.system(size: node.fontSize, weight: swiftFontWeight(node.fontWeight))))
        }
        if node.fontWeight > 0 {
            return AnyView(content.fontWeight(swiftFontWeight(node.fontWeight)))
        }
        return AnyView(content)
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
        case "drawer":
            // children[0] = content (full screen); children[1] = side panel. Scrim + slide-in.
            ZStack(alignment: .topLeading) {
                if node.children.count > 0 { CometNodeView(node: node.children[0]) }
                if node.drawerOpen {
                    Color.black.opacity(0.32).ignoresSafeArea()
                        .onTapGesture { node.onTap?() }
                    if node.children.count > 1 {
                        CometNodeView(node: node.children[1])
                            .transition(.move(edge: .leading))
                    }
                }
            }
            .animation(.easeInOut(duration: 0.25), value: node.drawerOpen)
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
        case "scroll":
            // The single content view is laid out (by Yoga) taller than the viewport and
            // self-positions, so a plain vertical ScrollView hosting it scrolls as one piece.
            ScrollView(.vertical, showsIndicators: true) {
                ForEach(node.children) { CometNodeView(node: $0) }
            }
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
        let hasCorners = node.cornerTL > 0 || node.cornerTR > 0 || node.cornerBR > 0 || node.cornerBL > 0
        let elevation = node.elevation
        let shape = UnevenRoundedRectangle(
            topLeadingRadius: node.cornerTL,
            bottomLeadingRadius: node.cornerBL,
            bottomTrailingRadius: node.cornerBR,
            topTrailingRadius: node.cornerTR,
            style: .continuous)
        if hasCorners || elevation > 0 || node.borderWidth > 0 {
            content
                .clipShape(shape)
                .overlay(node.borderWidth > 0
                    ? AnyView(shape.strokeBorder(colorFromARGB(node.borderColorARGB), lineWidth: node.borderWidth))
                    : AnyView(EmptyView()))
                .shadow(color: Color.black.opacity(elevation > 0 ? 0.18 : 0),
                        radius: elevation, x: 0, y: elevation > 0 ? elevation / 2 : 0)
        } else {
            content
        }
    }
}

private func colorFromARGB(_ argb: UInt32) -> Color {
    Color(red: Double((argb >> 16) & 0xFF) / 255.0,
          green: Double((argb >> 8) & 0xFF) / 255.0,
          blue: Double(argb & 0xFF) / 255.0,
          opacity: Double((argb >> 24) & 0xFF) / 255.0)
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
