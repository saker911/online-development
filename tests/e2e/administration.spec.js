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
  await page.locator('[name="LateReturnGraceMinutes"]').fill("7");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();
  await expect(page.locator('[name="AttendanceGraceMinutes"]')).toHaveValue("12");
  await expect(page.locator('[name="WorkEndExitGraceMinutes"]')).toHaveValue("35");
  await expect(page.locator('[name="LateReturnGraceMinutes"]')).toHaveValue("7");

  await page.goto("/Administration/DisplaySettings");
  await expect(page.getByRole("heading", { name: "شاشات العرض" })).toBeVisible();
});

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

test("selected organization name and logo appear across the application shell", async ({ page }) => {
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
    /\/uploads\/administration\/logo-.*\.png/i
  );

  await page.context().clearCookies();
  await page.goto("/Account/Login");
  await expect(page.locator(".login-page-brand-name")).toHaveText(organizationName);
  await expect(page.locator(".login-page-logo")).toHaveAttribute(
    "src",
    /\/uploads\/administration\/logo-.*\.png/i
  );

  await signIn(page);
  await page.goto("/Administration/Edit");
  await page.locator('input[name="removeLogo"]').check();
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();

  await page.context().clearCookies();
  await page.goto("/Account/Login");
  await expect(page.locator(".login-page-brand-name")).toHaveText(organizationName);
  await expect(page.locator(".login-page-logo")).toHaveAttribute(
    "src",
    /\/images\/organization-placeholder\.svg/i
  );
});
