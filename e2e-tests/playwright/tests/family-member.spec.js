const { test, expect } = require('@playwright/test');

test.describe('Family Members', () => {

  test('should display family members index page', async ({ page }) => {
    await page.goto('/Family');
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator('text=Add New Family Member')).toBeVisible();

    // Seed data should show at least one member
    await expect(page.locator('table tbody tr').first()).toBeVisible();
  });

  test('should create a new family member', async ({ page }) => {
    const unique = Date.now();
    await page.goto('/Family/Create');
    await expect(page.locator('h2')).toHaveText('Create');

    await page.fill('input#Name', `Person${unique}`);
    await page.fill('input#DisplayName', `Display${unique}`);

    await page.click('input[type="submit"][value="Create"]');

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Should show the new member in the table
    await expect(page.locator(`table tbody tr:has-text("Person${unique}")`)).toBeVisible();
  });

  test('should edit a family member', async ({ page }) => {
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

    // Update the display name
    await page.fill('input#DisplayName', `Updated${unique}`);

    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to index with updated value
    await expect(page.locator('h2')).toHaveText('Family Members');
    await expect(page.locator(`table tbody tr:has-text("Updated${unique}")`)).toBeVisible();
  });

  test('should delete a family member', async ({ page }) => {
    const unique = Date.now();

    // Create a member first
    await page.goto('/Family/Create');
    await page.fill('input#Name', `Doom${unique}`);
    await page.fill('input#DisplayName', `Doomed${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Click Delete on the new member
    const row = page.locator(`table tbody tr:has-text("Doom${unique}")`).first();
    page.on('dialog', dialog => dialog.accept());
    await row.locator('button:has-text("Delete")').click();

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Family Members');

    // Member should be gone
    await expect(page.locator(`table tbody tr:has-text("Doom${unique}")`)).toHaveCount(0);
  });
});
