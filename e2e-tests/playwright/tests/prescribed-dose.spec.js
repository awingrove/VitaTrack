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

    // Serving size for the selected supplement is shown read-only (Fish Oil -> 1 softgel)
    await expect(page.locator('p#ServingSize')).toHaveText('1 softgel');

    // Fill in multiplier (servings per day)
    await page.fill('input#Multiplier', '1.5');

    // Fill instructions
    await page.fill('input#Instructions', 'Take with food');
    await screenshot(page, testInfo, 'dose-create-form-filled');

    // Submit
    await page.click('input[type="submit"][value="Create"]');

    // Should redirect to index
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Should show the new dose in the table
    await expect(page.locator('table tbody tr').last()).toContainText('1.5');
    await expect(page.locator('table tbody tr').last()).toContainText('Take with food');
    await screenshot(page, testInfo, 'doses-after-create');
  });

  test('should create a prescribed dose without instructions', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');

    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Multiplier', '1.5');

    // Instructions intentionally left blank
    await page.click('input[type="submit"][value="Create"]');

    // Should redirect to index without a validation error
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('table tbody tr').last()).toContainText('1.5');
    await screenshot(page, testInfo, 'dose-create-no-instructions');
  });

  test('should edit a prescribed dose', async ({ page }, testInfo) => {
    const unique = Date.now();
    const origMultiplier = '2.5';

    // Create a dose to edit (avoids race conditions with seed data)
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Multiplier', origMultiplier);
    await page.fill('input#Instructions', `EditInstr${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Click Edit on the new dose
    const row = page.locator(`table tbody tr:has-text("EditInstr${unique}")`).last();
    await row.locator('a.btn-primary').click();

    // Should be on the edit page
    await expect(page.locator('h2')).toHaveText('Edit');

    // Stepper buttons are present on Edit too
    await expect(page.locator('.multiplier-stepper [data-stepper="increment"]')).toBeVisible();
    await screenshot(page, testInfo, 'dose-edit-form');

    // Modify the multiplier
    await page.fill('input#Multiplier', '3.75');

    // Save
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect to index with updated value
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator(`table tbody tr:has-text("EditInstr${unique}"):has-text("3.75")`)).toBeVisible();
    await screenshot(page, testInfo, 'doses-after-edit');
  });

  test('should delete a prescribed dose', async ({ page }, testInfo) => {
    const unique = Date.now();

    // First create a dose to delete (to avoid race conditions with seed data)
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Multiplier', '2.5');
    await page.fill('input#Instructions', `DelInstr${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await screenshot(page, testInfo, 'doses-before-delete');

    // Click Delete on the row and accept the confirm dialog
    const row = page.locator(`table tbody tr:has-text("DelInstr${unique}")`).first();
    page.on('dialog', dialog => dialog.accept());
    await row.locator('button:has-text("Delete")').click();

    // Should redirect to index without the deleted dose
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator(`table tbody tr:has-text("DelInstr${unique}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'doses-after-delete');
  });


  test('should filter doses by family member', async ({ page }, testInfo) => {
    const unique = Date.now();
    const memberCell = 'table tbody tr:has-text("FilterInstr") td:first-child';

    // Create a dose for the first member so we know the member name
    await page.goto('/PrescribedDose/Create');
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    const memberName = await page.locator('select#FamilyMemberId option:checked').textContent();
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Multiplier', '1');
    await page.fill('input#Instructions', `FilterInstr${unique}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    const memberCellText = (await page.locator(memberCell).first().textContent()).trim();

    // Apply the filter for that member
    await page.selectOption('select#familyMemberFilter', { label: memberName.trim() });
    await page.click('button:has-text("Apply")');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');

    // Our dose is visible and every row belongs to that member
    await expect(page.locator(`table tbody tr:has-text("FilterInstr${unique}")`)).toBeVisible();
    const memberCells = await page.locator('table tbody tr td:first-child').allTextContents();
    memberCells.forEach(cell => expect(cell.trim()).toBe(memberCellText));
    await screenshot(page, testInfo, 'doses-filtered');
  });

  test('should preselect filtered member when creating a dose', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose/Create');
    const memberName = (await page.locator('select#FamilyMemberId option:nth-child(2)').textContent()).trim();

    await page.goto('/PrescribedDose');
    await page.selectOption('select#familyMemberFilter', { label: memberName });
    await page.click('button:has-text("Apply")');

    // Add button now carries the filter through to the create form
    await page.click('text=Add New Prescribed Dose');
    await expect(page.locator('h2')).toHaveText('Create');
    const selected = (await page.locator('select#FamilyMemberId option:checked').textContent()).trim();
    expect(selected).toBe(memberName);
    await screenshot(page, testInfo, 'dose-create-preselected-member');
  });

  test('should show manufacturer column and brand in supplement dropdown', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose');
    await expect(page.locator('th:has-text("Manufacturer")')).toBeVisible();

    await page.click('text=Add New Prescribed Dose');
    await expect(page.locator('h2')).toHaveText('Create');
    // Seed supplements carry brands, rendered in brackets after the name
    await expect(page.locator('select#SupplementId')).toContainText('(');
    await screenshot(page, testInfo, 'dose-create-brand-dropdown');
  });

  test('should default multiplier to 1 and step by 0.25 with stepper buttons', async ({ page }, testInfo) => {
    await page.goto('/PrescribedDose/Create');
    await expect(page.locator('h2')).toHaveText('Create');

    const multiplier = page.locator('input#Multiplier');
    await expect(multiplier).toHaveValue('1');

    await page.click('.multiplier-stepper [data-stepper="increment"]');
    await expect(multiplier).toHaveValue('1.25');

    await page.click('.multiplier-stepper [data-stepper="decrement"]');
    await page.click('.multiplier-stepper [data-stepper="decrement"]');
    await expect(multiplier).toHaveValue('0.75');

    // Any numeric value can still be typed and submitted
    await page.selectOption('select#FamilyMemberId', { index: 1 });
    await page.selectOption('select#SupplementId', { index: 1 });
    await page.fill('input#Multiplier', '1.3');
    await page.fill('input#Instructions', `StepInstr${Date.now()}`);
    await page.click('input[type="submit"][value="Create"]');
    await expect(page.locator('h2')).toHaveText('Prescribed Doses');
    await expect(page.locator('table tbody tr').last()).toContainText('1.3');
    await screenshot(page, testInfo, 'dose-create-stepper');
  });
});
