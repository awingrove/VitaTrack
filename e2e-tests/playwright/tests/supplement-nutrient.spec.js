const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Supplement Nutrients', () => {

  test('should display nutrients for a supplement', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Click the Nutrients button for Vitamin C
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');

    // Should be on the nutrient index page
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Should show the seed nutrients for Vitamin C (at least 2)
    const rows = page.locator('table tbody tr');
    await expect(rows.first()).toBeVisible();
    expect(await rows.count()).toBeGreaterThanOrEqual(2);

    // Check specific values in the table (use first row, may be edited by parallel tests)
    const firstRow = rows.nth(0);
    await expect(firstRow).toContainText('Vitamin C');
    await screenshot(page, testInfo, 'vitamin-c-nutrients');
  });

  test('should add a new nutrient', async ({ page }, testInfo) => {
    const unique = Date.now();
    const nutrientName = `Selenium${unique}`;
    const specificForm = `Selenium Yeast ${unique}`;

    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Navigate to Vitamin C's nutrients
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Record current count
    const rows = page.locator('table tbody tr');
    const countBefore = await rows.count();
    await screenshot(page, testInfo, 'nutrients-before-add');

    // Click Add Nutrient
    await page.click('text=Add Nutrient');

    // Fill in the form with a unique name to avoid parallel worker collisions
    await page.fill('input[name="GenericName"]', nutrientName);
    await page.fill('input[name="SpecificForm"]', specificForm);
    await page.fill('input[name="Dosage"]', '55mcg');
    await screenshot(page, testInfo, 'nutrient-add-form');

    // Submit the form
    await page.click('input[type="submit"][value="Add Nutrient"]');

    // Should redirect back to index with the new nutrient
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr')).toHaveCount(countBefore + 1);

    // Check the new row
    const newRow = page.locator(`table tbody tr:has-text("${nutrientName}")`);
    await expect(newRow).toBeVisible();
    await expect(newRow).toContainText('55mcg');
    await screenshot(page, testInfo, 'nutrients-after-add');

    // Clean up: delete the nutrient we just created
    await newRow.locator('text=Delete').click();
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await page.click('input[type="submit"][value="Delete"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
  });

  test('should edit an existing nutrient', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Navigate to Vitamin C's nutrients
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Record current count
    const countBefore = await page.locator('table tbody tr').count();

    // Click Edit on the first row (Vitamin C / Ascorbic Acid)
    const firstRow = page.locator('table tbody tr').nth(0);
    await firstRow.locator('text=Edit').click();
    await screenshot(page, testInfo, 'nutrient-edit-form');

    // Modify the form
    await page.fill('input[name="Dosage"]', '1000mg');
    await page.fill('input[name="SpecificForm"]', 'Sodium Ascorbate');

    // Submit the form
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect back to index with updated values
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    // Check the updated row (may have been reordered by parallel tests)
    const updatedRow = page.locator('table tbody tr:has-text("Sodium Ascorbate")');
    await expect(updatedRow).toBeVisible();
    await expect(updatedRow).toContainText('1000mg');
    await screenshot(page, testInfo, 'nutrients-after-edit');
  });

  test('should delete a nutrient', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Navigate to Multivitamin's nutrients (has more variety)
    await page.click('tr:has-text("Multivitamin") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Multivitamin/);

    // Skip if no nutrients exist (deleted by parallel tests)
    const beforeCount = await page.locator('table tbody tr').count();
    if (beforeCount === 0) return;
    await screenshot(page, testInfo, 'nutrients-before-delete');

    // Click Delete on the first nutrient row
    const firstRow = page.locator('table tbody tr').first();
    await firstRow.locator('text=Delete').click();

    // Should be on the delete confirmation page
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await screenshot(page, testInfo, 'nutrient-delete-confirm');

    // Confirm deletion
    await page.click('input[type="submit"][value="Delete"]');

    // Should redirect back to index without the deleted nutrient
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    const afterCount = await page.locator('table tbody tr').count();
    expect(afterCount).toBe(beforeCount - 1);
    await screenshot(page, testInfo, 'nutrients-after-delete');
  });

  test('should show multiple supplements with their own nutrients', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Check Fish Oil nutrients
    await page.click('tr:has-text("Fish Oil") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Fish Oil/);
    await expect(page.locator('table tbody tr:has-text("Omega-3")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("1000mg")')).toBeVisible();
    await screenshot(page, testInfo, 'fish-oil-nutrients');

    // Navigate back
    await page.click('text=Back to Supplements');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Check Multivitamin nutrients (may have been modified by parallel tests)
    await page.click('tr:has-text("Multivitamin") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Multivitamin/);
    // Verify page loaded - show nutrient info or empty message
    const hasTable = await page.locator('table tbody tr').count();
    const hasEmptyMsg = await page.locator('.alert-info').count();
    expect(hasTable + hasEmptyMsg).toBeGreaterThan(0);
    await screenshot(page, testInfo, 'multivitamin-nutrients');
  });

  test('should show supplement serving info on nutrient page', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Navigate to Fish Oil nutrients
    await page.click('tr:has-text("Fish Oil") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Fish Oil/);

    // Should show the serving info in the text-muted paragraph
    await expect(page.locator('p.text-muted')).toHaveText(/1 softgel/);
    await expect(page.locator('p.text-muted')).toHaveText(/Kirkland/);
    await screenshot(page, testInfo, 'fish-oil-serving-info');
  });
});
