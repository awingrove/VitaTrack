const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Family Members', () => {

  test('should display family members index page', async ({ page }, testInfo) => {
    await page.goto('/Family');
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator('text=Add New Family Member')).toBeVisible();

    // Seed data should show at least one member
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await screenshot(page, testInfo, 'family-members-list');
  });

  test('should create a new family member', async ({ page }, testInfo) => {
    const unique = Date.now();
    await page.goto('/Family/Create');
    await expect(page.locator('h2')).toHaveText('Create');
    await screenshot(page, testInfo, 'family-create-form');

    await page.fill('input#Name', `Person${unique}`);
    await page.fill('input#DisplayName', `Display${unique}`);

    await page.click('input[type="submit"][value="Create"]');

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Should show the new member in the table
    await expect(page.locator(`table tbody tr:has-text("Person${unique}")`)).toBeVisible();
    await screenshot(page, testInfo, 'family-after-create');
  });

  test('should edit a family member', async ({ page }, testInfo) => {
    const unique = Date.now();

    // Create a member first
    await page.goto('/Family/Create');
    await page.fill('input#Name', `Edit${unique}`);
    await page.fill('input#DisplayName', `Orig${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Click Edit on the new member
    const row = page.locator(`table tbody tr:has-text("Edit${unique}")`).first();
    await row.locator('a.btn-primary').click();

    // Should be on edit page
    await expect(page.locator('h2')).toHaveText('Edit');
    await screenshot(page, testInfo, 'family-edit-form');

    // Update the display name
    await page.fill('input#DisplayName', `Updated${unique}`);

    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to index with updated value
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator(`table tbody tr:has-text("Updated${unique}")`)).toBeVisible();
    await screenshot(page, testInfo, 'family-after-edit');
  });

  test('should delete a family member', async ({ page }, testInfo) => {
    const unique = Date.now();

    // Create a member first
    await page.goto('/Family/Create');
    await page.fill('input#Name', `Doom${unique}`);
    await page.fill('input#DisplayName', `Doomed${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');
    await screenshot(page, testInfo, 'family-before-delete');

    // Click Delete on the new member (dialog must actually fire — CSP regression guard)
    const row = page.locator(`table tbody tr:has-text("Doom${unique}")`).first();
    let dialogFired = false;
    page.on('dialog', async dialog => {
      dialogFired = true;
      expect(dialog.message()).toMatch(/prescribed doses/i);
      await dialog.accept();
    });
    await row.locator('button:has-text("Delete")').click();
    expect(dialogFired).toBe(true);

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Member should be gone
    await expect(page.locator(`table tbody tr:has-text("Doom${unique}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'family-after-delete');
  });

  test('should delete a family member with prescribed doses', async ({ page }, testInfo) => {
    const unique = Date.now();

    // Create a family member
    await page.goto('/Family/Create');
    await page.fill('input#Name', `DoseOwner${unique}`);
    await page.fill('input#DisplayName', `Owner${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Create a prescribed dose for this member (uses seed data: first supplement)
    await page.goto('/PrescribedDose/Create');
    await page.selectOption('select#FamilyMemberId', { label: `Owner${unique}` });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Dosage', `TestDose${unique}`);
    await page.fill('input#FrequencyPerDay', '1');
    await page.fill('input#Instructions', 'Take with food');
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Verify the prescribed dose exists
    await expect(page.locator(`table tbody tr:has-text("TestDose${unique}")`)).toBeVisible();

    // Go to family page and delete the member
    await page.goto('/Family');
    const row = page.locator(`table tbody tr:has-text("DoseOwner${unique}")`).first();
    page.on('dialog', dialog => dialog.accept());
    await row.locator('button:has-text("Delete")').click();

    // Should redirect to index without the deleted member
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator(`table tbody tr:has-text("DoseOwner${unique}")`)).toHaveCount(0);

    // Verify the prescribed dose is also gone
    await page.goto('/PrescribedDose');
    await expect(page.locator(`table tbody tr:has-text("TestDose${unique}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'family-with-dose-after-delete');
  });
});
