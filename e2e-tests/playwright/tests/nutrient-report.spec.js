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

    // Either shows "No active prescribed doses" empty state or the per-member breakdown
    const noDosesAlert = page.locator('p.text-muted');
    const perMemberH3 = page.locator('h3:has-text("Per Family Member")');

    const hasAlert = await noDosesAlert.count();
    const hasPerMember = await perMemberH3.count();

    // One of these should be visible
    expect(hasAlert + hasPerMember).toBeGreaterThan(0);
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
  test('should show units for per-family-member nutrient totals', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    // Seed data: Alice takes Vitamin C dosed at 500mg; unit must travel with the number.
    // Whole totals render without trailing decimals ("500 mg", not "500.00 mg").
    await expect(page.locator('td:has-text("500 mg")').first()).toBeVisible();
    await expect(page.locator('td:has-text("500.00")')).toHaveCount(0);
    await screenshot(page, testInfo, 'nutrient-report-with-units');
  });

  test('should expand a member total into contributing supplements and link to edit', async ({ page }, testInfo) => {
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    // Seed: Alice's Vitamin C total (500 mg) comes from the "Vitamin C" supplement.
    const vitCRow = page.locator('tbody tr').filter({
      has: page.locator('td:text-is("Vitamin C")'),
    }).first();
    await vitCRow.getByRole('button', { name: '500 mg' }).click();

    // Supplement-count pill sits next to the amount.
    await expect(vitCRow.locator('span.badge.rounded-pill').first()).toBeVisible();
    await expect(vitCRow.locator('span.badge.rounded-pill').first()).toHaveText(/^\d+$/);

    const detailRow = page.locator('tr.collapse.show').filter({ hasText: 'Vitamin C (NatureMade)' });
    await expect(detailRow).toBeVisible();
    await expect(detailRow).toContainText('500 mg');

    // Supplement name jumps straight to the edit page.
    await detailRow.locator('a').first().click();
    await expect(page).toHaveURL(/\/Supplement\/Edit\/\d+/);
    await expect(page.locator('h2')).toHaveText('Edit Supplement');

    // Review step: nutrient amounts live one hop further on the nutrient editor.
    await page.click('text=Edit Nutrient Breakdown');
    await expect(page).toHaveTitle(/Edit Nutrients/);
    await expect(page.locator('input[name*=".Dosage"][value="500mg"]')).toBeVisible();
    await screenshot(page, testInfo, 'nutrient-breakdown-review-dosage');
  });
});
