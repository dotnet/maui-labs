// Public environment-variable contract retained for existing host setups:
//   MAUI_DEVFLOW_PLATFORM | MAUI_DEVFLOW_DEVICE | MAUI_DEVFLOW_AGENT_PORT |
//   MAUI_DEVFLOW_PROJECT_ROOT  and the tool overrides MAUI_CLI / ADB.

import type { DevFlowClientOptions } from "./types.js";

/** Parse a TCP port from an env string; returns undefined for anything invalid. */
export function parsePort(v: string | undefined | null): number | undefined {
  if (v == null) return undefined;
  const n = Number.parseInt(String(v).trim(), 10);
  return Number.isInteger(n) && n > 0 && n < 65536 ? n : undefined;
}

/** Read DevFlow client options from environment variables (only sets present keys). */
export function optionsFromEnv(env: NodeJS.ProcessEnv = process.env): Partial<DevFlowClientOptions> {
  const out: Partial<DevFlowClientOptions> = {};

  const platform = env.MAUI_DEVFLOW_PLATFORM?.trim();
  if (platform) out.platform = platform;

  const device = env.MAUI_DEVFLOW_DEVICE?.trim();
  if (device) out.device = device;

  const port = parsePort(env.MAUI_DEVFLOW_AGENT_PORT);
  if (port) out.agentPort = port;

  const projectRoot = env.MAUI_DEVFLOW_PROJECT_ROOT?.trim();
  if (projectRoot) out.projectRoot = projectRoot;

  const cli = env.MAUI_CLI?.trim();
  if (cli) out.mauiCliPath = cli;

  const adb = env.ADB?.trim();
  if (adb) out.adbPath = adb;

  return out;
}
