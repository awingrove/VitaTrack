const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Cost Report', () => {

  test('should display cost report page with heading', async ({ page }, testInfo) => {
    await page.goto('/Reporting/CostReport');
    await expect(page.locator('h2')).toHaveText('Cost Report');
    await expect(page.locator('h3').first()).toBeVisible();
    await screenshot(page, testInfo, 'cost-report-loaded');
  });

  test('should show supplement costs table with seeded data', async ({ page }, testInfo) => {
    await page.goto('/Reporting/CostReport');
    await expect(page.locator('h2')).toHaveText('Cost Report');

    // Should show the supplement costs table
    const supplementTable = page.locator('table').first();
    await expect(supplementTable.locator('th:has-text("Supplement")')).toBeVisible();
    await expect(supplementTable.locator('th:has-text("Brand")')).toBeVisible();
    await expect(supplementTable.locator('th:has-text("Monthly Cost")')).toBeVisible();

    // Should have seed data supplements with costs (from prescribed doses)
    await expect(supplementTable.locator('td:has-text("Vitamin C")')).toBeVisible();
    await expect(supplementTable.locator('td:has-text("Fish Oil")')).toBeVisible();
    await expect(supplementTable.locator('td:has-text("Multivitamin")')).toBeVisible();
    await screenshot(page, testInfo, 'cost-report-supplement-table');
  });

  test('should show family member costs table', async ({ page }, testInfo) => {
    await page.goto('/Reporting/CostReport');
    await expect(page.locator('h2')).toHaveText('Cost Report');

    // Should show the family member costs table (second table)
    const memberTable = page.locator('table').nth(1);
    await expect(memberTable.locator('th:has-text("Family Member")')).toBeVisible();

    // Should have at least one family member row with seed data
    await expect(memberTable.locator('tbody tr').first()).toBeVisible();
    await screenshot(page, testInfo, 'cost-report-family-table');
  });

  test('should show grand total', async ({ page }, testInfo) => {
    await page.goto('/Reporting/CostReport');
    await expect(page.locator('h2')).toHaveText('Cost Report');

    // Should show grand total section
    await expect(page.locator('h3:has-text("Grand Total")')).toBeVisible();
    await expect(page.locator('p.lead')).toBeVisible();
    await screenshot(page, testInfo, 'cost-report-grand-total');
  });
});
