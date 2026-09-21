const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// A model-validation rule must be exercised at EVERY endpoint that binds the
// nutrient model (AGENTS Entry-Point Coverage). These tests prove a missing
// required GenericName is surfaced as a friendly validation error -- never a 500.
test.describe('Nutrient model validation on every surface', () => {

  test('standalone Add Nutrient rejects empty GenericName without 500', async ({ page }, testInfo) => {
    const ts = Date.now();
    const supp = `ValSupp${ts}`;

    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'ValBrand');
    await page.fill('input#DailyDose', '1 tablet');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');

    await page.click(`tr:has-text("${supp}") >> text=Nutrients`);
    await expect(page.locator('h2')).toContainText(`Nutrients for ${supp}`);

    // Open the Add Nutrient form but submit with no name
    await page.click('text=Add Nutrient');
    await page.click('input[type="submit"][value="Add Nutrient"]');

    // Model validation rejects the empty name: we stay on the Add form
    // (never a 500, never a silent save -- a redirect would land on the
    // "Nutrients for ..." index instead).
    await expect(page.locator('h2')).toContainText('Add Nutrient to');
    await screenshot(page, testInfo, 'standalone-validation-error');
  });

  test('inline editor Save Changes rejects empty blend GenericName without 500', async ({ page }, testInfo) => {
    const ts = Date.now();
    const supp = `ValInline${ts}`;

    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'ValInlineBrand');
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#Cost', '9.99');
    page.on('dialog', dialog => dialog.accept());
    await page.click('button#enrich-btn');
    await expect(page.locator('#add-blend-row')).toBeVisible();

    // Add a blend row but leave GenericName empty
    await page.click('#add-blend-row');
    const row = page.locator('#nutrients-table tbody tr', { has: page.locator('.add-sub-nutrient') });
    await row.locator('input[name$=".Dosage"]').fill('500mg');

    await page.click('button:has-text("Save Changes")');

    // An empty-named nutrient is rejected server-side: nothing is persisted
    // (no 500, no silent save). The editor reloads showing the empty state.
    await expect(page.locator('#nutrients-table')).toContainText('No nutrients');
    await screenshot(page, testInfo, 'inline-validation-error');
  });
});
