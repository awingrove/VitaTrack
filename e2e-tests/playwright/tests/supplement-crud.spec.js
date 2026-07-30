const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Supplement CRUD', () => {

  test('should display supplements list', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator('text=Add New Supplement')).toBeVisible();

    // Seed data should show at least one supplement
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Check table columns exist
    await expect(page.locator('th:has-text("Name")')).toBeVisible();
    await expect(page.locator('th:has-text("Brand")')).toBeVisible();
    await expect(page.locator('th:has-text("Cost")')).toBeVisible();
    await screenshot(page, testInfo, 'supplement-list');
  });

  test('should edit a supplement', async ({ page }, testInfo) => {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await screenshot(page, testInfo, 'supplement-list-before-edit');

    // Click Edit on the first supplement
    const firstRow = page.locator('table tbody tr').first();
    await firstRow.locator('text=Edit').click();

    // Should be on edit page
    await expect(page.locator('h2')).toHaveText('Edit Supplement');

    // Verify form fields are populated
    await expect(page.locator('input#Name')).not.toBeEmpty();
    await expect(page.locator('input#Brand')).not.toBeEmpty();
    await screenshot(page, testInfo, 'supplement-edit-form');

    // Update the name
    const originalName = await page.locator('input#Name').inputValue();
    await page.fill('input#Name', originalName + ' Updated');

    // Save
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to review page (LLM enrichment)
    await expect(page.locator('h2')).toHaveText('Review Supplement');
    await screenshot(page, testInfo, 'supplement-review-page');

    // Confirm save
    await page.click('input[type="submit"][value="Confirm & Save"]');

    // Should redirect to supplements list
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator('table tbody tr:has-text("Updated")')).toBeVisible();
    await screenshot(page, testInfo, 'supplement-after-edit');
  });

  test('should delete a supplement', async ({ page }, testInfo) => {
    // Create a supplement first to avoid deleting seed data
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', 'ToDelete');
    await page.fill('input#Brand', 'TestBrand');
    await page.fill('input#DailyDose', '1 pill');
    await page.click('input[type="submit"][value="Create"]');

    // Should be on review page
    await expect(page.locator('h2')).toHaveText('Review Supplement');
    await screenshot(page, testInfo, 'supplement-review-before-delete');

    await page.click('input[type="submit"][value="Confirm & Save"]');

    // Should be on supplements list
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator('table tbody tr:has-text("ToDelete")')).toBeVisible();
    await screenshot(page, testInfo, 'supplement-list-before-delete');

    // Click delete button and confirm the JS dialog
    const row = page.locator('table tbody tr:has-text("ToDelete")');
    page.on('dialog', dialog => dialog.accept());
    await row.locator('button:has-text("Delete")').click();

    // Should be on supplements list without the deleted supplement
    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator('table tbody tr:has-text("ToDelete")')).toHaveCount(0);
    await screenshot(page, testInfo, 'supplement-after-delete');
  });
});
