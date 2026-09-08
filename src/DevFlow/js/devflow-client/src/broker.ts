// Broker discovery: read ~/.mauidevflow/broker.json, then GET /api/agents. Optionally
// (per BootstrapPolicy) spawn `maui devflow list` ONCE to start the broker when it is
// down. Bootstrapping is never on the per-operation hot path.

import { execFile } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { homedir } from "node:os";
import { join } from "node:path";
import { httpRaw, parseJsonSafe } from "./http.js";
import type { AgentRegistration, BootstrapPolicy, BrokerState } from "./types.js";

export function brokerStatePath(): string {
  return join(homedir(), ".mauidevflow", "broker.json");
}

/** Read the broker state file, or null if missing/malformed. */
export function readBrokerState(): BrokerState | null {
  try {
    const p = brokerStatePath();
    if (!existsSync(p)) return null;
    const s = parseJsonSafe(readFileSync(p, "utf8")) as BrokerState | null;
    return s && typeof s.port === "number" ? s : null;
  } catch {
    return null;
  }
}

/**
 * GET the broker's agent registry. The broker is an HttpListener that rejects any Host
 * header other than "localhost", so we send it explicitly. Returns null if unreachable.
 */
export async function fetchAgents(
  brokerPort: number,
  timeoutMs = 1500,
): Promise<AgentRegistration[] | null> {
  // Keep the socket address and Host header aligned. macOS HttpListener routes a
  // 127.0.0.1 socket with a localhost Host header to a generic 404 response.
  let r = await httpRaw(brokerPort, "GET", "/api/agents", { timeoutMs });
  if (!r.ok) {
    // Some Windows configurations cannot register the literal-IP prefix without a URL ACL.
    r = await httpRaw(brokerPort, "GET", "/api/agents", {
      timeoutMs,
      host: "localhost",
      hostHeader: `localhost:${brokerPort}`,
    });
  }
  if (!r.ok || !r.buffer) return null;
  const data = parseJsonSafe(r.buffer.toString("utf8"));
  return Array.isArray(data) ? (data as AgentRegistration[]) : null;
}

/** Resolve the `maui` CLI path: explicit override → ~/.dotnet/tools/maui → PATH. */
export function resolveMauiCli(override?: string): string {
  const exe = process.platform === "win32" ? "maui.exe" : "maui";
  if (override && existsSync(override)) return override;
  const wellKnown = join(homedir(), ".dotnet", "tools", exe);
  if (existsSync(wellKnown)) return wellKnown;
  return exe;
}

/** Spawn `maui devflow list --json` once to trigger the broker to start. */
export function bootstrapBrokerViaCli(cliPath: string, timeoutMs = 20000): Promise<boolean> {
  return new Promise<boolean>((resolve) => {
    execFile(
      cliPath,
      ["devflow", "list", "--json"],
      { timeout: timeoutMs, windowsHide: true, maxBuffer: 8 * 1024 * 1024 },
      (error) => resolve(!error),
    );
  });
}

export interface BrokerDiscovery {
  port: number;
  agents: AgentRegistration[];
}

export interface DiscoverBrokerOptions {
  bootstrap: BootstrapPolicy;
  cliPath?: string;
  brokerPort?: number;
}

/**
 * Discover the broker and its agent registry. With bootstrap "never" (the library
 * default) this only reads the state file / an explicit port and never spawns a process.
 */
export async function discoverBroker(opts: DiscoverBrokerOptions): Promise<BrokerDiscovery | null> {
  // Explicit broker port wins and skips the state file.
  if (opts.brokerPort) {
    const agents = await fetchAgents(opts.brokerPort);
    if (agents) return { port: opts.brokerPort, agents };
  }

  let state = readBrokerState();
  if (opts.bootstrap !== "always" && state) {
    const agents = await fetchAgents(state.port);
    if (agents) return { port: state.port, agents };
  }

  if (opts.bootstrap === "never") {
    // Best effort without spawning.
    if (state) {
      const agents = await fetchAgents(state.port);
      return agents ? { port: state.port, agents } : null;
    }
    return null;
  }

  // "once" (state missing/dead) or "always": spawn the CLI to ensure the broker is up.
  const cli = resolveMauiCli(opts.cliPath);
  await bootstrapBrokerViaCli(cli);
  state = readBrokerState();
  if (!state) return null;
  const agents = await fetchAgents(state.port);
  return { port: state.port, agents: agents ?? [] };
}
