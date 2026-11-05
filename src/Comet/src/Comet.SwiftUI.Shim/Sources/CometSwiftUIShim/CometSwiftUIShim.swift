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

private struct CometManualSafeAreaLayoutKey: EnvironmentKey {
    static let defaultValue = false
}

private struct CometNativeMeasurementKey: EnvironmentKey {
    static let defaultValue = false
}

private extension EnvironmentValues {
    var cometManualSafeAreaLayout: Bool {
        get { self[CometManualSafeAreaLayoutKey.self] }
        set { self[CometManualSafeAreaLayoutKey.self] = newValue }
    }

    var cometNativeMeasurement: Bool {
        get { self[CometNativeMeasurementKey.self] }
        set { self[CometNativeMeasurementKey.self] = newValue }
    }
}

@objc(CometNode) public class CometNode: NSObject, ObservableObject, Identifiable {
    @objc public let kind: String
    @Published var runs: [CometTextRun] = []   // FormattedText styled runs (empty => plain text)
    @Published var text: String = ""
    @Published var automationId: String = ""
    @Published var placeholder: String = ""
    @Published var imageUrl: String = ""
    @Published var imageData: Data = Data()
    @Published var iconName: String = ""        // cross-platform symbol name → SF Symbol
    @Published var iconGlyph: String = ""        // icon-font codepoint (when an icon font is used)
    @Published var iconFontFamily: String = ""   // icon-font family name
    @Published var isOn: Bool = false
    @Published var doubleValue: Double = 0
    @Published var children: [CometNode] = []
    @Published var backgroundARGB: UInt32 = 0   // 0 = none
    @Published var gradientStops: [UInt32] = [] // linear-gradient fill (wins over background)
    @Published var gradientDirection = 0 // 0 horizontal, 1 vertical, 2 diagonal (TL→BR)
    @Published var opacity: CGFloat = 1         // Comet Opacity → SwiftUI .opacity(); 1 = opaque
    @Published var isVisible: Bool = true       // Comet IsVisible; false => hidden + non-interactive
    @Published var textColorARGB: UInt32 = 0    // 0 = inherit (default foreground)
    @Published var fontSize: CGFloat = 0        // 0 = default (body)
    @Published var maxLines: Int = 0            // 0 = unlimited; else lineLimit + tail truncation
    @Published var characterSpacing: CGFloat = 0
    @Published var lineBreakMode: Int = 0
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
    @Published var borderGradientStops: [UInt32] = []   // gradient stroke (wins over color)
    @Published var borderGradientDirection = 2 // 0 horizontal, 1 vertical, 2 diagonal (TL→BR), 3 radial
    @Published var hasTapGesture: Bool = false  // view carries a Comet TapGesture
    @Published var hasLongPressGesture: Bool = false
    @Published var hasRecordGesture: Bool = false
    @Published var clipCircle: Bool = false
    @Published var borderless: Bool = false      // TextField: no rounded-border box (foundation field)
    @Published var outlined: Bool = false        // Button: OutlinedButton (bordered, no fill) vs filled
    @Published var buttonHasExplicitPadding: Bool = false
    @Published var drawerOpen: Bool = false     // Drawer: side panel shown
    @Published var fabExtended: Bool = true     // Fab: true = show label (pill); false = icon-only
    @Published var scrollToken: Int = 0          // List: bumped to animate the log to the newest row
    var lastAppliedScrollToken: Int = 0          // List: prevents replay when the native view reappears
    @Published var listEndSpacing: CGFloat = 0   // List: scrollable start/end space for centered boundary rows
    @Published var listScrollTargetIndex: Int = -1 // List: >= 0 targets a row; -1 targets the last row
    @Published var listScrollPosition: Int = 0   // List: 0 = start, 1 = center
    @Published var listScrollAnimated: Bool = false
    @Published var listContentGeneration: Int = 0
    @Published var listUsesLazyVStack: Bool = false // Centered retained lists avoid List row reuse during rebuilds
    @Published var listSnapsToCenter: Bool = false
    // AlertDialog (presented as a native SwiftUI .alert).
    @Published var dialogOpen: Bool = false
    private(set) var dialogPresentation: UInt64 = 0
    @Published var dialogTitle: String = ""
    @Published var dialogMessage: String = ""
    @Published var dialogButton: String = "OK"
    @Published var dialogDismissButton: String = ""
    @Published var dialogHasDismissButton: Bool = false
    // Yoga-computed parent-relative layout frame. hasFrame flips true once C# arranges this
    // node; until then the view uses native SwiftUI layout (so rendering is unchanged).
    @Published var frame: CGRect = .zero
    @Published var hasFrame: Bool = false
    @Published var contentTopInset: CGFloat = 0  // baseline-height inset (gold baselineHeight): pad content down
    // Callbacks into C# (set via host fns). ObjC blocks so .NET binds them as Actions.
    var onTap: (() -> Void)?            // Button action (-> Clicked)
    var onTapGesture: (() -> Void)?     // arbitrary-view tap gesture (-> OnGesture(Tap))
    var onLongPressGesture: (() -> Void)?   // long-press gesture (-> OnGesture(LongPress); Reply selection)
    var onRecordGesture: ((Double, Double, Double) -> Void)?
    var onRowVisibility: ((Double, Double) -> Void)?   // list rows: (index, 1=appeared/0=disappeared) -> scroll direction
    var onChangeString: ((String) -> Void)?
    var onChangeBool: ((Bool) -> Void)?
    var onChangeDouble: ((Double) -> Void)?
    var onDialogDismiss: (() -> Void)?  // native .alert dismissed (-> DialogDismissed)
    var onDialogConfirm: (() -> Void)?
    var onDialogDismissAction: (() -> Void)?
    var onFocused: (() -> Void)?        // TextField gained focus (-> Focused; gold onTextFieldFocused)
    var onCompleted: (() -> Void)?      // keyboard Done/submit (-> Completed)
    @Published var focusRequested: Bool = false  // DevFlow: C# requests native keyboard focus
    @Published var keyboardTypeCode: Int = 0     // 0=Default 1=Numeric 2=Email 3=Url 4=Telephone 5=Chat 6=Plain
    @Published var horizontal = false   // "list": row axis (LazyHStack in a horizontal ScrollView)
    @Published var iconFillFrame = false // "icon": non-square asset draws at the node frame
    @Published var fontItalic = false
    var onScroll: ((Double) -> Void)?   // ScrollView scrolled (-> ScrollView.AtTop / ScrollOffset)
    var onScrollTop: ((Double) -> Void)?   // list first row visibility (-> ListView.ScrolledFromTop)
    var onSelectionChanged: ((Double) -> Void)?   // TabView: tab selection changed (-> int index)
    var onDateChanged: ((Double) -> Void)?   // DatePicker: date changed (-> epoch seconds)
    @Published var selectedIndex: Int = 0   // TabView: active tab index
    @Published var dateEpochSeconds: Double = 0   // DatePicker: committed date as epoch seconds
    @Published var datePickerDraftEpochSeconds: Double = 0
    @Published var datePickerOpen: Bool = false   // DatePicker: dialog presentation state
    @Published var datePickerDialogMode: Bool = false // DatePicker: zero-sized sheet host vs inline control
    private var datePickerDraftIsDirty = false
    @Published var dateMinimumEpochSeconds: Double?
    @Published var dateMaximumEpochSeconds: Double?
    @Published var sliderMin: Double = 0   // Slider: minimum value
    @Published var sliderMax: Double = 1   // Slider: maximum value
    @Published var isRefreshing: Bool = false
    var onRefresh: (() -> Void)?
    var onBackRequest: (() -> Void)?
    var onSystemThemeChanged: (() -> Void)?
    @Published var navigationPath: [Int] = []
    @Published var backVisible: Bool = true
    @Published var backEnabled: Bool = true
    @Published var backTitle: String = ""
    // Yoga/manual-layout hosts own the raw window and apply Comet's published insets
    // explicitly. SwiftUI's hosting safe area must not inset that coordinate space again.
    @Published var manualSafeAreaLayout: Bool = false
    var onRefreshEnded: (() -> Void)?

