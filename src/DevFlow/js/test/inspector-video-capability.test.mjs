import assert from "node:assert/strict";
import test from "node:test";

import {
  probePreviewMediaCapabilities,
} from "../prototypes/inspector-video-preview/capability-probe.mjs";

test("probe reports decoder, MSE, and receiver support independently", async () => {
  class FakeDecoder {
    static async isConfigSupported(config) {
      return { supported: config.codec === "vp8", config };
    }
  }

  const result = await probePreviewMediaCapabilities({
    VideoDecoder: FakeDecoder,
    WebSocket: class {},
    MediaSource: {
      isTypeSupported: value => value.includes("vp8"),
    },
    RTCRtpReceiver: {
      getCapabilities: () => ({
        codecs: [{ mimeType: "video/VP8", clockRate: 90000 }],
      }),
    },
    document: { visibilityState: "hidden" },
    isSecureContext: true,
  });

  assert.equal(result.secureContext, true);
  assert.equal(result.visibilityState, "hidden");
  assert.equal(result.websocket, true);
  assert.equal(result.webCodecs.decoder["vp8"].supported, true);
  assert.equal(result.webCodecs.decoder["h264-baseline"].supported, false);
  assert.equal(result.mediaSource.types.vp8, true);
  assert.deepEqual(result.webRtc.mimeTypes, ["video/VP8"]);
});

test("probe fails closed when browser media APIs are unavailable", async () => {
  const result = await probePreviewMediaCapabilities({
    document: { visibilityState: "visible" },
    isSecureContext: false,
  });

  assert.equal(result.secureContext, false);
  assert.equal(result.websocket, false);
  assert.equal(result.webCodecs.decoderAvailable, false);
  assert.equal(result.webCodecs.decoder["h264-baseline"].supported, false);
  assert.equal(result.mediaSource.available, false);
  assert.equal(result.webRtc.available, false);
});
