const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

async function reachEditorForNewSupplement(page, testInfo, name) {
  await page.goto('/Supplement/Create');
  await expect(page.locator('h2')).toHaveText('Create Supplement');
  await page.fill('input#Name', name);
  await page.fill('input#Brand', `Brand${Date.now()}`);
  await page.fill('input#DailyDose', '1 tablet');
  await page.fill('input#Cost', '9.99');

  // Enrich (no URL) self-skips the LLM and swaps the nutrient editor into the page.
  page.on('dialog', dialog => dialog.accept());
  await page.click('button#enrich-btn');

  await expect(page.locator('#add-blend-row')).toBeVisible();
  await screenshot(page, testInfo, 'blend-editor-open');
  return name;
}

async function readSupplementId(page) {
  return await page.locator('input[name="supplementId"]').inputValue();
}

test.describe('Supplement Nutrient Blends', () => {

  test('should display a seeded blend with child nutrients', async ({ page }, testInfo) => {
    await page.goto('/Supplement/EditNutrients/3');
    await expect(page.locator('h4')).toContainText('Nutrients for Multivitamin');

    // The seeded "Proprietary Blend" parent row is rendered (value lives in an input).
    const blendRow = page.locator('#nutrients-table tbody tr:has(input[value="Proprietary Blend"])');
    await expect(blendRow).toBeVisible();
    await expect(blendRow.locator('.add-sub-nutrient')).toBeVisible();

    // The blend's two child nutrient rows are rendered (one with an empty dosage).
    await expect(page.locator('.blend-child')).toHaveCount(2);
    await expect(page.locator('.blend-child').first().locator('input').nth(0)).toHaveValue('Pectin');
    await expect(page.locator('.blend-child').nth(1).locator('input').nth(0)).toHaveValue('Botanical Extract');
    await expect(page.locator('.blend-child').nth(1).locator('input').nth(2)).toHaveValue('');
    await screenshot(page, testInfo, 'seeded-blend-display');
  });

  test('should add a blend with sub-nutrients and persist grouping', async ({ page }, testInfo) => {
    const name = `BlendSupp${Date.now()}`;
    await reachEditorForNewSupplement(page, testInfo, name);

    // Add a blend parent row.
    await page.click('#add-blend-row');
    const blendRow = page.locator('#nutrients-table tbody tr', { has: page.locator('.add-sub-nutrient') });
    await expect(blendRow).toBeVisible();
    await blendRow.locator('input[name$=".GenericName"]').fill('My Proprietary Blend');
    await blendRow.locator('input[name$=".Dosage"]').fill('500mg');

    // First sub-nutrient with a dosage.
    await blendRow.locator('.add-sub-nutrient').click();
    let children = page.locator('.blend-child');
    await expect(children).toHaveCount(1);
    await children.nth(0).locator('input[name$=".GenericName"]').fill('Sub One');
    await children.nth(0).locator('input[name$=".Dosage"]').fill('100mg');

    // Second sub-nutrient with EMPTY dosage (proves dosage is optional).
    await blendRow.locator('.add-sub-nutrient').click();
    children = page.locator('.blend-child');
    await expect(children).toHaveCount(2);
    await children.nth(1).locator('input[name$=".GenericName"]').fill('Sub Two Empty');
    await screenshot(page, testInfo, 'blend-with-two-children');

    // Save Changes.
    await page.click('button:has-text("Save Changes")');
    await expect(page.locator('text=Nutrients saved successfully.')).toBeVisible();

    // Editor now shows the blend + 2 child rows.
    await expect(page.locator('#nutrients-table tbody tr:has(input[value="My Proprietary Blend"])')).toBeVisible();
    await expect(page.locator('.blend-child')).toHaveCount(2);
    await screenshot(page, testInfo, 'blend-saved');

    // Reload the editor from the database to confirm the grouping persisted.
    const id = await readSupplementId(page);
    await page.goto(`/Supplement/EditNutrients/${id}`);
    await expect(page.locator('#nutrients-table tbody tr:has(input[value="My Proprietary Blend"])')).toBeVisible();
    await expect(page.locator('.blend-child')).toHaveCount(2);
    await expect(page.locator('.blend-child').nth(1).locator('input[name$=".GenericName"]')).toHaveValue('Sub Two Empty');
    await expect(page.locator('.blend-child').nth(1).locator('input[name$=".Dosage"]')).toHaveValue('');
    await screenshot(page, testInfo, 'blend-persisted');
  });

  test('should save a blend child without dosage', async ({ page }, testInfo) => {
    const name = `BlendNoDose${Date.now()}`;
    await reachEditorForNewSupplement(page, testInfo, name);

    await page.click('#add-blend-row');
    const blendRow = page.locator('#nutrients-table tbody tr', { has: page.locator('.add-sub-nutrient') });
    await blendRow.locator('input[name$=".GenericName"]').fill('Optional Dose Blend');
    await blendRow.locator('input[name$=".Dosage"]').fill('250mg');

    await blendRow.locator('.add-sub-nutrient').click();
    const child = page.locator('.blend-child').first();
    await expect(child).toBeVisible();
    await child.locator('input[name$=".GenericName"]').fill('Child No Dosage');
    // Intentionally leave the dosage input empty.

    await page.click('button:has-text("Save Changes")');
    await expect(page.locator('text=Nutrients saved successfully.')).toBeVisible();

    // No validation error blocked the save; child persisted with empty dosage.
    await expect(page.locator('text=One or more nutrient')).toHaveCount(0);
    await expect(page.locator('.blend-child').first().locator('input[name$=".GenericName"]')).toHaveValue('Child No Dosage');
    await expect(page.locator('.blend-child').first().locator('input[name$=".Dosage"]')).toHaveValue('');
    await screenshot(page, testInfo, 'blend-child-no-dosage');
  });
});
