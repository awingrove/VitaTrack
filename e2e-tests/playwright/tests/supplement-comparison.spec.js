const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Supplement comparison', () => {

  test('should compare selected supplements via the Compare Selected button', async ({ page }, testInfo) => {
    // Seed names/brands/dosages read from DbInit.EnsureCreated — no hardcoded DB ids.
    await page.goto('/Supplement');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Select two named rows by text, then click the list-page control.
    await page.locator('tr:has-text("Fish Oil") .row-checkbox').check();
    await page.locator('tr:has-text("Multivitamin") .row-checkbox').check();
    await screenshot(page, testInfo, 'compare-two-selected');

    await page.click('#compare-selected-btn');

    await expect(page.locator('h2')).toHaveText('Compare Supplements');

    // Column headers: nutrient label column, then name / brand / DailyDose per supplement.
    const headers = page.locator('thead th');
    await expect(headers.nth(0)).toHaveText('Nutrient');
    await expect(headers.nth(1)).toContainText('Fish Oil');
    await expect(headers.nth(1)).toContainText('Kirkland');
    await expect(headers.nth(1)).toContainText('1 softgel');
    await expect(headers.nth(2)).toContainText('Multivitamin');
    await expect(headers.nth(2)).toContainText('Centrum');
    await expect(headers.nth(2)).toContainText('1 tablet');

    // Shared nutrient: a row with non-empty cells in both columns.
    const vitaminDRow = page.locator('tbody tr').filter({ hasText: 'Vitamin D' });
    await expect(vitaminDRow).toHaveCount(1);
    await expect(vitaminDRow.locator('td').nth(1)).toContainText('200IU');
    await expect(vitaminDRow.locator('td').nth(2)).toContainText('20µg');

    // Gap: Multivitamin lacks Omega-3, so its cell is the em dash.
    const omega3Row = page.locator('tbody tr').filter({ hasText: 'Omega-3' });
    await expect(omega3Row).toHaveCount(1);
    await expect(omega3Row.locator('td').nth(1)).toContainText('1000mg');
    await expect(omega3Row.locator('td').nth(2)).toHaveText('—');

    await screenshot(page, testInfo, 'comparison-grid');
  });
});
