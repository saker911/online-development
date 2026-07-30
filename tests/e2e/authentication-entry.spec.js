const { test, expect } = require("@playwright/test");

test("public authentication offers Google trial and no Microsoft option", async ({ page }) => {
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
});
