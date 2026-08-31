import test from "node:test";
import assert from "node:assert/strict";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const sourceRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../../..");
const videoModule = pathToFileURL(
  resolve(sourceRoot, "Cli/Microsoft.Maui.Cli/DevFlow/Inspector/Web/inspector-video.js"),
).href;

const {
  splitNalUnits,
  groupAccessUnits,
  nalType,
  toAnnexB,
  codecFromSps,
  trailingNalUnit,
  parseStreamDescriptor,
  isVideoSupported,
  startsNewPicture,
  DeviceVideoSurface,
} = await import(videoModule);

// H.264 arrives as a byte stream that does not align to WebSocket messages or to frames. Every bug
// in this layer looks like "the video is corrupt" rather than like a framing mistake, so the
// splitting and grouping are pinned directly.

const START_LONG = [0, 0, 0, 1];
const START_SHORT = [0, 0, 1];

function nal(type, ...payload) {
  return [type & 0x1f, ...payload];
}

function bytes(...parts) {
  return new Uint8Array(parts.flat());
}

test("splits units on 4-byte start codes", () => {
  const input = bytes(START_LONG, nal(7, 1, 2), START_LONG, nal(8, 3), START_LONG, nal(5, 9, 9));
  const { units } = splitNalUnits(input);

  // The final unit is the remainder: without a following start code we cannot know it is complete.
  assert.equal(units.length, 2);
  assert.equal(nalType(units[0]), 7);
  assert.equal(nalType(units[1]), 8);
});

test("splits units on 3-byte start codes", () => {
  const input = bytes(START_SHORT, nal(7, 1), START_SHORT, nal(5, 2), START_SHORT, nal(1, 3));
  const { units } = splitNalUnits(input);

  assert.equal(units.length, 2);
  assert.equal(nalType(units[0]), 7);
  assert.equal(nalType(units[1]), 5);
});

test("returns the trailing bytes as a raw remainder rather than emitting a partial unit", () => {
  // The decisive property: a message boundary mid-NAL must not produce half a slice. The
  // remainder keeps its start code so it can simply be prepended to the next chunk.
  const input = bytes(START_LONG, nal(5, 1, 2, 3));
  const { units, remainder } = splitNalUnits(input);

  assert.equal(units.length, 0);
  assert.deepEqual([...remainder], [0, 0, 0, 1, 5, 1, 2, 3]);
});

test("a unit split across two messages reassembles intact", () => {
  const whole = bytes(START_LONG, nal(5, 1, 2, 3, 4), START_LONG, nal(1, 9));

  const first = whole.subarray(0, 6);
  const second = whole.subarray(6);

  const a = splitNalUnits(first);
  const combined = new Uint8Array(a.remainder.length + second.length);
  combined.set(a.remainder, 0);
  combined.set(second, a.remainder.length);
  const b = splitNalUnits(combined);

  const recovered = a.units.concat(b.units);
  assert.equal(recovered.length, 1);
  assert.equal(nalType(recovered[0]), 5);
  assert.deepEqual([...recovered[0]], [5, 1, 2, 3, 4]);
});

test("groups parameter sets with the keyframe that follows them", () => {
  // Handing an SPS to the decoder as if it were a picture is a classic way to get a decoder that
  // configures and then never outputs anything.
  //
  // Note the trailing unit: a picture is closed by what FOLLOWS it, because a slice alone cannot
  // prove no further slices of the same picture are coming. That costs one frame of latency on a
  // live stream and is what makes multi-slice encoders work at all.
  const units = [
    Uint8Array.from(nal(7, 1)),
    Uint8Array.from(nal(8, 2)),
    Uint8Array.from([5, 0x80, 3]),
    Uint8Array.from(nal(7, 4)),      // next picture's parameter set closes the first
  ];

  const { frames } = groupAccessUnits(units);

  assert.equal(frames.length, 1);
  assert.equal(frames[0].key, true);
  assert.equal(frames[0].nals.length, 3);
});

test("marks non-IDR slices as delta frames", () => {
  const { frames } = groupAccessUnits([
    Uint8Array.from([1, 0x80, 1]),
    Uint8Array.from([1, 0x80, 2]),
  ]);

  assert.equal(frames.length, 1);
  assert.equal(frames[0].key, false);
});

test("holds incomplete groups back as pending", () => {
  // Parameter sets with no following slice are not a picture yet.
  const { frames, pending } = groupAccessUnits([Uint8Array.from(nal(7, 1)), Uint8Array.from(nal(8, 2))]);

  assert.equal(frames.length, 0);
  assert.equal(pending.length, 2);
});

test("re-emits Annex-B with 4-byte start codes", () => {
  const out = toAnnexB([Uint8Array.from(nal(5, 7)), Uint8Array.from(nal(1, 8))]);

  assert.deepEqual([...out], [0, 0, 0, 1, 5, 7, 0, 0, 0, 1, 1, 8]);
});

