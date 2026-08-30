import { createHash } from "node:crypto";

const MAX_PROMPT_LENGTH = 8192;
const DUPLICATE_WINDOW_MS = 30000;

export async function dispatchAgentRequest(session, state, prompt, title, options = {}) {
  const request = typeof prompt === "string" ? prompt.trim() : "";
  if (!request || request.length > MAX_PROMPT_LENGTH) {
    return { ok: false, error: "The DevFlow agent request must contain 1 to 8192 characters." };
  }

  const now = typeof options.now === "function" ? options.now() : Date.now();
  const digest = createHash("sha256").update(request).digest("hex").slice(0, 16);
  if (state?._lastAgentRequestKey === digest &&
      now - (state._lastAgentRequestAt || 0) < DUPLICATE_WINDOW_MS) {
    return { ok: true, deduped: true, status: "This DevFlow request was already sent to Copilot" };
  }

  if (typeof session?.send !== "function") {
    return {
      ok: false,
      code: "unsupported_runtime",
      error: "This Copilot build cannot receive DevFlow agent requests directly.",
    };
  }

  const timeout = typeof options.timeout === "function"
    ? options.timeout
    : (promise) => promise;
  try {
    const result = await timeout(
      session.send({ prompt: request }),
      8000,
      "Send DevFlow agent request",
    );
    if (result?.timedOut) return { ok: false, error: result.error };
  } catch (e) {
    return { ok: false, error: `Could not send the DevFlow request: ${String(e?.message || e)}` };
  }

  if (state) {
    state._lastAgentRequestKey = digest;
    state._lastAgentRequestAt = now;
  }
  const requestTitle = typeof title === "string" ? title.trim().slice(0, 120) : "";
  return {
    ok: true,
    status: requestTitle
      ? `Sent "${requestTitle}" to Copilot`
      : "Sent the DevFlow request to Copilot",
  };
}
