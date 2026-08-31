// Live event stream over the agent's /ws/v1/ui/events WebSocket (DevFlow's own push
// channel — the same socket the broker relays to its web inspector). Emits parsed events
// ({ type: "treeChange" | "navigation" | "lifecycle" | "themeChange" | ..., data, timestamp }).
// Auto-reconnects with backoff; self-heals to whatever port `resolvePort` currently returns
// (so it follows an app that restarts on a new port). No-ops safely after close().

import http from "node:http";
import type { Socket } from "node:net";
import { parseJsonSafe } from "./http.js";
import { encodeFrame, FrameReader, newWebSocketKey, OPCODE } from "./ws-frame.js";

export interface DevFlowEvent {
  type: string;
  data?: unknown;
  timestamp?: string;
  [key: string]: unknown;
}

export interface EventStreamStatus {
  connected: boolean;
  port?: number;
  transport?: string;
}

export interface EventStreamHandle {
  close(): void;
}

export interface EventStreamOptions {
  /** Resolve the current agent port (null when unavailable). Called on every (re)connect. */
  resolvePort: () => Promise<number | null>;
  /** Return false when unsupported, true when supported, or null when capability is temporarily unknown. */
  supportsEvents?: (port: number) => Promise<boolean | null>;
  onEvent: (e: DevFlowEvent) => void;
  onStatus?: (s: EventStreamStatus) => void;
  /** Event names to subscribe to. Default ["all"]. */
  events?: string[];
  /** Capability recheck interval for polling-only agents. Default 60000ms. */
  unsupportedRetryMs?: number;
  /** Steady-state inactivity watchdog for a connected socket. Default 60000ms. */
  watchdogMs?: number;
}

/** Open a self-reconnecting event stream. Returns a handle with `close()`. */
export function openEventStream(opts: EventStreamOptions): EventStreamHandle {
  const { resolvePort, onEvent } = opts;
  const onStatus = opts.onStatus ?? (() => {});
  const subscribeEvents = opts.events && opts.events.length ? opts.events : ["all"];
  const steadyStateWatchdogMs = Math.max(1, opts.watchdogMs ?? 60_000);

  let active = true;
  let socket: Socket | null = null;
  let req: http.ClientRequest | null = null;
  let backoff = 500;
  let reconnectTimer: NodeJS.Timeout | null = null;
  let reconnecting = false;
  let watchdogTimer: NodeJS.Timeout | null = null;
  let watchdogGeneration = 0;

  const clearWatchdog = () => {
    watchdogGeneration++;
    if (watchdogTimer) {
      clearTimeout(watchdogTimer);
      watchdogTimer = null;
    }
  };

  const teardown = () => {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }
    clearWatchdog();
    try {
      socket?.destroy();
    } catch {
      /* ignore */
    }
    try {
      req?.destroy();
    } catch {
      /* ignore */
    }
    socket = null;
    req = null;
  };

  const armWatchdog = () => {
    if (!active || !socket) return;
    clearWatchdog();
    const generation = watchdogGeneration;
    watchdogTimer = setTimeout(() => {
      if (!active || !socket || generation !== watchdogGeneration) return;
      scheduleReconnect();
    }, steadyStateWatchdogMs);
  };

  const noteActivity = () => {
    if (!active || !socket) return;
    armWatchdog();
  };

  const scheduleReconnect = () => {
    if (!active) {
      teardown();
      return;
    }
    if (reconnecting) return;
    reconnecting = true;
    teardown();
    try {
      onStatus({ connected: false });
    } catch {
      /* ignore */
    }
    const wait = backoff;
    backoff = Math.min(backoff * 2, 5000);
    reconnectTimer = setTimeout(() => {
      reconnecting = false;
      if (active) void connect();
    }, wait);
  };

  const connect = async (): Promise<void> => {
    if (!active) return;
    let port: number | null;
    try {
      port = await resolvePort();
    } catch {
      return scheduleReconnect();
    }
    if (!active) return;
    if (!port) {
      try {
        onStatus({ connected: false, transport: "none" });
      } catch {
        /* ignore */
      }
      return scheduleReconnect();
    }
    if (opts.supportsEvents) {
      let supported: boolean | null;
      try {
        supported = await opts.supportsEvents(port);
      } catch {
        return scheduleReconnect();
      }
      if (!active) return;
      if (supported == null) return scheduleReconnect();
      if (!supported) {
        teardown();
        try {
          onStatus({ connected: false, port, transport: "polling-only" });
        } catch {
          /* ignore */
        }
        reconnecting = true;
        reconnectTimer = setTimeout(() => {
          reconnecting = false;
          if (active) void connect();
        }, Math.max(1000, opts.unsupportedRetryMs ?? 60_000));
        return;
      }
    }

    const key = newWebSocketKey();
    let upgraded = false;
    req = http.request({
      host: "127.0.0.1",
      port,
      path: "/ws/v1/ui/events",
      method: "GET",
      headers: {
        Host: `localhost:${port}`,
        Connection: "Upgrade",
        Upgrade: "websocket",
        "Sec-WebSocket-Version": "13",
        "Sec-WebSocket-Key": key,
      },
    });
    req.on("error", () => scheduleReconnect());
    // A non-upgrade HTTP response (404/400 from an old/misconfigured agent or a proxy)
    // would otherwise leave the stream silently dead — treat it as a failed handshake.
    req.on("response", (res) => {
      if (upgraded) return;
      try {
        res.resume();
      } catch {
        /* ignore */
      }
      scheduleReconnect();
    });
    // Bound the handshake so a stalled upgrade doesn't hang the stream forever.
    req.setTimeout(8000, () => {
      if (upgraded) return;
      try {
        req?.destroy();
      } catch {
        /* ignore */
      }
      scheduleReconnect();
    });
    req.on("upgrade", (_res, sock) => {
      upgraded = true;
      if (!active) {
        try {
          sock.destroy();
        } catch {
          /* ignore */
        }
        return;
      }
      socket = sock as Socket;
      backoff = 500;
      try {
        onStatus({ connected: true, port: port ?? undefined });
      } catch {
        /* ignore */
      }
      // The agent only starts emitting after it receives a subscribe frame.
      try {
        socket.write(
          encodeFrame(
            OPCODE.TEXT,
            Buffer.from(JSON.stringify({ type: "subscribe", data: { events: subscribeEvents } })),
          ),
        );
      } catch {
        /* ignore */
      }
      armWatchdog();

      const reader = new FrameReader({
        onMessage: (opcode, data) => {
          if (opcode !== OPCODE.TEXT) return;
          const parsed = parseJsonSafe(data.toString("utf8"));
          if (parsed && typeof parsed === "object") {
            try {
              onEvent(parsed as DevFlowEvent);
            } catch {
              /* a throwing host handler must not kill the stream */
            }
          }
        },
        onPing: (payload) => {
          noteActivity();
          try {
            socket?.write(encodeFrame(OPCODE.PONG, payload));
          } catch {
            /* ignore */
          }
        },
        onPong: () => {
          noteActivity();
        },
        onClose: () => scheduleReconnect(),
      });

      socket.on("data", (chunk: Buffer) => {
        noteActivity();
        reader.push(chunk);
      });
      socket.on("close", () => scheduleReconnect());
      socket.on("error", () => scheduleReconnect());
    });
    req.end();
  };

  void connect();
  return {
    close() {
      active = false;
      teardown();
    },
  };
}
