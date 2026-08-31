// Domain types mirroring the DevFlow agent + broker wire contract (camelCase),
// plus this library's typed result/error model.
//
// Wire sources (dotnet/maui-labs):
//   ElementInfo  → src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/ElementInfo.cs
//   Registration → src/Cli/Microsoft.Maui.Cli/DevFlow/Broker/AgentRegistration.cs
//   BrokerState  → same file (~/.mauidevflow/broker.json)

export interface Bounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface ElementState {
  displayed: boolean;
  enabled: boolean;
  selected: boolean;
  focused: boolean;
  opacity: number;
}

/**
 * A node in the MAUI visual tree, as serialized by the DevFlow agent. Field names
 * match the agent JSON exactly so payloads pass through untranslated. Unknown fields
 * are preserved via the index signature (forward-compatible).
 */
export interface ElementInfo {
  id: string;
  parentId?: string | null;
  type: string;
  fullType: string;
  framework?: string;
  automationId?: string | null;
  text?: string | null;
  value?: string | null;
  role?: string | null;
  isVisible?: boolean;
  isEnabled?: boolean;
  isFocused?: boolean;
  opacity?: number;
  traits?: string[] | null;
  state?: ElementState;
  bounds?: Bounds | null;
  windowBounds?: Bounds | null;
  gestures?: string[] | null;
  styleClass?: string[] | null;
  nativeType?: string | null;
  nativeProperties?: Record<string, string | null> | null;
  frameworkProperties?: Record<string, string | null> | null;
  children?: ElementInfo[] | null;
  // Populated from build-time XAML source maps. `null` when the element has no static XAML
  // declaration (synthetic or template-realized rows).
  sourceFile?: string | null;
  sourceLine?: number | null;
  sourceColumn?: number | null;
  // Short hash of the source .xaml at build time. A click-to-source consumer can hash the
  // current file and, on mismatch, treat the mapped line as stale rather than navigating to it.
  sourceHash?: string | null;
  [key: string]: unknown;
}

export interface AgentStatus {
  running: boolean;
  timestamp?: string;
  version?: string;
  app?: { name?: string; [key: string]: unknown };
  device?: { platform?: string; deviceType?: string; idiom?: string; [key: string]: unknown };
  window?: Partial<Bounds> & { [key: string]: unknown };
  [key: string]: unknown;
}

/** One entry from the broker's `GET /api/agents` registry. */
export interface AgentRegistration {
  id: string;
  /** Opaque broker-assigned generation; changes on every registration/reconnect. */
  instanceId?: string;
  project: string;
  tfm: string;
  platform: string;
  appName: string;
  packageId?: string | null;
  deviceId?: string | null;
  port: number;
  version?: string | null;
  sessionId?: string | null;
  processId?: number | null;
  connectedAt?: string;
  [key: string]: unknown;
}

/** Contents of `~/.mauidevflow/broker.json`. */
export interface BrokerState {
  pid: number;
  port: number;
  startedAt: string;
  /** Unguessable token that lets a local host shell embed the Inspector via `?embed={token}`. */
  embedToken?: string | null;
  /**
   * Per-broker-process secret for trusted local native hosts to request an exact, single-use
   * Inspector approval confirmation. It must never be passed to an Inspector iframe or agent.
   */
  nativeApprovalToken?: string | null;
}

/**
 * Identity of the agent an operation targeted. Surfaced on every result so a host
 * can display "controlling &lt;appName&gt; (&lt;platform&gt;) on port &lt;port&gt;" —
 * important once mutations are exposed to a webview.
 */
export interface AgentTarget {
  port: number;
  platform?: string;
  appName?: string;
  registration?: AgentRegistration;
}

export type DevFlowTheme = "light" | "dark" | "system" | "unspecified";

export interface ThemeResult {
  theme?: string;
  requestedTheme?: string;
  userAppTheme?: string;
  effectiveTheme?: string;
  supportedThemes?: string[];
  source?: string;
  success?: boolean;
  message?: string;
  [key: string]: unknown;
}

export type DevFlowErrorKind =
  | "broker-not-found"
  | "broker-unreachable"
  | "no-agents"
  | "agent-ambiguous"
  | "agent-unreachable"
  | "not-found"
  | "adb-unavailable"
  | "timeout"
  | "http"
  | "bad-json"
  | "capability-missing"
  | "action-rejected"
  | "invalid-argument"
  | "permission-denied"
  | "lease-held"
  | "stale-agent"
  | "stale-snapshot"
  | "disposed"
  | "unknown";

