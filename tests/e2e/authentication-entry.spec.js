const { test, expect } = require("@playwright/test");
const { completeInitialSetup } = require("./helpers/e2e-helpers");

test("public authentication offers Google trial and no Microsoft option", async ({ page }) => {
  await completeInitialSetup(page);
  await page.context().clearCookies();
  await page.goto("/Account/Login");

  await expect(
    page.getByRole("link", { name: "المتابعة باستخدام Google" })
  ).toBeVisible();
  await expect(page.getByText("Microsoft", { exact: true })).toHaveCount(0);

  await page.goto("/Subscription/Register?plan=monthly");
  await expect(
    page.getByRole("link", { name: "المتابعة باستخدام Google" })
  ).toContainText("ابدأ تجربة يومين باستخدام Google");
  await expect(page.getByText("Microsoft", { exact: true })).toHaveCount(0);
  await expect(page.locator('[name="OwnerEmail"]')).toHaveAttribute("autocomplete", "email");
  await expect(page.locator('[name="Password"]')).toHaveAttribute("autocomplete", "new-password");
  await expect(page.locator('[name="ConfirmPassword"]')).toHaveAttribute("autocomplete", "new-password");

  await page.goto("/Display/Register");
  const themeToggle = page.getByRole("button", { name: /الوضع النهاري مفعّل/ });
  if (await themeToggle.isVisible()) {
    await themeToggle.click();
  }
  await expect(page.locator(".login-page-logo[data-product-mark]")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
  await expect(page.locator(".login-page-logo[data-product-mark]")).toHaveCSS("filter", "none");
});
