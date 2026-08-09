const { expect, test } = require("@playwright/test");
const {
  createTestPngBuffer,
  ensureOwnerSignedIn,
  signIn,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

test("owner updates administration data and opens display settings", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  const suffix = uniqueSuffix();

  await page.goto("/Administration/Edit");
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();
  await page.locator('[name="OrganizationName"]').fill(`جهة قبول ${suffix}`);
  await page.locator('[name="DepartmentName"]').fill(`إدارة قبول ${suffix}`);
  await page.locator('[name="SignatureText"]').fill("توقيع قبول E2E");
  await page.locator('[name="AttendanceGraceMinutes"]').fill("12");
  await page.locator('[name="WorkEndExitGraceMinutes"]').fill("35");
  await page.locator('input[type="checkbox"][name="LeaveRequestsEnabled"]').check();
  await page.locator('[name="LateReturnGraceMinutes"]').fill("7");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();
  await expect(page.locator('[name="AttendanceGraceMinutes"]')).toHaveValue("12");
  await expect(page.locator('[name="WorkEndExitGraceMinutes"]')).toHaveValue("35");
  await expect(page.locator('input[type="checkbox"][name="LeaveRequestsEnabled"]')).toBeChecked();
  await expect(page.locator('[name="LateReturnGraceMinutes"]')).toHaveValue("7");

  await page.goto("/Administration/DisplaySettings");
  await expect(page.getByRole("heading", { name: "شاشات العرض" })).toBeVisible();
  await expect(page.getByRole("link", { name: "معاينة لوحة الانتظار" })).toBeVisible();
  expect(await page.getByRole("columnheader", { name: "الوضع" }).count()).toBeGreaterThan(0);
});

for (const viewport of [
  { name: "desktop", width: 1440, height: 900 },
  { name: "mobile", width: 390, height: 844 },
]) {
  test(`waiting board is privacy-safe and responsive on ${viewport.name}`, async ({ page }) => {
    await ensureOwnerSignedIn(page);
    await page.setViewportSize(viewport);
    await page.goto("/Display/WaitingBoard");

    await expect(page.getByRole("heading", { name: "بانتظار الوصول" })).toBeVisible();
    await expect(page.getByText("تُعرض أرقام المتابعة فقط لحماية خصوصية الزوار.")).toBeVisible();
    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }));
    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth + 1);
  });
}

test("backup page creates and downloads backup, restore rejects invalid upload", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Administration/Backup");
  await expect(page.getByRole("heading", { name: "النسخ الاحتياطي" })).toBeVisible();
  await page.getByRole("button", { name: "إنشاء نسخة احتياطية" }).click();
  await expect(page.getByRole("alert").getByText(/تم إنشاء النسخة الاحتياطية/)).toBeVisible();

  const downloadLink = page.locator('a[href*="DownloadBackup"]').first();
  await expect(downloadLink).toBeVisible();
  const downloadPromise = page.waitForEvent("download");
  await downloadLink.click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/\.zip$/i);

  page.once("dialog", (dialog) => dialog.accept());
  await page.locator('input[name="backupFile"]').setInputFiles({
    name: "invalid-backup.zip",
    mimeType: "application/zip",
    buffer: Buffer.from("not a zip"),
  });
  await page.getByRole("button", { name: "استعادة النسخة" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByRole("alert").or(page.locator(".validation-summary-errors")).getByText(/رفض|غير صالح|تعذر|فشل|آمن/).first()).toBeVisible();
});

test("administration rejects an image whose declared type does not match its content", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Administration/Edit");
  await page.locator('input[name="logoFile"]').setInputFiles({
    name: "disguised-logo.jpg",
    mimeType: "image/jpeg",
    buffer: createTestPngBuffer(15, 118, 110),
  });
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();

  await expect(page).toHaveURL(/\/Administration\/Edit/i);
  await expect(page.locator(".form-summary-only")).toContainText(/لا يطابق|محتوى الصورة/);
});

test("organization name remains contextual while the product mark stays consistent", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  const organizationName = `شركة تشغيل ${uniqueSuffix()}`;

  await page.goto("/Administration/Edit");
  await page.locator('[name="OrganizationName"]').fill(organizationName);
  await page.locator('input[name="logoFile"]').setInputFiles({
    name: "organization-logo.png",
    mimeType: "image/png",
    buffer: createTestPngBuffer(37, 99, 235),
  });
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();

  await expect(page.locator(".app-brand-copy").getByText(organizationName)).toBeVisible();
  await expect(page.locator(".app-brand-mark img")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
  await expect(page.locator(".app-brand-mark img")).toHaveCSS("filter", "none");
  await expect(page.locator(".app-brand-mark img")).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
  await expect(page.locator(".administration-media-preview img")).toHaveAttribute(
    "src",
    /\/uploads\/administration\/logo-.*\.png/i
  );

  await page.context().clearCookies();
  await page.goto("/Account/Login");
  await expect(page.locator(".login-page-topbar-brand small")).toHaveText(organizationName);
  await expect(page.locator(".login-page-topbar-brand img")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
  await expect(page.locator(".login-page-topbar-brand img")).toHaveCSS("filter", "none");

  await signIn(page);
  await page.goto("/Administration/Edit");
  await page.locator('input[name="removeLogo"]').check();
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();

  await page.context().clearCookies();
  await page.goto("/Account/Login");
  await expect(page.locator(".login-page-topbar-brand small")).toHaveText(organizationName);
  await expect(page.locator(".login-page-topbar-brand img")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
});
