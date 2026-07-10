import UIKit
import SwiftUI

// A retained node in the SwiftUI backend tree, mirroring the Compose ComposeNode.
// C# drives these through the @objc host functions; SwiftUI observes them so a property
// or child change re-renders the narrowest scope. @Published vars are Swift-only (the C#
// side mutates them via the host functions, never directly).
// A styled slice of a FormattedText (the Comet TextRun): text + optional colour, monospace face,
// background highlight, and underline — assembled into an AttributedString for rendering.
struct CometTextRun {
    let text: String
    let color: Color?
    let mono: Bool
    let background: Color?
    let underline: Bool
}

@objc(CometNode) public class CometNode: NSObject, ObservableObject, Identifiable {
    @objc public let kind: String
    @Published var runs: [CometTextRun] = []   // FormattedText styled runs (empty => plain text)
    @Published var text: String = ""
    @Published var placeholder: String = ""
    @Published var imageUrl: String = ""
    @Published var iconName: String = ""        // cross-platform symbol name → SF Symbol
    @Published var iconGlyph: String = ""        // icon-font codepoint (when an icon font is used)
    @Published var iconFontFamily: String = ""   // icon-font family name
    @Published var isOn: Bool = false
    @Published var doubleValue: Double = 0
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var opacity: CGFloat = 1         // Comet Opacity → SwiftUI .opacity(); 1 = opaque
    @Published var isVisible: Bool = true       // Comet IsVisible; false => hidden + non-interactive
    @Published var textColorARGB: UInt32 = 0    // 0 = inherit (default foreground)
    @Published var fontSize: CGFloat = 0        // 0 = default (body)
    @Published var maxLines: Int = 0            // 0 = unlimited; else lineLimit + tail truncation
    @Published var fontWeight: Int = 0          // 0 = default; otherwise Maui FontWeight (100–900)
    @Published var fontFamily: String = ""      // custom font family (e.g. "Montserrat"); "" = system
    @Published var padding: CGFloat = 0
    // Per-edge content padding (Yoga sizes the frame to include it; leaves inset their content by it).
    @Published var padTop: CGFloat = 0
    @Published var padLeading: CGFloat = 0
    @Published var padBottom: CGFloat = 0
    @Published var padTrailing: CGFloat = 0
    // Per-corner radii (top-left, top-right, bottom-right, bottom-left); clips content.
    @Published var cornerTL: CGFloat = 0
    @Published var cornerTR: CGFloat = 0
    @Published var cornerBR: CGFloat = 0
    @Published var cornerBL: CGFloat = 0
    @Published var elevation: CGFloat = 0        // soft drop shadow depth
    @Published var borderWidth: CGFloat = 0      // stroke width (e.g. avatar ring)
    @Published var borderColorARGB: UInt32 = 0   // stroke color
    @Published var hasTapGesture: Bool = false  // view carries a Comet TapGesture
    @Published var hasLongPressGesture: Bool = false
    @Published var borderless: Bool = false      // TextField: no rounded-border box (foundation field)
    @Published var outlined: Bool = false        // Button: OutlinedButton (bordered, no fill) vs filled
    @Published var drawerOpen: Bool = false     // Drawer: side panel shown
    @Published var fabExtended: Bool = true     // Fab: true = show label (pill); false = icon-only
    @Published var scrollToken: Int = 0          // List: bumped to animate the log to the newest row
    // AlertDialog (presented as a native SwiftUI .alert).
    @Published var dialogOpen: Bool = false
    @Published var dialogMessage: String = ""
    @Published var dialogButton: String = "OK"
    // Yoga-computed parent-relative layout frame. hasFrame flips true once C# arranges this
    // node; until then the view uses native SwiftUI layout (so rendering is unchanged).
    @Published var frame: CGRect = .zero
    @Published var hasFrame: Bool = false
    @Published var contentTopInset: CGFloat = 0  // baseline-height inset (gold baselineHeight): pad content down
    // Callbacks into C# (set via host fns). ObjC blocks so .NET binds them as Actions.
    var onTap: (() -> Void)?            // Button action (-> Clicked)
    var onTapGesture: (() -> Void)?     // arbitrary-view tap gesture (-> OnGesture(Tap))
    var onLongPressGesture: (() -> Void)?   // long-press gesture (-> OnGesture(LongPress); Reply selection)
    var onRowVisibility: ((Double, Double) -> Void)?   // list rows: (index, 1=appeared/0=disappeared) -> scroll direction
    var onChangeString: ((String) -> Void)?
    var onChangeBool: ((Bool) -> Void)?
    var onChangeDouble: ((Double) -> Void)?
    var onDialogDismiss: (() -> Void)?  // native .alert dismissed (-> DialogDismissed)
    var onFocused: (() -> Void)?        // TextField gained focus (-> Focused; gold onTextFieldFocused)
    @Published var horizontal = false   // "list": row axis (LazyHStack in a horizontal ScrollView)
    @Published var iconFillFrame = false // "icon": non-square asset draws at the node frame
    var onScroll: ((Double) -> Void)?   // ScrollView scrolled (-> ScrollView.AtTop / ScrollOffset)
    var onScrollTop: ((Double) -> Void)?   // list first row visibility (-> ListView.ScrolledFromTop)

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
        case "iconglyph": node.iconGlyph = value
        case "iconfontfamily": node.iconFontFamily = value
        case "fontfamily": node.fontFamily = value
        case "dialogmessage": node.dialogMessage = value
        case "dialogbutton": node.dialogButton = value
        default: break
        }
    }

    @objc(setBool:property:value:)
    public static func setBool(_ node: CometNode, property: String, value: Bool) {
        switch property {
        case "ison": node.isOn = value
        case "hastapgesture": node.hasTapGesture = value
        case "haslongpressgesture": node.hasLongPressGesture = value
        case "borderless": node.borderless = value
        case "outlined": node.outlined = value
        case "isvisible": node.isVisible = value
        case "draweropen": node.drawerOpen = value
        case "horizontal": node.horizontal = value
        case "iconfillframe": node.iconFillFrame = value
        case "fabextended": node.fabExtended = value
        case "dialogopen": node.dialogOpen = value
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
        case "pad.t": node.padTop = CGFloat(value)
        case "pad.l": node.padLeading = CGFloat(value)
        case "pad.b": node.padBottom = CGFloat(value)
        case "pad.r": node.padTrailing = CGFloat(value)
        case "value": node.doubleValue = value
        case "corner.tl": node.cornerTL = CGFloat(value)
        case "corner.tr": node.cornerTR = CGFloat(value)
        case "corner.br": node.cornerBR = CGFloat(value)
        case "corner.bl": node.cornerBL = CGFloat(value)
        case "elevation": node.elevation = CGFloat(value)
        case "opacity": node.opacity = CGFloat(value)
        case "contenttopinset": node.contentTopInset = CGFloat(value)
        case "fontsize": node.fontSize = CGFloat(value)
        case "maxlines": node.maxLines = Int(value)
        case "fontweight": node.fontWeight = Int(value)
        case "borderwidth": node.borderWidth = CGFloat(value)
        default: break
        }
    }

    // Animate a "list" node to its newest row (the ScrollViewReader observes scrollToken).
    @objc(scrollNodeToBottom:)
    public static func scrollToBottom(_ node: CometNode) {
        node.scrollToken &+= 1
    }

    // FormattedText styled runs (rebuilt on each Text_Runs change).
    @objc(clearTextRuns:)
    public static func clearTextRuns(_ node: CometNode) {
        node.runs.removeAll()
    }

    @objc(addTextRun:text:colorArgb:hasColor:mono:bgArgb:hasBg:underline:)
    public static func addTextRun(_ node: CometNode, text: String, colorArgb: UInt32, hasColor: Bool,
                                  mono: Bool, bgArgb: UInt32, hasBg: Bool, underline: Bool) {
        node.runs.append(CometTextRun(
            text: text,
            color: hasColor ? colorFromARGB(colorArgb) : nil,
            mono: mono,
            background: hasBg ? colorFromARGB(bgArgb) : nil,
            underline: underline))
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

    @objc(setDialogDismissHandler:handler:)
    public static func setDialogDismissHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onDialogDismiss = handler
    }

    @objc(setFocusHandler:handler:)
    public static func setFocusHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onFocused = handler
    }

    @objc(setScrollHandler:handler:)
    public static func setScrollHandler(_ node: CometNode, handler: @escaping @convention(block) (Double) -> Void) {
        node.onScroll = handler
    }

    // Top-relative twin (drives ListView.ScrolledFromTop — Reply's ExtendedFAB collapse).
    @objc(setScrollTopHandler:handler:)
    public static func setScrollTopHandler(_ node: CometNode, handler: @escaping @convention(block) (Double) -> Void) {
        node.onScrollTop = handler
    }

    @objc(setLongPressGestureHandler:handler:)
    public static func setLongPressGestureHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onLongPressGesture = handler
    }

    @objc(setRowVisibilityHandler:handler:)
    public static func setRowVisibilityHandler(_ node: CometNode, handler: @escaping @convention(block) (Double, Double) -> Void) {
        node.onRowVisibility = handler
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
            // packs it tight; the wrapped height drives multi-line bubbles. A maxLines clamp
            // caps the box; the rendered Text ellipsizes via lineLimit.
            var height = ceil(rect.height)
            if node.maxLines > 0 {
                height = min(height, ceil(font.lineHeight * CGFloat(node.maxLines)))
            }
            return CGSize(width: min(ceil(rect.width), w), height: height)
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
    @FocusState private var fieldFocused: Bool   // textfield focus → node.onFocused (gold onTextFieldFocused)
    @ViewBuilder
    var body: some View {
        switch node.kind {
        case "button":
            // .plain so SwiftUI doesn't tint the label with the system accent (the Comet .Color() is
            // authoritative). The pill FILL + corner clip come from the wrapper (background + surface
            // modifiers, the filled Material Button); an OutlinedButton draws its own border instead.
            Button(action: { node.onTap?() }) {
                Text(node.text)
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
                    .padding(.horizontal, 22)
                    .padding(.vertical, 8)
                    .overlay {
                        if node.outlined {
                            Capsule().stroke(colorFromARGB(node.textColorARGB), lineWidth: 1)
                        }
                    }
            }
            .buttonStyle(.plain)
        case "textfield":
            // Borderless = the gold's foundation BasicTextField (no rounded box) — the footer input.
            if node.borderless {
                TextField(node.placeholder, text: Binding(
                    get: { node.text }, set: { node.text = $0; node.onChangeString?($0) }))
                    .textFieldStyle(.plain)
                    .focused($fieldFocused)
                    .onChange(of: fieldFocused) { now in if now { node.onFocused?() } }
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
            } else {
                TextField(node.placeholder, text: Binding(
                    get: { node.text }, set: { node.text = $0; node.onChangeString?($0) }))
                    .textFieldStyle(.roundedBorder)
                    .focused($fieldFocused)
                    .onChange(of: fieldFocused) { now in if now { node.onFocused?() } }
            }
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
            if !node.iconGlyph.isEmpty {
                // Icon-font glyph (e.g. Material Icons) — the SAME glyph the Android backend draws,
                // rendered as a sized + tinted character in the registered icon font.
                Text(node.iconGlyph)
                    .font(.custom(node.iconFontFamily, size: node.fontSize > 0 ? node.fontSize : 24))
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
            } else if let logo = bundledImage(node.iconName) {
                // A bundled brand logo (e.g. the jetchat mark) — colourful when untinted (header),
                // a tinted template when a colour is set (the mono chat-row logo). Mirrors the Android
                // multicolor-asset path. Pinned to the icon size so it measures + can't overflow the
                // text — unless iconFillFrame asks for the node's (non-square) frame (the JetNews
                // 80×24 wordmark, the Android IconFillFrame twin).
                let logoW = node.iconFillFrame && node.frame.width > 0 ? node.frame.width : (node.fontSize > 0 ? node.fontSize : 24)
                let logoH = node.iconFillFrame && node.frame.height > 0 ? node.frame.height : (node.fontSize > 0 ? node.fontSize : 24)
                if node.textColorARGB != 0 {
                    Image(uiImage: logo).renderingMode(.template).resizable().aspectRatio(contentMode: .fit)
                        .frame(width: logoW, height: logoH)
                        .modifier(ForegroundModifier(argb: node.textColorARGB))
                } else {
                    Image(uiImage: logo).resizable().aspectRatio(contentMode: .fit)
                        .frame(width: logoW, height: logoH)
                }
            } else {
                // Real SF Symbol, tinted + sized — the iOS native icon idiom.
                Image(systemName: sfSymbol(node.iconName))
                    .font(.system(size: node.fontSize > 0 ? node.fontSize : 24))
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
            }
        case "image":
            // A bundled image (a bare name like "ali") renders from the app bundle — the iOS
            // counterpart of Compose's painterResource; an http(s) source loads asynchronously.
            if node.imageUrl.lowercased().hasPrefix("http") {
                AsyncImage(url: URL(string: node.imageUrl)) { phase in
                    if let image = phase.image {
                        image.resizable().aspectRatio(contentMode: .fill)
                    } else {
                        Color.gray.opacity(0.25)
                    }
                }
                .clipped()
            } else if let ui = bundledImage(node.imageUrl) {
                Image(uiImage: ui).resizable().aspectRatio(contentMode: .fill).clipped()
            } else {
                Color.gray.opacity(0.25)
            }
        default: // "text" and unknown leaves
            if !node.runs.isEmpty {
                // FormattedText: styled runs (the gold's AnnotatedString) → concatenated Text, so
                // per-run colour / code / link styling renders (e.g. white text on the primary bubble).
                runsText(node)
                    .modifier(FontModifier(node: node))
            } else {
                Text(node.text)
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
                    .lineLimit(node.maxLines > 0 ? node.maxLines : nil)
            }
        }
    }
}

