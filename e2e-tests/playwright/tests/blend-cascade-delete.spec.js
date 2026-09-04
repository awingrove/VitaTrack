const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Blend cascade delete', () => {

  test('deleting a blend parent removes its children', async ({ page }, testInfo) => {
    const ts = Date.now();
    const supp = `CascadeSupp${ts}`;
    const parent = `CascadeBlend${ts}`;
    const child = `CascadeChild${ts}`;

    // Isolated supplement so parallel workers never share rows
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'CascadeBrand');
    await page.fill('input#DailyDose', '1 cap');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Open the nutrient index for this supplement
    await page.click(`tr:has-text("${supp}") >> text=Nutrients`);
    await expect(page.locator('h2')).toContainText(`Nutrients for ${supp}`);

    // Add the blend parent as a root nutrient
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', parent);
    await page.fill('input[name="SpecificForm"]', 'BlendForm');
    await page.fill('input[name="Dosage"]', '500mg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${parent}")`)).toBeVisible();

    // Add a child under the blend parent
    await page.click('text=Add Nutrient');
    const parentValue = await page.locator('select[name="ParentNutrientId"] option', { hasText: parent }).getAttribute('value');
    await page.selectOption('select[name="ParentNutrientId"]', parentValue);
    await page.fill('input[name="GenericName"]', child);
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${child}")`)).toBeVisible();
    await screenshot(page, testInfo, 'cascade-blend-created');

    // Delete the blend parent -> should warn about cascade and remove children on confirm
    const parentRow = page.locator(`table tbody tr:has-text("${parent}")`);
    let dialogMessage = '';
    page.on('dialog', async dialog => {
      dialogMessage = dialog.message();
      await dialog.accept();
    });
    await parentRow.locator('form button:has-text("Delete")').click();

    expect(dialogMessage).toMatch(/child nutrients of this blend will also be deleted/i);
    await expect(page.locator(`table tbody tr:has-text("${parent}")`)).toHaveCount(0);
    await expect(page.locator(`table tbody tr:has-text("${child}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'cascade-blend-deleted');
  });
});
