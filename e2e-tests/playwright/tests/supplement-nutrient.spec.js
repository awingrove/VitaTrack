const { test, expect } = require('@playwright/test');

test.describe.configure({ mode: 'serial' });

test.describe('Supplement Nutrients', () => {

  test('should display nutrients for a supplement', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Click the Nutrients button for Vitamin C
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');

    // Should be on the nutrient index page
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Should show the seed nutrients for Vitamin C
    const rows = page.locator('table tbody tr');
    const count = await rows.count();
    expect(count).toBe(2); // Vitamin C 500mg, Iron 0mg

    // Check specific values in the table
    await expect(rows.nth(0)).toContainText('Vitamin C');
    await expect(rows.nth(0)).toContainText('Ascorbic Acid');
    await expect(rows.nth(0)).toContainText('500mg');
  });

  test('should add a new nutrient', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Navigate to Vitamin C's nutrients
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Click Add Nutrient
    await page.click('text=Add Nutrient');

    // Fill in the form
    await page.fill('input[name="GenericName"]', 'Selenium');
    await page.fill('input[name="SpecificForm"]', 'Selenium Yeast');
    await page.fill('input[name="Dosage"]', '55mcg');

    // Submit the form
    await page.click('input[type="submit"][value="Add Nutrient"]');

    // Should redirect back to index with the new nutrient
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    const rows = page.locator('table tbody tr');
    const count = await rows.count();
    expect(count).toBe(3); // Was 2, now 3

    // Check the new row
    await expect(rows.nth(2)).toContainText('Selenium');
    await expect(rows.nth(2)).toContainText('Selenium Yeast');
    await expect(rows.nth(2)).toContainText('55mcg');
  });

  test('should edit an existing nutrient', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Navigate to Vitamin C's nutrients
    await page.click('tr:has-text("Vitamin C") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Vitamin C/);

    // Click Edit on the first row (Vitamin C / Ascorbic Acid)
    const firstRow = page.locator('table tbody tr').nth(0);
    await firstRow.locator('text=Edit').click();

    // Modify the form
    await page.fill('input[name="Dosage"]', '1000mg');
    await page.fill('input[name="SpecificForm"]', 'Sodium Ascorbate');

    // Submit the form
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect back to index with updated values
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr')).toHaveCount(3);

    // Check the updated row
    const updatedRow = page.locator('table tbody tr').nth(0);
    await expect(updatedRow).toContainText('Sodium Ascorbate');
    await expect(updatedRow).toContainText('1000mg');
  });

  test('should delete a nutrient', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Navigate to Multivitamin's nutrients (has more variety)
    await page.click('tr:has-text("Multivitamin") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Multivitamin/);

    // Count rows before deletion
    const beforeCount = await page.locator('table tbody tr').count();
    expect(beforeCount).toBe(5); // 5 seed nutrients for Multivitamin

    // Click Delete on the Iron nutrient (Ferrous Fumarate row)
    const ironRow = page.locator('table tbody tr:has-text("Ferrous Fumarate")');
    await ironRow.locator('text=Delete').click();

    // Should be on the delete confirmation page
    await expect(page.locator('text=Are you sure')).toBeVisible();

    // Confirm deletion
    await page.click('input[type="submit"][value="Delete"]');

    // Should redirect back to index without the deleted nutrient
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    const afterCount = await page.locator('table tbody tr').count();
    expect(afterCount).toBe(beforeCount - 1);
    await expect(page.locator('table tbody tr:has-text("Ferrous Fumarate")')).not.toBeVisible();
  });

  test('should show multiple supplements with their own nutrients', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Check Fish Oil nutrients
    await page.click('tr:has-text("Fish Oil") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Fish Oil/);
    await expect(page.locator('table tbody tr:has-text("Omega-3")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("1000mg")')).toBeVisible();

    // Navigate back
    await page.click('text=Back to Supplements');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Check Multivitamin nutrients
    await page.click('tr:has-text("Multivitamin") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Multivitamin/);
    await expect(page.locator('table tbody tr:has-text("Vitamin A")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("900mcg")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("Calcium Carbonate")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("200mg")')).toBeVisible();
  });

  test('should show supplement serving info on nutrient page', async ({ page }) => {
    await page.goto('/Supplement');
    await page.waitForLoadState('networkidle');

    // Navigate to Fish Oil nutrients
    await page.click('tr:has-text("Fish Oil") >> text=Nutrients');
    await expect(page.locator('h2')).toHaveText(/Fish Oil/);

    // Should show the serving info in the text-muted paragraph
    await expect(page.locator('p.text-muted')).toHaveText(/1 softgel/);
    await expect(page.locator('p.text-muted')).toHaveText(/Kirkland/);
  });
});