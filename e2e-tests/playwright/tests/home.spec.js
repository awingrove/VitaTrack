const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Home Page', () => {
  test('should have correct title', async ({ page }, testInfo) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/Home - VitaTrack/);
    await screenshot(page, testInfo, 'home-loaded');
  });

  test('should navigate to Family page via link', async ({ page }, testInfo) => {
    await page.goto('/');
    await screenshot(page, testInfo, 'home-initial');

    await page.locator('text=View Family').click();
    await page.waitForURL(/\/Family(\/Index)?$/);
    await expect(page.locator('h2')).toHaveText('Family Members');
    await screenshot(page, testInfo, 'family-page');
  });

  test('should directly access Family page', async ({ page }, testInfo) => {
    await page.goto('/Family/Index');
    await expect(page).toHaveURL('/Family/Index');
    await expect(page.locator('h2')).toHaveText('Family Members');
    await screenshot(page, testInfo, 'family-direct-access');
  });
});