test("derives the WebCodecs AVC profile from an SPS", () => {
  assert.equal(codecFromSps(Uint8Array.from([7, 0x64, 0x00, 0x1f])), "avc1.64001f");
  assert.equal(codecFromSps(Uint8Array.from([5, 0x80, 1])), null);
});

test("extracts a complete trailing NAL from the raw Annex-B remainder", () => {
  assert.deepEqual(
    [...trailingNalUnit(Uint8Array.from([0, 0, 0, 1, 5, 0x80, 1]))],
    [5, 0x80, 1],
  );
  assert.equal(trailingNalUnit(Uint8Array.from([5, 0x80, 1])), null);
});

test("parses and bounds the upstream stream descriptor", () => {
  const descriptor = parseStreamDescriptor(JSON.stringify({
    encoding: "h264-annexb",
    framesPerSecond: 30,
    scale: 0.5,
    display: { pointWidth: 390, pointHeight: 844 },
    source: "idb",
    sourceDetail: "fallback",
  }));

  assert.equal(descriptor.source, "idb");
  assert.equal(descriptor.scale, 0.5);
  assert.equal(descriptor.display.pointWidth, 390);
  assert.throws(
    () => parseStreamDescriptor('{"encoding":"vp9","framesPerSecond":30,"scale":1}'),
    /unsupported encoding/i,
  );
  assert.throws(
    () => parseStreamDescriptor('{"encoding":"h264-annexb","framesPerSecond":90,"scale":1}'),
    /descriptor is invalid/i,
  );
});

test("groups multiple slices of one picture into a single frame", () => {
  // A picture may be coded as several slice NALs — common on hardware encoders under a bandwidth
  // cap. Submitting each as its own picture feeds the decoder partial frames, it errors, and video
  // dies permanently for the session. first_mb_in_slice, not the NAL type, marks a new picture.
  const firstSlice = Uint8Array.from([5, 0x80, 1]);   // first_mb_in_slice = 0
  const secondSlice = Uint8Array.from([5, 0x40, 2]);  // continuation of the same picture

  const { frames } = groupAccessUnits([
    Uint8Array.from(nal(7, 1)),
    Uint8Array.from(nal(8, 2)),
    firstSlice,
    secondSlice,
  ]);

  assert.equal(frames.length, 0, "the picture is still pending until something follows it");

  const { frames: closed } = groupAccessUnits([
    Uint8Array.from(nal(7, 1)),
    firstSlice,
    secondSlice,
    Uint8Array.from(nal(7, 1)),      // parameter set for the NEXT picture closes this one
    Uint8Array.from([5, 0x80, 3]),
  ]);

  assert.equal(closed.length, 1);
  assert.equal(closed[0].key, true);
  assert.equal(closed[0].nals.length, 3, "SPS + both slices");
});

test("starts a new picture when a slice begins at macroblock zero", () => {
  const first = Uint8Array.from([1, 0x80, 1]);
  const second = Uint8Array.from([1, 0x80, 2]);

  const { frames } = groupAccessUnits([first, second]);

  assert.equal(frames.length, 1, "the first picture is closed by the second starting");
});

test("identifies a first_mb_in_slice of zero", () => {
  assert.equal(startsNewPicture(Uint8Array.from([5, 0x80])), true);
  assert.equal(startsNewPicture(Uint8Array.from([5, 0x40])), false);
});

test("decodes frames delivered through the socket, not just through the parser", () => {
  // The wiring gap: every other decode test calls _consume directly, so a broken onmessage
  // handler — a wrong event property, or the ArrayBuffer guard rejecting a Blob because
  // binaryType was not honoured — would leave all of them passing while no frame ever decodes.
  const decoded = [];
  const configurations = [];
  const descriptors = [];
  let created = null;

  const scope = {
    VideoDecoder: class {
      constructor(init) { this.init = init; this.state = "unconfigured"; }
      configure(configuration) { configurations.push(configuration); this.state = "configured"; }
      decode(chunk) { decoded.push(chunk.type); }
      close() { this.state = "closed"; }
    },
    EncodedVideoChunk: class { constructor(init) { Object.assign(this, init); } },
    WebSocket: class {
      constructor(url) { this.url = url; created = this; }
      close() {}
    },
  };

  const surface = new DeviceVideoSurface({
    url: "ws://localhost/ws/video",
    canvas: { getContext: () => ({ drawImage() {} }) },
    scope,
    onDescriptor: (descriptor) => descriptors.push(descriptor),
  });

  assert.equal(surface.start(), true);
  assert.equal(created.binaryType, "arraybuffer", "a Blob would fail the ArrayBuffer guard");
  created.onmessage({
    data: JSON.stringify({
      encoding: "h264-annexb",
      framesPerSecond: 30,
      scale: 1,
      display: { pointWidth: 390, pointHeight: 844 },
      source: "idb",
    }),
  });

  const frame = bytes(
    START_LONG, nal(7, 0x42, 0xe0, 0x1e),
    START_LONG, [5, 0x80, 9],
    START_LONG, [1, 0x80, 7],     // closes the keyframe
    START_LONG, [1, 0x80, 8],     // and gives the parser something to hold as pending
  );
  created.onmessage({ data: frame.buffer.slice(frame.byteOffset, frame.byteOffset + frame.byteLength) });

  assert.deepEqual(decoded, ["key"]);
  assert.equal(descriptors.length, 1);
  assert.equal(surface.descriptor.source, "idb");
  assert.equal(configurations[0].codec, "avc1.42e01e");
  assert.equal(configurations[0].optimizeForLatency, false);
  assert.deepEqual(configurations[0].avc, { format: "annexb" });
});

