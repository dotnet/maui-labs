import type { Bounds } from "./types.js";

export interface DiagnosticProblem {
  id: string;
  kind: string;
  severity: string;
  code?: string | null;
  message: string;
  count: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  elementId?: string | null;
  elementType?: string | null;
  property?: string | null;
  bindingType?: string | null;
  bindingPath?: string | null;
  bindingMode?: string | null;
  sourceType?: string | null;
  converterType?: string | null;
  sourceFile?: string | null;
  sourceLine?: number | null;
  sourceColumn?: number | null;
}

export interface DiagnosticProblems {
  ok: true;
  enabled: boolean;
  revision: number;
  count: number;
  evicted: number;
  problems: DiagnosticProblem[];
}

export interface LayoutInspectionScope {
  mode?: "activePage" | "allWindows";
  rootElementId?: string;
  window?: number;
  includeDescendants?: boolean;
  includeNativeElements?: boolean;
  includeBlazorElements?: boolean;
  maxDepth?: number;
}

export interface LayoutInspectionRequest {
  /**
   * Payload shape version this request declares. Send "2.0" — the newest version both 2.0 and 2.1
   * agents accept. The agent ships inside the app under inspection and is updated independently of
   * this client, and it rejects an unknown request version outright; 2.1 removed response fields
   * and redefined suppressionKey, so declaring it buys a request nothing. Responses may declare
   * "2.1", which this client understands.
   */
  schemaVersion?: "1.0" | "2.0" | "2.1";
  scope?: LayoutInspectionScope;
  profile?: string;
  rules?: string[];
  minimumSeverity?: string;
  includeEvidence?: boolean;
  includePasses?: boolean;
  stability?: Record<string, unknown>;
  occlusion?: Record<string, unknown>;
  privacy?: Record<string, unknown>;
  suppressions?: Record<string, unknown>[];
  suppressionMode?: "report" | "ignore" | "off";
  elementId?: string;
  window?: number;
  maxElements?: number;
}

export interface LayoutElementReference {
  id: string;
  parentId?: string | null;
  type: string;
  automationId?: string | null;
  role?: string | null;
  interactive?: boolean;
  sourceFile?: string | null;
  sourceLine?: number | null;
  sourceColumn?: number | null;
}

export interface LayoutFinding {
  id: string;
  suppressionKey: string;
  ruleId: string;
  subtype?: string | null;
  outcome: string;
  confidence: string;
  severity: string;
  actionability: string;
  message: string;
  explanation: string;
  element?: LayoutElementReference | null;
  parent?: LayoutElementReference | null;
  relatedElements?: Array<{ relation: string; element: LayoutElementReference }>;
  fixCategories?: string[];
  evidence?: Record<string, unknown> | null;
  limitations?: string[];
  suppressed?: boolean;
  wouldSuppress?: boolean;
  suppressionReason?: string | null;
}

export interface LayoutDiagnosticsReport {
  schemaVersion: string;
  ruleSetVersion: string;
  snapshot: {
    id: string;
    capturedAt: string;
    platform: string;
    treeRevision: string;
    diagnosticsRevision: string;
    stable: boolean;
    stabilityReason?: string | null;
    nodeCount: number;
    windows: unknown[];
  };
  capturedUtc: string;
  platform: string;
  scope: {
    rootElementId?: string | null;
    window?: number | null;
    maxElements: number;
    elementsExamined: number;
    truncated: boolean;
    windowBounds?: Bounds | null;
  };
  coverage: {
    overall: string;
    rules: unknown[];
    opaqueSubtrees: LayoutElementReference[];
    limitations: string[];
    neverCaptured: string[];
  };
  summary: {
    violations: number;
    observations: number;
    incomplete: number;
    passes: number;
    notApplicable: number;
    suppressed: number;
    generatedFindings: number;
    filteredFindings: number;
    activeFindings: number;
    omittedFindings: number;
  };
  findings: LayoutFinding[];
  systemEvidence?: {
    status: string;
    deviceId?: string | null;
    capturedAt?: string | null;
    captureSkewMs?: number | null;
    geometryStable: boolean;
    foregroundOwner?: string | null;
    keyboardVisible?: boolean | null;
    screenshotCaptured: boolean;
    screenshotDigest?: string | null;
    elements: Array<{
      id: string;
      role?: string | null;
      type?: string | null;
      packageId?: string | null;
      interactive: boolean;
      bounds?: Bounds | null;
    }>;
    limitations: string[];
  } | null;
}

