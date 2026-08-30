// Live device video for the DevFlow Inspector.
//
// The Inspector's frame source has always been a screenshot: fetched, cached ~200ms, and re-polled.
// That is a slideshow of a thing the user is trying to interact with. Where the device layer can
// stream, this replaces it with decoded H.264 painted straight onto the stage backdrop.
//
// Three properties this module has to preserve, because the Inspector already depends on them:
//
//   1. It NEVER touches the agent. Frames come from the device host through the broker's proxy, so
//      video costs the app nothing and — critically — keeps arriving after the app has died.
//   2. It NEVER drives the tree. Frame cadence and tree cadence are separate: 50 frames a second
//      must not mean 50 visual-tree pulls a second. This module emits no refreshes at all.
//   3. It always degrades. No WebCodecs, no stream capability, an upstream that refuses — every
//      one of them falls back to the existing screenshot path rather than showing a dead panel.

const NAL_START_LONG = 4;
const NAL_START_SHORT = 3;
const IDLE_FLUSH_MS = 120;

/** H.264 NAL unit types we care about for framing decisions. */
const NAL_IDR = 5;   // keyframe slice
const NAL_NON_IDR = 1;
const NAL_SPS = 7;
const NAL_PPS = 8;

/**
 * Splits an Annex-B byte stream into NAL units.
 *
 * The stream arrives as WebSocket messages that do not necessarily align to frame boundaries, so
 * the caller carries a remainder between calls. Returning the tail rather than assuming alignment
 * is what stops a decoder from being fed half a slice, which it reports as a corrupt stream rather
 * than as a framing bug.
 */
export function splitNalUnits(bytes) {
  const units = [];
  let payloadStart = -1;
  let startCodeStart = -1;
  let i = 0;

  while (i + 2 < bytes.length) {
    const isLong = i + 3 < bytes.length &&
      bytes[i] === 0 && bytes[i + 1] === 0 && bytes[i + 2] === 0 && bytes[i + 3] === 1;
    const isShort = bytes[i] === 0 && bytes[i + 1] === 0 && bytes[i + 2] === 1;

    if (isLong || isShort) {
      if (payloadStart >= 0) units.push(bytes.subarray(payloadStart, i));
      startCodeStart = i;
      payloadStart = i + (isLong ? NAL_START_LONG : NAL_START_SHORT);
      i = payloadStart;
      continue;
    }
    i++;
  }

  // The remainder is a RAW tail including its start code, so a caller can prepend it to the next
  // chunk and split again. Returning the payload without its start code would make the reassembled
  // buffer begin mid-unit, and the next split would silently discard it — losing exactly the
  // frames that straddle a message boundary.
  const remainder = startCodeStart >= 0 ? bytes.subarray(startCodeStart) : bytes;
  return { units, remainder };
}

/** The NAL type of a unit body (the start code is already stripped). */
export function nalType(unit) {
  return unit.length > 0 ? (unit[0] & 0x1f) : 0;
}

/**
 * Groups NAL units into access units — one decodable picture each.
 *
 * A decoder must be handed whole pictures. Two ways to get this wrong, both of which surface as
 * "the video is corrupt" rather than as a framing bug:
 *
 *   * Parameter sets (SPS/PPS) emitted alone. They belong with the keyframe that follows them;
 *     feeding an SPS as if it were a picture yields a decoder that configures and then never
 *     outputs anything.
 *   * One chunk per SLICE. A single picture may be coded as several slice NALs — common on
 *     hardware encoders under a bandwidth cap — and submitting each as its own picture feeds the
 *     decoder partial frames, which it rejects. A new picture is detected by the slice header's
 *     first_mb_in_slice being zero rather than by the NAL type alone.
 */
export function groupAccessUnits(units, flushPending = false) {
  const frames = [];
  let pending = [];
  let pendingIsKey = false;
  let pendingHasSlice = false;

  const flush = () => {
    if (pending.length === 0) return;
    frames.push({ nals: pending, key: pendingIsKey });
    pending = [];
    pendingIsKey = false;
    pendingHasSlice = false;
  };

  for (const unit of units) {
    const type = nalType(unit);
    const isSlice = type === NAL_IDR || type === NAL_NON_IDR;

    if (isSlice) {
      // A slice that starts at macroblock 0 begins a new picture. One that does not is another
      // slice of the picture already being accumulated.
      if (pendingHasSlice && startsNewPicture(unit)) flush();

      pending.push(unit);
      pendingHasSlice = true;
      if (type === NAL_IDR) pendingIsKey = true;
      continue;
    }

    // A parameter set or delimiter after a complete picture belongs to the NEXT one.
    if (pendingHasSlice) flush();
    pending.push(unit);
  }

  // During normal streaming whatever is left may still be missing slices, so it is held back.
  // An explicit idle flush means no more bytes arrived within the bounded interval, which is the
  // only completion signal event-driven encoders provide for their trailing picture.
  if (flushPending && pendingHasSlice) flush();
  return { frames, pending };
}

