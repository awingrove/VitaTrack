const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// End-to-end journey: a user should be able to go from adding a family member,
// to a supplement with its nutrients, to prescribing a dose, and see that data
// reflected in the Nutrient Report — with no dead-ends or 500s along the way.
test.describe('Full daily tracking loop', () => {

  test('member + supplement + nutrient + dose appears in report', async ({ page }, testInfo) => {
    const ts = Date.now();
    const member = `LoopMember${ts}`;
    const supp = `LoopSupp${ts}`;
    const nutrient = `LoopNutrient${ts}`;

    // 1. Add a family member
    await page.goto('/Family/Create');
    await page.fill('input#Name', member);
    await page.fill('input#DisplayName', `Loop${ts}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator(`table tbody tr:has-text("${member}")`)).toBeVisible();

    // 2. Add a supplement (plain save, no URL)
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', supp);
    await page.fill('input#Brand', 'LoopBrand');
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#Cost', '9.99');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator(`table tbody tr:has-text("${supp}")`)).toBeVisible();

    // 3. Add a nutrient to the supplement
    await page.click(`tr:has-text("${supp}") >> text=Nutrients`);
    await expect(page.locator('h2')).toContainText(`Nutrients for ${supp}`);
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', nutrient);
    await page.fill('input[name="SpecificForm"]', 'Ascorbic Acid');
    await page.fill('input[name="Dosage"]', '500mg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${nutrient}")`)).toBeVisible();

    // 4. Prescribe a dose for the member + supplement
    await page.goto('/PrescribedDose/Create');
    await page.selectOption('select#FamilyMemberId', { label: `Loop${ts}` });
    const suppValue = await page.locator(`select#SupplementId option:has-text("${supp}")`).first().getAttribute('value');
    await page.selectOption('select#SupplementId', suppValue);
    await page.fill('input#Multiplier', '1');
    await page.fill('input#Instructions', 'With breakfast');
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator(`table tbody tr:has-text("${supp}")`)).toBeVisible();

    // 5. The report must reflect the prescribed dose
    await page.goto('/Reporting/NutrientReport');
    await expect(page.locator('h2')).toHaveText('Daily Nutrient Report');

    // The prescribed supplement shows up in the report's supplements table
    const supplementsTable = page.locator('table').last();
    await expect(supplementsTable.locator(`tr:has-text("${supp}")`)).toBeVisible();
    // The family member (by DisplayName) is represented in the per-member breakdown
    await expect(page.getByText(`Loop${ts}`, { exact: false })).toBeVisible();
    await screenshot(page, testInfo, 'daily-loop-report');
  });
});
