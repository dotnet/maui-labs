import type { BrokerState } from "@maui-devflow/client";

export interface NativeApprovalScope {
  allowedActions: string[];
  allowedSelectors: string[];
  allowedRoutes: string[];
  allowedSideEffectClasses: string[];
  maxActionCount: number;
  maxValueBytes: number;
}

export interface NativeApprovalRequest {
  approvalRequestId: string;
  kind: string;
  intent: string;
  approvedScope: NativeApprovalScope;
  grantDurationSeconds: number;
  appName: string;
  platform: string;
  scopeSummary: string;
}

export interface NativeApprovalTarget {
  brokerPort: number;
  agentId: string;
}

export interface NativeApprovalResult {
  ok: boolean;
  message?: string;
  error?: string;
}

const REQUEST_ID = /^[A-Za-z0-9_-]{1,256}$/;
const KIND = /^[a-z][a-z0-9-]{0,63}$/;
const TOKEN = /^[A-Za-z0-9_-]{32,256}$/;

function boundedString(value: unknown, maximum: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maximum;
}

function boundedList(value: unknown, maximumItems: number, maximumItemLength: number): value is string[] {
  return Array.isArray(value) &&
    value.length <= maximumItems &&
    value.every((item) => boundedString(item, maximumItemLength)) &&
    new Set(value).size === value.length;
}

export function isNativeApprovalRequest(value: unknown): value is NativeApprovalRequest {
  if (!value || typeof value !== "object") return false;
  const request = value as Partial<NativeApprovalRequest>;
  const scope = request.approvedScope;
  return boundedString(request.approvalRequestId, 256) &&
    REQUEST_ID.test(request.approvalRequestId) &&
    boundedString(request.kind, 64) &&
    KIND.test(request.kind) &&
    boundedString(request.intent, 1024) &&
    boundedString(request.appName, 256) &&
    boundedString(request.platform, 128) &&
    boundedString(request.scopeSummary, 1024) &&
    Number.isInteger(request.grantDurationSeconds) &&
    (request.grantDurationSeconds ?? 0) >= 1 &&
    (request.grantDurationSeconds ?? 0) <= 900 &&
    !!scope &&
    boundedList(scope.allowedActions, 64, 512) &&
    scope.allowedActions.length > 0 &&
    boundedList(scope.allowedSelectors, 128, 512) &&
    boundedList(scope.allowedRoutes, 64, 512) &&
    boundedList(scope.allowedSideEffectClasses, 64, 256) &&
    Number.isInteger(scope.maxActionCount) &&
    scope.maxActionCount >= 1 &&
    scope.maxActionCount <= 10_000 &&
    Number.isInteger(scope.maxValueBytes) &&
    scope.maxValueBytes >= 0 &&
    scope.maxValueBytes <= 1_048_576;
}

function scopeForWire(scope: NativeApprovalScope): NativeApprovalScope {
  return {
    allowedActions: [...scope.allowedActions],
    allowedSelectors: [...scope.allowedSelectors],
    allowedRoutes: [...scope.allowedRoutes],
    allowedSideEffectClasses: [...scope.allowedSideEffectClasses],
    maxActionCount: scope.maxActionCount,
    maxValueBytes: scope.maxValueBytes,
  };
}

async function postJson(fetchImpl: typeof fetch, url: string, body: unknown, headers: Record<string, string>): Promise<{ ok: boolean; status: number; body: any }> {
  const response = await fetchImpl(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  return {
    ok: response.ok,
    status: response.status,
    body: await response.json().catch(() => null),
  };
}

function resultError(body: any, fallback: string): string {
  if (typeof body?.error === "string") return body.error;
  if (typeof body?.error?.message === "string") return body.error.message;
  return fallback;
}

/**
 * Uses the local owner-only broker state only after the outer host's modal confirmation. The
 * confirmation capability remains in this function and is consumed immediately; callers receive
 * no reusable bearer.
 */
export async function performNativeApproval(
  request: NativeApprovalRequest,
  target: NativeApprovalTarget,
  readState: () => BrokerState | null,
  fetchImpl: typeof fetch = fetch,
): Promise<NativeApprovalResult> {
  if (!isNativeApprovalRequest(request)) {
    return { ok: false, error: "The native approval request contains invalid or unbounded values." };
  }
  if (!Number.isInteger(target.brokerPort) || target.brokerPort < 1 || target.brokerPort > 65535 ||
      !boundedString(target.agentId, 256)) {
    return { ok: false, error: "The current Inspector target is invalid." };
  }

  const state = readState();
  // The source workspace can typecheck against a previously installed client package. Keep the
  // runtime read forward-compatible while BrokerState declares this field in the current package.
  const token = (state as (BrokerState & { nativeApprovalToken?: string | null }) | null)?.nativeApprovalToken;
  if (!state || state.port !== target.brokerPort || !boundedString(token, 256) || !TOKEN.test(token)) {
    return { ok: false, error: "The local broker state does not match this Inspector or has no native approval token." };
  }

  const root = `http://localhost:${state.port}/inspector/${encodeURIComponent(target.agentId)}/api/workbench`;
  const approvedScope = scopeForWire(request.approvedScope);
  let issue;
  try {
    issue = await postJson(fetchImpl, `${root}/approval-confirmations/issue`, {
      action: "agent-request-approve",
      subjectId: request.approvalRequestId,
      approvedScope,
      grantDurationSeconds: request.grantDurationSeconds,
    }, { "X-DevFlow-Host-Approval-Token": token });
  } catch {
    return { ok: false, error: "The trusted local Inspector could not be reached to issue approval." };
  }
  const confirmationCapability = issue.body?.confirmationCapability;
  if (!issue.ok || !boundedString(confirmationCapability, 512) || !TOKEN.test(confirmationCapability)) {
    return { ok: false, error: resultError(issue.body, `Native approval confirmation was rejected (HTTP ${issue.status}).`) };
  }

  let approval;
  try {
    approval = await postJson(
      fetchImpl,
      `${root}/agent-requests/${encodeURIComponent(request.approvalRequestId)}/approve`,
      {
        // The broker refuses an approve without this flag. It is not the authority — the
        // single-use capability below is — but it keeps a caller from approving by accident.
        humanConfirmed: true,
        approvedScope,
        grantDurationSeconds: request.grantDurationSeconds,
        confirmationCapability,
      },
      {},
    );
  } catch {
    return { ok: false, error: "The trusted local Inspector could not apply the approved request." };
  }
  if (!approval.ok || approval.body?.ok !== true) {
    return { ok: false, error: resultError(approval.body, `Native approval was rejected (HTTP ${approval.status}).`) };
  }
  return {
    ok: true,
    message: typeof approval.body.message === "string"
      ? approval.body.message.slice(0, 1024)
      : "Approved by the trusted VS Code host.",
  };
}