/**
 * Whether a slice NAL starts a new picture, i.e. its first_mb_in_slice is 0.
 *
 * first_mb_in_slice is the first ue(v) field of the slice header, so a value of 0 is encoded as a
 * single leading 1 bit — the top bit of the first RBSP byte.
 */
export function startsNewPicture(unit) {
  return unit.length > 1 && (unit[1] & 0x80) !== 0;
}

/** Re-emits NAL units as a single Annex-B buffer with 4-byte start codes. */
export function toAnnexB(nals) {
  let size = 0;
  for (const n of nals) size += NAL_START_LONG + n.length;

  const out = new Uint8Array(size);
  let offset = 0;
  for (const n of nals) {
    out[offset] = 0; out[offset + 1] = 0; out[offset + 2] = 0; out[offset + 3] = 1;
    out.set(n, offset + NAL_START_LONG);
    offset += NAL_START_LONG + n.length;
  }
  return out;
}

/** Returns the RFC 6381 AVC codec string carried by an SPS NAL, when present. */
export function codecFromSps(unit) {
  if (nalType(unit) !== NAL_SPS || unit.length < 4) return null;
  return `avc1.${[unit[1], unit[2], unit[3]]
    .map((value) => value.toString(16).padStart(2, '0'))
    .join('')}`;
}

/** Returns the NAL body from a raw remainder that begins with an Annex-B start code. */
export function trailingNalUnit(bytes) {
  if (!bytes || bytes.length < 4) return null;
  const prefixLength =
    bytes.length >= 5 && bytes[0] === 0 && bytes[1] === 0 && bytes[2] === 0 && bytes[3] === 1
      ? NAL_START_LONG
      : bytes[0] === 0 && bytes[1] === 0 && bytes[2] === 1
        ? NAL_START_SHORT
        : 0;
  return prefixLength > 0 && bytes.length > prefixLength
    ? bytes.subarray(prefixLength)
    : null;
}

/** Parses and bounds the descriptor sent as the first text message on the upstream stream. */
export function parseStreamDescriptor(value) {
  const parsed = typeof value === 'string' ? JSON.parse(value) : value;
  if (!parsed || typeof parsed !== 'object' || parsed.encoding !== 'h264-annexb') {
    throw new Error('The device video stream uses an unsupported encoding.');
  }

  const fps = Number(parsed.framesPerSecond);
  const scale = Number(parsed.scale);
  if (!Number.isFinite(fps) || fps < 1 || fps > 60 ||
      !Number.isFinite(scale) || scale < 0.1 || scale > 1) {
    throw new Error('The device video stream descriptor is invalid.');
  }

  const source = typeof parsed.source === 'string' && parsed.source.length <= 64
    ? parsed.source
    : 'unknown';
  const sourceDetail = typeof parsed.sourceDetail === 'string' && parsed.sourceDetail.length <= 512
    ? parsed.sourceDetail
    : null;
  const display = parsed.display && typeof parsed.display === 'object'
    ? parsed.display
    : null;

  return {
    encoding: 'h264-annexb',
    framesPerSecond: fps,
    scale,
    display,
    source,
    sourceDetail,
  };
}

/** Whether this browser can decode the stream at all. */
export function isVideoSupported(globalScope) {
  const scope = globalScope || (typeof globalThis !== 'undefined' ? globalThis : {});
  return typeof scope.VideoDecoder === 'function' && typeof scope.EncodedVideoChunk === 'function';
}

/**
 * A live device video surface.
 *
 * Owns the socket, the decoder and the canvas, and reports upward only two things: that a frame
 * arrived, or that video is not going to work. The Inspector reacts to the second by staying on
 * screenshots — it never has to know why.
 */
export class DeviceVideoSurface {
  constructor(options) {
    const opts = options || {};
    this.url = opts.url;
    this.canvas = opts.canvas;
    this.scope = opts.scope || (typeof globalThis !== 'undefined' ? globalThis : {});
    this.onFrame = typeof opts.onFrame === 'function' ? opts.onFrame : () => {};
    this.onUnavailable = typeof opts.onUnavailable === 'function' ? opts.onUnavailable : () => {};
    this.onDescriptor = typeof opts.onDescriptor === 'function' ? opts.onDescriptor : () => {};

    this._socket = null;
    this._decoder = null;
    this._remainder = new Uint8Array(0);
    this._pending = [];
    this._started = false;
    this._sawKeyFrame = false;
    this._closed = false;
    this._frameCount = 0;
    this._codec = 'avc1.64001f';
    this._source = null;
    this._descriptor = null;
    this._idleTimer = null;
    this._idleFlushMs = Number.isFinite(Number(opts.idleFlushMs))
      ? Math.min(1000, Math.max(1, Number(opts.idleFlushMs)))
      : IDLE_FLUSH_MS;
  }

  get frameCount() { return this._frameCount; }
  get descriptor() { return this._descriptor; }

  start() {
    if (this._started || this._closed) return false;
    if (!isVideoSupported(this.scope) || !this.url || !this.canvas) {
      this.onUnavailable('This browser cannot decode the device video stream.');
      return false;
    }

    this._started = true;
    try {
      this._openSocket();
      return true;
    } catch (e) {
      this.onUnavailable('The device video stream could not be opened.');
      return false;
    }
  }