// A bundled image by bare name: the asset catalog / .png via UIImage(named:), else a loose
// resource file (e.g. someone_else.jpg) found by trying common extensions in the main bundle.
private func bundledImage(_ name: String) -> UIImage? {
    if let img = UIImage(named: name) { return img }
    for ext in ["png", "jpg", "jpeg"] {
        if let path = Bundle.main.path(forResource: name, ofType: ext),
           let img = UIImage(contentsOfFile: path) {
            return img
        }
    }
    return nil
}

// Assemble a FormattedText's styled runs by concatenating SwiftUI Text segments (this measures
// reliably via sizeThatFits, unlike Text(AttributedString)). Per-run colour wins; monospace runs
// use a system monospaced face at the base size; underline = links. (Per-run background — code-span
// highlight — isn't expressible via Text concatenation, so it's dropped; the mono face still reads.)
private func runsText(_ node: CometNode) -> Text {
    let baseSize = node.fontSize > 0 ? node.fontSize : 16
    var combined = Text(verbatim: "")
    for run in node.runs {
        var seg = Text(verbatim: run.text)
        if let c = run.color { seg = seg.foregroundColor(c) }
        if run.mono { seg = seg.font(.system(size: baseSize, design: .monospaced)) }
        if run.underline { seg = seg.underline() }
        combined = combined + seg
    }
    return combined
}