test("reports unsupported when WebCodecs is missing", () => {
  assert.equal(isVideoSupported({}), false);
  assert.equal(isVideoSupported({ VideoDecoder: function () {}, EncodedVideoChunk: function () {} }), true);
});

test("flushes an event-driven stream's final picture after an idle gap", async () => {
  const decoded = [];
  let decoderFlushes = 0;
  let created = null;
  const scope = {
    VideoDecoder: class {
      constructor() { this.state = "unconfigured"; }
      configure() { this.state = "configured"; }
      decode(chunk) { decoded.push(chunk.type); }
      flush() { decoderFlushes++; return Promise.resolve(); }
      close() { this.state = "closed"; }
    },
    EncodedVideoChunk: class { constructor(init) { Object.assign(this, init); } },
    WebSocket: class {
      constructor() { created = this; }
      close() {}
    },
  };
  const surface = new DeviceVideoSurface({
    url: "ws://localhost/ws/video",
    canvas: { getContext: () => ({ drawImage() {} }) },
    scope,
    idleFlushMs: 5,
  });

  surface.start();
  created.onmessage({
    data: JSON.stringify({
      encoding: "h264-annexb",
      framesPerSecond: 30,
      scale: 1,
      source: "idb",
    }),
  });
  const frame = bytes(
    START_LONG, nal(7, 0x64, 0x00, 0x1f),
    START_LONG, nal(8, 1),
    START_LONG, [5, 0x80, 9],
  );
  created.onmessage({ data: frame.buffer.slice(frame.byteOffset, frame.byteOffset + frame.byteLength) });

  await new Promise((resolve) => setTimeout(resolve, 30));

  assert.deepEqual(decoded, ["key"]);
  assert.equal(decoderFlushes, 1);
  surface.stop();
});

test("degrades instead of starting when the browser cannot decode", () => {
  // The Inspector must fall back to screenshots, not show a dead panel.
  let reason = null;
  const surface = new DeviceVideoSurface({
    url: "ws://localhost/ws/video",
    canvas: {},
    scope: {},
    onUnavailable: (m) => { reason = m; },
  });

  assert.equal(surface.start(), false);
  assert.match(reason, /cannot decode/i);
});

test("degrades when there is no stream url", () => {
  let reason = null;
  const surface = new DeviceVideoSurface({
    canvas: {},
    scope: { VideoDecoder: function () {}, EncodedVideoChunk: function () {} },
    onUnavailable: (m) => { reason = m; },
  });

  assert.equal(surface.start(), false);
  assert.ok(reason);
});

test("discards frames until the first keyframe arrives", () => {
  // A decoder started mid-stream has no reference frames, so delta frames before the first
  // keyframe decode to garbage. This is why the stream takes a moment to appear.
  const decoded = [];
  const scope = {
    VideoDecoder: class {
      constructor(init) { this.init = init; this.state = "unconfigured"; }
      configure() { this.state = "configured"; }
      decode(chunk) { decoded.push(chunk.type); }
      close() { this.state = "closed"; }
    },
    EncodedVideoChunk: class {
      constructor(init) { Object.assign(this, init); }
    },
    WebSocket: class { constructor() { this.readyState = 0; } close() {} },
  };

  const surface = new DeviceVideoSurface({
    url: "ws://localhost/ws/video",
    canvas: { getContext: () => ({ drawImage() {} }) },
    scope,
  });
  surface.start();

  // Two delta frames arrive before any keyframe and are dropped. The keyframe itself is still
  // pending at this point, because a picture is closed by what follows it.
  surface._consume(bytes(
    START_LONG, [1, 0x80, 1],
    START_LONG, [1, 0x80, 2],
    START_LONG, [5, 0x80, 3],
    START_LONG, [1, 0x80, 4],
  ));

  assert.deepEqual(decoded, [], "nothing decodes before a keyframe is complete");

  // The next slices close the keyframe and then a delta, which is now decodable because the
  // decoder finally has a reference frame.
  surface._consume(bytes(START_LONG, [1, 0x80, 5], START_LONG, [1, 0x80, 6]));

  assert.deepEqual(decoded, ["key", "delta"]);
});
