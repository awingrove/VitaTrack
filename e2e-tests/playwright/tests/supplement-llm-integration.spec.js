const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Supplement LLM Integration (Real API)', () => {

  test('should extract nutrients from a real product URL using LLM', async ({ page }, testInfo) => {
    const apiKey = process.env.LLM_API_KEY || process.env.VitaTrack__ApiKey;
    test.skip(!apiKey, 'Skipping — no API key configured');

    test.setTimeout(180000);

    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click('text=Add New Supplement');

    await page.fill('input[name="Name"]', 'Children\'s Mindlinxr');
    await page.fill('input[name="Brand"]', 'BioCare');
    await page.fill('input[name="DailyDose"]', '1 scoop (5g)');
    await page.fill('input[name="ManufacturerUrl"]', 'https://www.biocare.co.uk/children-s-mindlinxr-multinutrient-150g');
    await page.fill('input[name="Cost"]', '29.99');
    await screenshot(page, testInfo, 'llm-create-form-filled');

    // Submit — this will trigger LLM enrichment (may take 5-30s)
    await page.click('input[type="submit"][value="Create"]');

    // Wait for the Review page — the LLM call may take time
    await expect(page.locator('h2')).toHaveText('Review Supplement', { timeout: 90000 });

    // The LLM must succeed — no error alerts allowed
    const errorAlert = page.locator('.alert-warning');
    await expect(errorAlert).toHaveCount(0, { timeout: 5000 });

    // Verify the supplement details are shown
    await expect(page.locator('input[name="Name"]')).toHaveValue('Children\'s Mindlinxr');
    await expect(page.locator('input[name="Brand"]')).toHaveValue('BioCare');
    await expect(page.locator('input[name="ManufacturerUrl"]')).toHaveValue('https://www.biocare.co.uk/children-s-mindlinxr-multinutrient-150g');
    await screenshot(page, testInfo, 'llm-review-page');

    // Verify nutrients were extracted by the LLM
    const nutrientCount = await page.locator('input[name^="nutrients["][name$="].GenericName"]').count();
    console.log(`LLM extracted ${nutrientCount} nutrients`);
    expect(nutrientCount).toBeGreaterThan(0);

    // Get the first nutrient name
    const firstGenericName = await page.locator('input[name="nutrients[0].GenericName"]').inputValue();
    console.log('First extracted nutrient:', firstGenericName);
    expect(firstGenericName.length).toBeGreaterThan(0);

    // Verify at least one nutrient name is meaningful (common supplement ingredient)
    let foundMeaningful = false;
    for (let i = 0; i < Math.min(nutrientCount, 10); i++) {
      const name = await page.locator(`input[name="nutrients[${i}].GenericName"]`).inputValue();
      if (/vitamin|zinc|iron|calcium|magnesium|selenium|iodine|chromium|copper|manganese|potassium/i.test(name)) {
        foundMeaningful = true;
        console.log(`Found meaningful nutrient at index ${i}: ${name}`);
        break;
      }
    }
    expect(foundMeaningful).toBeTruthy();
    await screenshot(page, testInfo, 'llm-extracted-nutrients');

    // Save and verify
    await page.click('input[type="submit"][value="Confirm & Save"]');
    await expect(page.locator('h2')).toHaveText('Supplements', { timeout: 15000 });

    // Verify the supplement was saved
    const savedRow = page.locator('tr:has-text("Children\'s Mindlinxr")').last();
    await expect(savedRow).toBeVisible({ timeout: 10000 });

    // Verify the supplement details appear in the list
    await expect(savedRow).toContainText('BioCare');
    await expect(savedRow).toContainText('29.99');

    // Check the nutrients were saved
    await savedRow.locator('text=Nutrients').click();
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    const savedRows = page.locator('table tbody tr');
    const savedRowCount = await savedRows.count();
    console.log(`Saved nutrient rows: ${savedRowCount}`);
    expect(savedRowCount).toBeGreaterThan(0);
    await screenshot(page, testInfo, 'llm-nutrients-saved');
  });

  test('should allow manual nutrient entry when LLM fails', async ({ page }, testInfo) => {
    test.setTimeout(60000);

    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click('text=Add New Supplement');

    const unique = Date.now();
    const suppName = `ManualEntry${unique}`;
    await page.fill('input[name="Name"]', suppName);
    await page.fill('input[name="Brand"]', 'TestBrand');
    await page.fill('input[name="DailyDose"]', '1 capsule');

    // No ManufacturerUrl — LLM enrichment is skipped, no API call made
    await screenshot(page, testInfo, 'manual-create-form');

    await page.click('input[type="submit"][value="Create"]');

    // Should be on Review page with no error
    await expect(page.locator('h2')).toHaveText('Review Supplement');
    await expect(page.locator('input[name="Name"]')).toHaveValue(suppName);

    // No LLM nutrients — should show empty nutrient section with add button
    const hasLlmNutrients = await page.locator('input[name="nutrients[0].GenericName"]').count() > 0;
    expect(hasLlmNutrients).toBeFalsy();

    // Add a nutrient manually
    await page.click('button#add-nutrient-row');
    await page.locator('input[name="nutrients[0].GenericName"]').fill('Magnesium');
    await page.locator('input[name="nutrients[0].SpecificForm"]').fill('Magnesium Citrate');
    await page.locator('input[name="nutrients[0].Dosage"]').fill('100mg');
    await screenshot(page, testInfo, 'manual-nutrient-added');

    // Save
    await page.click('input[type="submit"][value="Confirm & Save"]');
    await expect(page.locator('h2')).toHaveText('Supplements', { timeout: 15000 });

    // Verify supplement saved
    const savedRow = page.locator(`table tbody tr:has-text("${suppName}")`).last();
    await expect(savedRow).toBeVisible({ timeout: 10000 });

    // Verify nutrient saved
    await savedRow.locator('text=Nutrients').click();
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr:has-text("Magnesium")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("Magnesium Citrate")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("100mg")')).toBeVisible();
    await screenshot(page, testInfo, 'manual-nutrient-saved');
  });
});
