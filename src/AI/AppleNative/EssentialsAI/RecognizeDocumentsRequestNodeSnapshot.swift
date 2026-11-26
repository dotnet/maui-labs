import Foundation

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
enum RecognizeDocumentsRequestNodeKind: Int {
    case title = 0
    case paragraph = 1
    case table = 2
    case tableCell = 3
    case list = 4
    case listItem = 5
    case barcode = 6
}

@available(iOS 26.0, macCatalyst 26.0, macOS 26.0, tvOS 26.0, visionOS 26.0, *)
struct RecognizeDocumentsRequestNodeSnapshot {
    let kind: RecognizeDocumentsRequestNodeKind
    let path: String
    let parentPath: String?
    var text: String? = nil
    var polygon: [NSNumber] = []
    var confidence: NSNumber? = nil
    var rowIndex: NSNumber? = nil
    var columnIndex: NSNumber? = nil
    var rowSpan: NSNumber? = nil
    var columnSpan: NSNumber? = nil
    var itemString: String? = nil
    var markerString: String? = nil
    var markerType: String? = nil
    var symbology: String? = nil
    var payloadString: String? = nil
    var payloadData: Data? = nil
    var isGS1DataCarrier: NSNumber? = nil
    var isColorInverted: NSNumber? = nil
    var supplementalPayloadString: String? = nil
    var supplementalPayloadData: Data? = nil
    var supplementalCompositeType: String? = nil
    var textAlignment: String? = nil
    var recognitionLanguages: [String]? = nil
    var detectedData: [[String: Any]] = []
    var candidates: [[String: Any]] = []
    var words: [[String: Any]]? = nil

    func snapshotObject() -> [String: Any] {
        var snapshot: [String: Any] = [
            "kind": kindName,
            "path": path,
            "polygon": polygon.map(\.doubleValue),
        ]
        snapshot["parentPath"] = parentPath
        snapshot["text"] = text
        snapshot["confidence"] = confidence
        snapshot["rowIndex"] = rowIndex
        snapshot["columnIndex"] = columnIndex
        snapshot["rowSpan"] = rowSpan
        snapshot["columnSpan"] = columnSpan
        snapshot["itemString"] = itemString
        snapshot["markerString"] = markerString
        snapshot["markerType"] = markerType
        snapshot["symbology"] = symbology
        snapshot["payloadString"] = payloadString
        snapshot["payloadDataBase64"] = payloadData?.base64EncodedString()
        snapshot["isGS1DataCarrier"] = isGS1DataCarrier
        snapshot["isColorInverted"] = isColorInverted
        snapshot["supplementalPayloadString"] = supplementalPayloadString
        snapshot["supplementalPayloadDataBase64"] =
            supplementalPayloadData?.base64EncodedString()
        snapshot["supplementalCompositeType"] = supplementalCompositeType
        snapshot["textAlignment"] = textAlignment
        snapshot["recognitionLanguages"] = recognitionLanguages
        snapshot["detectedData"] = detectedData
        snapshot["candidates"] = candidates
        snapshot["words"] = words
        return snapshot
    }

    private var kindName: String {
        switch kind {
        case .title: return "title"
        case .paragraph: return "paragraph"
        case .table: return "table"
        case .tableCell: return "tableCell"
        case .list: return "list"
        case .listItem: return "listItem"
        case .barcode: return "barcode"
        @unknown default: return "unknown"
        }
    }
}
