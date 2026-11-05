import { createHash } from "node:crypto";
import { agentIdentityKey, resolveAgentTarget } from "./targeting.mjs";

function rememberTarget(st, agent) {
  st.targetAgentId = agent?.id || null;
  st.targetIdentity = agentIdentityKey(agent);
  st.targetPort = Number.isFinite(agent?.port) ? agent.port : st.targetPort;
}

function queueTargetMutation(st, operation) {
  const run = (st.targetMutation ?? Promise.resolve()).then(operation, operation);
  st.targetMutation = run.then(() => undefined, () => undefined);
  return run;
}

const superseded = () => ({
  ok: false,
  connected: false,
  error: "The Inspector target changed and must be selected explicitly.",
});

export function createTargetController({ readBrokerState, fetchBrokerAgents }) {
  async function liveBrokerAgents() {
    const state = readBrokerState();
    return state?.port ? fetchBrokerAgents(state.port) : null;
  }

  async function alignStoreTarget(st, agent) {
    const resolved = st.store.device.resolvedAgent?.();
    if (resolved?.id === agent.id &&
        st.store.device.whichPort?.() === agent.port &&
        Number(st.store.device.opts?.agentPort) === agent.port)
      return true;

    const snap = await st.store.selectAgent({ port: agent.port, platform: agent.platform });
    const aligned = st.store.device.resolvedAgent?.();
    return snap?.connected === true &&
      st.store.device.whichPort?.() === agent.port &&
      (!aligned?.id || aligned.id === agent.id);
  }

  function selectTargetAgent(st, { platform, port } = {}) {
    const requestedPort = port == null ? null : Number(port);
    const revision = (st.targetRevision ?? 0) + 1;
    // Record intent before any discovery/connection await, including queued switches.
    st.targetRevision = revision;
    st.targetExplicit = true;
    st.targetAgentId = null;
    st.targetIdentity = null;
    st.targetPort = Number.isFinite(requestedPort) ? requestedPort : null;

    return queueTargetMutation(st, async () => {
      if (st.targetRevision !== revision) return superseded();
      const agents = await liveBrokerAgents();
      if (st.targetRevision !== revision) return superseded();
      const candidates = Array.isArray(agents) ? agents.filter((agent) =>
        Number.isFinite(requestedPort)
          ? agent.port === requestedPort
          : !platform || String(agent.platform).toLowerCase() === String(platform).toLowerCase()) : [];
      const requested = candidates.length === 1 ? candidates[0] : null;
      if (requested) rememberTarget(st, requested);

      const snap = await st.store.selectAgent({ platform, port });
      if (st.targetRevision !== revision) return superseded();
      if (snap?.connected === true) {
        const resolved = st.store.device.resolvedAgent?.();
        const connectedPort = st.store.device.whichPort?.();
        if ((requested && resolved?.id && resolved.id !== requested.id) ||
            (Number.isFinite(st.targetPort) && connectedPort !== st.targetPort))
          return { ok: false, connected: false, error: "The selected DevFlow agent could not be connected." };
        if (resolved?.id) rememberTarget(st, resolved);
        else if (!requested && Number.isFinite(connectedPort)) st.targetPort = connectedPort;
      }
      return snap;
    });
  }

  function syncTargetAgent(st, agentId) {
    const revision = st.targetRevision ?? 0;
    return queueTargetMutation(st, async () => {
      if ((st.targetRevision ?? 0) !== revision) return superseded();
      const agents = await liveBrokerAgents();
      if ((st.targetRevision ?? 0) !== revision) return superseded();
      const requested = Array.isArray(agents) ? agents.find((agent) => agent.id === agentId) : null;
      if (!requested)
        return { ok: false, error: "The selected DevFlow agent is no longer running." };
      const wrongTarget = st.targetIdentity
        ? agentIdentityKey(requested) !== st.targetIdentity
        : st.targetAgentId
          ? requested.id !== st.targetAgentId
          : (st.targetExplicit || Number.isFinite(st.targetPort)) && requested.port !== st.targetPort;
      if (wrongTarget)
        return superseded();

      const aligned = await alignStoreTarget(st, requested);
      if ((st.targetRevision ?? 0) !== revision) return superseded();
      if (!aligned)
        return { ok: false, error: "The selected DevFlow agent could not be connected." };
      rememberTarget(st, requested);
      return { ok: true, agentId: requested.id, port: requested.port };
    });
  }

  async function resolveInspectorState(st) {
    const snapshot = st.store.snapshot();
    let appName = snapshot.connected ? snapshot.info?.appName || null : null;
    try {
      const state = readBrokerState();
      if (!state?.port)
        return { ready: false, state: "broker", appName, inspectorUrl: null, agentId: null };
      const agents = await fetchBrokerAgents(state.port);
      if (agents === null)
        return { ready: false, state: "broker", appName, inspectorUrl: null, agentId: null };

      const resolved = st.targetExplicit ? null : st.store.device.resolvedAgent?.();
      if (st.targetExplicit && !st.targetAgentId && !st.targetIdentity && !Number.isFinite(st.targetPort))
        return { ready: false, state: "target", appName, inspectorUrl: null, agentId: null };
      const target = resolveAgentTarget(agents, {
        targetId: st.targetAgentId || resolved?.id || null,
        targetIdentity: st.targetIdentity || agentIdentityKey(resolved),
        preferredPort: st.targetPort ?? (st.targetExplicit ? null : st.store.device.whichPort?.()),
      });
      const match = target.agent;
      if (!match?.id)
        return { ready: false, state: target.state, appName, inspectorUrl: null, agentId: null };

      appName = match.appName || appName;
      // HttpListener requires a localhost Host header, not 127.0.0.1.
      const base = `http://localhost:${state.port}/inspector/${encodeURIComponent(match.id)}/`;
      const inspectorUrl = state.embedToken ? `${base}?embed=${encodeURIComponent(state.embedToken)}` : base;
      const generation = createHash("sha256")
        .update(`${state.port}|${state.startedAt || ""}|${state.embedToken || ""}`)
        .digest("hex").slice(0, 16);
      return { ready: true, state: "ready", appName, inspectorUrl, agentId: match.id, generation };
    } catch {
      return { ready: false, state: "broker", appName, inspectorUrl: null, agentId: null };
    }
  }

  return { selectTargetAgent, syncTargetAgent, resolveInspectorState };
}
