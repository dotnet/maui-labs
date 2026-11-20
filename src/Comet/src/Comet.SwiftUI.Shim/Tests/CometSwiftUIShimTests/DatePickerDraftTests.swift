import XCTest
@testable import CometSwiftUIShim

final class DatePickerDraftTests: XCTestCase {
    func testDismissedDraftIsDiscardedBeforeReopenAndDone() {
        let node = CometNode(kind: "datepicker")
        let committed = 1_789_084_800.0
        let cancelledDraft = 1_794_268_800.0
        var committedValues: [Double] = []
        var dismissCount = 0

        node.onDateChanged = { committedValues.append($0) }
        node.onDialogDismiss = { dismissCount += 1 }

        // The retained backend currently replays IsOpen before the selected date.
        node.setDatePickerOpen(true)
        node.setCommittedDateEpoch(committed)
        XCTAssertEqual(node.datePickerDraftEpochSeconds, committed)
        node.setDatePickerDraftEpoch(cancelledDraft)
        node.dismissDatePicker()

        XCTAssertEqual(node.dateEpochSeconds, committed)
        XCTAssertEqual(node.datePickerDraftEpochSeconds, committed)
        XCTAssertEqual(committedValues, [])
        XCTAssertEqual(dismissCount, 1)

        node.setDatePickerOpen(true)
        XCTAssertEqual(node.datePickerDraftEpochSeconds, committed)
        node.commitDatePickerDraft()

        XCTAssertEqual(node.dateEpochSeconds, committed)
        XCTAssertEqual(committedValues, [committed])
        XCTAssertFalse(node.datePickerOpen)
    }

    func testDoneCommitsOnlyTheCurrentDraftOnce() {
        let node = CometNode(kind: "datepicker")
        let committed = 1_789_084_800.0
        let draft = 1_794_268_800.0
        var committedValues: [Double] = []
        var dismissCount = 0

        node.onDateChanged = { committedValues.append($0) }
        node.onDialogDismiss = { dismissCount += 1 }
        node.setCommittedDateEpoch(committed)
        node.setDatePickerOpen(true)
        node.setDatePickerDraftEpoch(draft)

        node.commitDatePickerDraft()
        node.commitDatePickerDraft()

        XCTAssertEqual(node.dateEpochSeconds, draft)
        XCTAssertEqual(committedValues, [draft])
        XCTAssertEqual(dismissCount, 0)
        XCTAssertFalse(node.datePickerOpen)
    }
}
