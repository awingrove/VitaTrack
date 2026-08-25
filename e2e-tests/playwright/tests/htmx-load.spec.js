const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('HTMX loads under CSP', () => {
  test('htmx global is defined on home page', async ({ page }, testInfo) => {
    await page.goto('/');
    const htmxPresent = await page.evaluate(() => typeof window.htmx === 'object' && typeof window.htmx.ajax === 'function');
    expect(htmxPresent).toBeTruthy();
    await screenshot(page, testInfo, 'htmx-loaded');
  });

  test('htmx script is self-hosted (no CDN)', async ({ page }) => {
    const requests = [];
    page.on('request', (req) => requests.push(req.url()));
    await page.goto('/');
    const htmxRequests = requests.filter((u) => u.includes('htmx'));
    expect(htmxRequests.length).toBeGreaterThan(0);
    const pageOrigin = new URL(page.url()).origin;
    expect(htmxRequests.every((u) => !/^https?:\/\//i.test(u) || new URL(u).origin === pageOrigin)).toBeTruthy();
    expect(htmxRequests.some((u) => u.includes('unpkg.com'))).toBeFalsy();
  });

  test('no CSP violations block htmx', async ({ page }) => {
    const violations = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error' && msg.text().includes('Content Security Policy')) {
        violations.push(msg.text());
      }
    });
    page.on('pageerror', (err) => {
      if (/Content Security Policy/i.test(err.message)) violations.push(err.message);
    });
    await page.goto('/');
    await page.waitForTimeout(500);
    expect(violations).toEqual([]);
  });
});