export interface EvidencePreviewRequest {
  includeScreenshot?: boolean;
  includeWorkflow?: boolean;
  elementId?: string;
  logLimit?: number;
  networkLimit?: number;
  workflow?: string;
}

export interface EvidenceEntryMetadata {
  name: string;
  description: string;
  count?: number | null;
  bytes: number;
  sha256?: string | null;
}

export interface EvidencePreviewPlan {
  ok: boolean;
  schema: string;
  formatVersion: number;
  redactionVersion: number;
  source: string;
  generatedUtc: string;
  app?: Record<string, unknown> | null;
  platform?: Record<string, unknown> | null;
  included: EvidenceEntryMetadata[];
  excluded: Array<{ name: string; reason: string }>;
  neverIncluded: string[];
  screenshot: { requested: boolean; included: boolean; omittedReason?: string | null };
  counts: Record<string, number>;
  limits: Record<string, number>;
  warnings: string[];
  suggestedFileName: string;
  outputPath?: string | null;
  estimatedBytes: number;
  selectedElementId?: string | null;
}

export interface EvidencePreview {
  ok: true;
  plan: EvidencePreviewPlan;
}

const object = (value: unknown): value is Record<string, unknown> =>
  !!value && typeof value === "object" && !Array.isArray(value);
const string = (value: unknown): value is string => typeof value === "string";
const finite = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value);
const integer = (value: unknown): value is number => Number.isInteger(value);
const stringArray = (value: unknown): value is string[] => Array.isArray(value) && value.every(string);

export function isDiagnosticProblems(value: unknown): value is DiagnosticProblems {
  if (!object(value) || value.ok !== true || typeof value.enabled !== "boolean" ||
      !integer(value.revision) || !integer(value.count) || !integer(value.evicted) ||
      !Array.isArray(value.problems)) return false;
  return value.problems.every((item) => object(item) &&
    string(item.id) && string(item.kind) && string(item.severity) && string(item.message) &&
    integer(item.count) && string(item.firstSeenUtc) && string(item.lastSeenUtc));
}

export type LayoutDiagnosticsResponse =
  | { ok: true; report: LayoutDiagnosticsReport }
  | { ok: false; error: string; type?: string; retryable?: boolean };

export function isLayoutDiagnosticsResponse(
  value: unknown,
): value is LayoutDiagnosticsResponse {
  if (!object(value)) return false;
  if (value.ok === false) {
    return string(value.error) &&
      (value.type == null || string(value.type)) &&
      (value.retryable == null || typeof value.retryable === "boolean");
  }
  if (value.ok !== true || !object(value.report)) return false;
  const report = value.report;
  return string(report.schemaVersion) && string(report.ruleSetVersion) &&
    object(report.snapshot) && string(report.snapshot.id) && string(report.snapshot.capturedAt) &&
    string(report.capturedUtc) && string(report.platform) &&
    object(report.scope) && integer(report.scope.elementsExamined) &&
    object(report.coverage) && string(report.coverage.overall) &&
    Array.isArray(report.coverage.limitations) && Array.isArray(report.coverage.neverCaptured) &&
    object(report.summary) && integer(report.summary.violations) &&
    Array.isArray(report.findings) && report.findings.every((finding) =>
      object(finding) && string(finding.id) && string(finding.ruleId) &&
      string(finding.outcome) && string(finding.message));
}

export function isEvidencePreview(value: unknown): value is EvidencePreview {
  if (!object(value) || value.ok !== true || !object(value.plan)) return false;
  const plan = value.plan;
  return typeof plan.ok === "boolean" && string(plan.schema) &&
    integer(plan.formatVersion) && integer(plan.redactionVersion) &&
    string(plan.source) && string(plan.generatedUtc) &&
    Array.isArray(plan.included) && plan.included.every((entry) =>
      object(entry) && string(entry.name) && string(entry.description) && finite(entry.bytes)) &&
    Array.isArray(plan.excluded) && plan.excluded.every((entry) =>
      object(entry) && string(entry.name) && string(entry.reason)) &&
    stringArray(plan.neverIncluded) && object(plan.screenshot) &&
    typeof plan.screenshot.requested === "boolean" &&
    typeof plan.screenshot.included === "boolean" &&
    object(plan.counts) && Object.values(plan.counts).every(finite) &&
    object(plan.limits) && Object.values(plan.limits).every(finite) &&
    stringArray(plan.warnings) && string(plan.suggestedFileName) && finite(plan.estimatedBytes);
}
