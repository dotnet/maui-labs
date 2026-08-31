export function createInspectorApi(basePath, inspectorToken) {
  function headers(contentType) {
    const result = {};
    if (contentType) result['Content-Type'] = contentType;
    if (inspectorToken) result['X-DevFlow-Inspector-Token'] = inspectorToken;
    return result;
  }

  async function postDetailed(path, body) {
    try {
      const response = await fetch(`${basePath}${path}`, {
        method: 'POST',
        headers: headers('application/json'),
        body: JSON.stringify(body),
      });
      const responseBody = await response.json().catch(() => ({}));
      return { ok: response.ok, status: response.status, body: responseBody };
    } catch (error) {
      console.error(`${path} failed:`, error);
      return { ok: false, status: 0, body: null, error: String(error) };
    }
  }

  async function post(path, body) {
    const result = await postDetailed(path, body);
    return result.ok ? result.body : null;
  }

  async function getDetailed(path) {
    try {
      const response = await fetch(`${basePath}${path}`, {
        headers: headers(),
        cache: 'no-store',
      });
      const responseBody = await response.json().catch(() => ({}));
      return { ok: response.ok, status: response.status, body: responseBody };
    } catch (error) {
      console.error(`${path} failed:`, error);
      return { ok: false, status: 0, body: null, error: String(error) };
    }
  }

  async function postBinary(path, bytes, contentType = 'application/octet-stream') {
    try {
      return await fetch(`${basePath}${path}`, {
        method: 'POST',
        headers: headers(contentType),
        body: bytes,
      });
    } catch (error) {
      console.error(`${path} failed:`, error);
      return null;
    }
  }

  async function postBlob(path, body) {
    try {
      return await fetch(`${basePath}${path}`, {
        method: 'POST',
        headers: headers('application/json'),
        body: JSON.stringify(body),
      });
    } catch (error) {
      console.error(`${path} failed:`, error);
      return null;
    }
  }

  return Object.freeze({ post, postDetailed, getDetailed, postBinary, postBlob });
}