import { Buffer } from "node:buffer";

export const REQUEST_BODY_MAX_BYTES = 2 * 1024 * 1024;

export function selectInspectorAgent(agents, connectedPort) {
  if (!Array.isArray(agents) || agents.length === 0) return null;
  if (connectedPort) {
    return agents.find((agent) => Number(agent?.port) === Number(connectedPort)) || null;
  }
  return agents.length === 1 ? agents[0] : null;
}

export function readJsonBody(req, maxBytes = REQUEST_BODY_MAX_BYTES) {
  return new Promise((resolve) => {
    let settled = false;
    let ended = false;
    let tooLarge = false;
    let byteLength = 0;
    const chunks = [];

    const finish = (result) => {
      if (settled) return;
      settled = true;
      resolve(result);
    };

    req.on("data", (chunk) => {
      if (tooLarge) return;
      const buffer = Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk);
      byteLength += buffer.length;
      if (byteLength > maxBytes) {
        tooLarge = true;
        chunks.length = 0;
        finish({ ok: false, status: 413, error: "request body too large" });
        req.resume();
        return;
      }
      chunks.push(buffer);
    });
    req.once("end", () => {
      ended = true;
      if (tooLarge) return;
      try {
        const raw = Buffer.concat(chunks, byteLength).toString("utf8");
        finish({ ok: true, value: JSON.parse(raw || "{}") });
      } catch {
        finish({ ok: false, status: 400, error: "invalid JSON" });
      }
    });
    req.once("aborted", () => finish({ ok: false, status: 400, error: "request aborted" }));
    req.once("error", () => finish({ ok: false, status: 400, error: "request failed" }));
    req.once("close", () => {
      if (!ended) finish({ ok: false, status: 400, error: "request closed" });
    });
  });
}
