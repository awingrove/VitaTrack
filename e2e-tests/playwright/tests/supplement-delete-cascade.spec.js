const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// Deleting a supplement that has nutrients and a prescribed dose must cascade
// (app-enforced) and not leave orphaned rows or throw a FK error.
test.describe('Supplement delete cascade', () => {

  test('deleting a supplement also removes its nutrients and prescribed doses', async ({ page }, testInfo) => {
    const ts = Date.now();
    const supp = `CascadeSupp${ts}`;
    const member = `CascadeOwner${ts}`;
    const nutrient = `CascadeNutrient${ts}`;

    // Family member to own the dose
    await page.goto('/Family/Create');
    await page.fill('input#Name', member);
    await page.fill('input#DisplayName', `Owner${ts}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Supplement
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'CascadeBrand');
    await page.fill('input#DailyDose', '1 cap');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Add a nutrient
    await page.click(`tr:has-text("${supp}") >> text=Nutrients`);
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', nutrient);
    await page.fill('input[name="SpecificForm"]', 'Ascorbic Acid');
    await page.fill('input[name="Dosage"]', '500mg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${nutrient}")`)).toBeVisible();

    // Prescribe a dose for this supplement + member
    await page.goto('/PrescribedDose/Create');
    await page.selectOption('select#FamilyMemberId', { label: `Owner${ts}` });
    const suppValue = await page.locator(`select#SupplementId option:has-text("${supp}")`).first().getAttribute('value');
    await page.selectOption('select#SupplementId', suppValue);
    await page.fill('input#Multiplier', '1');
    await page.fill('input#Instructions', 'With food');
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator(`table tbody tr:has-text("${supp}")`)).toBeVisible();

    // Delete the supplement
    await page.goto('/Supplement');
    const row = page.locator(`table tbody tr:has-text("${supp}")`);
    let dialogMessage = '';
    page.on('dialog', async dialog => {
      dialogMessage = dialog.message();
      await dialog.accept();
    });
    await row.locator('form button:has-text("Delete")').click();
    expect(dialogMessage).toMatch(/nutrients and prescribed doses/i);
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator(`table tbody tr:has-text("${supp}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'supplement-deleted');

    // The prescribed dose must be gone too (cascade)
    await page.goto('/PrescribedDose');
    await expect(page.locator(`table tbody tr:has-text("${supp}")`)).toHaveCount(0);
  });
});
