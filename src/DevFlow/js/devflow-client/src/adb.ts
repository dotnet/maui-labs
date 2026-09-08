// ADB port-forwarding for Android agents (they live inside the emulator/device, so the
// agent's TCP port must be forwarded to the host before the client can reach it).
//
// The forwarding implementation is isolated behind AdbForwarder so callers use the same client
// API for local and Android agents. Commands are idempotent and complement the CLI's broker
// reverse/forward repair when a JS host runs independently of a CLI command.

import { execFile } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";

/** Resolve the adb executable: explicit override → ANDROID_HOME/SDK_ROOT/LOCALAPPDATA → PATH. */
export function resolveAdb(override?: string): string {
  const exe = process.platform === "win32" ? "adb.exe" : "adb";
  if (override && existsSync(override)) return override;
  const roots = [
    process.env.ANDROID_HOME,
    process.env.ANDROID_SDK_ROOT,
    process.env.LOCALAPPDATA ? join(process.env.LOCALAPPDATA, "Android", "Sdk") : undefined,
  ];
  for (const root of roots) {
    if (!root) continue;
    const candidate = join(root, "platform-tools", exe);
    if (existsSync(candidate)) return candidate;
  }
  return exe;
}

interface RunResult {
  ok: boolean;
  stdout: string;
  stderr: string;
}

function run(adb: string, args: string[], timeoutMs = 8000): Promise<RunResult> {
  return new Promise<RunResult>((resolve) => {
    execFile(
      adb,
      args,
      { encoding: "utf8", timeout: timeoutMs, windowsHide: true, maxBuffer: 8 * 1024 * 1024 },
      (error, stdout, stderr) => {
        resolve({
          ok: !error,
          stdout: stdout || "",
          stderr: stderr || (error ? String(error.message || error) : ""),
        });
      },
    );
  });
}

/** Idempotent adb `forward tcp:P tcp:P` management for a single device. */
export class AdbForwarder {
  private readonly adb: string;
  private readonly device?: string;

  constructor(opts: { adbPath?: string; device?: string } = {}) {
    this.adb = resolveAdb(opts.adbPath);
    this.device = opts.device;
  }

  private args(rest: string[]): string[] {
    return this.device ? ["-s", this.device, ...rest] : rest;
  }

  /** True if at least one device/emulator is attached. */
  async hasDevice(): Promise<boolean> {
    const r = await run(this.adb, ["devices"]);
    if (!r.ok) return false;
    // Skip the "List of devices attached" header line.
    return /\bdevice\b/.test(r.stdout.split(/\r?\n/).slice(1).join("\n"));
  }

  /** Ports already forwarded (host side). */
  async forwardList(): Promise<Set<number>> {
    const set = new Set<number>();
    const r = await run(this.adb, this.args(["forward", "--list"]));
    if (r.ok) {
      for (const line of r.stdout.split(/\r?\n/)) {
        const m = line.match(/\btcp:(\d+)\s+tcp:\d+/);
        if (m && m[1]) set.add(Number(m[1]));
      }
    }
    return set;
  }

  /** Ensure `tcp:P tcp:P` forwards exist for each requested port (no-op if present). */
  async ensureForwards(ports: number[]): Promise<void> {
    let existing = new Set<number>();
    try {
      existing = await this.forwardList();
    } catch {
      /* ignore — best effort */
    }
    const missing = ports.filter((p) => !existing.has(p));
    await Promise.all(
      missing.map((p) =>
        run(this.adb, this.args(["forward", `tcp:${p}`, `tcp:${p}`])).catch(() => undefined),
      ),
    );
  }
}
