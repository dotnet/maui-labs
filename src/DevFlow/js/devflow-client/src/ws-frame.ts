// Minimal RFC 6455 client framing, isolated so it can be tested hard (fragmentation,
// ping/pong/close, masking) without a live socket. Zero dependencies by design — the
// agent's /ws/v1/ui/events stream needs only masked client→server text frames, ping/pong,
// and reassembly of possibly-fragmented server→client frames.

import crypto from "node:crypto";

export const OPCODE = {
  CONTINUATION: 0x0,
  TEXT: 0x1,
  BINARY: 0x2,
  CLOSE: 0x8,
  PING: 0x9,
  PONG: 0xa,
} as const;

/**
 * Encode and MASK a single client→server frame (FIN set; masking is mandatory per
 * RFC 6455 §5.3). Payloads are small JSON control/subscribe messages.
 */
export function encodeFrame(opcode: number, payload: Buffer): Buffer {
  const len = payload.length;
  let header: Buffer;
  if (len < 126) {
    header = Buffer.from([0x80 | opcode, 0x80 | len]);
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[0] = 0x80 | opcode;
    header[1] = 0x80 | 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = 0x80 | opcode;
    header[1] = 0x80 | 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  const mask = crypto.randomBytes(4);
  const masked = Buffer.alloc(len);
  for (let i = 0; i < len; i++) masked[i] = payload[i] ^ mask[i % 4];
  return Buffer.concat([header, mask, masked]);
}

export interface DecodedFrame {
  fin: boolean;
  opcode: number;
  payload: Buffer;
  /** Remaining bytes after this frame (for the next decode call). */
  rest: Buffer;
}

/**
 * Decode exactly one frame from the front of `buf`. Returns null when the buffer does
 * not yet hold a complete frame (caller should accumulate more bytes and retry).
 */
export function decodeFrame(buf: Buffer): DecodedFrame | null {
  if (buf.length < 2) return null;
  const b0 = buf[0];
  const b1 = buf[1];
  const fin = (b0 & 0x80) !== 0;
  const opcode = b0 & 0x0f;
  const masked = (b1 & 0x80) !== 0;
  let len = b1 & 0x7f;
  let off = 2;
  if (len === 126) {
    if (buf.length < 4) return null;
    len = buf.readUInt16BE(2);
    off = 4;
  } else if (len === 127) {
    if (buf.length < 10) return null;
    len = Number(buf.readBigUInt64BE(2));
    off = 10;
  }
  const maskLen = masked ? 4 : 0;
  if (buf.length < off + maskLen + len) return null;
  let payload = buf.subarray(off + maskLen, off + maskLen + len);
  if (masked) {
    const m = buf.subarray(off, off + 4);
    const out = Buffer.alloc(len);
    for (let i = 0; i < len; i++) out[i] = payload[i] ^ m[i % 4];
    payload = out;
  }
  return { fin, opcode, payload: Buffer.from(payload), rest: buf.subarray(off + maskLen + len) };
}

/** A fresh Sec-WebSocket-Key value (16 random bytes, base64). */
export function newWebSocketKey(): string {
  return crypto.randomBytes(16).toString("base64");
}

/**
 * Reassembles (possibly fragmented) data frames into complete text/binary messages,
 * while surfacing control frames (ping/pong/close) to the caller. Feed raw socket
 * chunks to `push`; it invokes the handlers as complete frames/messages arrive.
 */
export class FrameReader {
  private buf: Buffer = Buffer.alloc(0);
  private continuationOpcode = 0;
  private parts: Buffer[] = [];
  private partsSize = 0;
  private closed = false;

  constructor(
    private readonly handlers: {
      onMessage: (opcode: number, data: Buffer) => void;
      onPing: (payload: Buffer) => void;
      onPong: (payload: Buffer) => void;
      onClose: () => void;
    },
    private readonly maxMessageBytes = 64 * 1024 * 1024,
  ) {}

  push(chunk: Buffer): void {
    if (this.closed) return;
    this.buf = this.buf.length ? Buffer.concat([this.buf, chunk]) : chunk;
    // Guard against an announced-but-never-completed huge frame growing the buffer forever.
    if (this.buf.length > this.maxMessageBytes) return this.fail();
    let frame: DecodedFrame | null;
    while ((frame = decodeFrame(this.buf))) {
      this.buf = frame.rest;
      switch (frame.opcode) {
        case OPCODE.CLOSE:
          return this.fail();
        case OPCODE.PING:
          this.handlers.onPing(frame.payload);
          continue;
        case OPCODE.PONG:
          this.handlers.onPong(frame.payload);
          continue;
        default: {
          // A continuation frame with no active fragmented message is a protocol error.
          if (frame.opcode === OPCODE.CONTINUATION && this.continuationOpcode === 0) {
            return this.fail();
          }
          const op = frame.opcode === OPCODE.CONTINUATION ? this.continuationOpcode : frame.opcode;
          if (frame.opcode !== OPCODE.CONTINUATION) {
            this.continuationOpcode = frame.opcode;
            this.parts = [];
            this.partsSize = 0;
          }
          this.parts.push(frame.payload);
          this.partsSize += frame.payload.length;
          if (this.partsSize > this.maxMessageBytes) return this.fail();
          if (frame.fin) {
            const data = Buffer.concat(this.parts);
            this.parts = [];
            this.partsSize = 0;
            this.continuationOpcode = 0;
            this.handlers.onMessage(op, data);
          }
        }
      }
    }
  }

  private fail(): void {
    this.closed = true;
    this.buf = Buffer.alloc(0);
    this.parts = [];
    this.partsSize = 0;
    this.handlers.onClose();
  }
}
