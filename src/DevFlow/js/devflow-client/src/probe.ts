// Tiny liveness probe. Lives in its own module so `resolve.ts` can verify a port
// without depending on the full `agent.ts` (keeps the dependency graph one-way:
// http → probe → resolve, never resolve ↔ agent).

import { httpJson } from "./http.js";
import type { AgentStatus } from "./types.js";

/**
 * Probe `GET /api/v1/agent/status` on a port. Returns the status if a DevFlow agent
 * answered with a well-formed payload, else null. Uses a short timeout (default 600ms)
 * so a full port scan stays fast.
 */
export async function probeStatus(port: number, timeoutMs = 600): Promise<AgentStatus | null> {
  const r = await httpJson(port, "GET", "/api/v1/agent/status", { timeoutMs });
  if (
    r.ok &&
    r.data &&
    typeof r.data === "object" &&
    (r.data as Record<string, unknown>).running !== undefined
  ) {
    return r.data as AgentStatus;
  }
  return null;
}