    public var id: ObjectIdentifier { ObjectIdentifier(self) }
    func setDialogOpen(_ value: Bool) {
        if value && !dialogOpen {
            dialogPresentation &+= 1
        }
        dialogOpen = value
    }

    func dismissDialog(presentation: UInt64) {
        // A dismissed SwiftUI alert can deliver its binding update after another alert opens.
        guard presentation == dialogPresentation, dialogOpen else { return }
        dialogOpen = false
        onDialogDismiss?()
    }

    func setCommittedDateEpoch(_ value: Double) {
        dateEpochSeconds = value
        if !datePickerOpen || !datePickerDraftIsDirty {
            datePickerDraftEpochSeconds = value
        }
    }

    func setDatePickerOpen(_ value: Bool) {
        if value && !datePickerOpen {
            datePickerDraftEpochSeconds = dateEpochSeconds
            datePickerDraftIsDirty = false
        } else if !value {
            datePickerDraftEpochSeconds = dateEpochSeconds
            datePickerDraftIsDirty = false
        }
        datePickerOpen = value
    }

    func setDatePickerDraftEpoch(_ value: Double) {
        datePickerDraftEpochSeconds = value
        datePickerDraftIsDirty = true
    }

    func dismissDatePicker() {
        guard datePickerOpen else { return }
        datePickerDraftEpochSeconds = dateEpochSeconds
        datePickerDraftIsDirty = false
        datePickerOpen = false
        onDialogDismiss?()
    }

    func commitDatePickerDraft() {
        guard datePickerOpen else { return }
        let committed = datePickerDraftEpochSeconds
        dateEpochSeconds = committed
        datePickerDraftIsDirty = false
        datePickerOpen = false
        onDateChanged?(committed)
    }

    var selectableDateRange: ClosedRange<Date> {
        let lower = dateMinimumEpochSeconds.map(localCalendarDate(fromProtocolEpoch:)) ?? Date.distantPast
        let upper = dateMaximumEpochSeconds.map(localCalendarDate(fromProtocolEpoch:)) ?? Date.distantFuture
        return lower <= upper ? lower...upper : lower...lower
    }

    @objc public init(kind: String) {
        self.kind = kind
        super.init()
    }

