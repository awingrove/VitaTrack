const { test, expect } = require('@playwright/test');

test.describe('Home Page', () => {
  test('should have correct title', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/Home - VitaTrack/);
  });

  test('should navigate to Family page via link', async ({ page }) => {
    await page.goto('/');

    await page.locator('text=View Family').click();
    await page.waitForURL(/\/Family(\/Index)?$/);
    await expect(page.locator('h2')).toHaveText('Family Members');
  });

  test('should directly access Family page', async ({ page }) => {
    await page.goto('/Family/Index');
    await expect(page).toHaveURL('/Family/Index');
    await expect(page.locator('h2')).toHaveText('Family Members');
  });
});
