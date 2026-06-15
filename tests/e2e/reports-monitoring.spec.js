const { expect, test } = require("@playwright/test");
const {
  createVisitorPermit,
  downloadPdfAndAssert,
  ensureOwnerSignedIn,
} = require("./helpers/e2e-helpers");

test("reports index, permits report, PDF, and user activity report open", async ({ page }) => {
  await createVisitorPermit(page);

  await page.goto("/Reports");
  await expect(page.getByRole("heading", { name: "اختر نوع التقرير" })).toBeVisible();

  await page.goto("/Reports/Permits");
  await expect(page.getByRole("heading", { name: "تقارير تصاريح المركبات" })).toBeVisible();
  await downloadPdfAndAssert(page, page.getByRole("link", { name: "طباعة التقرير" }));

  await page.goto("/Users/ActivityLog");
  await expect(page.getByRole("heading", { name: /سجل عمليات المستخدمين/ })).toBeVisible();
});

test("monitoring page ranges and snapshot endpoint work", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  for (const range of ["today", "7d", "30d"]) {
    await page.goto(`/Monitoring?range=${range}`);
    await expect(page.getByRole("heading", { name: /المراقبة|لوحة المتابعة|Monitoring/ })).toBeVisible();
  }

  const response = await page.goto("/Monitoring/Snapshot?range=today");
  expect(response?.ok()).toBeTruthy();
  await expect(page.locator("[data-monitoring-dashboard]")).toBeVisible();
});