    /// Maps the C# keyboard code (0=Default 1=Numeric 2=Email 3=Url 4=Telephone 5=Chat 6=Plain)
    /// to a SwiftUI-compatible UIKeyboardType.
    var swiftUIKeyboardType: UIKeyboardType {
        switch keyboardTypeCode {
        case 1: return .decimalPad   // Numeric — allows decimal values like 6.5
        case 2: return .emailAddress
        case 3: return .URL
        case 4: return .phonePad
        case 6: return .asciiCapable // Plain
        default: return .default     // Default, Text, Chat
        }
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
        case "automationid": node.automationId = value
        case "placeholder": node.placeholder = value
        case "imageurl": node.imageUrl = value
        case "icon": node.iconName = value
        case "iconglyph": node.iconGlyph = value
        case "iconfontfamily": node.iconFontFamily = value
        case "fontfamily": node.fontFamily = value
        case "dialogmessage": node.dialogMessage = value
        case "dialogbutton": node.dialogButton = value
        case "dialogtitle": node.dialogTitle = value
        case "dialogdismissbutton": node.dialogDismissButton = value
        case "backtitle": node.backTitle = value
        default: break
        }
    }

    @objc(setBool:property:value:)
    public static func setBool(_ node: CometNode, property: String, value: Bool) {
        switch property {
        case "ison": node.isOn = value
        case "hastapgesture": node.hasTapGesture = value
        case "haslongpressgesture": node.hasLongPressGesture = value
        case "hasrecordgesture": node.hasRecordGesture = value
        case "clipcircle": node.clipCircle = value
        case "borderless": node.borderless = value
        case "outlined": node.outlined = value
        case "buttonhasexplicitpadding": node.buttonHasExplicitPadding = value
        case "isvisible": node.isVisible = value
        case "draweropen": node.drawerOpen = value
        case "horizontal": node.horizontal = value
        case "iconfillframe": node.iconFillFrame = value
        case "fontitalic": node.fontItalic = value
        case "fabextended": node.fabExtended = value
        case "dialogopen": node.setDialogOpen(value)
        case "dialoghasdismissbutton": node.dialogHasDismissButton = value
        case "datepickeropen": node.setDatePickerOpen(value)
        case "datepickerdialogmode": node.datePickerDialogMode = value
        case "refreshing": node.isRefreshing = value
        case "backvisible": node.backVisible = value
        case "backenabled": node.backEnabled = value
        case "manualsafearealayout": node.manualSafeAreaLayout = value
        case "focusrequested": node.focusRequested = value
        case "lazyvstack": node.listUsesLazyVStack = value
        case "snapcenter": node.listSnapsToCenter = value
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
        case "listendspacing": node.listEndSpacing = CGFloat(value)
        case "fontsize": node.fontSize = CGFloat(value)
        case "maxlines": node.maxLines = Int(value)
        case "characterspacing": node.characterSpacing = CGFloat(value)
        case "linebreakmode": node.lineBreakMode = Int(value)
        case "fontweight": node.fontWeight = Int(value)
        case "borderwidth": node.borderWidth = CGFloat(value)
        case "gradientdirection": node.gradientDirection = Int(value)
        case "bordergradientdirection": node.borderGradientDirection = Int(value)
        case "selectedindex": node.selectedIndex = Int(value)
        case "dateepoch": node.setCommittedDateEpoch(value)
        case "datemin": node.dateMinimumEpochSeconds = value.isNaN ? nil : value
        case "datemax": node.dateMaximumEpochSeconds = value.isNaN ? nil : value
        case "slidermin": node.sliderMin = value
        case "slidermax": node.sliderMax = value
        case "keyboardtype": node.keyboardTypeCode = Int(value)
        case "navigationdepth":
            let depth = max(0, Int(value))
            node.navigationPath = depth > 0 ? Array(1...depth) : []
        default: break
        }
    }

    @objc(setData:data:)
    public static func setData(_ node: CometNode, data: Data) {
        node.imageData = data
    }

    // Animate a "list" node to its newest row (the ScrollViewReader observes scrollToken).
    @objc(clearGradientStops:)
    public static func clearGradientStops(_ node: CometNode) {
        node.gradientStops = []
    }

    @objc(clearBorderGradientStops:)
    public static func clearBorderGradientStops(_ node: CometNode) {
        node.borderGradientStops = []
    }

    @objc(addBorderGradientStop:argb:)
    public static func addBorderGradientStop(_ node: CometNode, argb: UInt32) {
        node.borderGradientStops.append(argb)
    }

    @objc(addGradientStop:argb:)
    public static func addGradientStop(_ node: CometNode, argb: UInt32) {
        node.gradientStops.append(argb)
    }

    @objc(scrollNodeToBottom:)
    public static func scrollToBottom(_ node: CometNode) {
        node.listScrollTargetIndex = -1
        node.scrollToken &+= 1
    }

    @objc(scrollNode:toIndex:position:)
    public static func scroll(_ node: CometNode, toIndex index: Int, position: Int) {
        node.listScrollTargetIndex = index
        node.listScrollPosition = position
        node.listScrollAnimated = false
        node.scrollToken &+= 1
    }

    @objc(scrollNode:toIndex:position:animated:)
    public static func scrollAnimated(
        _ node: CometNode,
        toIndex index: Int,
        position: Int,
        animated: Bool) {
        node.listScrollTargetIndex = index
        node.listScrollPosition = position
        node.listScrollAnimated = animated
        node.scrollToken &+= 1
    }

    @objc(markListContentReady:)
    public static func markListContentReady(_ node: CometNode) {
        node.listContentGeneration &+= 1
    }

    @objc(resetListScrollReplay:)
    public static func resetListScrollReplay(_ node: CometNode) {
        node.lastAppliedScrollToken = 0
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

    @objc(setDialogConfirmHandler:handler:)
    public static func setDialogConfirmHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onDialogConfirm = handler
    }

    @objc(setDialogDismissActionHandler:handler:)
    public static func setDialogDismissActionHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onDialogDismissAction = handler
    }

    @objc(setFocusHandler:handler:)
    public static func setFocusHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onFocused = handler
    }

    @objc(setCompletedHandler:handler:)
    public static func setCompletedHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onCompleted = handler
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

    @objc(setRecordGestureHandler:handler:)
    public static func setRecordGestureHandler(
        _ node: CometNode,
        handler: @escaping @convention(block) (Double, Double, Double) -> Void
    ) {
        node.onRecordGesture = handler
    }

    @objc(setRowVisibilityHandler:handler:)
    public static func setRowVisibilityHandler(_ node: CometNode, handler: @escaping @convention(block) (Double, Double) -> Void) {
        node.onRowVisibility = handler
    }

    @objc(setSelectionChangedHandler:handler:)
    public static func setSelectionChangedHandler(_ node: CometNode, handler: @escaping @convention(block) (Double) -> Void) {
        node.onSelectionChanged = handler
    }

    @objc(setDateChangedHandler:handler:)
    public static func setDateChangedHandler(_ node: CometNode, handler: @escaping @convention(block) (Double) -> Void) {
        node.onDateChanged = handler
    }

    @objc(setRefreshHandler:handler:)
    public static func setRefreshHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onRefresh = handler
    }

    @objc(setRefreshEndedHandler:handler:)
    public static func setRefreshEndedHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onRefreshEnded = handler
    }

    @objc(setBackRequestHandler:handler:)
    public static func setBackRequestHandler(_ node: CometNode, handler: @escaping @convention(block) () -> Void) {
        node.onBackRequest = handler
    }

    @objc(setSystemThemeChangedHandler:handler:)
    public static func setSystemThemeChangedHandler(
        _ node: CometNode,
        handler: @escaping @convention(block) () -> Void
    ) {
        node.onSystemThemeChanged = handler
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
            let paragraph = NSMutableParagraphStyle()
            switch node.lineBreakMode {
            case 1: paragraph.lineBreakMode = .byCharWrapping
            case 2: paragraph.lineBreakMode = .byClipping
            case 3: paragraph.lineBreakMode = .byTruncatingHead
            case 4: paragraph.lineBreakMode = .byTruncatingTail
            case 5: paragraph.lineBreakMode = .byTruncatingMiddle
            default: paragraph.lineBreakMode = .byWordWrapping
            }
            let rect = (node.text as NSString).boundingRect(
                with: CGSize(width: w, height: .greatestFiniteMagnitude),
                options: [.usesLineFragmentOrigin, .usesFontLeading],
                attributes: [.font: font, .kern: node.characterSpacing, .paragraphStyle: paragraph],
                context: nil)
            // Report the ACTUAL used width (≤ constraint) so a short label hugs and the flex row
            // packs it tight; the wrapped height drives multi-line bubbles. A maxLines clamp
            // caps the box; the rendered Text ellipsizes via lineLimit.
            var height = ceil(rect.height)
            let truncates = node.lineBreakMode >= 2
            let effectiveMax = truncates ? 1 : node.maxLines
            if effectiveMax > 0 {
                height = min(height, ceil(font.lineHeight * CGFloat(effectiveMax)))
            }
            return CGSize(width: min(ceil(rect.width), w), height: height)
        }

        // Icon: a square box at the symbol's point size.
        if node.kind == "icon" {
            let s = node.fontSize > 0 ? node.fontSize : 24
            return CGSize(width: s, height: s)
        }

        if node.kind == "texteditor" {
            let size = node.fontSize > 0 ? node.fontSize : UIFont.preferredFont(forTextStyle: .body).pointSize
            let font = UIFont.systemFont(ofSize: size, weight: uiFontWeight(node.fontWeight))
            let rect = (node.text as NSString).boundingRect(
                with: CGSize(width: w, height: .greatestFiniteMagnitude),
                options: [.usesLineFragmentOrigin, .usesFontLeading],
                attributes: [.font: font],
                context: nil)
            return CGSize(width: w, height: max(96, ceil(rect.height) + 32))
        }

        if node.kind == "datepicker" {
            let host = UIHostingController(rootView: CometNodeView(node: node))
            host.view.backgroundColor = .clear
            return host.sizeThatFits(in: CGSize(width: w, height: .greatestFiniteMagnitude))
        }

        if node.kind == "button" && node.buttonHasExplicitPadding {
            // The hosting measurement includes the explicit content padding rendered below.
            // Yoga/Grid owns that outer-box padding, so remove it here and let the layout
            // engine add it once. Any remaining native Button minimum stays intact.
            let horizontalPadding = node.padLeading + node.padTrailing
            let verticalPadding = node.padTop + node.padBottom
            let measuredContent = CometLeafContent(node: node)
                .environment(\.cometNativeMeasurement, true)
            let host = UIHostingController(rootView: measuredContent)
            host.view.backgroundColor = .clear
            let measured = host.sizeThatFits(in: CGSize(
                width: w + horizontalPadding,
                height: .greatestFiniteMagnitude))
            return CGSize(
                width: max(0, measured.width - horizontalPadding),
                height: max(0, measured.height - verticalPadding))
        }

        // Interactive controls (button/textfield/toggle/slider) don't wrap; SwiftUI sizes them.
        let measuredContent = CometLeafContent(node: node)
            .environment(\.cometNativeMeasurement, true)
        let host = UIHostingController(rootView: measuredContent)
        host.view.backgroundColor = .clear
        return host.sizeThatFits(in: CGSize(width: w, height: .greatestFiniteMagnitude))
    }

    // Returns a UIViewController hosting the SwiftUI tree rooted at `root`.
    @objc(hostControllerForRoot:)
    public static func hostController(_ root: CometNode) -> UIViewController {
        let rootView = CometNodeView(node: root)
            .environment(\.cometManualSafeAreaLayout, root.manualSafeAreaLayout)
            .modifier(ManualKeyboardSafeAreaModifier(enabled: root.manualSafeAreaLayout))
        let controller = CometHostingController(rootView: rootView)
        if root.manualSafeAreaLayout {
            if #available(iOS 16.4, *) {
                controller.safeAreaRegions = []
            }
        }
        controller.onSystemThemeChanged = { root.onSystemThemeChanged?() }
        return controller
    }

    private final class CometHostingController<Content: View>: UIHostingController<Content> {
        var onSystemThemeChanged: (() -> Void)?

        override func traitCollectionDidChange(_ previousTraitCollection: UITraitCollection?) {
            super.traitCollectionDidChange(previousTraitCollection)
            if previousTraitCollection?.userInterfaceStyle != traitCollection.userInterfaceStyle {
                onSystemThemeChanged?()
            }
        }
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
    @Environment(\.cometNativeMeasurement) private var nativeMeasurement
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
                    .modifier(ButtonContentPaddingModifier(node: node))
                    .modifier(ButtonHitAreaModifier(
                        fillFrame: node.hasFrame && !nativeMeasurement))
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
                    .keyboardType(node.swiftUIKeyboardType)
                    .focused($fieldFocused)
                    .modifier(TextInputAccessoryModifier(
                        isFocused: $fieldFocused,
                        onCompleted: { node.onCompleted?() }))
                    .onChange(of: fieldFocused) { now in if now { node.onFocused?() } }
                    .onChange(of: node.focusRequested) { req in if req { fieldFocused = true; node.focusRequested = false } }
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
            } else {
                TextField(node.placeholder, text: Binding(
                    get: { node.text }, set: { node.text = $0; node.onChangeString?($0) }))
                    .textFieldStyle(.roundedBorder)
                    .keyboardType(node.swiftUIKeyboardType)
                    .focused($fieldFocused)
                    .modifier(TextInputAccessoryModifier(
                        isFocused: $fieldFocused,
                        onCompleted: { node.onCompleted?() }))
                    .onChange(of: fieldFocused) { now in if now { node.onFocused?() } }
                    .onChange(of: node.focusRequested) { req in if req { fieldFocused = true; node.focusRequested = false } }
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
            }
        case "texteditor":
            ZStack(alignment: .topLeading) {
                if node.text.isEmpty && !node.placeholder.isEmpty {
                    Text(node.placeholder)
                        .foregroundColor(.secondary)
                        .padding(.horizontal, 5)
                        .padding(.vertical, 8)
                }
                TextEditor(text: Binding(
                    get: { node.text },
                    set: { node.text = $0; node.onChangeString?($0) }))
                    .modifier(TextEditorChromeModifier(borderless: node.borderless))
                    .focused($fieldFocused)
                    .modifier(TextInputAccessoryModifier(
                        isFocused: $fieldFocused,
                        onCompleted: { node.onCompleted?() }))
                    .onChange(of: fieldFocused) { now in if now { node.onFocused?() } }
                    .onChange(of: node.focusRequested) { req in if req { fieldFocused = true; node.focusRequested = false } }
                    .modifier(ForegroundModifier(argb: node.textColorARGB))
                    .modifier(FontModifier(node: node))
            }
        case "toggle":
            Toggle("", isOn: Binding(
                get: { node.isOn },
                set: { node.isOn = $0; node.onChangeBool?($0) }))
                .labelsHidden()
        case "slider":
            Slider(value: Binding(
                get: { node.doubleValue },
                set: { node.doubleValue = $0; node.onChangeDouble?($0) }),
                   in: node.sliderMin...node.sliderMax)
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
            if !node.imageData.isEmpty, let ui = UIImage(data: node.imageData) {
                Image(uiImage: ui).resizable().aspectRatio(contentMode: .fill).clipped()
            } else if node.imageUrl.lowercased().hasPrefix("http") {
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
            } else if FileManager.default.fileExists(atPath: node.imageUrl),
                      let ui = UIImage(contentsOfFile: node.imageUrl) {
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
                    .tracking(node.characterSpacing)
                    .modifier(TextLineBreakModifier(node: node))
            }
        }
    }
}

