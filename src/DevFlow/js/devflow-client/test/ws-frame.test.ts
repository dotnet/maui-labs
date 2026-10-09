import { test } from "node:test";
import assert from "node:assert/strict";
import { encodeFrame, decodeFrame, FrameReader, OPCODE } from "../src/ws-frame.js";

/** Build an UNMASKED server→client frame (servers must not mask). */
function serverFrame(opcode: number, payload: Buffer, fin = true): Buffer {
  const len = payload.length;
  let header: Buffer;
  const b0 = (fin ? 0x80 : 0) | opcode;
  if (len < 126) {
    header = Buffer.from([b0, len]);
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[0] = b0;
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[0] = b0;
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  return Buffer.concat([header, payload]);
}

test("encodeFrame → decodeFrame roundtrip (masked client frame)", () => {
  for (const size of [0, 5, 125, 126, 200, 65535, 65536, 70000]) {
    const payload = Buffer.alloc(size, 0x61);
    const framed = encodeFrame(OPCODE.TEXT, payload);
    const decoded = decodeFrame(framed);
    assert.ok(decoded, `decode failed at size ${size}`);
    assert.equal(decoded!.opcode, OPCODE.TEXT);
    assert.equal(decoded!.fin, true);
    assert.deepEqual(decoded!.payload, payload);
    assert.equal(decoded!.rest.length, 0);
  }
});

test("decodeFrame returns null for incomplete buffers", () => {
  const full = encodeFrame(OPCODE.TEXT, Buffer.from("hello world"));
  assert.equal(decodeFrame(full.subarray(0, 1)), null);
  assert.equal(decodeFrame(full.subarray(0, full.length - 3)), null);
});

test("decodeFrame leaves trailing bytes in rest", () => {
  const a = encodeFrame(OPCODE.TEXT, Buffer.from("aa"));
  const b = encodeFrame(OPCODE.TEXT, Buffer.from("bb"));
  const decoded = decodeFrame(Buffer.concat([a, b]));
  assert.ok(decoded);
  assert.equal(decoded!.rest.length, b.length);
});

test("FrameReader reassembles a fragmented text message", () => {
  const messages: string[] = [];
  const reader = new FrameReader({
    onMessage: (op, data) => {
      if (op === OPCODE.TEXT) messages.push(data.toString("utf8"));
    },
    onPing: () => {},
    onPong: () => {},
    onClose: () => {},
  });
  reader.push(serverFrame(OPCODE.TEXT, Buffer.from("hel"), false));
  reader.push(serverFrame(OPCODE.CONTINUATION, Buffer.from("lo"), true));
  assert.deepEqual(messages, ["hello"]);
});

test("FrameReader handles a frame split across chunks", () => {
  const messages: string[] = [];
  const reader = new FrameReader({
    onMessage: (op, data) => messages.push(data.toString("utf8")),
    onPing: () => {},
    onPong: () => {},
    onClose: () => {},
  });
  const frame = serverFrame(OPCODE.TEXT, Buffer.from("chunky"));
  reader.push(frame.subarray(0, 2));
  reader.push(frame.subarray(2));
  assert.deepEqual(messages, ["chunky"]);
});

test("FrameReader surfaces ping and close", () => {
  let pinged = false;
  let closed = false;
  const reader = new FrameReader({
    onMessage: () => {},
    onPing: () => {
      pinged = true;
    },
    onPong: () => {},
    onClose: () => {
      closed = true;
    },
  });
  reader.push(serverFrame(OPCODE.PING, Buffer.from("p")));
  assert.equal(pinged, true);
  reader.push(serverFrame(OPCODE.CLOSE, Buffer.alloc(0)));
  assert.equal(closed, true);
});

test("FrameReader rejects a stray continuation frame (protocol violation → close)", () => {
  let closed = false;
  let messages = 0;
  const reader = new FrameReader({
    onMessage: () => {
      messages++;
    },
    onPing: () => {},
    onPong: () => {},
    onClose: () => {
      closed = true;
    },
  });
  reader.push(serverFrame(OPCODE.CONTINUATION, Buffer.from("orphan"), true));
  assert.equal(closed, true);
  assert.equal(messages, 0);
  // After a protocol failure the reader is inert.
  reader.push(serverFrame(OPCODE.TEXT, Buffer.from("ignored")));
  assert.equal(messages, 0);
});
