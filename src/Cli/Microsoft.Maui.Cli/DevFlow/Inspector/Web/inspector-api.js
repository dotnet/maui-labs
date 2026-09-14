export function createInspectorApi(basePath, inspectorToken) {
  async function postDetailed(path, body) {
    try {
      const response = await fetch(`${basePath}${path}`, {
        method: 'POST',
        headers: inspectorToken
          ? { 'Content-Type': 'application/json', 'X-DevFlow-Inspector-Token': inspectorToken }
          : { 'Content-Type': 'application/json' },
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

  return Object.freeze({ post, postDetailed });
}