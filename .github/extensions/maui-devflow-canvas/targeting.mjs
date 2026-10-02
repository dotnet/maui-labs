function normalizeIdentity(value) {
  return String(value ?? "").trim().toLowerCase();
}

function normalizeProject(value) {
  const normalized = String(value ?? "").trim().replace(/\\/g, "/");
  return process.platform === "win32" ? normalized.toLowerCase() : normalized;
}

export function agentIdentityKey(agent) {
  if (!agent) return null;
  const project = normalizeProject(agent.project);
  // Filename-only project and default session identities can collide across worktrees.
  if (!project.startsWith("/") && !/^[a-z]:\//i.test(project)) return null;
  const parts = [
    project,
    normalizeIdentity(agent.tfm),
    normalizeIdentity(agent.platform),
    normalizeIdentity(agent.appName),
  ];
  if (!parts.every(Boolean)) return null;
  const sessionId = normalizeIdentity(agent.sessionId);
  if (sessionId) parts.push(sessionId);
  return parts.join("\u0000");
}

export function resolveAgentTarget(
  agents,
  { targetId = null, targetIdentity = null, preferredPort = null } = {},
) {
  const liveAgents = Array.isArray(agents) ? agents : [];
  const hasPinnedTarget = !!targetId || !!targetIdentity || Number.isFinite(preferredPort);

  if (targetId) {
    const exact = liveAgents.find((agent) => agent?.id === targetId);
    if (exact) return { agent: exact, state: "ready" };
  }

  if (targetIdentity) {
    const matches = liveAgents.filter((agent) => agentIdentityKey(agent) === targetIdentity);
    if (matches.length === 1) return { agent: matches[0], state: "ready" };
    return { agent: null, state: matches.length > 1 ? "multiple" : "target" };
  }

  if (targetId)
    return { agent: null, state: "target" };

  if (Number.isFinite(preferredPort)) {
    const portMatch = liveAgents.find((agent) => agent?.port === preferredPort);
    if (portMatch) return { agent: portMatch, state: "ready" };
  }

  if (hasPinnedTarget)
    return { agent: null, state: "target" };
  if (liveAgents.length === 1)
    return { agent: liveAgents[0], state: "ready" };
  return { agent: null, state: liveAgents.length > 1 ? "multiple" : "app" };
}
