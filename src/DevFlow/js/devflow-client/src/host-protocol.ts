export const INSPECTOR_HOST_PROTOCOL = Object.freeze({
  currentVersion: 2,
  minimumVersion: 1,
});

// The closed set of surfaces that can embed the Inspector. Host identity never selects a layout or
// a behaviour — it exists for presence copy and for the broker's source-apply security contract,
// which matches these values exactly.
export const INSPECTOR_HOST_IDS = Object.freeze(["browser", "vscode", "canvas"] as const);

export type InspectorHostId = (typeof INSPECTOR_HOST_IDS)[number];

export interface InspectorHostCapabilityDescriptor {
  name: string;
  version: number;
  constraints?: Readonly<Record<string, string | number | boolean>>;
}

export interface InspectorHostManifest {
  type: "devflow:host";
  v: 1;
  protocol: {
    version: number;
    minimumVersion: number;
    maximumVersion: number;
  };
  hostId: InspectorHostId;
  hostLabel: string;
  interactionSessionId: string;
  capabilities: string[];
  capabilityDescriptors: InspectorHostCapabilityDescriptor[];
}

export function createInspectorHostManifest(options: {
  hostId: InspectorHostId;
  hostLabel: string;
  interactionSessionId: string;
  capabilities: readonly string[];
}): InspectorHostManifest {
  const capabilities = [...new Set(options.capabilities.filter((name) =>
    typeof name === "string" && name.length > 0 && name.length <= 128))];
  return {
    type: "devflow:host",
    v: 1,
    protocol: {
      version: INSPECTOR_HOST_PROTOCOL.currentVersion,
      minimumVersion: INSPECTOR_HOST_PROTOCOL.minimumVersion,
      maximumVersion: INSPECTOR_HOST_PROTOCOL.currentVersion,
    },
    hostId: options.hostId,
    hostLabel: options.hostLabel,
    interactionSessionId: options.interactionSessionId,
    capabilities,
    capabilityDescriptors: capabilities.map((name) => ({ name, version: 1 })),
  };
}
