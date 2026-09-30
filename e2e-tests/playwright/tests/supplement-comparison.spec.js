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

  test('should gate Compare Selected below two and cap at five', async ({ page }, testInfo) => {
    // Create our own rows so the page has >= 6 regardless of seed state —
    // the shared in-memory DB gives no guarantee about row count.
    const unique = Date.now();
    for (let i = 0; i < 6; i++) {
      await page.goto('/Supplement/Create');
      await page.fill('input#Name', `GateSupp${unique}_${i}`);
      await page.fill('input#Brand', 'GateBrand');
      await page.fill('input#DailyDose', '1 tablet');
      await page.fill('input#Cost', '4.99');
      await page.click('button[hx-post="/Supplement/CreateSave"]');
      await expect(page.locator('h2')).toHaveText('Supplements');
    }

    await page.goto('/Supplement');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // 0 checked -> button gated.
    await expect(page.locator('#compare-selected-btn')).toHaveClass(/disabled/);

    // 1 checked (one named row) -> still gated.
    await page.locator(`tr:has-text("GateSupp${unique}_0") .row-checkbox`).check();
    await expect(page.locator('#compare-selected-btn')).toHaveClass(/disabled/);

    // Select-all flips every box via delete-selected.js; compare-selected.js
    // caps the selection at 5 in DOM order and reveals the hint.
    await page.click('#select-all');
    await expect(page.locator('#compare-hint')).toBeVisible();
    await expect(page.locator('.row-checkbox:checked')).toHaveCount(5);

    // Attempt a 6th -> cap holds at 5.
    await page.locator('.row-checkbox:not(:checked)').first().click();
    await expect(page.locator('.row-checkbox:checked')).toHaveCount(5);
    await expect(page.locator('#compare-hint')).toBeVisible();

    await screenshot(page, testInfo, 'compare-gating-cap');
  });

  test('should redirect to the list when no valid ids resolve', async ({ page }, testInfo) => {
    // 999999 parses but resolves to no supplement; abc fails to parse.
    await page.goto('/Supplement/Compare?ids=999999,abc');
    await expect(page.locator('h2')).toHaveText('Supplements');
    await screenshot(page, testInfo, 'compare-stale-id-redirect');
  });
});
