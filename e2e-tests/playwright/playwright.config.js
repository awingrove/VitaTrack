// playwright.config.js
const { defineConfig, devices } = require('@playwright/test');

// E2E_PORT lets the suite run on another port (e.g., while a dev server holds 5000).
const e2eBaseUrl = `http://localhost:${process.env.E2E_PORT || 5000}`;

module.exports = defineConfig({
  testDir: './tests',
  timeout: 30 * 1000,
  expect: { timeout: 5000 },
  // Tests run in parallel against a single shared in-memory SQLite database.
  // Concurrent writes can occasionally cause flaky failures ("database is locked").
  // In CI, consider setting workers: 1 to eliminate this.
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',

  // Clean the test database before each test run
  globalSetup: require.resolve('./global-setup'),

  use: {
    actionTimeout: 0,
    baseURL: e2eBaseUrl,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  webServer: {
    command: `dotnet run --environment Test --urls ${e2eBaseUrl}`,
    cwd: '../../VitaTrack.Web',
    url: e2eBaseUrl,
    timeout: 120 * 1000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Test',
    },
  },
});