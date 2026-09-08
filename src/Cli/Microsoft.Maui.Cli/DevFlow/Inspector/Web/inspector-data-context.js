const MAX_DATA_CHARS = 14000;
const MAX_ENVELOPE_BYTES = 18000;
const MAX_STRING_CHARS = 2000;
const MAX_STRING_SCAN_CHARS = MAX_STRING_CHARS * 4;
const MAX_TITLE_BYTES = 512;
const MAX_APP_NAME_BYTES = 512;
const SUPPORTED_SCOPES = new Set(['logs', 'network', 'preferences', 'device', 'sensors', 'files', 'alerts']);
const FOLLOW_UP_TOOLS = Object.freeze({
  logs: ['maui_logs'],
  network: ['maui_network', 'maui_network_detail'],
  preferences: ['maui_preferences_list', 'maui_preferences_get'],
  device: ['maui_device_info', 'maui_display_info', 'maui_battery_info', 'maui_connectivity'],
  sensors: ['maui_sensors_list', 'maui_sensors_start', 'maui_sensors_stop'],
  files: ['maui_storage_roots', 'maui_files_list', 'maui_files_download'],
  alerts: [],
});

const SECRET_KEY = /token|secret|password|auth|apikey|api[_-]?key|cookie|connection\s*string/i;
const JWT = /\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b/g;
const BEARER = /(bearer\s+)[A-Za-z0-9._~+/=-]{12,}/gi;
const SENSITIVE_HEADER = /\b(authorization|proxy-authorization|cookie|set-cookie)\s*:\s*[^\r\n]*/gi;
const SECRET_ASSIGNMENT = /((?:token|secret|password|pwd|api[_-]?key|authorization|cookie|connection\s*string)\s*[=:]\s*)(?:"[^"]*"|'[^']*'|[^\s,;]+)/gi;
const URL_START = /h[\t\r\n]*t[\t\r\n]*t[\t\r\n]*p[\t\r\n]*s?[\t\r\n]*:(?:[\\/\t\r\n]+|(?=[a-z0-9%]))|https?%(?:25)*3a%(?:25)*(?:2f|5c)%(?:25)*(?:2f|5c)/gi;
const LITERAL_URL_START = /^https?:(?:[\\/]+|(?=[a-z0-9%]))/i;
const ENCODED_URL_START = /^https?%(?:25)*3a%(?:25)*(?:2f|5c)%(?:25)*(?:2f|5c)/i;
const MIXED_ENCODED_URL_START = /^https?:%(?:25)*(?:2f|5c)%(?:25)*(?:2f|5c)/i;
const URL_TRAILING_PUNCTUATION = /[\])},.;!?:]$/;
const QUERY_SECRET = /^(?:code|sig)$/i;
const QUERY_SECRET_SUFFIX = /(?:^|[-_.])(?:key|signature|credential)$/i;
const PATH_SECRET = /(;(?:j?sessionid|phpsessid)=)[^/;?#]+/gi;
const MAX_URL_DEPTH = 4;
const MAX_URL_DECODE_DEPTH = 6;
const MAX_URL_CANDIDATES = 32;

export function supportsDataContextScope(scope) {
  return SUPPORTED_SCOPES.has(scope);
}

export function isSecretContextKey(key) {
  return !!key && SECRET_KEY.test(String(key));
}

function utf8ByteLength(value) {
  return new TextEncoder().encode(String(value)).length;
}

function serializedByteLength(value) {
  return utf8ByteLength(JSON.stringify(value));
}

function truncateUtf8(value, maxBytes) {
  const text = String(value);
  if (maxBytes <= 0) return '';
  if (utf8ByteLength(text) <= maxBytes) return text;

  const codePoints = Array.from(text);
  let low = 0;
  let high = codePoints.length;
  while (low < high) {
    const mid = Math.ceil((low + high) / 2);
    if (utf8ByteLength(codePoints.slice(0, mid).join('')) <= maxBytes) low = mid;
    else high = mid - 1;
  }
  return codePoints.slice(0, low).join('');
}

function decodeQueryKey(value) {
  let decoded = String(value || '').replace(/\+/g, ' ');
  for (let attempt = 0; attempt < MAX_URL_DECODE_DEPTH; attempt++) {
    const next = decoded.replace(/%([a-f0-9]{2})/gi, (_, hex) => String.fromCharCode(parseInt(hex, 16)));
    if (next === decoded) break;
    decoded = next;
  }
  return { value: decoded, unresolved: /%[a-f0-9]{2}/i.test(decoded) };
}

function isSensitiveQueryKey(key) {
  const decodedKey = decodeQueryKey(key);
  if (decodedKey.unresolved) return true;
  const decoded = decodedKey.value;
  const normalized = decoded.replace(/[^a-z0-9]/gi, '').toLowerCase();
  return isSecretContextKey(decoded)
    || QUERY_SECRET.test(decoded)
    || QUERY_SECRET_SUFFIX.test(decoded)
    || normalized === 'key'
    || normalized === 'code'
    || normalized === 'sig'
    || normalized === 'hmac'
    || normalized === 'hdnts'
    || normalized === 'hdnea'
    || normalized === 'ticket'
    || normalized === 'session'
    || normalized === 'sid'
    || normalized === 'oobcode'
    || normalized === 'samlart'
    || normalized === 'samlresponse'
    || normalized.endsWith('subscriptionkey')
    || normalized.endsWith('signature')
    || normalized.endsWith('credential')
    || normalized.endsWith('accesskeyid')
    || normalized.endsWith('sessionid')
    || normalized.endsWith('sessiontoken')
    || normalized === 'phpsessid'
    || normalized === 'googleaccessid';
}

function isUrlHardStop(character) {
  return character === '<'
    || character === '>'
    || character === '`'
    || (/\s/.test(character) && character !== '\t' && character !== '\r' && character !== '\n');
}

function scanUrlEnd(value, start) {
  const preceding = start > 0 ? value[start - 1] : '';
  const wrapperQuote = /['"]/.test(preceding) ? preceding : null;
  const wrapperOpen = preceding === '(' || preceding === '[' || preceding === '{' ? preceding : null;
  const wrapperClose = wrapperOpen === '(' ? ')' : wrapperOpen === '[' ? ']' : wrapperOpen === '{' ? '}' : null;
  let wrapperDepth = 0;
  let quote = null;
  for (let index = start; index < value.length; index++) {
    const character = value[index];
    if (!quote && wrapperOpen && character === wrapperOpen) {
      wrapperDepth++;
      continue;
    }
    if (!quote && wrapperClose && character === wrapperClose) {
      if (wrapperDepth > 0) {
        wrapperDepth--;
        continue;
      }
      return index;
    }
    if (isUrlHardStop(character) && !quote) return index;
    if (character === '<' || character === '>' || character === '`') return index;
    if (quote) {
      if (character === quote && value[index - 1] !== '\\') quote = null;
      continue;
    }
    if (wrapperQuote && character === wrapperQuote) {
      const next = value[index + 1];
      if (next === undefined || isUrlHardStop(next) || /[\])},.;!?:]/.test(next)) return index;
    }
    if ((character === '"' || character === "'") && value[index - 1] !== '\\') quote = character;
  }
  return value.length;
}

