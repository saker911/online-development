const { defineConfig, devices } = require("@playwright/test");
const fs = require("fs");
const os = require("os");
const path = require("path");

const e2eWorkDir = path.join(
  os.tmpdir(),
  `VehiclePermitSystemWeb-e2e-${Date.now()}-${Math.random().toString(16).slice(2)}`
);
const e2eStorageRoot = path.join(e2eWorkDir, "storage");
const e2eBaseUrl = "http://127.0.0.1:5617";
fs.mkdirSync(e2eWorkDir, { recursive: true });
fs.writeFileSync(path.join(e2eWorkDir, "VehiclePermitSystemWeb.csproj"), "<Project />");

module.exports = defineConfig({
  testDir: "./tests/e2e",
  timeout: 60_000,
  expect: {
    timeout: 10_000,
  },
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  use: {
    baseURL: e2eBaseUrl,
    acceptDownloads: true,
    trace: "retain-on-failure",
  },
  webServer: {
    command: `dotnet run --project "${path.join(__dirname, "VehiclePermitSystemWeb.csproj")}" --no-build --no-launch-profile`,
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      App__Urls: e2eBaseUrl,
      Data__Provider: "Sqlite",
      ConnectionStrings__SqliteConnection: "Data Source=vehicle-permit-system.db",
      DevelopmentUseLocalData: "true",
      OnlineEdition__PrivacyMinimized: "false",
      OnlineEdition__CollectNationalId: "true",
      Security__CookieSecurePolicy: "SameAsRequest",
      Security__LoginPermitLimit: "100",
      Security__LoginWindowSeconds: "1",
      VehiclePermitSystemWeb__StorageRoot: e2eStorageRoot,
    },
    url: e2eBaseUrl,
    timeout: 120_000,
    reuseExistingServer: false,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
