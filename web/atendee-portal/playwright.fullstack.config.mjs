import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/e2e",
  testMatch: "fullstack.spec.mjs",
  fullyParallel: false,
  forbidOnly: true,
  retries: 0,
  reporter: "line",
  use: {
    baseURL: "http://localhost:4176",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    ...devices["Desktop Chrome"]
  },
  webServer: {
    command: "npm run dev:server",
    url: "http://localhost:4176",
    env: {
      PORT: "4176",
      ATENDEE_API_PROXY_TARGET: process.env.ATENDEE_API_PROXY_TARGET ?? ""
    },
    reuseExistingServer: false,
    timeout: 120_000
  }
});
