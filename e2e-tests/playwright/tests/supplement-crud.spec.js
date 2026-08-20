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

  test('should create a new supplement', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = `NewSupp${unique}`;
    const suppBrand = `NewBrand${unique}`;

    await page.goto('/Supplement/Create');
    await expect(page.locator('h2')).toHaveText('Create Supplement');
    await screenshot(page, testInfo, 'supplement-create-form');

    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', suppBrand);
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#Cost', '12.99');
    await screenshot(page, testInfo, 'supplement-create-filled');

    // Save does a plain persist (no enrichment) and HX-Redirects to the list
    await page.click('button:has-text("Save")');

    // Should be on supplements list
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Verify the new supplement appears in the table
    await expect(page.locator(`table tbody tr:has-text("${suppName}")`)).toBeVisible();
    await expect(page.locator(`table tbody tr:has-text("${suppName}")`)).toContainText(suppBrand);
    await screenshot(page, testInfo, 'supplement-create-verified');
  });

  test('should render Brand as external link when URL present', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = `LinkSupp${unique}`;
    const suppBrand = `LinkBrand${unique}`;

    await page.goto('/Supplement/Create');
    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', suppBrand);
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#ManufacturerUrl', 'https://example.com/product');
    await page.fill('input#Cost', '9.99');
    await page.click('button:has-text("Save")');

    await expect(page.locator('h2')).toHaveText('Supplements');

    const row = page.locator(`table tbody tr:has-text("${suppName}")`);
    const brandLink = row.locator('td a').filter({ hasText: suppBrand });
    await expect(brandLink).toBeVisible();
    await expect(brandLink).toHaveAttribute('target', '_blank');
    await expect(brandLink).toHaveAttribute('rel', /noopener/);
    await expect(brandLink).toHaveAttribute('href', 'https://example.com/product');
    await screenshot(page, testInfo, 'supplement-brand-link');
  });

  test('should edit a supplement', async ({ page }, testInfo) => {
    // Accept the overwrite-confirm dialog that Enrich raises for seeded supplements
    page.on('dialog', dialog => dialog.accept());

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

    // Enrich (not plain Save) still performs enrichment and redirects to Review
    await page.click('button#enrich-btn');

    // Should redirect to review page (LLM enrichment self-skips without API key)
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
    await page.click('button:has-text("Save")');

    // Save redirects straight to the list (no inline nutrient editor)
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

  test('should delete multiple supplements via checkboxes', async ({ page }, testInfo) => {
    const unique = Date.now();
    const names = [`BulkA${unique}`, `BulkB${unique}`, `BulkC${unique}`];

    // Create 3 supplements (Save goes straight to the list)
    for (const name of names) {
      await page.goto('/Supplement/Create');
      await page.fill('input#Name', name);
      await page.fill('input#Brand', 'BulkBrand');
      await page.fill('input#DailyDose', '1 pill');
      await page.click('button:has-text("Save")');
      await expect(page.locator('h2')).toHaveText('Supplements');
    }

    // Add a nutrient to the first supplement
    await page.click(`tr:has-text("${names[0]}") >> text=Nutrients`);
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', 'Vitamin C');
    await page.fill('input[name="SpecificForm"]', 'Ascorbic Acid');
    await page.fill('input[name="Dosage"]', '500mg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr:has-text("Vitamin C")')).toBeVisible();

    // Go back to supplement list
    await page.goto('/Supplement');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Verify all 3 are present
    for (const name of names) {
      await expect(page.locator(`table tbody tr:has-text("${name}")`)).toBeVisible();
    }

    // Select only the 3 new supplements by their individual checkboxes
    for (const name of names) {
      await page.locator(`tr:has-text("${name}") .row-checkbox`).check();
    }
    await screenshot(page, testInfo, 'bulk-delete-selected');

    // Click Delete Selected and confirm dialog
    page.on('dialog', dialog => dialog.accept());
    await page.locator('#delete-selected-btn').click();

    // Should be on supplements list without the deleted supplements
    await expect(page.locator('h2')).toHaveText('Supplements');
    for (const name of names) {
      await expect(page.locator(`table tbody tr:has-text("${name}")`)).toHaveCount(0);
    }
    await screenshot(page, testInfo, 'bulk-delete-done');
  });

  test('should show separate Save and Enrich buttons on Create', async ({ page }, testInfo) => {
    await page.goto('/Supplement/Create');
    await expect(page.locator('h2')).toHaveText('Create Supplement');

    await expect(page.locator('#enrich-btn')).toBeVisible();
    await expect(page.locator('button:has-text("Save")')).toBeVisible();
    await screenshot(page, testInfo, 'create-save-enrich-buttons');
  });

  test('should save without enrichment navigates to list (Create)', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = `PlainSave${unique}`;

    await page.goto('/Supplement/Create');
    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', 'PlainBrand');
    await page.fill('input#DailyDose', '1 tablet');
    await page.fill('input#Cost', '5.49');
    await page.click('button:has-text("Save")');

    await expect(page.locator('h2')).toHaveText('Supplements');
    await expect(page.locator(`table tbody tr:has-text("${suppName}")`)).toBeVisible();
    await screenshot(page, testInfo, 'create-plain-save-list');
  });

  test('should warn before enriching when supplement already has nutrients (Edit)', async ({ page }, testInfo) => {
    let dialogMessage = '';
    page.on('dialog', async dialog => {
      dialogMessage = dialog.message();
      await dialog.accept();
    });

    await page.goto('/Supplement/Edit/1');
    await expect(page.locator('h2')).toHaveText('Edit Supplement');
    await expect(page.locator('#enrich-btn')).toBeVisible();

    const count = await page.locator('#enrich-btn').getAttribute('data-nutrient-count');
    expect(count).toMatch(/\d+/);
    expect(parseInt(count, 10)).toBeGreaterThan(0);

    await page.click('button#enrich-btn');

    // Overwrite warning should have been raised (and accepted)
    expect(dialogMessage).toMatch(/overwrite/i);

    // Enrichment proceeds to the Review page
    await expect(page.locator('h2')).toHaveText('Review Supplement');
    await screenshot(page, testInfo, 'edit-enrich-warning');
  });

  test('should save without enrichment on Edit navigates to list', async ({ page }, testInfo) => {
    await page.goto('/Supplement/Edit/1');
    await expect(page.locator('h2')).toHaveText('Edit Supplement');

    await page.click('button[formaction="/Supplement/EditSave"]');

    await expect(page.locator('h2')).toHaveText('Supplements');
    await screenshot(page, testInfo, 'edit-plain-save-list');
  });
});
