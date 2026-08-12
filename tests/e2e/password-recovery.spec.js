const { test, expect } = require("@playwright/test");
const { completeInitialSetup } = require("./helpers/e2e-helpers");

test("password recovery stays private and responsive", async ({ page }) => {
  await completeInitialSetup(page);
  await page.context().clearCookies();
  await page.goto("/Account/Login");

  const recoveryLink = page.getByRole("link", { name: "نسيت كلمة المرور؟" });
  await expect(recoveryLink).toBeVisible();
  await recoveryLink.click();
  await expect(page.getByRole("heading", { name: "نسيت كلمة المرور؟" })).toBeVisible();

  await page.locator('[name="AccountIdentifier"]').fill("missing@example.test");
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByRole("heading", { name: "تحقق من بريدك" })).toBeVisible();
  await expect(page.locator(".login-page-subtitle")).toContainText(
    "راجع بريدك الإلكتروني"
  );
  await expect(page.locator(".login-page-subtitle")).not.toContainText("رابط");

  await page.goto("/Account/ResetPassword?token=invalid-test-token");
  await expect(page.getByRole("heading", { name: "تعذر استخدام الرابط" })).toBeVisible();
  await expect(page.getByRole("link", { name: "طلب رابط جديد" })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/Account/ForgotPassword?tenant=default");
  await expect(page.getByRole("heading", { name: "نسيت كلمة المرور؟" })).toBeVisible();
  const horizontalOverflow = await page.evaluate(
    () => document.documentElement.scrollWidth > document.documentElement.clientWidth
  );
  expect(horizontalOverflow).toBe(false);
});
