const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// A user who creates a supplement without a manufacturer URL should still be able
// to enrich it manually via the inline editor and have those nutrients persist.
test.describe('Enrich without URL inline round-trip', () => {

  test('inline editor saves nutrients that appear on the nutrient index', async ({ page }, testInfo) => {
    const ts = Date.now();
    const supp = `InlineSupp${ts}`;
    const nutrient = `InlineNutrient${ts}`;

    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'InlineBrand');
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#Cost', '9.99');

    // Enrich with no URL self-skips the LLM and opens the inline nutrient editor.
    page.on('dialog', dialog => dialog.accept());
    await page.click('button#enrich-btn');
    await expect(page.locator('#add-blend-row')).toBeVisible();
    await screenshot(page, testInfo, 'inline-editor-open');

    // Add a plain nutrient row via the blend row (root nutrient, no children)
    await page.click('#add-blend-row');
    const row = page.locator('#nutrients-table tbody tr', { has: page.locator('.add-sub-nutrient') });
    await row.locator('input[name$=".GenericName"]').fill(nutrient);
    await row.locator('input[name$=".Dosage"]').fill('500mg');

    await page.click('button:has-text("Save Changes")');
    await expect(page.locator('text=Nutrients saved successfully.')).toBeVisible();
    await expect(page.locator(`tr:has(input[value="${nutrient}"]), tr:has-text("${nutrient}")`).first()).toBeVisible();
    await screenshot(page, testInfo, 'inline-editor-saved');

    // Reload the nutrient index to prove persistence (not just a stale DOM).
    // On the index the name renders inside an <input value=...>, so match that.
    const id = await page.locator('input[name="supplementId"]').inputValue();
    await page.goto(`/Supplement/EditNutrients/${id}`);
    await expect(page.locator(`table tbody tr:has(input[value="${nutrient}"])`)).toBeVisible();
    await expect(page.locator(`table tbody tr:has(input[value="${nutrient}"]) input[value="500mg"]`)).toBeVisible();
    await screenshot(page, testInfo, 'inline-editor-persisted');
  });
});
