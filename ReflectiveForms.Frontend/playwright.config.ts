import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for E2E testing ReflectiveForms
 * See: https://playwright.dev/docs/test-configuration
 */
export default defineConfig({
  testDir: './e2e',

  // Run tests in parallel
  fullyParallel: true,

  // Fail the build on CI if you accidentally left test.only in the source code
  forbidOnly: !!process.env.CI,

  // Retry on CI only
  retries: process.env.CI ? 2 : 0,

  // Tests share a single backend database so they must run serially to avoid
  // cross-file deleteAll interference.
  workers: 1,

  // Reporter to use
  reporter: [
    ['html', { open: 'never' }],
    ['list']
  ],

  // Shared settings for all projects
  use: {
    // Base URL for navigation — must match vite dev server port
    baseURL: 'http://localhost:3000',

    // Collect trace when retrying the failed test
    trace: 'on-first-retry',

    // Take screenshots on failure
    screenshot: 'only-on-failure',

    // Video recording
    video: 'retain-on-failure',

    // Increase default timeout for auto-save debounce
    actionTimeout: 15000,
  },

  // Global timeout per test (generous for auto-save round-trips)
  timeout: 60000,

  // Configure projects for major browsers
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
    },
    // Mobile viewports
    {
      name: 'Mobile Chrome',
      use: { ...devices['Pixel 5'] },
    },
  ],

  // Run your local dev server before starting the tests
  webServer: [
    {
      // Start the .NET backend (Sample1)
      // On CI the solution is already built in Release mode — skip rebuild
      command: process.env.CI
        ? 'cd ../ReflectiveForms.Sample1 && dotnet run --configuration Release --no-build'
        : 'cd ../ReflectiveForms.Sample1 && dotnet run',
      url: 'http://localhost:9000',
      reuseExistingServer: !process.env.CI,
      // AI model loading can be slow on CI runners
      timeout: (process.env.CI ? 180 : 120) * 1000,
    },
    {
      // Start the React frontend
      // On CI, route API calls through Vite's proxy to avoid cross-origin
      // cookie/CORS issues in headless Chromium
      command: process.env.CI
        ? 'VITE_API_BASE_URL=/rf/api npm run dev'
        : 'npm run dev',
      url: 'http://localhost:3000',
      reuseExistingServer: !process.env.CI,
      timeout: 120 * 1000,
    },
  ],
});
