const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Prescribed Doses', () => {

  test('should display prescribed doses index page', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('text=Add New Prescribed Dose')).toBeVisible();
    await screenshot(page, testInfo, 'doses-list');
  });

  test('should navigate to create form', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    await page.click('text=Add New Prescribed Dose');
    await expect(page.locator('h2')).toHaveText('Create');

    // Should have dropdowns for family member and supplement
    await expect(page.locator('select#FamilyMemberId')).toBeVisible();
    await expect(page.locator('select#SupplementId')).toBeVisible();
    await screenshot(page, testInfo, 'dose-create-form');
  });

  test('should create a new prescribed dose', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');

    // Select first family member
    await page.selectOption('select#FamilyMemberId', { index: 1 });

    // Select first supplement
    await page.selectOption('select#SupplementId', { index: 1 });

    // Fill in dosage
    await page.fill('input#Dosage', '500mg');

    // Fill frequency
    await page.fill('input#FrequencyPerDay', '2');

    // Fill instructions
    await page.fill('input#Instructions', 'Take with food');
    await screenshot(page, testInfo, 'dose-create-form-filled');

    // Submit
    await page.click('input[type="submit"][value="Create"]');

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Should show the new dose in the table
    await expect(page.locator('table tbody tr').last()).toContainText('500mg');
    await expect(page.locator('table tbody tr').last()).toContainText('Take with food');
    await screenshot(page, testInfo, 'doses-after-create');
  });

  test('should edit a prescribed dose', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // There should be at least one row (from seed data)
    await expect(page.locator('table tbody tr').first()).toBeVisible();

    // Click Edit on the first row
    const firstRow = page.locator('table tbody tr').first();
    await firstRow.locator('text=Edit').click();

    // Should be on the edit page
    await expect(page.locator('h2')).toHaveText('Edit');
    await screenshot(page, testInfo, 'dose-edit-form');

    // Modify the dosage
    await page.fill('input#Dosage', '1000mg');

    // Save
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to index with updated value
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('td:has-text("1000mg")')).toBeVisible();
    await screenshot(page, testInfo, 'doses-after-edit');
  });

  test('should delete a prescribed dose', async ({ page }, testInfo) => {
    // First create a dose to delete (to avoid race conditions with seed data)
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Dosage', 'DeleteMe');
    await page.fill('input#FrequencyPerDay', '1');
    await page.fill('input#Instructions', 'Temporary dose');
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await screenshot(page, testInfo, 'doses-before-delete');

    // Click Delete on the first row
    const firstRow = page.locator('table tbody tr').first();
    const row = page.locator('table tbody tr:has-text("DeleteMe")').last();
    await row.locator('a.btn-danger').click();

    // Should be on the delete confirmation page
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await screenshot(page, testInfo, 'dose-delete-confirm');

    // Confirm deletion
    await page.click('input[type="submit"][value="Delete"]');

    // Should redirect to index — just verify the page loaded correctly
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await screenshot(page, testInfo, 'doses-after-delete');
  });
});
