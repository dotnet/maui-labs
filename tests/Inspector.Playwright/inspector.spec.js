// @ts-check
const { test, expect } = require('@playwright/test');

test.describe('DevFlow Inspector HTML output', () => {

  test('page loads with correct viewport dimensions from screenshot', async ({ page }) => {
    await page.goto('/');

    const viewport = page.locator('#app-viewport');
    await expect(viewport).toBeVisible();

    // Viewport should use actual screenshot dimensions (not hardcoded 390x844)
    const style = await viewport.getAttribute('style');
    expect(style).toContain('width:');
    expect(style).toContain('height:');

    // Should NOT be iPhone defaults
    expect(style).not.toContain('width:390px');
    expect(style).not.toContain('height:844px');
  });

  test('page contains screenshot image', async ({ page }) => {
    await page.goto('/');

    const screenshot = page.locator('#screenshot');
    await expect(screenshot).toBeVisible();
    expect(await screenshot.getAttribute('src')).toBe('/screenshot.png');
  });

  test('page has no toolbar or inspector chrome', async ({ page }) => {
    await page.goto('/');

    // Should NOT have toolbar elements — the host inspector adds its own
    await expect(page.locator('#devflow-toolbar')).toHaveCount(0);
    await expect(page.locator('#btn-back')).toHaveCount(0);
    await expect(page.locator('#btn-refresh')).toHaveCount(0);
    await expect(page.locator('#connection-status')).toHaveCount(0);
  });

  test('elements have no hover highlighting styles', async ({ page }) => {
    await page.goto('/');

    // Check that CSS doesn't include hover outline
    const cssResponse = await page.request.get('/devflow.css');
    const cssText = await cssResponse.text();
    expect(cssText).not.toContain(':hover');
    expect(cssText).not.toContain('outline');
  });

  test('elements rendered as positioned divs with data attributes', async ({ page }) => {
    await page.goto('/');

    const elements = page.locator('.devflow-element');
    const count = await elements.count();
    expect(count).toBeGreaterThan(0);

    // First element should have required data attributes
    const first = elements.first();
    expect(await first.getAttribute('data-id')).toBeTruthy();
    expect(await first.getAttribute('data-type')).toBeTruthy();
  });

  test('elements have correct positioning styles', async ({ page }) => {
    await page.goto('/');

    // Find an element with bounds that has positive dimensions
    const elements = page.locator('.devflow-element[style*="left:"]');
    const count = await elements.count();
    expect(count).toBeGreaterThan(0);

    const style = await elements.first().getAttribute('style');
    expect(style).toContain('position:absolute');
    expect(style).toMatch(/left:\d/);
    expect(style).toMatch(/top:\d/);
  });

  test('screenshot endpoint returns PNG', async ({ page }) => {
    const response = await page.request.get('/screenshot.png');
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toBe('image/png');

    const body = await response.body();
    // PNG magic bytes
    expect(body[0]).toBe(0x89);
    expect(body[1]).toBe(0x50); // P
    expect(body[2]).toBe(0x4E); // N
    expect(body[3]).toBe(0x47); // G
  });

  test('CSS served as separate file', async ({ page }) => {
    const response = await page.request.get('/devflow.css');
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toBe('text/css');

    const text = await response.text();
    expect(text).toContain('#app-viewport');
    expect(text).toContain('.devflow-element');
  });

  test('JS served as separate file', async ({ page }) => {
    const response = await page.request.get('/devflow.js');
    expect(response.status()).toBe(200);
    expect(response.headers()['content-type']).toBe('application/javascript');

    const text = await response.text();
    expect(text).toContain('app-viewport');
    expect(text).toContain('/api/tap');
  });

  test('element tree is nested (children inside parents)', async ({ page }) => {
    await page.goto('/');

    // Find a parent element that contains child elements
    const nestedParent = page.locator('.devflow-element > .devflow-element');
    const count = await nestedParent.count();
    expect(count).toBeGreaterThan(0);
  });

  test('data attributes use camelCase naming from DevFlow API', async ({ page }) => {
    await page.goto('/');

    // Check attributes directly on elements rather than raw HTML
    const elemWithVis = page.locator('.devflow-element[data-isVisible]');
    expect(await elemWithVis.count()).toBeGreaterThan(0);

    const elemWithEnabled = page.locator('.devflow-element[data-isEnabled]');
    expect(await elemWithEnabled.count()).toBeGreaterThan(0);

    // Verify camelCase naming convention
    const elemWithFullType = page.locator('.devflow-element[data-fullType]');
    expect(await elemWithFullType.count()).toBeGreaterThan(0);
  });
});
