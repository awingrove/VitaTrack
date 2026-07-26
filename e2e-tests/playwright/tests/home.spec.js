const { test, expect } = require('@playwright/test');

test.describe('Home Page', () => {
  test('should have correct title', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/Home - VitaTrack/);
  });

  test('should navigate to Family page via link', async ({ page }) => {
    await page.goto('/');
    
    // Click the View Family link
    await page.locator('text=View Family').click();
    
    // Wait a bit for navigation to complete
    await page.waitForTimeout(1000);
    
    // Check that we're on the Family page (either /Family or /Family/Index)
    const url = page.url();
    expect(url).toMatch(/\/Family(\/Index)?$/);
    await expect(page.locator('h2')).toHaveText('Family Members');
  });

  test('should directly access Family page', async ({ page }) => {
    await page.goto('/Family/Index');
    await expect(page).toHaveURL('/Family/Index');
    await expect(page.locator('h2')).toHaveText('Family Members');
  });
});
