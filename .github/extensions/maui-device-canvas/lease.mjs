import { randomBytes } from "node:crypto";
import { readFile } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";

const HEARTBEAT_MS = 3000;

export async function readBrokerState(path = join(homedir(), ".mauidevflow", "broker.json")) {
  try {
    const state = JSON.parse(await readFile(path, "utf8"));
    return Number.isInteger(state?.port) && state.port > 0 && state.port <= 65535
      ? state
      : null;
  } catch {
    return null;
  }
}

export class DeviceLeaseClient {
  constructor(options = {}) {
    this._readState = options.readState || readBrokerState;
    this._fetch = options.fetch || globalThis.fetch;
    this._heartbeatMs = options.heartbeatMs || HEARTBEAT_MS;
  }

  async run(ctx, target, operation) {
    const state = await this._readState();
    if (!state?.port || typeof this._fetch !== "function") {
      throw new Error(
        "The DevFlow broker is required for coordinated device mutation. " +
        "Start it with 'maui devflow broker start'.",
      );
    }
    const instanceId = String(ctx?.instanceId || "");
    if (!instanceId) throw new Error("The canvas instance identity is unavailable.");
    const leaseId = `maui-device-canvas:${randomBytes(16).toString("hex")}`;
    const transactionId = `canvas-call:${randomBytes(16).toString("hex")}`;
    const request = (action, transaction = null) => this._control(
      state.port,
      action,
      leaseId,
      transaction,
      target,
    );

    const claim = await request("claim");
    if (!claim.allowed) throw new Error(claim.error || leaseConflict(claim));
    const begin = await request("begin", transactionId);
    if (!begin.allowed || begin.transactionId !== transactionId) {
      await request("release").catch(() => {});
      throw new Error(begin.error || "The broker could not open the device mutation transaction.");
    }

    let finished = false;
    let heartbeatLost = null;
    const operationCancellation = new AbortController();
    const heartbeat = setInterval(() => {
      request("heartbeat", transactionId)
        .then((status) => {
          if (!status.allowed || status.transactionId !== transactionId) {
            heartbeatLost = new Error(status.error || "The coordinated device lease was lost.");
            heartbeatLost.unknownCompletion = true;
            operationCancellation.abort(heartbeatLost);
          }
        })
        .catch(() => {
          heartbeatLost = new Error("The DevFlow broker stopped renewing the coordinated device lease.");
          heartbeatLost.unknownCompletion = true;
          operationCancellation.abort(heartbeatLost);
        });
    }, this._heartbeatMs);

    try {
      const result = await operation(operationCancellation.signal);
      if (heartbeatLost) throw heartbeatLost;
      finished = true;
      const cleanupFailures = [];
      try {
        const ended = await request("end", transactionId);
        if (ended.error) cleanupFailures.push(ended.error);
      } catch (error) {
        cleanupFailures.push(String(error?.message || error));
      }
      try {
        const released = await request("release");
        if (released.error) cleanupFailures.push(released.error);
      } catch (error) {
        cleanupFailures.push(String(error?.message || error));
      }
      if (cleanupFailures.length) {
        // The device mutation already completed. Reporting it as failed would invite an unsafe
        // retry, so preserve the result and leave any unclosed transaction to the broker TTL.
        console.warn(`Mobile Canvas lease cleanup was incomplete: ${cleanupFailures.join("; ")}`);
      }
      return result;
    } catch (error) {
      if (error?.unknownCompletion !== true) {
        await request("end", transactionId).catch(() => {});
        await request("release").catch(() => {});
      }
      throw error;
    } finally {
      clearInterval(heartbeat);
      if (!finished && heartbeatLost) {
        // Unknown completion remains transaction-bound until the broker's bounded TTL expires.
      }
    }
  }

  async _control(port, action, leaseId, transactionId, target) {
    const response = await this._fetch(`http://localhost:${port}/api/device-leases`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        action,
        leaseId,
        transactionId,
        deviceId: target?.deviceId || null,
        catalog: target?.catalog === true,
        force: false,
      }),
    });
    const body = await response.json().catch(() => null);
    if (!response.ok || body?.ok !== true)
      return { allowed: false, error: body?.error || `Broker lease request failed (${response.status}).` };
    return body;
  }
}

function leaseConflict(status) {
  return status?.heldByOther
    ? `Another DevFlow session (${status.label || status.holderKind || "unknown"}) is driving this device.`
    : "The broker did not grant the device mutation lease.";
}