private struct ButtonContentPaddingModifier: ViewModifier {
    @ObservedObject var node: CometNode

    @ViewBuilder
    func body(content: Content) -> some View {
        if node.buttonHasExplicitPadding {
            content.padding(EdgeInsets(
                top: node.padTop,
                leading: node.padLeading,
                bottom: node.padBottom,
                trailing: node.padTrailing))
        } else {
            content
                .padding(.horizontal, 22)
                .padding(.vertical, 8)
        }
    }
}

private struct ButtonHitAreaModifier: ViewModifier {
    let fillFrame: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if fillFrame {
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .center)
                .contentShape(Rectangle())
        } else {
            content
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
    case "home", "house": return "house.fill"
    case "coffee", "coffee_maker", "local_cafe": return "cup.and.saucer.fill"
    case "feed", "view_list", "history": return "list.bullet"
    case "star": return "star.fill"
    case "favorite", "heart": return "heart.fill"
    case "delete", "trash": return "trash.fill"
    case "check", "checkmark": return "checkmark.circle.fill"
    case "refresh": return "arrow.clockwise"
    case "calendar": return "calendar"
    case "notification", "notifications", "bell": return "bell.fill"
    case "warning": return "exclamationmark.triangle"
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
private func customUIFont(_ family: String, _ size: CGFloat, _ weight: Int, italic: Bool = false) -> UIFont? {
    // Prefer the real per-weight face by PostScript name (e.g. "Montserrat-Medium", "Karla-Bold") so
    // the actual weight renders rather than a synthesized one.
    var font: UIFont?
    if let f = UIFont(name: weightedFontName(family, weight), size: size) {
        font = f
    } else if let base = UIFont(name: family, size: size) {
        let traits: [UIFontDescriptor.TraitKey: Any] = [.weight: uiFontWeight(weight)]
        font = UIFont(descriptor: base.fontDescriptor.addingAttributes([.traits: traits]), size: size)
    }
    guard var resolved = font else { return nil }
    if italic {
        // SwiftUI's .italic() can't slant a custom UIFont-backed Font, so bake the
        // slant here: a real italic face via the symbolic trait when the family has
        // one, else a synthesized oblique (skew matrix) — same idea as Android.
        if let d = resolved.fontDescriptor.withSymbolicTraits(
               resolved.fontDescriptor.symbolicTraits.union(.traitItalic)),
           d.symbolicTraits.contains(.traitItalic) {
            resolved = UIFont(descriptor: d, size: size)
        } else {
            let skew = CGAffineTransform(a: 1, b: 0, c: 0.25, d: 1, tx: 0, ty: 0)
            resolved = UIFont(descriptor: resolved.fontDescriptor.withMatrix(skew), size: size)
        }
    }
    return resolved
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
        let italicize: (AnyView) -> AnyView = node.fontItalic
            ? { v in AnyView(v.italic()) }
            : { v in v }
        if !node.fontFamily.isEmpty,
           let f = customUIFont(node.fontFamily, size, node.fontWeight, italic: node.fontItalic) {
            // Slant is baked into the UIFont — .italic() is a no-op on custom fonts.
            return AnyView(content.font(Font(f)))
        }
        if node.fontSize > 0 {
            return italicize(AnyView(content.font(.system(size: node.fontSize, weight: swiftFontWeight(node.fontWeight)))))
        }
        if node.fontWeight > 0 {
            return italicize(AnyView(content.fontWeight(swiftFontWeight(node.fontWeight))))
        }
        return italicize(AnyView(content))
    }
}