function splitUrlSuffix(value) {
  let end = value.length;
  while (end > 0) {
    const terminal = value[end - 1];
    if (URL_TRAILING_PUNCTUATION.test(terminal)) {
      end--;
      continue;
    }
    if (terminal === '"' || terminal === "'") {
      let quoteCount = 0;
      for (let index = 0; index < end; index++) {
        if (value[index] === terminal && value[index - 1] !== '\\') quoteCount++;
      }
      if (quoteCount % 2 === 1) {
        end--;
        continue;
      }
    }
    break;
  }
  return { core: value.slice(0, end), suffix: value.slice(end) };
}

function normalizeHttpUrl(value) {
  value = value.replace(/[\t\r\n]/g, '');
  const match = /^(https?):[\\/]*/i.exec(value);
  if (!match) return null;
  const remainder = value.slice(match[0].length);
  const queryIndex = remainder.search(/[?#]/);
  const pathEnd = queryIndex >= 0 ? queryIndex : remainder.length;
  return `${match[1]}://${remainder.slice(0, pathEnd).replace(/\\/g, '/')}${remainder.slice(pathEnd)}`;
}

function resolveUrlCandidate(value) {
  let resolved = value;
  let encodingDepth = 0;
  for (let attempt = 0; attempt <= MAX_URL_DECODE_DEPTH; attempt++) {
    if (MIXED_ENCODED_URL_START.test(resolved)) {
      if (attempt === MAX_URL_DECODE_DEPTH) return null;
      try {
        const next = decodeURIComponent(resolved);
        if (next === resolved) return null;
        resolved = next;
        continue;
      } catch {
        return null;
      }
    }
    if (LITERAL_URL_START.test(resolved)) {
      const normalized = normalizeHttpUrl(resolved);
      return normalized ? { value: normalized, encodingDepth } : null;
    }
    if (!ENCODED_URL_START.test(resolved) || attempt === MAX_URL_DECODE_DEPTH) return null;
    try {
      const next = decodeURIComponent(resolved);
      if (next === resolved) return null;
      resolved = next;
      encodingDepth++;
    } catch {
      return null;
    }
  }
  return null;
}

function encodeUrlLayers(value, layers) {
  for (let layer = 0; layer < layers; layer++) value = encodeURIComponent(value);
  return value;
}

function redactQueryValue(value) {
  const leadingWhitespace = value.match(/^\s*/)?.[0] || '';
  const trailingWhitespace = value.match(/\s*$/)?.[0] || '';
  const core = value.slice(leadingWhitespace.length, value.length - trailingWhitespace.length);
  const quote = core[0] === '"' || core[0] === "'" ? core[0] : '';
  const closingQuote = quote && core.length > 1 && core.endsWith(quote) ? quote : '';
  return leadingWhitespace + quote + '<redacted>' + closingQuote + trailingWhitespace;
}

function redactQuery(query, depth, budget) {
  let result = '';
  let start = 0;
  for (let index = 0; index <= query.length; index++) {
    if (index < query.length && query[index] !== '&') continue;
    const pair = query.slice(start, index);
    const equals = pair.indexOf('=');
    if (equals < 0) {
      result += redactUrls(pair, depth + 1, budget);
    } else {
      const key = pair.slice(0, equals);
      const value = pair.slice(equals + 1);
      const redactedKey = redactUrls(key, depth + 1, budget);
      result += redactedKey + '=' + (isSensitiveQueryKey(key)
        ? redactQueryValue(value)
        : redactUrls(value, depth + 1, budget));
    }
    if (index < query.length) result += '&';
    start = index + 1;
  }
  return result;
}

function redactUrl(value, depth, budget) {
  const rawParts = splitUrlSuffix(value);
  if (!rawParts.core || depth > MAX_URL_DEPTH) return '<redacted-url>' + rawParts.suffix;
  const candidate = resolveUrlCandidate(rawParts.core);
  if (!candidate) return '<redacted-url>' + rawParts.suffix;

  const resolvedParts = splitUrlSuffix(candidate.value);
  const resolved = resolvedParts.core;
  let parsed;
  try {
    parsed = new URL(resolved);
  } catch {
    return '<redacted-url>' + resolvedParts.suffix + rawParts.suffix;
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    return '<redacted-url>' + resolvedParts.suffix + rawParts.suffix;
  }

  const scheme = /^(https?):\/\//i.exec(resolved);
  if (!scheme) return '<redacted-url>' + resolvedParts.suffix + rawParts.suffix;
  const authorityStart = scheme[0].length;
  let authorityEnd = resolved.length;
  for (const delimiter of ['/', '?', '#']) {
    const index = resolved.indexOf(delimiter, authorityStart);
    if (index >= 0) authorityEnd = Math.min(authorityEnd, index);
  }

  let authority = resolved.slice(authorityStart, authorityEnd);
  const userInfoEnd = authority.lastIndexOf('@');
  if (userInfoEnd >= 0 || parsed.username || parsed.password) {
    if (userInfoEnd < 0) return '<redacted-url>' + resolvedParts.suffix + rawParts.suffix;
    authority = 'redacted@' + authority.slice(userInfoEnd + 1);
  }

  const queryStart = resolved.indexOf('?', authorityEnd);
  const fragmentStart = resolved.indexOf('#', authorityEnd);
  const pathEnd = Math.min(
    queryStart >= 0 ? queryStart : resolved.length,
    fragmentStart >= 0 ? fragmentStart : resolved.length);
  const path = redactUrls(resolved.slice(authorityEnd, pathEnd), depth + 1, budget)
    .replace(PATH_SECRET, '$1<redacted>');
  const queryEnd = fragmentStart >= 0 ? fragmentStart : resolved.length;
  const query = queryStart >= 0 && queryStart < queryEnd
    ? '?' + redactQuery(resolved.slice(queryStart + 1, queryEnd), depth, budget)
    : '';
  const fragment = fragmentStart >= 0 ? '#<redacted>' : '';
  const sanitized = scheme[0] + authority + path + query + fragment + resolvedParts.suffix;
  return encodeUrlLayers(sanitized, candidate.encodingDepth) + rawParts.suffix;
}

function redactUrls(value, depth, budget) {
  const contextBudget = budget || { remaining: MAX_URL_CANDIDATES, truncated: false };
  const matcher = new RegExp(URL_START.source, URL_START.flags);
  let result = '';
  let cursor = 0;
  let searchFrom = 0;
  while (searchFrom < value.length) {
    matcher.lastIndex = searchFrom;
    const match = matcher.exec(value);
    if (!match) break;
    result += value.slice(cursor, match.index);
    if (contextBudget.remaining <= 0) {
      contextBudget.truncated = true;
      return result + '<redacted-context>';
    }
    const end = scanUrlEnd(value, match.index);
    contextBudget.remaining--;
    result += redactUrl(value.slice(match.index, end), depth || 0, contextBudget);
    cursor = end;
    searchFrom = end;
  }
  return result + value.slice(cursor);
}

function sanitizeValue(value, keyName, depth, state) {
  if (isSecretContextKey(keyName)) return '<redacted>';
  if (value === null || value === undefined || typeof value === 'boolean' || typeof value === 'number') return value;
  if (typeof value === 'string') {
    const scanValue = value.length > MAX_STRING_SCAN_CHARS ? value.slice(0, MAX_STRING_SCAN_CHARS) : value;
    if (scanValue.length !== value.length) state.truncated = true;
    const budget = { remaining: MAX_URL_CANDIDATES, truncated: false };
    let text = redactUrls(scanValue, 0, budget);
    if (budget.truncated) state.truncated = true;
    text = text
      .replace(SENSITIVE_HEADER, '$1: <redacted>')
      .replace(JWT, '<jwt>')
      .replace(BEARER, '$1<redacted>')
      .replace(SECRET_ASSIGNMENT, '$1<redacted>');
    if (text.length > MAX_STRING_CHARS) {
      state.truncated = true;
      text = text.slice(0, MAX_STRING_CHARS) + '…';
    }
    return text;
  }
  if (depth >= 8) {
    state.truncated = true;
    return '<max-depth>';
  }
  if (Array.isArray(value)) {
    if (value.length > 200) state.truncated = true;
    return value.slice(0, 200).map((item) => sanitizeValue(item, null, depth + 1, state));
  }
  if (typeof value === 'object') {
    const result = {};
    const keys = Object.keys(value);
    if (keys.length > 100) state.truncated = true;
    for (const key of keys.slice(0, 100)) result[key] = sanitizeValue(value[key], key, depth + 1, state);
    return result;
  }
  return String(value);
}

export function createDataSnapshot({ scope, title, payload, itemCount, metadata, agent }) {
  const state = { truncated: false };
  const safeTitle = truncateUtf8(
    String(sanitizeValue(String(title || scope), 'title', 0, state)),
    MAX_TITLE_BYTES);
  const safeAgent = sanitizeValue(agent || {}, null, 0, state);
  const safeAppName = safeAgent && typeof safeAgent === 'object' && !Array.isArray(safeAgent)
    && typeof safeAgent.appName === 'string'
    ? truncateUtf8(safeAgent.appName, MAX_APP_NAME_BYTES)
    : null;
  if (safeAgent && typeof safeAgent === 'object' && !Array.isArray(safeAgent))
    safeAgent.appName = safeAppName;
  const sanitized = sanitizeValue(payload, null, 0, state);
  const serialized = JSON.stringify(sanitized);
  let data = sanitized;
  let dataFormat = 'json';
  if (serialized.length > MAX_DATA_CHARS) {
    state.truncated = true;
    data = serialized.slice(0, MAX_DATA_CHARS);
    dataFormat = 'json-prefix';
  }
  const snapshot = {
    kind: 'dataSnapshot',
    scope,
    title: safeTitle,
    appName: safeAppName,
    agent: safeAgent,
    capturedAt: new Date().toISOString(),
    itemCount: Number.isFinite(itemCount) ? itemCount : null,
    truncated: state.truncated,
    redacted: true,
    dataFormat,
    data,
    metadata: sanitizeValue(metadata || {}, null, 0, state),
    followUpTools: FOLLOW_UP_TOOLS[scope] || [],
  };
  snapshot.truncated = state.truncated;

  if (serializedByteLength(snapshot) > MAX_ENVELOPE_BYTES) {
    snapshot.truncated = true;
    snapshot.dataFormat = 'json-prefix';
    const dataText = typeof snapshot.data === 'string' ? snapshot.data : JSON.stringify(snapshot.data);
    snapshot.data = '';
    let available = MAX_ENVELOPE_BYTES - serializedByteLength(snapshot) - 32;
    if (available < 0) {
      snapshot.metadata = {};
      snapshot.title = truncateUtf8(snapshot.title, 256);
      snapshot.appName = snapshot.appName ? truncateUtf8(snapshot.appName, 256) : null;
      available = MAX_ENVELOPE_BYTES - serializedByteLength(snapshot) - 32;
    }
    snapshot.data = truncateUtf8(dataText, Math.max(0, available));
    while (serializedByteLength(snapshot) > MAX_ENVELOPE_BYTES && snapshot.data.length > 0) {
      const excess = serializedByteLength(snapshot) - MAX_ENVELOPE_BYTES;
      snapshot.data = truncateUtf8(
        snapshot.data,
        Math.max(0, utf8ByteLength(snapshot.data) - excess - 64));
    }
    if (serializedByteLength(snapshot) > MAX_ENVELOPE_BYTES) {
      snapshot.data = '';
      snapshot.metadata = {};
      snapshot.agent = null;
      snapshot.appName = null;
      snapshot.followUpTools = [];
      snapshot.title = truncateUtf8(String(scope || 'Data'), 128);
    }
  }
  return snapshot;
}