import type { AgentRegistration } from "@maui-devflow/client";

export function normalizeAgentIdentityPart(value: unknown): string | null {
  const trimmed = typeof value === "string" ? value.trim() : "";
  return trimmed ? trimmed : null;
}

export function agentRuntimeIdentity(agent: AgentRegistration): string {
  return [
    agent.id,
    agent.sessionId ?? "",
    agent.processId ?? "",
    agent.deviceId ?? "",
    agent.packageId ?? "",
  ].join("|");
}

export function sameAgentIdentity(
  current: AgentRegistration,
  candidate: AgentRegistration,
): boolean {
  const identityParts = [
    normalizeAgentIdentityPart(current.project),
    normalizeAgentIdentityPart(current.tfm),
    normalizeAgentIdentityPart(current.platform),
    normalizeAgentIdentityPart(current.appName),
    normalizeAgentIdentityPart(current.packageId),
    normalizeAgentIdentityPart(current.deviceId),
    normalizeAgentIdentityPart(current.sessionId),
    normalizeAgentIdentityPart(current.processId == null ? null : String(current.processId)),
  ];
  if (identityParts.slice(0, 4).some((part) => part == null)) return false;

  const candidateOptionalParts = [
    candidate.packageId,
    candidate.deviceId,
    candidate.sessionId,
    candidate.processId == null ? null : String(candidate.processId),
  ];
  return identityParts[0] === normalizeAgentIdentityPart(candidate.project) &&
    identityParts[1] === normalizeAgentIdentityPart(candidate.tfm) &&
    identityParts[2] === normalizeAgentIdentityPart(candidate.platform) &&
    identityParts[3] === normalizeAgentIdentityPart(candidate.appName) &&
    identityParts.slice(4).every((part, index) =>
      part == null || part === normalizeAgentIdentityPart(candidateOptionalParts[index]));
}

export function selectRefreshedAgent(
  agents: AgentRegistration[],
  current: AgentRegistration,
): AgentRegistration | null {
  const sameIdentityMatches = agents.filter((candidate) => sameAgentIdentity(current, candidate));
  const currentId = normalizeAgentIdentityPart(current.id);
  if (currentId) {
    const exactIdMatches = sameIdentityMatches.filter(
      (candidate) => normalizeAgentIdentityPart(candidate.id) === currentId,
    );
    if (exactIdMatches.length === 1) return exactIdMatches[0];
    if (exactIdMatches.length > 1) return null;
  }

  return sameIdentityMatches.length === 1 ? sameIdentityMatches[0] : null;
}