// Applies an explicit text/foreground color when Comet set one (argb != 0); otherwise leaves
// the default so it adapts to light/dark like native SwiftUI text.
private struct AccessibilityIdentifierModifier: ViewModifier {
    let identifier: String

    func body(content: Content) -> some View {
        if identifier.isEmpty {
            content
        } else {
            content.accessibilityIdentifier(identifier)
        }
    }
}

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

private struct TextInputAccessoryModifier: ViewModifier {
    var isFocused: FocusState<Bool>.Binding
    let onCompleted: () -> Void

    func body(content: Content) -> some View {
        content.toolbar {
            ToolbarItemGroup(placement: .keyboard) {
                if isFocused.wrappedValue {
                    Spacer()
                    Button {
                        isFocused.wrappedValue = false
                        onCompleted()
                    } label: {
                        Image(systemName: "checkmark")
                    }
                    .buttonStyle(.borderedProminent)
                    .frame(width: 44, height: 44)
                    .clipShape(Circle())
                    .accessibilityLabel("Done")
                }
            }
        }
    }
}

private struct ManualKeyboardSafeAreaModifier: ViewModifier {
    let enabled: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if enabled {
            content.ignoresSafeArea(.keyboard, edges: .bottom)
        } else {
            content
        }
    }
}

private func isYogaContainer(_ kind: String) -> Bool {
    return kind == "vstack" || kind == "hstack" || kind == "zstack" || kind == "grid"
}

private func listChildID(_ child: CometNode) -> AnyHashable {
    child.automationId.isEmpty
        ? AnyHashable(child.id)
        : AnyHashable(child.automationId)
}

private func selectorChildID(_ child: CometNode) -> String {
    child.automationId.isEmpty ? String(describing: child.id) : child.automationId
}

private func applyPendingListScroll(_ node: CometNode, _ proxy: ScrollViewProxy) {
    guard node.scrollToken != 0,
          node.scrollToken != node.lastAppliedScrollToken else { return }

    let token = node.scrollToken
    let applyScroll = {
        guard node.scrollToken == token,
              node.listScrollTargetIndex >= 0,
              node.listScrollTargetIndex < node.children.count else { return }

        let anchor: UnitPoint = node.listScrollPosition == 1 ? .center : .top
        node.lastAppliedScrollToken = token
        if node.listScrollAnimated {
            withAnimation(.easeInOut(duration: 0.35)) {
                if node.listUsesLazyVStack {
                    proxy.scrollTo(
                        selectorChildID(node.children[node.listScrollTargetIndex]),
                        anchor: anchor)
                } else {
                    proxy.scrollTo(listChildID(node.children[node.listScrollTargetIndex]), anchor: anchor)
                }
            }
        } else {
            if node.listUsesLazyVStack {
                proxy.scrollTo(
                    selectorChildID(node.children[node.listScrollTargetIndex]),
                    anchor: anchor)
            } else {
                proxy.scrollTo(listChildID(node.children[node.listScrollTargetIndex]), anchor: anchor)
            }
        }
    }

    if node.listScrollAnimated {
        // List rows are rebuilt in the same update that publishes the scroll
        // request. Defer one main-queue turn so SwiftUI has applied the new
        // row generation before resolving the target identity.
        DispatchQueue.main.async {
            applyScroll()
            for delay in [0.15, 0.45, 0.9] {
                DispatchQueue.main.asyncAfter(deadline: .now() + delay) {
                    guard node.scrollToken == token else { return }
                    applyScroll()
                }
            }
        }
    } else {
        applyScroll()
    }
    if node.listScrollTargetIndex < 0,
       let last = node.children.last {
        node.lastAppliedScrollToken = token
        withAnimation { proxy.scrollTo(last.id, anchor: .bottom) }
    }
}

@ViewBuilder
private func selectorSnapLayout<V: View>(_ view: V) -> some View {
    if #available(iOS 17.0, macOS 14.0, *) {
        view.scrollTargetLayout()
    } else {
        view
    }
}

