import { defineConfig } from '@playwright/test';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

// End-to-end tests against the real app on a throw-away SQLite file (auto-migrated and seeded on start: teacher@demo.test,
// board demo-board / join code DEMO01). `npm run build` must have filled ../wwwroot. Needs Chromium: `npx playwright install chromium`.
const port = process.env.E2E_PORT || '5058';
const db = join(tmpdir(), `beecoding-e2e-${process.pid}.db`);
export default defineConfig({
  testDir: 'e2e',
  workers: 1,
  use: { baseURL: `http://localhost:${port}`, viewport: { width: 1200, height: 800 } },
  webServer: {
    command: 'dotnet run --project .. --no-launch-profile',
    url: `http://localhost:${port}/api/auth/config`,
    timeout: 180_000,
    reuseExistingServer: !process.env.CI,
    env: { ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: `http://localhost:${port}`, ConnectionStrings__Default: `Data Source=${db}`},
  },
});
