/**
 * The bridge message types the Inspector page sends in `request` mode — it awaits a
 * `devflow:hostResult` carrying the same `requestId`.
 *
 * `devflow:sendToCopilot`, `devflow:openSource`, `devflow:recordingComplete`, and
 * `devflow:selectionChanged` are deliberately absent: the page sends those one-way through
 * `hostBridge.notify()`, which attaches no `requestId`. Listing them here would make the host's
 * "a result-bearing message must carry a request id" guard drop them before their handlers run, so
 * opening a source file or saving a recording draft would silently do nothing.
 *
 * Source *apply* is deferred to a later layer, so the three apply/selection request types are
 * absent too. This host reviews reviewed source proposals read-only: it opens the diff and never
 * writes the file.
 */
const REQUEST_BRIDGE_TYPES = new Set([
  "devflow:attachCopilot",
  "devflow:attachData",
  "devflow:pickWorkflow",
  "devflow:saveTestBundle",
  "devflow:loadTestBundle",
  "devflow:pickTrace",
  "devflow:requestTestProposal",
  "devflow:openSourceDiff",
  "devflow:nativeApproval",
]);

export function requiresBridgeRequestId(type: string | undefined): boolean {
  return typeof type === "string" && REQUEST_BRIDGE_TYPES.has(type);
}

export function isBridgeResultMessage(type: string | undefined): boolean {
  return requiresBridgeRequestId(type);
}
