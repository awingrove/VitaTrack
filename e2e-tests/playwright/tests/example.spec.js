const { test, expect } = require('@playwright/test');

test('home page has correct title', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/Home - VitaTrack/);
});

test('family page loads correctly', async ({ page }) => {
  await page.goto('/Family/Index');
  await expect(page).toHaveURL('/Family/Index');
  await expect(page.locator('h2')).toHaveText('Family Members');
});