  _openSocket() {
    const socket = new this.scope.WebSocket(this.url);
    socket.binaryType = 'arraybuffer';
    this._socket = socket;

    socket.onmessage = (event) => {
      if (typeof event.data === 'string') {
        try {
          this._descriptor = parseStreamDescriptor(event.data);
          this._source = this._descriptor.source;
          this.onDescriptor(this._descriptor);
        } catch (e) {
          this._fail(String(e?.message || e));
        }
        return;
      }
      if (!(event.data instanceof ArrayBuffer)) return;
      this._consume(new Uint8Array(event.data));
    };
    // A refused or dropped stream is not an error state for the Inspector — it is the signal to
    // stay on screenshots, which is exactly what it was doing a moment ago.
    socket.onerror = () => this.onUnavailable('The device video stream is unavailable.');
    socket.onclose = () => {
      if (!this._closed) this.onUnavailable('The device video stream ended.');
    };
  }

  _consume(chunk) {
    const combined = this._concat(this._remainder, chunk);
    const { units, remainder } = splitNalUnits(combined);
    this._remainder = remainder;

    const all = this._pending.concat(units);
    const { frames, pending } = groupAccessUnits(all);
    this._pending = pending;

    for (const frame of frames) this._decode(frame);
    this._armIdleFlush();
  }

  _armIdleFlush() {
    this._clearIdleFlush();
    const schedule = typeof this.scope.setTimeout === 'function'
      ? this.scope.setTimeout.bind(this.scope)
      : setTimeout;
    this._idleTimer = schedule(() => {
      this._idleTimer = null;
      this._flushIdle();
    }, this._idleFlushMs);
  }

  _clearIdleFlush() {
    if (this._idleTimer == null) return;
    const cancel = typeof this.scope.clearTimeout === 'function'
      ? this.scope.clearTimeout.bind(this.scope)
      : clearTimeout;
    cancel(this._idleTimer);
    this._idleTimer = null;
  }

  _flushIdle() {
    if (this._closed) return;

    const trailing = trailingNalUnit(this._remainder);
    const all = trailing ? this._pending.concat([trailing]) : this._pending;
    this._remainder = new Uint8Array(0);
    const { frames, pending } = groupAccessUnits(all, true);
    this._pending = pending;
    for (const frame of frames) this._decode(frame);

    if (this._source === 'idb' && typeof this._decoder?.flush === 'function') {
      Promise.resolve(this._decoder.flush()).catch(() => {
        if (!this._closed) this._fail('The device video decoder could not drain its buffered frame.');
      });
    }
  }

  _concat(a, b) {
    if (a.length === 0) return b;
    const out = new Uint8Array(a.length + b.length);
    out.set(a, 0);
    out.set(b, a.length);
    return out;
  }

  _decode(frame) {
    // A decoder started mid-stream has no reference frames, so anything before the first keyframe
    // decodes to garbage or an error. Waiting is both correct and briefly visible as the stream
    // taking a moment to appear.
    if (!this._sawKeyFrame && !frame.key) return;
    this._sawKeyFrame = true;

    try {
      const reportedCodec = frame.nals.map(codecFromSps).find(Boolean);
      if (reportedCodec && reportedCodec !== this._codec) {
        this._codec = reportedCodec;
        if (this._decoder && this._decoder.state !== 'closed') this._decoder.close();
        this._decoder = null;
      }
      if (!this._decoder) this._configureDecoder();

      const chunk = new this.scope.EncodedVideoChunk({
        type: frame.key ? 'key' : 'delta',
        timestamp: this._frameCount * 1000,
        data: toAnnexB(frame.nals),
      });
      this._decoder.decode(chunk);
    } catch (e) {
      this._fail('The device video stream could not be decoded.');
    }
  }

  _configureDecoder() {
    const canvas = this.canvas;
    const ctx = canvas.getContext('2d');

    this._decoder = new this.scope.VideoDecoder({
      output: (image) => {
        try {
          // Size the canvas to the frame, not to the layout: the stage already scales it, and
          // resizing here to the CSS box would resample every frame for nothing.
          if (canvas.width !== image.displayWidth || canvas.height !== image.displayHeight) {
            canvas.width = image.displayWidth;
            canvas.height = image.displayHeight;
          }
          ctx.drawImage(image, 0, 0);
          this._frameCount++;
          this.onFrame();
        } finally {
          image.close();
        }
      },
      error: () => this._fail('The device video decoder failed.'),
    });

    this._decoder.configure({
      codec: this._codec,
      optimizeForLatency: this._source !== 'idb',
      hardwareAcceleration: 'prefer-hardware',
      avc: { format: 'annexb' },
    });
  }

  _fail(message) {
    this.onUnavailable(message);
    this.stop();
  }

  stop() {
    this._closed = true;
    this._clearIdleFlush();
    try { if (this._socket) this._socket.close(); } catch (e) { /* already closing */ }
    try {
      if (this._decoder && this._decoder.state !== 'closed') this._decoder.close();
    } catch (e) { /* already closed */ }
    this._socket = null;
    this._decoder = null;
  }
}
