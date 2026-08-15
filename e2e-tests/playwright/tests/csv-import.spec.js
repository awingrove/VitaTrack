const { test, expect } = require('@playwright/test');
const path = require('path');

test.describe('CSV Import', () => {

  test('should open import modal and download sample CSV', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();
    await expect(page.locator('#importCsvModal')).toContainText('Import Supplements from CSV');
    await expect(page.locator('a:has-text("Download sample CSV")')).toBeVisible();
  });

  test('should import a valid CSV and show report', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();

    const csvPath = path.join(__dirname, '..', 'test-data', 'sample-import.csv');
    await page.setInputFiles('input[type="file"]', csvPath);
    await page.click('button:has-text("Upload & Import")');

    await expect(page.locator('#import-report-container .card')).toBeVisible({ timeout: 60000 });
    await expect(page.locator('#import-report-container')).toContainText('Imported');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Alpha');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Beta');
    await expect(page.locator('#import-report-container')).toContainText('TestCSV Product Gamma');
  });

  test('should show error for empty file input', async ({ page }) => {
    await page.goto('/Supplement');
    await page.click('button:has-text("Import CSV")');
    await expect(page.locator('#importCsvModal')).toBeVisible();

    await expect(page.locator('input[type="file"]')).toHaveAttribute('required', '');
  });

});
