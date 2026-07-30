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

  // Clean the test database before each test run
  globalSetup: require.resolve('./global-setup'),

  use: {
    actionTimeout: 0,
    baseURL: 'http://localhost:5000',
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
    command: 'dotnet run --urls http://localhost:5000',
    cwd: '../../VitaTrack.Web',
    url: 'http://localhost:5000',
    timeout: 120 * 1000,
    reuseExistingServer: false,
    env: {
      ASPNETCORE_ENVIRONMENT: 'Test',
    },
  },
});