const { test, expect } = require("@playwright/test");
const {
  ensureTenantManagerSignedIn,
  createVisitorPermit,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

test("tenant notification center supports filtering, read state, dismissal, and settings", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);

  const driverName = `تنبيه تصريح ${uniqueSuffix()}`;
  await createVisitorPermit(page, { driverName });

  await page.goto("/Notifications?state=unread");
  await expect(page.getByRole("heading", { name: "مركز الإشعارات" })).toBeVisible();
  const row = page.locator(".notification-row", { hasText: driverName }).first();
  await expect(row).toBeVisible();
  await expect(row).toHaveClass(/is-unread/);

  await row.getByRole("button", { name: "تمت القراءة" }).click();
  await expect(page).toHaveURL(/state=unread/i);
  await expect(page.locator(".notification-row", { hasText: driverName })).toHaveCount(0);

  await page.goto("/Notifications?category=permit&state=read");
  const readRow = page.locator(".notification-row", { hasText: driverName }).first();
  await expect(readRow).toBeVisible();
  await readRow.getByRole("button", { name: "إخفاء الإشعار" }).click();
  await expect(page.locator(".notification-row", { hasText: driverName })).toHaveCount(0);

  await page.goto("/Notifications/Settings");
  await expect(page.getByRole("heading", { name: "الإشعارات والاحتفاظ" })).toBeVisible();
  await page.locator('[name="NotificationRetentionDays"]').fill("120");
  await page.locator('[name="EmailOutboxRetentionDays"]').fill("45");
  await page.locator('[name="AuditLogRetentionDays"]').fill("540");
  await page.getByRole("button", { name: "حفظ الإعدادات" }).click();
  await expect(page.getByText("تم حفظ إعدادات الإشعارات والاحتفاظ بالبيانات.")).toBeVisible();
  await expect(page.locator('[name="NotificationRetentionDays"]')).toHaveValue("120");
  await expect(page.locator('[name="EmailOutboxRetentionDays"]')).toHaveValue("45");
  await expect(page.locator('[name="AuditLogRetentionDays"]')).toHaveValue("540");

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/Notifications");
  const centerLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    pageWidth: document.documentElement.scrollWidth,
    filterWidth: document.querySelector(".notification-filters")?.getBoundingClientRect().width ?? 0,
  }));
  expect(centerLayout.pageWidth).toBeLessThanOrEqual(centerLayout.viewportWidth + 1);
  expect(centerLayout.filterWidth).toBeLessThanOrEqual(centerLayout.viewportWidth);

  await page.goto("/Notifications/Settings");
  const settingsLayout = await page.evaluate(() => ({
    viewportWidth: window.innerWidth,
    pageWidth: document.documentElement.scrollWidth,
    columns: getComputedStyle(document.querySelector(".notification-toggle-grid")).gridTemplateColumns,
  }));
  expect(settingsLayout.pageWidth).toBeLessThanOrEqual(settingsLayout.viewportWidth + 1);
  expect(settingsLayout.columns.trim().split(/\s+/)).toHaveLength(1);
});

test("notification mutation endpoints require authentication", async ({ page }) => {
  const response = await page.request.post("/Notifications/MarkRead", {
    form: { id: "1" },
    maxRedirects: 0,
  });
  expect([302, 401, 403]).toContain(response.status());
});
