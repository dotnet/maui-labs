#!/usr/bin/env node
// Exercises the pinned Mobile Canvas binary through the exact MCP command editors use.
//
// Contract-only:
//   node eng/smoke-tests/mobile-canvas-companion-smoke-test.mjs --maui <path>
//
// Device-backed:
//   node eng/smoke-tests/mobile-canvas-companion-smoke-test.mjs --maui <path> \
//     --platform android --require-device --restart-host
//
// The script installs only the runtime pinned by this maui CLI. It never stops or kills a host
// that was already running when the script began.
import { once } from 'node:events';
import { existsSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { spawn, spawnSync } from 'node:child_process';
import process from 'node:process';

const argv = process.argv.slice(2);
const option = name => {
  const index = argv.indexOf(name);
  return index >= 0 ? argv[index + 1] : null;
};
const flag = name => argv.includes(name);

const EXE = option('--maui') ?? process.env.MAUI_EXE ?? 'maui';
const REQUIRED_PLATFORM = option('--platform');
const REQUIRE_DEVICE = flag('--require-device');
const RESTART_HOST = flag('--restart-host');
const CRASH_HOST = flag('--crash-host');

const EXPECTED_TOOLS = [
  'mobile_device_catalog',
  'mobile_device_list',
  'mobile_device_get',
  'mobile_device_get_selected',
  'mobile_device_select',
  'mobile_device_create',
  'mobile_device_boot',
  'mobile_device_shutdown',
  'mobile_device_restart',
  'mobile_device_reveal',
  'mobile_device_erase',
  'mobile_device_delete',
  'mobile_device_tap',
  'mobile_device_swipe',
  'mobile_device_type_text',
  'mobile_device_press_key',
  'mobile_device_press_button',
  'mobile_device_long_press',
  'mobile_device_rotate',
  'mobile_device_display',
  'mobile_device_ui_dump',
  'mobile_device_ui_find',
  'mobile_device_ui_tap',
  'mobile_device_app_list',
  'mobile_device_app_launch',
  'mobile_device_app_terminate',
  'mobile_device_app_install',
  'mobile_device_app_uninstall',
  'mobile_device_log',
  'mobile_device_crashes',
  'mobile_device_crash_report',
  'mobile_device_file_list',
  'mobile_device_file_pull',
  'mobile_device_file_push',
  'mobile_device_file_delete',
  'mobile_device_file_mkdir',
  'mobile_device_permission_list',
  'mobile_device_permission_set',
  'mobile_device_settings_get',
  'mobile_device_settings_set',
  'mobile_device_presentation_get',
  'mobile_device_presentation_set',
  'mobile_device_app_op_list',
  'mobile_device_app_op_set',
  'mobile_device_hardware_get',
  'mobile_device_location_set',
  'mobile_device_location_clear',
  'mobile_device_battery_set',
  'mobile_device_network_set',
  'mobile_device_notification_push',
  'mobile_device_sms_send',
  'mobile_device_calls',
  'mobile_device_call',
  'mobile_device_biometric',
  'mobile_device_clipboard_get',
  'mobile_device_clipboard_set',
  'mobile_device_media_add',
  'mobile_device_screenshot',
  'mobile_device_recording_start',
  'mobile_device_recording_stop',
  'mobile_device_recording_status',
].sort();

let failed = false;
const step = (name, ok, detail = '') => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? ` -- ${detail}` : ''}`);
  if (!ok) failed = true;
  return ok;
};

const sleep = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

function runMaui(args, timeout = 60 * 1000) {
  const result = spawnSync(EXE, args, {
    encoding: 'utf8',
    timeout,
    maxBuffer: 16 * 1024 * 1024,
    env: process.env,
  });
  if (result.error) {
    throw new Error(
      `${EXE} ${args.join(' ')} did not complete within ${timeout / 1000}s: ${result.error.message}`);
  }
  if (result.status !== 0) {
    throw new Error(
      `${EXE} ${args.join(' ')} exited ${result.status}: `
      + `${(result.stderr || result.stdout || '').trim().slice(-4000)}`);
  }
  return result.stdout.trim();
}

function parseLastJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    const lines = text.split(/\r?\n/).map(line => line.trim()).filter(Boolean).reverse();
    for (const line of lines) {
      try { return JSON.parse(line); } catch { }
    }
  }
  throw new Error(`Expected JSON output, received: ${text.slice(-2000)}`);
}

const hostStatus = () => parseLastJson(
  runMaui(['devflow', 'devices', 'host', 'status', '--json']));

class McpSession {
  constructor() {
    this.child = spawn(EXE, ['devflow', 'devices', 'host', 'mcp'], {
      stdio: ['pipe', 'pipe', 'pipe'],
      env: process.env,
    });
    this.nextId = 1;
    this.pending = new Map();
    this.buffer = '';
    this.stderr = '';

    this.child.stdout.on('data', chunk => {
      this.buffer += chunk.toString('utf8');
      let newline;
      while ((newline = this.buffer.indexOf('\n')) >= 0) {
        const line = this.buffer.slice(0, newline).trim();
        this.buffer = this.buffer.slice(newline + 1);
        if (!line) continue;
        let message;
        try { message = JSON.parse(line); } catch { continue; }
        const pending = this.pending.get(message.id);
        if (pending) {
          this.pending.delete(message.id);
          clearTimeout(pending.timeout);
          pending.resolve(message);
        }
      }
    });
    this.child.stderr.on('data', chunk => {
      this.stderr = (this.stderr + chunk.toString('utf8')).slice(-8000);
    });
    this.child.on('exit', (code, signal) => {
      for (const pending of this.pending.values()) {
        clearTimeout(pending.timeout);
        pending.reject(new Error(
          `Mobile Canvas MCP exited (${code ?? signal ?? 'unknown'}): ${this.stderr}`));
      }
      this.pending.clear();
    });
  }

  send(method, params) {
    return new Promise((resolve, reject) => {
      const id = this.nextId++;
      const timeout = setTimeout(() => {
        if (this.pending.delete(id)) {
          reject(new Error(`Timed out waiting for ${method}. ${this.stderr}`));
        }
      }, 180000);
      this.pending.set(id, { resolve, reject, timeout });
      this.child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
    });
  }

  async initialize() {
    const response = await this.send('initialize', {
      protocolVersion: '2024-11-05',
      capabilities: {},
      clientInfo: { name: 'mobile-canvas-companion-smoke', version: '1' },
    });
    if (response.error) throw new Error(JSON.stringify(response.error));
    this.child.stdin.write(
      JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  }

  async listTools() {
    const response = await this.send('tools/list', {});
    if (response.error) throw new Error(JSON.stringify(response.error));
    return response.result?.tools ?? [];
  }

  async call(name, args = {}) {
    const response = await this.send('tools/call', { name, arguments: args });
    if (response.error) throw new Error(`${name}: ${JSON.stringify(response.error)}`);
    if (response.result?.isError) {
      const text = response.result.content?.map(part => part.text ?? '').join('') ?? '';
      throw new Error(`${name}: ${text || 'tool returned isError=true'}`);
    }
    return response.result;
  }

  async close() {
    if (this.child.exitCode !== null) return;
    this.child.stdin.end();
    await Promise.race([once(this.child, 'exit'), sleep(2000)]);
    if (this.child.exitCode === null) {
      this.child.kill();
      await Promise.race([once(this.child, 'exit'), sleep(2000)]);
    }
  }
}

function structuredValue(result) {
  if (result?.structuredContent !== undefined) return result.structuredContent;
  for (const part of result?.content ?? []) {
    if (typeof part.text !== 'string') continue;
    try { return JSON.parse(part.text); } catch { }
  }
  return null;
}

function findDeviceArray(value) {
  if (Array.isArray(value) && value.every(item => item && typeof item === 'object')) {
    if (value.length === 0 || value.some(item => typeof item.id === 'string')) return value;
  }
  if (!value || typeof value !== 'object') return null;
  for (const nested of Object.values(value)) {
    const found = findDeviceArray(nested);
    if (found) return found;
  }
  return null;
}

function findNamedValue(value, name) {
  if (!value || typeof value !== 'object') return null;
  if (Object.prototype.hasOwnProperty.call(value, name)) return value[name];
  for (const nested of Object.values(value)) {
    const found = findNamedValue(nested, name);
    if (found !== null && found !== undefined) return found;
  }
  return null;
}

function hostOrientation(value) {
  const normalized = String(value ?? '').replace(/[^a-z]/gi, '').toLowerCase();
  return {
    portrait: 'portrait',
    portraitupsidedown: 'portrait-upside-down',
    landscapeleft: 'landscape-left',
    landscaperight: 'landscape-right',
  }[normalized];
}

async function verifyToolInventory(session, label) {
  const tools = await session.listTools();
  const actual = tools.map(tool => tool.name).sort();
  const missing = EXPECTED_TOOLS.filter(name => !actual.includes(name));
  const extra = actual.filter(name => !EXPECTED_TOOLS.includes(name));
  const inventoryMatches =
    actual.length === EXPECTED_TOOLS.length && missing.length === 0 && extra.length === 0;
  step(
    `pinned MCP inventory ${label}`,
    inventoryMatches,
    `${actual.length} tools; missing=${missing.join(',') || 'none'}; extra=${extra.join(',') || 'none'}`);
  if (!inventoryMatches) throw new Error('The pinned MCP tool inventory changed.');
}

async function openMcp(label) {
  const session = new McpSession();
  try {
    await session.initialize();
    await verifyToolInventory(session, label);
    return session;
  } catch (error) {
    await session.close();
    throw error;
  }
}

async function crashOwnedHost() {
  const statePath = join(homedir(), '.mobile-canvas', 'hosts', 'v1.0', 'host.json');
  if (!existsSync(statePath)) throw new Error(`Host state was not found at ${statePath}.`);
  const state = JSON.parse(readFileSync(statePath, 'utf8'));
  const processId = Number(state.processId);
  if (!Number.isInteger(processId) || processId <= 0) {
    throw new Error('The owned host did not publish a valid processId.');
  }

  process.kill(processId, 'SIGKILL');
  for (let attempt = 0; attempt < 50; attempt++) {
    try {
      process.kill(processId, 0);
      await sleep(100);
    } catch {
      return;
    }
  }
  throw new Error(`The owned host process ${processId} did not exit.`);
}

let session = null;
let startedHost = false;
let bootedByScript = null;

try {
  const initial = hostStatus();
  if (!initial.installed || !initial.integrityValid) {
    runMaui(['devflow', 'devices', 'host', 'install', '--json'], 10 * 60 * 1000);
  }

  if (!initial.running) {
    runMaui(['devflow', 'devices', 'host', 'start', '--json']);
    startedHost = true;
  }

  const running = hostStatus();
  step(
    'pinned companion is installed, verified, and running',
    running.version === '0.1.16' && running.integrityValid === true && running.running === true,
    `version=${running.version}; runtime=${running.runtimeKey}`);

  session = await openMcp('before host recovery');

  if (RESTART_HOST && startedHost) {
    await session.close();
    session = null;
    runMaui(['devflow', 'devices', 'host', 'stop', '--json']);
    runMaui(['devflow', 'devices', 'host', 'start', '--json']);
    session = await openMcp('after clean restart');
    step('clean host restart preserves the MCP contract', true);
  } else if (RESTART_HOST) {
    step('clean host restart preserves the MCP contract', true, 'skipped for a pre-existing host');
  }

  if (CRASH_HOST && startedHost) {
    await session.close();
    session = null;
    await crashOwnedHost();
    runMaui(['devflow', 'devices', 'host', 'start', '--json']);
    session = await openMcp('after crash recovery');
    step('host crash recovery republishes a usable MCP contract', true);
  } else if (CRASH_HOST) {
    step('host crash recovery republishes a usable MCP contract', true, 'skipped for a pre-existing host');
  }

  const listed = await session.call('mobile_device_list');
  const devices = findDeviceArray(structuredValue(listed)) ?? [];
  const runDeviceChecks = REQUIRE_DEVICE || Boolean(REQUIRED_PLATFORM);
  const candidates = REQUIRED_PLATFORM
    ? devices.filter(device =>
        String(device.platform ?? '').toLowerCase() === REQUIRED_PLATFORM.toLowerCase())
    : devices;
  const device = runDeviceChecks
    ? candidates.find(item =>
        item.isBooted === true || String(item.state ?? '').toLowerCase() === 'booted')
      ?? candidates[0]
    : null;

  if (REQUIRE_DEVICE && !device) {
    throw new Error(
      `No ${REQUIRED_PLATFORM ?? 'mobile'} device was discovered. `
      + `Available: ${devices.map(item => `${item.platform}:${item.id}`).join(', ') || 'none'}`);
  }

  if (device) {
    const deviceId = device.id;
    const wasBooted = device.isBooted === true || String(device.state ?? '').toLowerCase() === 'booted';
    if (!wasBooted) {
      await session.call('mobile_device_boot', { deviceId });
      bootedByScript = deviceId;
    }

    const refreshed = structuredValue(await session.call('mobile_device_get', { deviceId }));
    step(
      'real companion resolves the selected device',
      JSON.stringify(refreshed).includes(deviceId),
      deviceId);

    const display = structuredValue(await session.call('mobile_device_display', { deviceId }));
    step(
      'device-backed display geometry is available',
      JSON.stringify(display).length > 10,
      deviceId);

    const orientation = hostOrientation(findNamedValue(display, 'orientation'));
    if (!orientation) throw new Error('The device display did not report a supported orientation.');
    await session.call('mobile_device_rotate', { deviceId, orientation });
    step('device-backed input succeeds without changing orientation', true, orientation);

    const screenshot = await session.call('mobile_device_screenshot', { deviceId });
    const image = screenshot.content?.find(part =>
      part.type === 'image' && part.mimeType === 'image/png' && typeof part.data === 'string');
    step(
      'device-backed PNG media succeeds',
      Boolean(image?.data),
      image?.data ? `about ${Math.floor(image.data.length * 0.75)} PNG bytes` : 'no image block');
  } else {
    step('device-backed checks', true, 'contract-only run');
  }
} catch (error) {
  step('mobile canvas companion smoke test completed', false, error?.stack ?? String(error));
} finally {
  if (bootedByScript && session) {
    try {
      await session.call('mobile_device_shutdown', { deviceId: bootedByScript });
    } catch (error) {
      step('device started by the smoke test was shut down', false, String(error));
    }
  }
  if (session) await session.close();
  if (startedHost) {
    try {
      runMaui(['devflow', 'devices', 'host', 'stop', '--json']);
    } catch (error) {
      step('companion started by the smoke test was stopped', false, String(error));
    }
  }
}

process.exitCode = failed ? 1 : 0;