/** Structured failure. Hosts inspect `kind` (and `candidates` for ambiguity) to react. */
export interface DevFlowError {
  kind: DevFlowErrorKind;
  message: string;
  operation?: string;
  target?: AgentTarget;
  status?: number;
  bodySnippet?: string;
  retriable?: boolean;
  candidates?: AgentRegistration[];
  cause?: string;
}

/**
 * Discriminated result. A `null`/`false` API is too lossy for a shared foundation
 * library — hosts need to tell users *why* an operation failed.
 */
export type DevFlowResult<T> =
  | { ok: true; value: T; target?: AgentTarget }
  | { ok: false; error: DevFlowError };

export type BootstrapPolicy = "never" | "once" | "always";

export interface DevFlowClientOptions {
  /** Pin a specific agent HTTP port (still verified live before use). */
  agentPort?: number;
  /** Prefer agents whose platform/TFM contains this hint, e.g. "android", "windows". */
  platform?: string;
  /** Android device/emulator serial (e.g. "emulator-5554"); enables ADB forwarding. */
  device?: string;
  /** Prefer agents whose project file lives under this folder. */
  projectRoot?: string;
  /** Explicit broker port (skips reading ~/.mauidevflow/broker.json). */
  brokerPort?: number;
  /**
   * Whether the client may spawn `maui devflow list` to START the broker when its
   * state file is missing/dead. Default "never": a library should not spawn processes
   * behind the caller's back (surprise hangs on VS Code activation, wrong env, etc.).
   * The host decides when to bootstrap — ideally after a user action.
   */
  bootstrapBroker?: BootstrapPolicy;
  /**
   * When multiple agents match after filtering, pick the most-recently-connected one
   * instead of failing with `agent-ambiguous`. Default false — silently choosing the
   * wrong app to mutate is dangerous. Opt in only for convenience tooling.
   */
  allowAmbiguousMostRecent?: boolean;
  /**
   * Retry a dropped request on a genuine socket error even for MUTATING operations
   * (tap/fill/scroll/navigate/setProperty/...). Default false: a lost response can mean
   * the agent already applied the change, so a retry would double-apply it. Reads are
   * always retried regardless of this flag.
   */
  retryMutations?: boolean;
  /** Per-request timeout (ms). Default 8000. */
  requestTimeoutMs?: number;
  /** Per-port liveness probe timeout (ms) used during resolution. Default 600. */
  probeTimeoutMs?: number;
  /** Override the `maui` CLI path (else MAUI_CLI, then ~/.dotnet/tools/maui). */
  mauiCliPath?: string;
  /** Override the `adb` path (else ADB, then ANDROID_HOME/SDK platform-tools). */
  adbPath?: string;
  /**
   * Enable ADB port-forwarding for Android agents. Default: auto; broker registrations determine
   * when forwarding is needed. Blind fallback discovery requires an Android/device hint or
   * explicit true. Set false to disable all JS-side ADB commands.
   */
  adb?: boolean;
  /** Stable mutation lease identity. Generated automatically when omitted. */
  mutationLeaseId?: string;
  /** Caller kind shown to other hosts while this client holds the lease. */
  mutationLeaseHolderKind?: string;
  /** Human-readable caller label shown to other hosts. */
  mutationLeaseLabel?: string;
  /** Automatically claim the mutation lease before mutations. Default true. */
  autoAcquireMutationLease?: boolean;
}

export interface MutationLeaseStatus {
  ok: boolean;
  allowed: boolean;
  youHold: boolean;
  heldByOther: boolean;
  leaseId?: string | null;
  holderKind?: string | null;
  label?: string | null;
  expiresInMs?: number;
  authority?: string | null;
  error?: string | null;
}

export interface MutationRecordingStatus {
  ok: boolean;
  recording: boolean;
  recordingId?: string | null;
  name?: string | null;
  steps: number;
  seq?: number | null;
  fragile?: boolean;
  empty?: boolean;
  markdown?: string | null;
  warnings?: string[] | null;
  error?: string | null;
}

export function ok<T>(value: T, target?: AgentTarget): DevFlowResult<T> {
  return target ? { ok: true, value, target } : { ok: true, value };
}

export function err<T = never>(error: DevFlowError): DevFlowResult<T> {
  return { ok: false, error };
}

/** Narrowing helper for callers that prefer exceptions at a boundary. */
export function unwrap<T>(result: DevFlowResult<T>): T {
  if (result.ok) return result.value;
  const e = result.error;
  throw new Error(`DevFlow ${e.operation ?? "operation"} failed (${e.kind}): ${e.message}`);
}
