const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// The one spec that talks to a genuine provider. Every other service in this suite is
// stood in for by a real local socket (see service-connection.spec.js), which is how the
// app's own half is covered; this file covers the other half, and cannot without a
// provider.
//
// The credential is LLM_API_KEY, and it is the *input* to a connect a user performs
// rather than a proxy for capability. The app takes its connection from the database and
// nothing binds a config key any more, so there is exactly one way an e2e can give it
// one: type it into the Settings form, the same as a user would. A spec that seeded the
// row would be asserting against a connection the app never agreed to, and a spec that
// read the variable's presence as permission to run would fail for a reason that has
// nothing to do with the code — which is what the second skip below exists to prevent.
//
// LLM_BASE_URL and LLM_MODEL are required alongside it for the same reason the model is
// required by the app: there is no configured default left to fall back on, and a
// completion with no model is refused rather than guessed at. test-e2e.sh prompts for
// all three and exports all three empty in CI, so the whole file self-skips there.
//
// The base URL is the gateway ROOT. The app appends its own "v1/models"
// (ServiceEndpoint.Resolve), so a base that already ends in a version segment asks for
// /v1/v1/models, gets a 404, and the connection comes back unverified — which looks
// exactly like a rejected key and is the reason the skip reason below names three causes
// rather than one. test-e2e.sh's prompt says so too; this says it where the value is read.
//
// CROSS-FILE HAZARD, if you add a test here that writes the connection: there is at most
// one active connection, and service-connection.spec.js is a different file that also
// writes it. `describe.configure({mode:'serial'})` below serialises only WITHIN this file —
// under fullyParallel two workers can hold two copies of this file (or this file and
// service-connection.spec.js) at once, and one will demote the other's connection
// mid-test. The trigger is narrow today: these tests need a working provider key, and CI
// runs workers:1, so neither is affected by default. It becomes live the moment someone
// runs this file locally in parallel with a real key, or a second file starts connecting.
// The fix is not more serial mode — it is that the second writer is a different file, and
// the app allows one active row. Keep the write in one file.
const apiKey = process.env.LLM_API_KEY;
const baseUrl = process.env.LLM_BASE_URL;
const model = process.env.LLM_MODEL;

// Resolved before anything navigates. The skip is decided from the environment alone, so
// it is settled before the first page.goto: a decision taken after a navigation can be
// reached differently on a second run, when a connection row left behind by an earlier
// test is what the page is showing.
const missing = ['LLM_API_KEY', 'LLM_BASE_URL', 'LLM_MODEL'].filter(name => !process.env[name]);
const skipWithoutProvider =
  `Skipping — ${missing.join(', ')} not set in the environment. This is the only spec that needs a `
  + 'real provider; every other service in the suite is a local socket.';

/**
 * Connects the app to the provider through its own form, and lets the app's probe say
 * whether the connection is usable. Reads the badge rather than asserting a state: a
 * credential that is set but not accepted — expired, mistyped, or aimed at a provider
 * with no model list — is a reason this spec cannot say anything about the code, not a
 * failure a developer can act on. That is the whole reason the earlier version of this
 * file went red for a developer who simply had the variable exported.
 *
 * The reason below names all THREE causes, because the badge alone distinguishes none of
 * them and the page's own note — which now names all three too — cannot say which one
 * happened: it describes the state, not this connection. That is the gap the reason
 * covers, and the case worth checking first is the base URL, because the app appends its
 * own v1/models, so a base that already carried a version segment asks for
 * /v1/v1/models, comes back 404, and leaves unverified a connection that would otherwise
 * have enriched fine. So a skip naming only the key and the model list would send the next
 * person to re-paste a credential that was never the problem. Cause (2) is also
 * deliberately generous, and it was priced: a valid key against a provider with no model
 * list at all lands there too.
 */
async function connectRealProvider(page) {
  await page.goto('/ServiceConnection/Index');
  await page.getByLabel('Base URL').fill(baseUrl);
  await page.getByLabel('API key').fill(apiKey);
  await page.getByLabel('Model (optional)').fill(model);
  await page.getByRole('button', { name: 'Save connection' }).click();
  await expect(page.getByText('Saved connection')).toBeVisible();

  const badge = page.locator('#connection-state .badge');
  await expect(badge).toBeVisible();
  test.skip((await badge.innerText()).trim() !== 'verified',
    `Skipping — the app's probe did not confirm the connection to ${baseUrl}, so these tests `
    + `cannot say anything about the code. Three causes produce this badge and only the first `
    + `is a credential problem: (1) the key was not accepted; (2) the provider does not serve a `
    + `model list at the path the app asks for; (3) nothing is listening on that host. Cause (2) `
    + `includes a base URL that already ends in a version segment — the app appends its own `
    + `v1/models, so it asked for ${baseUrl}/v1/models, and a /v1 on the end of the base makes `
    + `that a doubled path that 404s. Drop the /v1 and re-run. Re-pasting the key fixes neither.`);

  await expect(page.getByText('Could not confirm the connection.')).toHaveCount(0);
}

