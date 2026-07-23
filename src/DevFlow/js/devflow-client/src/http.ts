// Raw HTTP transport: one socket per request (the agent replies `Connection: close`),
// never throws, and resolves a structured result instead.

import http from "node:http";

export interface RawResponse {
  ok: boolean;
  status: number;
  buffer?: Buffer;
  error?: string;
}

export interface RawRequestOptions {
  json?: unknown;
  timeoutMs?: number;
  headers?: Record<string, string>;
  /** Dial host. Default 127.0.0.1. */
  host?: string;
  /**
   * Explicit `Host` header. Required for the broker, whose HttpListener rejects any
   * Host other than "localhost".
   */
  hostHeader?: string;
}

/** Perform a single loopback HTTP request. Never throws; failures come back as `{ ok:false }`. */
export function httpRaw(
  port: number,
  method: string,
  path: string,
  opts: RawRequestOptions = {},
): Promise<RawResponse> {
  const { json = null, timeoutMs = 8000, host = "127.0.0.1", hostHeader, headers = {} } = opts;
  return new Promise<RawResponse>((resolve) => {
    const data = json != null ? Buffer.from(JSON.stringify(json)) : null;
    const req = http.request(
      {
        host,
        port,
        path,
        method,
        agent: false,
        headers: {
          Connection: "close",
          Accept: "application/json",
          ...headers,
          ...(hostHeader ? { Host: hostHeader } : {}),
          ...(data ? { "Content-Type": "application/json", "Content-Length": data.length } : {}),
        },
      },
      (res) => {
        const chunks: Buffer[] = [];
        res.on("data", (c: Buffer) => chunks.push(c));
        res.on("end", () => {
          const status = res.statusCode ?? 0;
          resolve({ ok: status >= 200 && status < 300, status, buffer: Buffer.concat(chunks) });
        });
      },
    );
    req.on("error", (e: NodeJS.ErrnoException) =>
      resolve({ ok: false, status: 0, error: e.code || String(e.message || e) }),
    );
    req.setTimeout(timeoutMs, () => {
      req.destroy();
      resolve({ ok: false, status: 0, error: "timeout" });
    });
    if (data) req.write(data);
    req.end();
  });
}

export interface JsonResponse {
  ok: boolean;
  status: number;
  data: unknown;
  error?: string;
}

/** Like httpRaw but decodes the body as JSON (best-effort; never throws). */
export async function httpJson(
  port: number,
  method: string,
  path: string,
  opts: RawRequestOptions = {},
): Promise<JsonResponse> {
  const r = await httpRaw(port, method, path, opts);
  let data: unknown = null;
  if (r.buffer && r.buffer.length) data = parseJsonSafe(r.buffer.toString("utf8"));
  return { ok: r.ok, status: r.status, data, error: r.error };
}

/** Parse JSON, tolerating a stray non-JSON preamble/suffix (some CLIs pollute stdout). */
export function parseJsonSafe(s: string): unknown {
  const t = (s || "").trim();
  if (!t) return null;
  try {
    return JSON.parse(t);
  } catch {
    /* fall through to salvage */
  }
  const start = t.search(/[[{]/);
  if (start >= 0) {
    const end = Math.max(t.lastIndexOf("}"), t.lastIndexOf("]"));
    if (end > start) {
      try {
        return JSON.parse(t.slice(start, end + 1));
      } catch {
        /* ignore */
      }
    }
  }
  return null;
}

/**
 * True only for genuine dead-connection signals that warrant re-resolving the agent
 * (app restarted on a new port). A request-level timeout against a LIVE socket is NOT
 * one of these — re-resolving would just double the latency.
 */
export function isConnError(r: { status?: number; error?: string } | null | undefined): boolean {
  if (!r) return false;
  const e = String(r.error || "");
  if (/timeout/i.test(e) && !/ETIMEDOUT/i.test(e)) return false;
  if (r.status === 0) return true;
  return !!r.error && /ECONNRESET|ECONNREFUSED|ETIMEDOUT|socket hang up|EPIPE|ENOTFOUND/i.test(e);
}
