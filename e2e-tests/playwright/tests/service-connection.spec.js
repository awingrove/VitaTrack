const { test, expect } = require('@playwright/test');
const { screenshot } = require('../helpers/screenshot');

// The tracer proves the round trip: a connection saved through the Settings page is
// still there on a fresh page load, read back out of SQLite. Nothing here talks to an
// AI service — there is no probe yet, so no double is needed to stand in for one.
//
// Serial, and not a style choice: there is at most one active connection by design,
// so these two tests contend on a single row. Under fullyParallel they would demote
// each other's connection and fail for a reason that has nothing to do with the code.
test.describe.serial('Service Connection', () => {
  test('should save a connection from the Settings page and show it after reload', async ({ page }, testInfo) => {
    // Arrive by clicking, not by goto: the nav item is the entry point under test.
    await page.goto('/');
    await page.getByRole('link', { name: 'Settings' }).click();
    await expect(page).toHaveURL(/\/ServiceConnection(\/Index)?$/);
    await expect(page.locator('h2')).toHaveText('Settings');
    await screenshot(page, testInfo, 'service-connection-empty');

    const baseUrl = 'https://api.example.com';
    const apiKey = 'sk-tracer-abcd1234';

    await page.getByLabel('BaseUrl').fill(baseUrl);
    await page.getByLabel('ApiKey').fill(apiKey);
    await page.getByLabel('Model (optional)').fill('gpt-4o-mini');
    await page.getByRole('button', { name: 'Save connection' }).click();

    // The redirect lands back on Index with the saved state and a masked key.
    await expect(page).toHaveURL(/\/ServiceConnection(\/Index)?$/);
    await expect(page.getByText('Saved connection')).toBeVisible();
    await expect(page.getByText(baseUrl, { exact: true })).toBeVisible();
    await expect(page.getByText('••••1234', { exact: true })).toBeVisible();
    await expect(page.getByText('unverified', { exact: true })).toBeVisible();
    await expect(page.getByText(apiKey, { exact: false })).toHaveCount(0);
    await screenshot(page, testInfo, 'service-connection-saved');

    // The round trip the tracer exists to prove: it survived a fresh read of the DB.
    await page.reload();
    await expect(page.getByText('Saved connection')).toBeVisible();
    await expect(page.getByText(baseUrl, { exact: true })).toBeVisible();
    await expect(page.getByText('••••1234', { exact: true })).toBeVisible();
    // The key field is a password input and is never prefilled, even after a save.
    await expect(page.getByLabel('ApiKey')).toHaveValue('');
    // The saved values prefill the form, so a reconnect does not retype them.
    await expect(page.getByLabel('BaseUrl')).toHaveValue(baseUrl);
    await expect(page.getByLabel('Model (optional)')).toHaveValue('gpt-4o-mini');
  });

  test('should reject a save with a blank URL and keep the connection unchanged', async ({ page }, testInfo) => {
    await page.goto('/ServiceConnection/Index');

    // A save of the valid values first, so the rejection has something to leave alone.
    const baseUrl = 'https://api.rejected.example.com';
    await page.getByLabel('BaseUrl').fill(baseUrl);
    await page.getByLabel('ApiKey').fill('sk-reject-abcd9999');
    await page.getByRole('button', { name: 'Save connection' }).click();
    await expect(page.getByText(baseUrl, { exact: true })).toBeVisible();

    // Clearing the URL and resubmitting must not blank the stored connection.
    await page.getByLabel('BaseUrl').fill('');
    await page.getByLabel('ApiKey').fill('sk-reject-abcd9999');
    await page.getByRole('button', { name: 'Save connection' }).click();

    await expect(page.getByText('Base URL is required.')).toBeVisible();
    await expect(page.getByText(baseUrl, { exact: true })).toBeVisible();
    await screenshot(page, testInfo, 'service-connection-rejected');

    await page.reload();
    await expect(page.getByText(baseUrl, { exact: true })).toBeVisible();
  });
});