@ViewBuilder
private func selectorSnapBehavior<V: View>(_ view: V, enabled: Bool) -> some View {
    if enabled, #available(iOS 17.0, macOS 14.0, *) {
        view.scrollTargetBehavior(.viewAligned)
    } else {
        view
    }
}

@ViewBuilder
private func centeredSelectorStack(_ node: CometNode) -> some View {
    // Selector lists are short, fixed-height native controls. Materialize every row so
    // ScrollViewReader can resolve distant targets before they enter the viewport.
    let edgeSpacing = max(node.listEndSpacing, node.frame.height / 2)
    VStack(spacing: 0) {
        if edgeSpacing > 0 {
            Color.clear
                .frame(height: edgeSpacing)
                .accessibilityHidden(true)
        }
        ForEach(node.children) { child in
            CometNodeView(node: child)
                .frame(maxWidth: .infinity, alignment: .topLeading)
                .id(selectorChildID(child))
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
        if edgeSpacing > 0 {
            Color.clear
                .frame(height: edgeSpacing)
                .accessibilityHidden(true)
        }
    }
}

// Recursively renders a CometNode tree as SwiftUI. Once C#'s Yoga engine has arranged a flow
// container (hasFrame), its children are positioned absolutely from the computed frames;
// otherwise native SwiftUI layout is used (rendering unchanged until the engine drives it).
struct CometNodeView: View {
    @ObservedObject var node: CometNode
    @Environment(\.cometManualSafeAreaLayout) private var manualSafeAreaLayout

    var body: some View {
        // Each node sizes + positions ITSELF from its own (observed) frame, so a re-arrange
        // (reflow) re-renders just that node and the layout adapts live — the parent can't
        // observe the children's frames in its own body. Order matters: size → background
        // (so it fills the arranged frame) → offset (move the whole node into place).
        content
            .modifier(SizeModifier(node: node))
            .modifier(BackgroundModifier(argb: node.backgroundARGB, stops: node.gradientStops,
                                         direction: node.gradientDirection))
            .modifier(SurfaceModifier(node: node)) // rounded corners + elevation (Material card)
            .modifier(TapGestureModifier(node: node))
            .modifier(RecordGestureModifier(node: node))
            .modifier(AccessibilityIdentifierModifier(identifier: node.automationId))
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
            if manualSafeAreaLayout && !node.backVisible {
                // Hidden-chrome manual-layout pages own navigation and Back in their content.
                // Render the active screen directly: NavigationStack otherwise shifts the full
                // fixed-size Yoga page to reveal a focused field above the keyboard.
                if let current = node.children.last {
                    CometNodeView(node: current)
                        .modifier(ManualNavigationSafeAreaModifier(enabled: true))
                        .modifier(NavigationBackButtonModifier(node: node))
                }
            } else {
                // A real NavigationStack supplies the native back button and interactive edge-swipe.
                // C# remains the stack authority: a native pop requests back, and a page guard may
                // leave the path unchanged until its command explicitly pops.
                NavigationStack(path: Binding(
                    get: { node.navigationPath },
                    set: { newPath in
                        let oldCount = node.navigationPath.count
                        node.navigationPath = newPath
                        if newPath.count < oldCount {
                            node.onBackRequest?()
                            let depth = max(0, node.children.count - 1)
                            node.navigationPath = depth > 0 ? Array(1...depth) : []
                        }
                    }
                )) {
                    Group {
                        if let root = node.children.first {
                            CometNodeView(node: root)
                                .modifier(ManualNavigationSafeAreaModifier(enabled: false))
                                .modifier(NavigationBackButtonModifier(node: node))
                        }
                    }
                    .navigationDestination(for: Int.self) { index in
                        if index >= 0 && index < node.children.count {
                            CometNodeView(node: node.children[index])
                                .modifier(ManualNavigationSafeAreaModifier(enabled: false))
                                .modifier(NavigationBackButtonModifier(node: node))
                        }
                    }
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                .ignoresSafeArea(.keyboard, edges: .bottom)
            }
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
                Group {
                    if node.listUsesLazyVStack {
                        // SwiftUI List retains its row host and can lose the remainder of a
                        // fully replaced generation: after a selector rerender it may keep only
                        // the first row's geometry. Centered selector lists are short-lived,
                        // fixed-row native scroll surfaces, so use ScrollView/LazyVStack where
                        // row identity and scroll geometry are owned by the Comet node itself.
                        selectorSnapBehavior(
                            ScrollView(.vertical, showsIndicators: false) {
                                selectorSnapLayout(centeredSelectorStack(node))
                            },
                            enabled: node.listSnapsToCenter)
                    } else {
                        List {
                            if node.listEndSpacing > 0 {
                                Color.clear
                                    .frame(height: node.listEndSpacing)
                                    .listRowInsets(EdgeInsets())
                                    .listRowSeparator(.hidden)
                                    .listRowBackground(Color.clear)
                                    .accessibilityHidden(true)
                            }
                            ForEach(node.children) { child in
                                CometNodeView(node: child)
                                    .listRowInsets(EdgeInsets())
                                    .listRowSeparator(.hidden)
                                    // SwiftUI's List owns a separate row host. Clearing that host lets
                                    // its system background show through even when the Comet row paints
                                    // its own surface, which is visible in the numeric selector columns.
                                    .listRowBackground(
                                        child.backgroundARGB == 0
                                            ? Color.clear
                                            : argbColor(child.backgroundARGB))
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
                            if node.listEndSpacing > 0 {
                                Color.clear
                                    .frame(height: node.listEndSpacing)
                                    .listRowInsets(EdgeInsets())
                                    .listRowSeparator(.hidden)
                                    .listRowBackground(Color.clear)
                                    .accessibilityHidden(true)
                            }
                        }
                        .listStyle(.plain)
                        .scrollContentBackground(.hidden)
                        .background(Color.clear)
                    }
                }
                .onAppear {
                    // C# can publish the one-shot request before SwiftUI attaches this
                    // ScrollViewReader. Replay that pending token exactly once on attach.
                    DispatchQueue.main.async {
                        applyPendingListScroll(node, proxy)
                    }
                }
                .onChange(of: node.scrollToken) { _ in
                    applyPendingListScroll(node, proxy)
                }
                .onChange(of: node.listContentGeneration) { _ in
                    applyPendingListScroll(node, proxy)
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
        case "refresh":
            Group {
                if let content = node.children.first {
                    CometNodeView(node: content)
                }
            }
            .refreshable {
                node.isRefreshing = true
                node.onRefresh?()
                while node.isRefreshing && !Task.isCancelled {
                    do {
                        try await Task.sleep(for: .milliseconds(50))
                    } catch is CancellationError {
                        break
                    } catch {
                        break
                    }
                }
                node.isRefreshing = false
                node.onRefreshEnded?()
            }
        case "alert":
            // Native SwiftUI .alert (the iOS counterpart of Compose's AlertDialog): a zero-size
            // host that presents modally when C# opens the dialog. Confirm has the default role;
            // the optional source dismiss button has the native cancel role. The presentation
            // binding handles lifecycle dismissal separately so system dismissal never runs an
            // action and button selection cannot dispatch the action twice.
            let presentation = node.dialogPresentation
            let title = node.dialogTitle.isEmpty ? node.dialogMessage : node.dialogTitle
            let message = node.dialogTitle.isEmpty ? "" : node.dialogMessage
            Color.clear.frame(width: 0, height: 0)
                .alert(title, isPresented: Binding(
                    get: { node.dialogOpen },
                    // Source commands own presentation state; native actions below own dismissal.
                    // SwiftUI can deliver a redundant false update through the next alert's binding.
                    set: { _ in })) {
                    Button(node.dialogButton) {
                        guard presentation == node.dialogPresentation else { return }
                        node.onDialogConfirm?()
                        node.dismissDialog(presentation: presentation)
                    }
                    if node.dialogHasDismissButton {
                        Button(node.dialogDismissButton, role: .cancel) {
                            guard presentation == node.dialogPresentation else { return }
                            node.onDialogDismissAction?()
                            node.dismissDialog(presentation: presentation)
                        }
                    }
                } message: {
                    if !message.isEmpty {
                        Text(message)
                    }
                }
                .id(presentation)
        case "tabview":
            // Native SwiftUI TabView with real UITabBar: each child node becomes a tab page
            // with a .tabItem driven by the child's text (title) and iconName (SF Symbol).
            // Selection binding routes taps back to C# via onSelectionChanged.
            SwiftUI.TabView(selection: Binding(
                get: { node.selectedIndex },
                set: { newVal in
                    node.selectedIndex = newVal
                    node.onSelectionChanged?(Double(newVal))
                }
            )) {
                ForEach(Array(node.children.enumerated()), id: \.element.id) { index, child in
                    CometNodeView(node: child)
                        .tabItem {
                            if !child.iconName.isEmpty {
                                Image(systemName: sfSymbol(child.iconName))
                            }
                            if !child.text.isEmpty {
                                SwiftUI.Text(child.text)
                            }
                        }
                        .tag(index)
                }
            }
        case "datepicker":
            // Native SwiftUI DatePicker: an inline date control. The dateEpochSeconds property
            // carries the selected date as Unix epoch seconds; changes route back to C# via
            // onDateChanged. Supplying IsOpen selects a zero-sized sheet host; without it,
            // the native compact picker remains inline even while the dialog state is false.
            if node.datePickerDialogMode {
                Color.clear.frame(width: 0, height: 0)
                    .sheet(isPresented: Binding(
                        get: { node.datePickerOpen },
                        set: {
                            if $0 {
                                node.setDatePickerOpen(true)
                            } else {
                                node.dismissDatePicker()
                            }
                        }
                    )) {
                        VStack {
                            SwiftUI.DatePicker(
                                node.text.isEmpty ? "Select Date" : node.text,
                                selection: Binding(
                                    get: { localCalendarDate(fromProtocolEpoch: node.datePickerDraftEpochSeconds) },
                                    set: { node.setDatePickerDraftEpoch(protocolEpoch(fromLocalDate: $0)) }
                                ),
                                in: node.selectableDateRange,
                                displayedComponents: .date
                            )
                            .datePickerStyle(.graphical)
                            .padding()
                            Button("Done") {
                                node.commitDatePickerDraft()
                            }
                            .padding()
                        }
                        .presentationDetents([.medium])
                    }
            } else {
                SwiftUI.DatePicker(
                    node.text.isEmpty ? "Date" : node.text,
                    selection: Binding(
                        get: { localCalendarDate(fromProtocolEpoch: node.dateEpochSeconds) },
                        set: { newDate in
                            let epoch = protocolEpoch(fromLocalDate: newDate)
                            node.dateEpochSeconds = epoch
                            node.onDateChanged?(epoch)
                        }
                    ),
                    in: node.selectableDateRange,
                    displayedComponents: .date
                )
                .datePickerStyle(.compact)
            }
        case "spacer":
            // Real SwiftUI Spacer — fills available space in a stack, sized by the Yoga frame.
            SwiftUI.Spacer()
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
        case "grid":
            ZStack(alignment: .topLeading) { ForEach(node.children) { CometNodeView(node: $0) } }
        case "vstack":
            VStack(alignment: .leading, spacing: 0) { ForEach(node.children) { CometNodeView(node: $0) } }.padding(node.padding)
        case "button":
            // The Button label owns its padding inside the native hit target.
            CometLeafContent(node: node)
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
            let framed = content.frame(width: node.frame.width, height: node.frame.height,
                                       alignment: node.kind == "icon" ? .center : .topLeading)
            if node.kind == "text" && node.lineBreakMode == 2 {
                framed.clipped()
            } else {
                framed
            }
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

// SwiftUI twin of Compose detectDragGesturesAfterLongPress. State values match
// GestureState: 0 began, 1 changed, 2 ended, 3 cancelled. Changed deltas are
// incremental, not cumulative, matching Compose's onDrag callback.
private struct RecordGestureModifier: ViewModifier {
            @ObservedObject var node: CometNode
            @State private var active = false
            @State private var lastTranslation: CGSize = .zero

            func body(content: Content) -> some View {
                if node.hasRecordGesture {
                    content
                        .contentShape(Rectangle())
                        .gesture(
                            LongPressGesture(minimumDuration: 0.5)
                                .sequenced(before: DragGesture(minimumDistance: 0))
                                .onChanged { value in
                                    switch value {
                                    case .first(true):
                                        beginIfNeeded()
                                    case .second(true, let drag):
                                        beginIfNeeded()
                                        guard let drag else { return }
                                        let current = drag.translation
                                        node.onRecordGesture?(
                                            1,
                                            Double(current.width - lastTranslation.width),
                                            Double(current.height - lastTranslation.height))
                                        lastTranslation = current
                                    default:
                                        break
                                    }
                                }
                                .onEnded { value in
                                    switch value {
                                    case .second(true, _):
                                        finishIfActive(state: 2)
                                    default:
                                        finishIfActive(state: 3)
                                    }
                                })
                        .onChange(of: node.hasRecordGesture) { enabled in
                            if !enabled { finishIfActive(state: 3) }
                        }
                        .onDisappear { finishIfActive(state: 3) }
                } else {
                    content
                }
            }

            private func beginIfNeeded() {
                guard !active else { return }
                active = true
                lastTranslation = .zero
                node.onRecordGesture?(0, 0, 0)
    }

            private func finishIfActive(state: Double) {
                guard active else { return }
                active = false
                lastTranslation = .zero
                node.onRecordGesture?(state, 0, 0)
            }
}

private struct TextLineBreakModifier: ViewModifier {
    @ObservedObject var node: CometNode

    func body(content: Content) -> some View {
        if node.lineBreakMode == 2 {
            content
                .lineLimit(1)
                .fixedSize(horizontal: true, vertical: false)
        } else if node.lineBreakMode >= 3 {
            content
                .lineLimit(1)
                .truncationMode(node.lineBreakMode == 3 ? .head : node.lineBreakMode == 5 ? .middle : .tail)
        } else {
            content.lineLimit(node.maxLines > 0 ? node.maxLines : nil)
        }
    }
}

private struct NavigationBackButtonModifier: ViewModifier {
    @ObservedObject var node: CometNode

    func body(content: Content) -> some View {
        if !node.backVisible {
            content
                .navigationBarBackButtonHidden(true)
                .toolbar(.hidden, for: .navigationBar)
        } else if !node.backTitle.isEmpty || !node.backEnabled {
            content
                .navigationBarBackButtonHidden(true)
                .toolbar(.visible, for: .navigationBar)
                .toolbar {
                    ToolbarItem(placement: .navigationBarLeading) {
                        Button(action: { node.onBackRequest?() }) {
                            HStack(spacing: 4) {
                                Image(systemName: "chevron.left")
                                Text(node.backTitle.isEmpty ? "Back" : node.backTitle)
                            }
                        }
                        .disabled(!node.backEnabled)
                    }
                }
        } else {
            content.toolbar(.visible, for: .navigationBar)
        }
    }
}

// NavigationStack introduces a screen-content safe-area boundary independently of the
// UIHostingController. A Yoga/manual-layout host with hidden native navigation chrome owns
// that entire coordinate space, so its actual root/destination content must ignore the
// container safe area too. Native-layout hosts and visible navigation bars keep defaults.
private struct ManualNavigationSafeAreaModifier: ViewModifier {
    let enabled: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if enabled {
            content.ignoresSafeArea([.container, .keyboard])
        } else {
            content
        }
    }
}

private struct TextEditorChromeModifier: ViewModifier {
    let borderless: Bool

    @ViewBuilder
    func body(content: Content) -> some View {
        if borderless {
            content
                .scrollContentBackground(.hidden)
                .background(Color.clear)
        } else {
            content.scrollContentBackground(.hidden)
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
        if node.clipCircle {
            content
                .clipShape(Circle())
                .overlay(node.borderWidth > 0
                    ? AnyView(Circle().strokeBorder(colorFromARGB(node.borderColorARGB), lineWidth: node.borderWidth))
                    : AnyView(EmptyView()))
                .shadow(color: Color.black.opacity(elevation > 0 ? 0.18 : 0),
                        radius: elevation, x: 0, y: elevation > 0 ? elevation / 2 : 0)
        } else if hasCorners || elevation > 0 || node.borderWidth > 0 || node.borderGradientStops.count > 1 {
            content
                .clipShape(shape)
                .overlay(node.borderGradientStops.count > 1
                    // Gradient stroke along the spec's axis (Android twin honors it too;
                    // radial borders fall back to diagonal until a consumer needs them).
                    ? AnyView(shape.strokeBorder(LinearGradient(
                        colors: node.borderGradientStops.map(argbColor),
                        startPoint: node.borderGradientDirection == 0 ? .leading
                            : node.borderGradientDirection == 1 ? .top : .topLeading,
                        endPoint: node.borderGradientDirection == 0 ? .trailing
                            : node.borderGradientDirection == 1 ? .bottom : .bottomTrailing), lineWidth: 2))
                    : node.borderWidth > 0
                    ? AnyView(shape.strokeBorder(colorFromARGB(node.borderColorARGB), lineWidth: node.borderWidth))
                    : AnyView(EmptyView()))
                .shadow(color: Color.black.opacity(elevation > 0 ? 0.18 : 0),
                        radius: elevation, x: 0, y: elevation > 0 ? elevation / 2 : 0)
        } else {
            content
        }
    }
}

private func localCalendarDate(fromProtocolEpoch epoch: Double) -> Date {
    let instant = Date(timeIntervalSince1970: epoch)
    var utc = Calendar(identifier: .gregorian)
    utc.timeZone = TimeZone(secondsFromGMT: 0)!
    let components = utc.dateComponents([.year, .month, .day], from: instant)
    return Calendar.current.date(from: components) ?? instant
}

private func protocolEpoch(fromLocalDate date: Date) -> Double {
    let components = Calendar.current.dateComponents([.year, .month, .day], from: date)
    var utc = Calendar(identifier: .gregorian)
    utc.timeZone = TimeZone(secondsFromGMT: 0)!
    return utc.date(from: components)?.timeIntervalSince1970 ?? date.timeIntervalSince1970
}

private func colorFromARGB(_ argb: UInt32) -> Color {
    Color(red: Double((argb >> 16) & 0xFF) / 255.0,
          green: Double((argb >> 8) & 0xFF) / 255.0,
          blue: Double(argb & 0xFF) / 255.0,
          opacity: Double((argb >> 24) & 0xFF) / 255.0)
}

private func argbColor(_ argb: UInt32) -> Color {
    Color(
        red:   Double((argb >> 16) & 0xFF) / 255.0,
        green: Double((argb >> 8) & 0xFF) / 255.0,
        blue:  Double(argb & 0xFF) / 255.0,
        opacity: Double((argb >> 24) & 0xFF) / 255.0)
}

private struct BackgroundModifier: ViewModifier {
    let argb: UInt32
    var stops: [UInt32] = []
    var direction: Int = 0
    func body(content: Content) -> some View {
        if stops.count > 1, direction == 3 {
            // Radial (Jetcaster's home scrim). SwiftUI needs explicit radii — size
            // isn't known here, so a screen-scale end radius approximates Compose's
            // bounds-fitted radial (documented deviation until a consumer needs more).
            content.background(RadialGradient(
                colors: stops.map(argbColor),
                center: .center, startRadius: 0, endRadius: 500))
        } else if stops.count > 1 {
            // Linear gradient (Jetsnack fills, Jetcaster scrims) — the LinearGradient
            // twin of Compose's horizontal/vertical/linearGradient; stops spaced evenly.
            let (start, end): (UnitPoint, UnitPoint) = switch direction {
            case 1: (.top, .bottom)
            case 2: (.topLeading, .bottomTrailing)
            default: (.leading, .trailing)
            }
            content.background(LinearGradient(
                colors: stops.map(argbColor),
                startPoint: start, endPoint: end))
        } else if argb == 0 {
            content
        } else {
            content.background(argbColor(argb))
        }
    }
}
