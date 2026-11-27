export const VIDEO_CODEC_PROBES = Object.freeze([
  Object.freeze({ id: "h264-baseline", codec: "avc1.42E01E", codedWidth: 390, codedHeight: 844 }),
  Object.freeze({ id: "vp8", codec: "vp8", codedWidth: 390, codedHeight: 844 }),
  Object.freeze({ id: "vp9-profile0", codec: "vp09.00.30.08", codedWidth: 390, codedHeight: 844 }),
  Object.freeze({ id: "av1-main", codec: "av01.0.04M.08", codedWidth: 390, codedHeight: 844 }),
]);

async function probeCodec(api, config) {
  if (!api || typeof api.isConfigSupported !== "function") {
    return { available: false, supported: false, error: "api-unavailable" };
  }
  try {
    const result = await api.isConfigSupported(config);
    return {
      available: true,
      supported: result?.supported === true,
      error: null,
    };
  } catch (error) {
    return {
      available: true,
      supported: false,
      error: String(error?.message ?? error),
    };
  }
}

function probeMediaSource(environment) {
  const mediaSource = environment.MediaSource;
  if (!mediaSource || typeof mediaSource.isTypeSupported !== "function") {
    return { available: false, types: {} };
  }
  const types = {
    h264: 'video/mp4; codecs="avc1.42E01E"',
    vp8: 'video/webm; codecs="vp8"',
    vp9: 'video/webm; codecs="vp09.00.30.08"',
    av1: 'video/webm; codecs="av01.0.04M.08"',
  };
  return {
    available: true,
    types: Object.fromEntries(
      Object.entries(types).map(([name, value]) => [name, mediaSource.isTypeSupported(value)]),
    ),
  };
}

function readRtpCapabilities(api) {
  if (!api || typeof api.getCapabilities !== "function") {
    return { available: false, mimeTypes: [] };
  }
  try {
    const codecs = api.getCapabilities("video")?.codecs ?? [];
    return {
      available: true,
      mimeTypes: [...new Set(codecs.map(codec => codec.mimeType).filter(Boolean))].sort(),
    };
  } catch (error) {
    return { available: true, mimeTypes: [], error: String(error?.message ?? error) };
  }
}

function probeWebRtc(environment) {
  return readRtpCapabilities(environment.RTCRtpReceiver);
}

export async function probePreviewMediaCapabilities(environment = globalThis) {
  const decoder = {};
  for (const probe of VIDEO_CODEC_PROBES) {
    decoder[probe.id] = await probeCodec(environment.VideoDecoder, {
      codec: probe.codec,
      codedWidth: probe.codedWidth,
      codedHeight: probe.codedHeight,
    });
  }

  return {
    secureContext: environment.isSecureContext === true,
    visibilityState: environment.document?.visibilityState ?? null,
    websocket: typeof environment.WebSocket === "function",
    websocketStream: typeof environment.WebSocketStream === "function",
    webCodecs: {
      decoderAvailable: typeof environment.VideoDecoder === "function",
      decoder,
    },
    mediaSource: probeMediaSource(environment),
    webRtc: probeWebRtc(environment),
  };
}
