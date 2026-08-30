export type DevFlowDiagnosticReferenceKind = "problem" | "layout";

export function supportsDiagnosticExplanation(kind: DevFlowDiagnosticReferenceKind): boolean {
  return kind === "problem" || kind === "layout";
}
