const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

async function expectNoHorizontalOverflow(page) {
  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
}

test.beforeEach(async ({ page }) => {
  await ensureOwnerSignedIn(page);
});

for (const viewport of [
  { name: "desktop", width: 1440, height: 900 },
  { name: "mobile", width: 390, height: 844 },
]) {
  test(`establishment page follows the public shell on ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.goto("/establishment");

    await expect(page.getByRole("heading", { name: "بيانات المنشأة والتواصل" })).toBeVisible();
    await expect(page.locator(".establishment-page-shell")).toBeVisible();
    await expectNoHorizontalOverflow(page);

    const shellHeight = await page.locator(".establishment-page-shell").evaluate(
      (element) => element.getBoundingClientRect().height
    );
    expect(shellHeight).toBeGreaterThanOrEqual(viewport.height - 1);
  });

  test(`platform settings stay aligned on ${viewport.name}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await page.goto("/PlatformSettings");

    await expect(page.getByRole("heading", { name: "بيانات المنشأة والتواصل" })).toBeVisible();
    await expect(page.locator(".platform-settings-section")).toHaveCount(3);
    await expectNoHorizontalOverflow(page);
  });
}