// Cross-platform symbol name → SF Symbol (the iOS counterpart of the Compose Icons mapping).
private func sfSymbol(_ name: String) -> String {
    // Cross-platform GENERIC names (the shared sample uses Android/Material-ish names) → the closest
    // SF Symbol. Anything not listed falls through and, if it's itself a real SF Symbol name, is used
    // verbatim — so the WHOLE SF Symbol library is available (e.g. Icon("mic.fill"), Icon("paperplane"))
    // without adding a case here.
    switch name {
    case "search": return "magnifyingglass"
    case "info": return "info.circle"
    case "menu": return "line.3.horizontal"
    case "send": return "paperplane.fill"
    case "place", "location": return "mappin.circle.fill"
    case "mic", "microphone": return "mic"
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
    case "arrow_down", "arrow_downward", "expand_more": return "chevron.down"
    case "add": return "plus"
    case "edit": return "pencil"
    case "mood", "emoji": return "face.smiling"
    case "at": return "at"
    case "photo", "image": return "photo"
    case "video", "duo": return "video"
    default:
        // Pass the name through if it's a valid SF Symbol; otherwise a clear "unknown" glyph.
        return UIImage(systemName: name) != nil ? name : "questionmark.square.dashed"
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
    // Prefer the real per-weight face by PostScript name (e.g. "Montserrat-Medium", "Karla-Bold") so
    // the actual weight renders rather than a synthesized one.
    if let f = UIFont(name: weightedFontName(family, weight), size: size) { return f }
    guard let base = UIFont(name: family, size: size) else { return nil }
    let traits: [UIFontDescriptor.TraitKey: Any] = [.weight: uiFontWeight(weight)]
    let descriptor = base.fontDescriptor.addingAttributes([.traits: traits])
    return UIFont(descriptor: descriptor, size: size)
}

// "<Family>-<Weight>" PostScript-name convention for the bundled per-weight faces.
private func weightedFontName(_ family: String, _ weight: Int) -> String {
    let suffix: String
    switch weight {
    case 700...: suffix = "Bold"
    case 600..<700: suffix = "SemiBold"
    case 500..<600: suffix = "Medium"
    default: suffix = "Regular"
    }
    return "\(family)-\(suffix)"
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
            .modifier(OpacityModifier(node: node)) // Comet Opacity / IsVisible (applied last so a
                                                   // hidden node is also non-interactive)
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
            // Pin the screen to the TOP of the nav area: when the keyboard shrinks the screen's
            // laid-out height below the nav's full height, the shorter screen must stay top-aligned
            // (so its footer sits just above the keyboard) instead of centering in the leftover space.
            ZStack(alignment: .topLeading) {
                if let top = node.children.last { CometNodeView(node: top) } else { EmptyView() }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
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
        case "list" where node.horizontal:
            // Horizontal list (JetNews' popular carousel): the LazyHStack twin of the
            // Compose LazyRow branch. Rows are Yoga-laid at their intrinsic width by C#;
            // each renders as its own lazy item.
            ScrollView(.horizontal, showsIndicators: false) {
                LazyHStack(spacing: 0) {
                    ForEach(node.children) { child in
                        CometNodeView(node: child)
                            .frame(width: child.frame.width, height: child.frame.height)
                            .id(child.id)
                    }
                }
            }
        case "list":
            // Full-bleed, separator-free rows so a Yoga-laid-out row (which already carries its
            // own padding) spans edge-to-edge exactly like the Compose LazyColumn. Wrapped in a
            // ScrollViewReader so C# can animate the log to the newest message (JumpToBottom /
            // after-send) by bumping node.scrollToken — the native counterpart of the Compose
            // LazyListState scroller (Comet's IListView.ScrollToBottom -> scrollNodeToBottom).
            ScrollViewReader { proxy in
                List {
                    ForEach(node.children) { child in
                        CometNodeView(node: child)
                            .listRowInsets(EdgeInsets())
                            .listRowSeparator(.hidden)
                            .id(child.id)
                            // Drive ScrolledAway (the JumpToBottom FAB): the newest message is the last
                            // child — when it's on screen we're at the bottom (0), when it scrolls off
                            // we're scrolled away (1). The C# list node maps this onto IListView.ScrolledAway.
                            .onAppear {
                                if child.id == node.children.last?.id { node.onScroll?(0) }
                                if child.id == node.children.first?.id { node.onScrollTop?(0) }
                                if let idx = node.children.firstIndex(where: { $0.id == child.id }) {
                                    node.onRowVisibility?(Double(idx), 1)
                                }
                            }
                            .onDisappear {
                                if child.id == node.children.last?.id { node.onScroll?(1) }
                                if child.id == node.children.first?.id { node.onScrollTop?(1) }
                                if let idx = node.children.firstIndex(where: { $0.id == child.id }) {
                                    node.onRowVisibility?(Double(idx), 0)
                                }
                            }
                    }
                }
                .listStyle(.plain)
                .onChange(of: node.scrollToken) { _ in
                    if let last = node.children.last {
                        withAnimation { proxy.scrollTo(last.id, anchor: .bottom) }
                    }
                }
            }
        case "scroll":
            // The single content view is laid out (by Yoga) taller than the viewport and
            // self-positions, so a plain vertical ScrollView hosting it scrolls as one piece.
            // The scroll offset is reported back to C# (-> the ScrollView's AtTop / ScrollOffset,
            // which drive the profile FAB's collapse on scroll).
            if #available(iOS 18.0, *) {
                // onScrollGeometryChange observes the live scroll geometry, so it fires for BOTH a
                // finger scroll and a programmatic contentOffset change. A GeometryReader/preference
                // (the fallback below) only re-fires on a SwiftUI layout pass, which a UIScrollView
                // contentOffset change does NOT trigger — so it silently misses programmatic scrolls.
                ScrollView(.vertical, showsIndicators: true) {
                    ForEach(node.children) { CometNodeView(node: $0) }
                }
                .onScrollGeometryChange(for: Double.self) { geo in
                    Double(geo.contentOffset.y + geo.contentInsets.top)
                } action: { _, newValue in
                    node.onScroll?(newValue)
                }
            } else {
                // Pre-iOS-18 fallback: GeometryReader probe + preference (onScrollGeometryChange is iOS 18+).
                ScrollView(.vertical, showsIndicators: true) {
                    ForEach(node.children) { CometNodeView(node: $0) }
                        .background(GeometryReader { geo in
                            Color.clear.preference(key: CometScrollOffsetKey.self,
                                                   value: -geo.frame(in: .named("cometScroll")).minY)
                        })
                }
                .coordinateSpace(name: "cometScroll")
                .onPreferenceChange(CometScrollOffsetKey.self) { value in node.onScroll?(Double(value)) }
            }
        case "alert":
            // Native SwiftUI .alert (the iOS counterpart of Compose's AlertDialog): a zero-size
            // host that presents modally when C# opens the dialog; the button / scrim dismiss
            // routes back to C# (DialogDismissed).
            Color.clear.frame(width: 0, height: 0)
                .alert(node.dialogMessage, isPresented: Binding(
                    get: { node.dialogOpen },
                    set: { node.dialogOpen = $0; if !$0 { node.onDialogDismiss?() } })) {
                    Button(node.dialogButton, role: .cancel) {}
                }
        // Cross-axis alignment matches Yoga's flex-start default (and Compose's Row=Top / Column=Start),
        // NOT SwiftUI's center default — so any subtree not yet Yoga-arranged still aligns like Android
        // instead of centering. Spacing is 0 because Yoga owns gaps (the arranged path bakes them into
        // the child offsets); the native default 8pt would double them where the fallback is hit.
        case "fab":
            // iOS has no Material FAB, so the native idiom is a real Button (so it raises the
            // tap, gets the system press feedback, and reads as a button to accessibility) whose
            // content is an icon + optional label row. The node's frame positions + sizes it
            // (from Yoga); the CometNodeView modifiers add the capsule background.
            // fabExtended drives label show/hide with a 200ms easeInOut — matching the gold's
            // AnimatingFabContent transition duration (transitionDuration = 200).
            Button(action: { node.onTap?() }) {
                HStack(spacing: 8) {
                    if let icon = node.children.first { CometNodeView(node: icon) }
                    if node.fabExtended, node.children.count > 1 {
                        CometNodeView(node: node.children[1])
                    }
                }
                .animation(.easeInOut(duration: 0.2), value: node.fabExtended)
                .fixedSize(horizontal: true, vertical: false)   // the label is single-line — never wrap it
                .frame(maxWidth: .infinity, maxHeight: .infinity) // center the row within the capsule frame
                .padding(.horizontal, node.padding)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
        case "hstack":
            HStack(alignment: .top, spacing: 0) { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "zstack":
            ZStack(alignment: .topLeading) { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "vstack":
            VStack(alignment: .leading, spacing: 0) { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "textfield":
            // Inset the field's content by its (Yoga-sized) padding so the placeholder/text sit padded
            // in, matching the gold (e.g. the footer input's 20/14/20/10). Render-only — the measure
            // path hosts CometLeafContent directly, so the padding isn't double-counted into the frame.
            CometLeafContent(node: node)
                .padding(EdgeInsets(top: node.padTop + node.contentTopInset, leading: node.padLeading,
                                    bottom: node.padBottom, trailing: node.padTrailing))
        default:
            // Leaves honour their own (Yoga-sized) content padding — e.g. a section-header Text
            // padded 28 in / 18 tall. Render-only (the measure path hosts CometLeafContent directly,
            // so the padding isn't double-counted into the frame).
            CometLeafContent(node: node)
                .padding(EdgeInsets(top: node.padTop + node.contentTopInset, leading: node.padLeading,
                                    bottom: node.padBottom, trailing: node.padTrailing))
        }
    }
}

// Sizes a node to its Yoga-computed frame (observing the node, so reflow re-applies it).
// Icons CENTER in their frame (a 24dp star glyph in a 40dp circle — nothing else would
// center it, and topLeading pinned it to the corner); everything else keeps topLeading,
// matching Yoga's coordinate origin.
private struct SizeModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        if node.hasFrame {
            content.frame(width: node.frame.width, height: node.frame.height,
                          alignment: node.kind == "icon" ? .center : .topLeading)
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

// Applies Comet's Opacity / IsVisible. A node that is hidden (IsVisible == false) or fully
// transparent (Opacity ~ 0) is also made non-interactive via allowsHitTesting(false), so an
// invisible overlay in a ZStack lets touches fall through to the views beneath it.
private struct OpacityModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        let alpha = node.isVisible ? node.opacity : 0
        return content
            .opacity(alpha)
            .allowsHitTesting(alpha > 0.01)
    }
}

// Carries the scroll content's offset (negative minY in the scroll coordinate space) up to the
// ScrollView via a SwiftUI preference, so C# can mirror it onto AtTop / ScrollOffset.
private struct CometScrollOffsetKey: PreferenceKey {
    static var defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) { value = nextValue() }
}

// Routes a SwiftUI tap on an arbitrary view to the node's gesture callback (the iOS
// counterpart of Compose's Modifier.Clickable). contentShape makes the whole padded
// frame — including transparent areas of a stack — hittable.
private struct TapGestureModifier: ViewModifier {
    @ObservedObject var node: CometNode
    func body(content: Content) -> some View {
        if node.hasTapGesture && node.hasLongPressGesture {
            content
                .contentShape(Rectangle())
                .onTapGesture { node.onTapGesture?() }
                .onLongPressGesture { node.onLongPressGesture?() }
        } else if node.hasTapGesture {
            content
                .contentShape(Rectangle())
                .onTapGesture { node.onTapGesture?() }
        } else if node.hasLongPressGesture {
            content
                .contentShape(Rectangle())
                .onLongPressGesture { node.onLongPressGesture?() }
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
