const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

test.beforeEach(async ({ page }) => {
  await ensureOwnerSignedIn(page);
});

test("owner can publish a timed offer and see it in the public plans", async ({ page }) => {
  await page.goto("/Pricing");

  await expect(page.getByRole("heading", { name: "الأسعار والعروض" })).toBeVisible();
  await expect(page.locator(".pricing-plan-editor")).toHaveCount(3);

  const originalPrice = page.locator('[name="Plans[0].OriginalPrice"]');
  const offerLabel = page.locator('[name="Plans[0].OfferLabel"]');
  await originalPrice.fill("59");
  await offerLabel.fill("عرض اختبار النشر");

  await page.getByRole("button", { name: "حفظ ونشر الأسعار" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByText("تم تحديث الأسعار والعروض ونشرها في صفحات الاشتراك.")).toBeVisible();

  await page.goto("/Subscription/Plans");
  const firstPlan = page.locator(".subscription-plan-card").first();
  await expect(firstPlan.getByText("عرض اختبار النشر")).toBeVisible();
  await expect(firstPlan.locator(".subscription-plan-price del")).toContainText("59");

  await page.goto("/Pricing");
  await page.locator('[name="Plans[0].OriginalPrice"]').fill("");
  await page.locator('[name="Plans[0].OfferLabel"]').fill("");
  await page.getByRole("button", { name: "حفظ ونشر الأسعار" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByText("تم تحديث الأسعار والعروض ونشرها في صفحات الاشتراك.")).toBeVisible();
});

for (const viewport of [
  { name: "desktop", width: 1440, height: 900 },
  { name: "mobile", width: 390, height: 844 },
]) {
  test(`pricing editor stays orderly on ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.goto("/Pricing");

    await expect(page.locator(".pricing-plan-editor")).toHaveCount(3);
    await expect(page.locator("[data-date-field]")).toHaveCount(6);
    await expect(page.locator("[data-date-hijri-switch]")).toHaveCount(6);
    await expect(page.locator("[data-duration-preview]").first()).not.toBeEmpty();
    const dimensions = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }));

    expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth + 1);
  });
}
