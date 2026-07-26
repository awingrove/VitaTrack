const { test, expect } = require('@playwright/test');

test.describe.configure({ mode: 'serial' });

test.describe('Supplement LLM Enrichment Flow', () => {

  test('should show review page when creating a supplement without a URL', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    await page.click('text=Add New Supplement');

    // Fill in the form WITHOUT a manufacturer URL — LLM enrichment is skipped
    await page.fill('input[name="Name"]', 'Test Supplement E2E');
    await page.fill('input[name="Brand"]', 'Test Brand');
    await page.fill('input[name="DailyDose"]', '1 capsule');
    await page.fill('input[name="Cost"]', '19.99');
    // No ManufacturerUrl — ensures no API call

    await page.click('input[type="submit"][value="Create"]');

    // Should be on the Review page
    await expect(page.locator('h2')).toHaveText('Review Supplement');

    // Should show the supplement details as read-only
    await expect(page.locator('input[name="Name"]')).toHaveValue('Test Supplement E2E');
    await expect(page.locator('input[name="Brand"]')).toHaveValue('Test Brand');
    await expect(page.locator('input[name="DailyDose"]')).toHaveValue('1 capsule');
    await expect(page.locator('input[name="Cost"]')).toHaveValue('19.99');

    // Should show the editable nutrient section
    await expect(page.locator('h4')).toContainText('Nutrients');
  });

  test('should allow adding nutrients on the review page and saving', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');
    await page.click('text=Add New Supplement');

    await page.fill('input[name="Name"]', 'Review Test E2E');
    await page.fill('input[name="Brand"]', 'Test Brand');
    await page.fill('input[name="DailyDose"]', '2 tablets');
    await page.fill('input[name="Cost"]', '15.00');
    // No URL — ensures no API call

    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Review Supplement');

    // Add a nutrient manually
    await page.click('button#add-nutrient-row');
    await page.locator('input[name="nutrients[0].GenericName"]').fill('Magnesium');
    await page.locator('input[name="nutrients[0].SpecificForm"]').fill('Magnesium Glycinate');
    await page.locator('input[name="nutrients[0].Dosage"]').fill('200mg');

    // Save
    const [request] = await Promise.all([
        page.waitForRequest(req => req.url().includes('/Supplement/ConfirmCreate')),
        page.click('input[type="submit"][value="Confirm & Save"]')
    ]);

    await page.waitForTimeout(300);
    await expect(page.locator('h2')).toHaveText('Supplements', { timeout: 15000 });

    // Verify the supplement was saved
    const supplementRow = page.locator('tr:has-text("Review Test E2E")');
    await expect(supplementRow).toBeVisible({ timeout: 10000 });

    // Verify the nutrients were saved
    await supplementRow.locator('text=Nutrients').click();
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr:has-text("Magnesium")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("Magnesium Glycinate")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("200mg")')).toBeVisible();
  });

  test('should allow removing nutrients on the review page using the remove button', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');
    await page.click('text=Add New Supplement');

    await page.fill('input[name="Name"]', 'Remove Test E2E');
    await page.fill('input[name="Brand"]', 'Test Brand');
    await page.fill('input[name="DailyDose"]', '1 serving');
    await page.fill('input[name="Cost"]', '10.00');
    // No URL — ensures no API call

    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Review Supplement');

    // Add two nutrients
    await page.click('button#add-nutrient-row');
    await page.locator('input[name="nutrients[0].GenericName"]').fill('Zinc');
    await page.locator('input[name="nutrients[0].SpecificForm"]').fill('Zinc Picolinate');
    await page.locator('input[name="nutrients[0].Dosage"]').fill('5mg');

    await page.click('button#add-nutrient-row');
    await page.locator('input[name="nutrients[1].GenericName"]').fill('Vitamin D');
    await page.locator('input[name="nutrients[1].SpecificForm"]').fill('Cholecalciferol');
    await page.locator('input[name="nutrients[1].Dosage"]').fill('1000IU');

    // Remove the second nutrient
    await page.locator('button.remove-row').nth(1).click();

    // Save
    await page.click('input[type="submit"][value="Confirm & Save"]');

    // Verify only the Zinc nutrient was saved
    await page.click('tr:has-text("Remove Test E2E") >> text=Nutrients');
    await expect(page.locator('table tbody tr:has-text("Zinc")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("Vitamin D")')).not.toBeVisible();
  });
});