// The suite shares one active connection and the design allows at most one, so this file
// hands it back rather than leaving a developer's provider configured for the specs that
// run after it.
async function disconnect(page) {
  await page.goto('/ServiceConnection/Index');
  page.once('dialog', dialog => dialog.accept());
  await page.getByRole('button', { name: 'Disconnect' }).click();
  await expect(page.getByText('Connect a service')).toBeVisible();
}

test.describe('Supplement LLM Integration (Real API)', () => {
  // Serial, not a style choice: the two provider tests each connect, and there is one
  // connection row. Under fullyParallel they would demote each other's connection
  // mid-test and fail for a reason that has nothing to do with the provider.
  test.describe.configure({ mode: 'serial' });

  test('should extract nutrients from a real product URL using LLM', async ({ page }, testInfo) => {
    test.skip(missing.length > 0, skipWithoutProvider);

    test.setTimeout(180000);

    await connectRealProvider(page);
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click('text=Add New Supplement');

    await page.fill('input[name="Name"]', 'Children\'s Mindlinxr');
    await page.fill('input[name="Brand"]', 'BioCare');
    await page.fill('input[name="DailyDose"]', '1 scoop (5g)');
    await page.fill('input[name="ManufacturerUrl"]', 'https://www.biocare.co.uk/children-s-mindlinxr-multinutrient-150g');
    await page.fill('input[name="Cost"]', '29.99');
    await screenshot(page, testInfo, 'llm-create-form-filled');

    // Submit — this will trigger LLM enrichment (may take 5-30s), HTMX swaps nutrient editor inline
    await page.click('button#enrich-btn');

    // Wait for the nutrient editor to appear — the LLM call may take time
    await expect(page.locator('h4')).toContainText('Nutrients for', { timeout: 90000 });

    // The LLM must succeed — no error alerts allowed
    const errorAlert = page.locator('.alert-info, .alert-warning');
    await expect(errorAlert).toHaveCount(0, { timeout: 5000 });

    // Verify the nutrient editor appeared
    await expect(page.locator('#nutrients-table')).toBeVisible();
    await screenshot(page, testInfo, 'llm-nutrient-editor');

    // Verify nutrients were extracted by the LLM
    const nutrientCount = await page.locator('input[name^="nutrients["][name$="].GenericName"]').count();
    console.log(`LLM extracted ${nutrientCount} nutrients`);
    expect(nutrientCount).toBeGreaterThan(0);

    // Get the first nutrient name
    const firstGenericName = await page.locator('input[name="nutrients[0].GenericName"]').inputValue();
    console.log('First extracted nutrient:', firstGenericName);
    expect(firstGenericName.length).toBeGreaterThan(0);

    // Verify at least one nutrient name is meaningful (common supplement ingredient)
    let foundMeaningful = false;
    for (let i = 0; i < Math.min(nutrientCount, 10); i++) {
      const name = await page.locator(`input[name="nutrients[${i}].GenericName"]`).inputValue();
      if (/vitamin|zinc|iron|calcium|magnesium|selenium|iodine|chromium|copper|manganese|potassium/i.test(name)) {
        foundMeaningful = true;
        console.log(`Found meaningful nutrient at index ${i}: ${name}`);
        break;
      }
    }
    expect(foundMeaningful).toBeTruthy();
    await screenshot(page, testInfo, 'llm-extracted-nutrients');

    // Navigate back to supplements list
    await page.click('a:has-text("Done")');
    await expect(page.locator('h2')).toHaveText('Supplements', { timeout: 15000 });

    // Verify the supplement was saved
    const savedRow = page.locator('tr:has-text("Children\'s Mindlinxr")').last();
    await expect(savedRow).toBeVisible({ timeout: 10000 });

    // Verify the supplement details appear in the list
    await expect(savedRow).toContainText('BioCare');
    await expect(savedRow).toContainText('29.99');

    // Check the nutrients were saved
    await savedRow.locator('text=Nutrients').click();
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);

    const savedRows = page.locator('table tbody tr');
    const savedRowCount = await savedRows.count();
    console.log(`Saved nutrient rows: ${savedRowCount}`);
    expect(savedRowCount).toBeGreaterThan(0);
    await screenshot(page, testInfo, 'llm-nutrients-saved');

    await disconnect(page);
  });

  test('should allow manual nutrient entry when LLM fails', async ({ page }, testInfo) => {
    test.setTimeout(60000);

    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click('text=Add New Supplement');

    const unique = Date.now();
    const suppName = `ManualEntry${unique}`;
    await page.fill('input[name="Name"]', suppName);
    await page.fill('input[name="Brand"]', 'TestBrand');
    await page.fill('input[name="DailyDose"]', '1 capsule');

    // No ManufacturerUrl — LLM enrichment is skipped, no API call made
    await screenshot(page, testInfo, 'manual-create-form');

    await page.click('button#enrich-btn');

    // Nutrient editor should appear inline via HTMX
    await expect(page.locator('h4')).toContainText('Nutrients for');
    await expect(page.locator('#nutrients-table')).toBeVisible();

    // No LLM nutrients — should show empty nutrient section with add button
    const hasLlmNutrients = await page.locator('input[name="nutrients[0].GenericName"]').count() > 0;
    expect(hasLlmNutrients).toBeFalsy();

    // Add a nutrient manually
    await page.click('button#add-nutrient-row');
    await page.locator('input[name="nutrients[0].GenericName"]').fill('Magnesium');
    await page.locator('input[name="nutrients[0].SpecificForm"]').fill('Magnesium Citrate');
    await page.locator('input[name="nutrients[0].Dosage"]').fill('100mg');
    await screenshot(page, testInfo, 'manual-nutrient-added');

    // Save nutrients via HTMX
    await page.click('button:has-text("Save Changes")');

    // Should show success message
    await expect(page.locator('.alert-success')).toContainText('Nutrients saved successfully');
    await screenshot(page, testInfo, 'manual-nutrient-saved');

    // Navigate back to supplements list
    await page.click('a:has-text("Done")');
    await expect(page.locator('h2')).toHaveText('Supplements', { timeout: 15000 });

    // Verify supplement saved
    const savedRow = page.locator(`table tbody tr:has-text("${suppName}")`).last();
    await expect(savedRow).toBeVisible({ timeout: 10000 });

    // Verify nutrient saved
    await savedRow.locator('text=Nutrients').click();
    await expect(page.locator('h2')).toHaveText(/Nutrients for/);
    await expect(page.locator('table tbody tr:has-text("Magnesium")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("Magnesium Citrate")')).toBeVisible();
    await expect(page.locator('table tbody tr:has-text("100mg")')).toBeVisible();
    await screenshot(page, testInfo, 'manual-nutrient-verified');
  });

  test('should extract blend with 6 sub-nutrients from G.I. Detox URL', async ({ page }, testInfo) => {
    test.skip(missing.length > 0, skipWithoutProvider);

    test.setTimeout(180000);

    await connectRealProvider(page);
    await page.goto('/Supplement');
    await expect(page.locator('table tbody tr').first()).toBeVisible();
    await page.click('text=Add New Supplement');

    await page.fill('input[name="Name"]', 'G.I. Detox');
    await page.fill('input[name="Brand"]', 'Biocidin Botanicals');
    await page.fill('input[name="DailyDose"]', '1 capsule');
    await page.fill('input[name="ManufacturerUrl"]', 'https://supplementhub.co.uk/products/gi-detox-60-capsules-biocidin-botanicals');
    await page.fill('input[name="Cost"]', '29.99');
    await screenshot(page, testInfo, 'gi-detox-create-form');

    // Click Enrich button to trigger LLM extraction
    await page.click('button#enrich-btn');

    // Wait for the nutrient editor to appear — the LLM call may take time
    await expect(page.locator('h4')).toContainText('Nutrients for', { timeout: 90000 });

    // The LLM must succeed — no error alerts allowed
    const errorAlert = page.locator('.alert-info, .alert-warning');
    await expect(errorAlert).toHaveCount(0, { timeout: 5000 });

    // Verify the nutrient editor appeared
    await expect(page.locator('#nutrients-table')).toBeVisible();
    await screenshot(page, testInfo, 'gi-detox-nutrient-editor');

    // Verify a blend parent row exists (look for "Proprietary Blend" or similar)
    const blendRows = page.locator('tr:has(.add-sub-nutrient)');
    const blendCount = await blendRows.count();
    console.log(`Found ${blendCount} blend parent rows`);
    expect(blendCount).toBeGreaterThan(0);

    // Get the first blend row
    const firstBlend = blendRows.first();
    const blendName = await firstBlend.locator('input[name$=".GenericName"]').inputValue();
    console.log('Blend name:', blendName);

    // Verify the blend has 6 child rows (sub-nutrients)
    const childRows = page.locator('tr.blend-child');
    const childCount = await childRows.count();
    console.log(`Found ${childCount} child nutrient rows`);
    expect(childCount).toBe(6);

    // Verify each child has a GenericName
    for (let i = 0; i < childCount; i++) {
      const childName = await childRows.nth(i).locator('input[name$=".GenericName"]').inputValue();
      console.log(`Child ${i + 1}:`, childName);
      expect(childName.length).toBeGreaterThan(0);
    }

    await screenshot(page, testInfo, 'gi-detox-blend-with-children');

    await disconnect(page);
  });
});
