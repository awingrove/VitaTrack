// playwright.config.js
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests',
  timeout: 30 * 1000,
  expect: { timeout: 5000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',

  // Clean the test database before running tests
  globalSetup: require.resolve('./global-setup'),

  use: {
    actionTimeout: 0,
    baseURL: 'http://localhost:5000',
    trace: 'on-first-retry',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  /* Run your local dev server before starting the tests.
     Uses a separate test database so the real app DB is never touched. */
  webServer: {
    command: 'dotnet run',
    cwd: '../../VitaTrack.Web',
    port: 5000,
    timeout: 120 * 1000,
    reuseExistingServer: !process.env.CI,
    env: {
      // Override connection string to use a dedicated test database
      'ConnectionStrings__Default': 'Data Source=VitaTrack.Test.db',
    },
  },
});