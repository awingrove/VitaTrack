const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Nutrient Report', () => {

  test('should arrive via nav bar Report link', async ({ page }, testInfo) => {
    await page.goto('/');
    await page.click('nav >> text=Report');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');
    await screenshot(page, testInfo, 'nutrient-report-via-nav');
  });

  test('should display nutrient report page', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');
    await screenshot(page, testInfo, 'nutrient-report-loaded');
  });

  test('should show supplements in report table', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    // Should show the supplements section heading
    await expect(page.locator('h3:has-text("Supplements in Report")')).toBeVisible();

    // Should show supplement data (from prescribed doses)
    const supplementsTable = page.locator('table').last();
    await expect(supplementsTable.locator('th:has-text("Name")')).toBeVisible();
    await expect(supplementsTable.locator('th:has-text("Brand")')).toBeVisible();
    await screenshot(page, testInfo, 'nutrient-report-supplements-table');
  });

  test('should show no-active-doses message or nutrient data', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    // Either shows "No active prescribed doses" alert or nutrient tables
    const noDosesAlert = page.locator('.alert-info');
    const grandTotalH3 = page.locator('h3:has-text("Grand Total")');

    const hasAlert = await noDosesAlert.count();
    const hasGrandTotal = await grandTotalH3.count();

    // One of these should be visible
    expect(hasAlert + hasGrandTotal).toBeGreaterThan(0);
    await screenshot(page, testInfo, 'nutrient-report-data-or-empty');
  });

  test('should have link to manage prescribed doses', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    await expect(page.locator('text=Manage Prescribed Doses')).toBeVisible();
    await page.click('text=Manage Prescribed Doses');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await screenshot(page, testInfo, 'navigated-to-doses');
  });
});
