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
    const unique = Date.now();
    const origDosage = `EditMe${unique}`;

    // Create a dose to edit (avoids race conditions with seed data)
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Dosage', origDosage);
    await page.fill('input#FrequencyPerDay', '1');
    await page.fill('input#Instructions', 'Original instructions');
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Click Edit on the new dose
    const row = page.locator(`table tbody tr:has-text("${origDosage}")`).last();
    await row.locator('a.btn-primary').click();

    // Should be on the edit page
    await expect(page.locator('h2')).toHaveText('Edit');
    await screenshot(page, testInfo, 'dose-edit-form');

    // Modify the dosage
    await page.fill('input#Dosage', `${unique}mg`);

    // Save
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to index with updated value
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator(`table tbody tr:has-text("${unique}mg")`)).toBeVisible();
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

    // Click Delete on the row and accept the confirm dialog
    const row = page.locator('table tbody tr:has-text("DeleteMe")').last();
    let deleteDialogMessage = '';
    page.on('dialog', async dialog => {
      deleteDialogMessage = dialog.message();
      await dialog.accept();
    });
    await row.locator('form button:has-text("Delete")').click();
    expect(deleteDialogMessage).toMatch(/delete this prescribed dose/i);
    await screenshot(page, testInfo, 'dose-delete-confirm');

    // Should redirect to index and the deleted dose should be gone
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('table tbody tr:has-text("DeleteMe")')).toHaveCount(0);
    await screenshot(page, testInfo, 'doses-after-delete');
  });
});
