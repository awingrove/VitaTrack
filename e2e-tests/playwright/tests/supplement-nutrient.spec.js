const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

test.describe('Supplement Nutrients', () => {

  // Create a dedicated supplement per test: seed data shifts and parallel
  // workers mutate shared rows, so tests must never navigate via seed names.
  async function createOwnSupplement(page, unique, { brand, dailyDose } = {}) {
    const suppName = `NutrientTest${unique}`;
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', brand ?? `Brand${unique}`);
    await page.fill('input#DailyDose', dailyDose ?? '1 tablet');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');
    return suppName;
  }

  async function openNutrientsFor(page, suppName) {
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click(`tr:has-text("${suppName}") >> text=Nutrients`);
    await expect(page.locator('h2')).toHaveText(`Nutrients for ${suppName}`);
  }

  async function addNutrient(page, { name, form, dosage }) {
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', name);
    if (form) {
      await page.fill('input[name="SpecificForm"]', form);
    }
    if (dosage) {
      await page.fill('input[name="Dosage"]', dosage);
    }
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator('h2')).toContainText('Nutrients for');
  }

  // Adds a child under an existing root nutrient of the current supplement
  async function addChildUnderFirstBlend(page, name, dosage) {
    await page.click('text=Add Nutrient');
    const parentValue = await page.locator('#ParentNutrientId option:not([value=""])').first().getAttribute('value');
    expect(parentValue).toBeTruthy();
    await page.selectOption('#ParentNutrientId', parentValue);
    await page.fill('input[name="GenericName"]', name);
    if (dosage) {
      await page.fill('input[name="Dosage"]', dosage);
    }
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator('h2')).toContainText('Nutrients for');
  }

  test('should display nutrients for a supplement', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = await createOwnSupplement(page, unique);
    await openNutrientsFor(page, suppName);

    // Fresh supplement starts empty
    await expect(page.locator('.alert-info')).toContainText('No nutrients defined');

    // Add two nutrients, then verify both display on the index
    const first = `DisplayOne${unique}`;
    const second = `DisplayTwo${unique}`;
    await addNutrient(page, { name: first, form: `FormA${unique}`, dosage: '10mg' });
    await addNutrient(page, { name: second, form: `FormB${unique}`, dosage: '20mg' });

    const rows = page.locator('table tbody tr');
    await expect(rows.first()).toBeVisible();
    expect(await rows.count()).toBe(2);
    await expect(rows.first()).toContainText(first);
    await screenshot(page, testInfo, 'supplement-nutrients-displayed');
  });

  test('should add a new nutrient', async ({ page }, testInfo) => {
    const unique = Date.now();
    const nutrientName = `Selenium${unique}`;
    const specificForm = `Selenium Yeast ${unique}`;
    const suppName = await createOwnSupplement(page, unique);

    await openNutrientsFor(page, suppName);

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
    const afterCount = await page.locator('table tbody tr').count();
    expect(afterCount).toBeGreaterThanOrEqual(countBefore + 1);

    // Check the new row
    const newRow = page.locator(`table tbody tr:has-text("${nutrientName}")`);
    await expect(newRow).toBeVisible();
    await expect(newRow).toContainText('55mcg');
    await screenshot(page, testInfo, 'nutrients-after-add');

    // Clean up: delete the nutrient we just created
    await newRow.locator('a.btn-danger').click();
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await page.click('input[type="submit"][value="Delete"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
  });

  test('should display blend hierarchy with badge on nutrient index', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = await createOwnSupplement(page, unique);
    const blendName = `Proprietary Blend${unique}`;
    const childName = `Pectin${unique}`;

    await openNutrientsFor(page, suppName);

    // Build a blend parent + child in our own supplement
    await addNutrient(page, { name: blendName, form: `BlendForm${unique}`, dosage: '500mg' });
    await addChildUnderFirstBlend(page, childName);

    // Blend parent carries the blend badge; children are indented under it
    const blendRow = page.locator(`table tbody tr:has-text("${blendName}")`);
    await expect(blendRow.locator('.badge:has-text("blend")')).toBeVisible();
    const childRow = page.locator(`table tbody tr:has-text("${childName}")`);
    await expect(childRow).toBeVisible();
    await expect(childRow).toContainText('↳');
    await screenshot(page, testInfo, 'blend-hierarchy-on-index');
  });

  test('should add a sub-nutrient under a blend without a dosage', async ({ page }, testInfo) => {
    const unique = Date.now();
    const nutrientName = `BlendChild${unique}`;
    const suppName = await createOwnSupplement(page, unique);

    await openNutrientsFor(page, suppName);

    // Seed a root nutrient in our own supplement to act as the blend parent
    await addNutrient(page, { name: `BlendParent${unique}`, form: `ParentForm${unique}`, dosage: '1g' });

    await page.click('text=Add Nutrient');

    // Pick an existing root nutrient as the parent blend
    const parentValue = await page.locator('#ParentNutrientId option:not([value=""])').first().getAttribute('value');
    expect(parentValue).toBeTruthy();
    await page.selectOption('#ParentNutrientId', parentValue);

    // Dosage becomes optional once a parent is selected
    await expect(page.locator('#Dosage')).not.toHaveAttribute('required', /.*/);

    await page.fill('input[name="GenericName"]', nutrientName);
    // Leave Dosage and SpecificForm blank on purpose

    await page.click('input[type="submit"][value="Add Nutrient"]');

    // Should redirect back to index with the new child row saved
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    const newRow = page.locator(`table tbody tr:has-text("${nutrientName}")`);
    await expect(newRow).toBeVisible();
    await screenshot(page, testInfo, 'blend-child-without-dosage');

    // Clean up: delete the nutrient we just created
    await newRow.locator('a.btn-danger').click();
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await page.click('input[type="submit"][value="Delete"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
  });

  test('should edit an existing nutrient', async ({ page }, testInfo) => {
    const unique = Date.now();
    const nutrientName = `EditMe${unique}`;
    const specificForm = `Form${unique}`;
    const suppName = await createOwnSupplement(page, unique);

    await openNutrientsFor(page, suppName);

    // Create a nutrient to edit (avoids race conditions with seed data)
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', nutrientName);
    await page.fill('input[name="SpecificForm"]', specificForm);
    await page.fill('input[name="Dosage"]', '25mcg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    // Verify the new nutrient exists
    const newRow = page.locator(`table tbody tr:has-text("${nutrientName}")`);
    await expect(newRow).toBeVisible();

    // Click Edit on the new nutrient
    await newRow.locator('a.btn-primary').click();
    await screenshot(page, testInfo, 'nutrient-edit-form');

    // Modify the form
    await page.fill('input[name="Dosage"]', '500mg');
    await page.fill('input[name="SpecificForm"]', `Updated${unique}`);

    // Submit the form
    await page.click('input[type="submit"][value="Save"]');

    // Should redirect back to index with updated values
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    const updatedRow = page.locator(`table tbody tr:has-text("Updated${unique}")`);
    await expect(updatedRow).toBeVisible();
    await expect(updatedRow).toContainText('500mg');
    await screenshot(page, testInfo, 'nutrients-after-edit');

    // Clean up: delete the nutrient we just edited
    await updatedRow.locator('text=Delete').click();
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await page.click('input[type="submit"][value="Delete"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
  });

  test('should delete a nutrient', async ({ page }, testInfo) => {
    const unique = Date.now();
    const nutrientName = `DeleteMe${unique}`;
    const suppName = await createOwnSupplement(page, unique);

    await openNutrientsFor(page, suppName);

    // Create a nutrient to delete (avoids race conditions with seed data)
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', nutrientName);
    await page.fill('input[name="SpecificForm"]', `Form${unique}`);
    await page.fill('input[name="Dosage"]', '10mcg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    // Record current count
    const beforeCount = await page.locator('table tbody tr').count();
    await screenshot(page, testInfo, 'nutrients-before-delete');

    // Click Delete on the new nutrient
    const targetRow = page.locator(`table tbody tr:has-text("${nutrientName}")`);
    await targetRow.locator('a.btn-danger').click();

    // Should be on the delete confirmation page
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await screenshot(page, testInfo, 'nutrient-delete-confirm');

    // Confirm deletion
    await page.click('input[type="submit"][value="Delete"]');

    // Should redirect back to index without the deleted nutrient
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    const afterCount = await page.locator('table tbody tr').count();
    expect(afterCount).toBe(beforeCount - 1);
    await expect(page.locator(`table tbody tr:has-text("${nutrientName}")`)).toHaveCount(0);
    await screenshot(page, testInfo, 'nutrients-after-delete');
  });

  test('should show multiple supplements with their own nutrients', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppA = await createOwnSupplement(page, unique);
    const suppB = await createOwnSupplement(page, unique + 1);
    const nutrientA = `Omega-3${unique}`;
    const nutrientB = `Calcium${unique}`;

    // Check first supplement's nutrients
    await openNutrientsFor(page, suppA);
    await addNutrient(page, { name: nutrientA, form: `Form${unique}`, dosage: '1000mg' });
    await expect(page.locator(`table tbody tr:has-text("${nutrientA}")`)).toBeVisible();
    await expect(page.locator(`table tbody tr:has-text("1000mg")`)).toBeVisible();
    await screenshot(page, testInfo, 'fish-oil-nutrients');

    // Navigate back
    await page.click('text=Back to Supplements');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Check second supplement's nutrients are independent
    await openNutrientsFor(page, suppB);
    await addNutrient(page, { name: nutrientB, form: `Form${unique}b`, dosage: '50mg' });
    await expect(page.locator(`table tbody tr:has-text("${nutrientB}")`)).toBeVisible();
    await screenshot(page, testInfo, 'multivitamin-nutrients');
  });

  test('should warn about blend children when bulk deleting nutrients', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = `BulkWarn${unique}`;

    // Create an isolated supplement so parallel workers never share rows
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', 'WarnBrand');
    await page.fill('input#DailyDose', '1 cap');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');

    // Add two nutrients
    await page.click(`tr:has-text("${suppName}") >> text=Nutrients`);
    for (const name of [`WarnA${unique}`, `WarnB${unique}`]) {
      await page.click('text=Add Nutrient');
      await page.fill('input[name="GenericName"]', name);
      await page.fill('input[name="SpecificForm"]', `${name}-form`);
      await page.fill('input[name="Dosage"]', '5mg');
      await page.click('input[type="submit"][value="Add Nutrient"]');
      await expect(page.locator(`table tbody tr:has-text("${name}")`)).toBeVisible();
    }

    // Select them and click Delete Selected, then DISMISS the dialog
    let dialogFired = false;
    let dialogMessage = '';
    page.on('dialog', async dialog => {
      dialogFired = true;
      dialogMessage = dialog.message();
      await dialog.dismiss();
    });

    const boxes = page.locator('.row-checkbox');
    await boxes.nth(0).check();
    await boxes.nth(1).check();
    await page.locator('#delete-selected-btn').click();

    expect(dialogFired).toBe(true);
    expect(dialogMessage).toMatch(/child nutrients of selected blends/i);

    // Dismissal must have prevented the delete
    await expect(page.locator(`table tbody tr:has-text("WarnA${unique}")`)).toBeVisible();
    await screenshot(page, testInfo, 'nutrient-bulk-delete-warning');
  });

  test('should show blend-child cascade warning when deleting a blend', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = `BlendCascade${unique}`;
    const parentName = `BlendParent${unique}`;
    const childName = `BlendChild${unique}`;

    // Isolated supplement so parallel workers never share rows
    await page.goto('/Supplement/Create');
    await page.fill('input#Name', suppName);
    await page.fill('input#Brand', 'BlendBrand');
    await page.fill('input#DailyDose', '1 cap');
    await page.click('button[hx-post="/Supplement/CreateSave"]');
    await expect(page.locator('h2')).toHaveText('Supplements');
    await page.click(`tr:has-text("${suppName}") >> text=Nutrients`);

    // Add the blend parent
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', parentName);
    await page.fill('input[name="SpecificForm"]', `${parentName}-form`);
    await page.fill('input[name="Dosage"]', '10mg');
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${parentName}")`)).toBeVisible();

    // Add a child under that blend
    await page.click('text=Add Nutrient');
    await page.fill('input[name="GenericName"]', childName);
    const optionValue = await page.locator('select[name="ParentNutrientId"] option', { hasText: parentName }).getAttribute('value');
    await page.selectOption('select[name="ParentNutrientId"]', optionValue);
    await page.click('input[type="submit"][value="Add Nutrient"]');
    await expect(page.locator(`table tbody tr:has-text("${childName}")`)).toBeVisible();

    // Open the blend parent's delete confirmation page
    const parentRow = page.locator(`table tbody tr:has-text("${parentName}")`);
    await parentRow.locator('a.btn-danger').click();
    await expect(page.locator('text=Are you sure')).toBeVisible();
    await expect(page.locator("text=/child nutrients will also be deleted/i")).toBeVisible();
    await screenshot(page, testInfo, 'blend-delete-cascade-warning');

    // Confirming deletes the blend AND its children
    await page.click('input[type="submit"][value="Delete"]');
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator(`table tbody tr:has-text("${parentName}")`)).toHaveCount(0);
    await expect(page.locator(`table tbody tr:has-text("${childName}")`)).toHaveCount(0);
  });

  test('should sort nutrients by a column when header clicked', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = await createOwnSupplement(page, unique);

    await openNutrientsFor(page, suppName);
    await addNutrient(page, { name: `Zinc${unique}`, form: `Form${unique}`, dosage: '10mg' });
    await addNutrient(page, { name: `Algae${unique}`, form: `Form${unique}b`, dosage: '20mg' });

    await page.locator('th[data-sort-key="GenericName"]').click();
    const firstName = (await page.locator('table tbody tr:first-child td[data-sort-key="GenericName"]').textContent()).trim();
    const secondName = (await page.locator('table tbody tr:nth-child(2) td[data-sort-key="GenericName"]').textContent()).trim();
    expect(String(firstName).localeCompare(String(secondName), undefined, { numeric: true }) <= 0).toBeTruthy();

    await screenshot(page, testInfo, 'nutrient-sorted');
  });

  test('should show supplement serving info on nutrient page', async ({ page }, testInfo) => {
    const unique = Date.now();
    const suppName = await createOwnSupplement(page, unique, { brand: `Kirkland${unique}`, dailyDose: '1 softgel' });

    // Navigate to the supplement's nutrients
    await openNutrientsFor(page, suppName);

    // Should show the serving info in the text-muted paragraph
    await expect(page.locator('p.text-muted')).toHaveText(/1 softgel/);
    await expect(page.locator('p.text-muted')).toHaveText(new RegExp(`Kirkland${unique}`));
    await screenshot(page, testInfo, 'fish-oil-serving-info');
  });
});
