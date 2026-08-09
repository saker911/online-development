const { test, expect } = require("@playwright/test");
const { completeInitialSetup } = require("./helpers/e2e-helpers");

test.describe("Landing product visual", () => {
  test.beforeEach(async ({ page }) => {
    await completeInitialSetup(page);
    await page.context().clearCookies();
  });

  test("shows the product ecosystem scene in light and dark themes", async ({ page }) => {
    await page.goto("/");

    const visual = page.locator(".public-landing-product-visual");
    const scene = visual.locator(".product-ecosystem-scene");
    const monitor = scene.locator(".product-scene-monitor");
    const phone = scene.locator(".product-scene-phone");

    await expect(visual).toBeVisible();
    await expect(scene).toBeVisible();
    await expect(monitor).toBeVisible();
    await expect(phone).toBeVisible();

    const desktopSizing = await page.evaluate(() => ({
      visualWidth: document
        .querySelector(".public-landing-product-visual")
        ?.getBoundingClientRect().width,
      titleFontSize: Number.parseFloat(
        getComputedStyle(document.querySelector(".public-landing-copy h1")).fontSize
      ),
    }));

    expect(desktopSizing.visualWidth).toBeLessThanOrEqual(620);
    expect(desktopSizing.titleFontSize).toBeLessThanOrEqual(46);

    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));

    await expect(scene).toBeVisible();
    await expect(monitor).toBeVisible();
    await expect(phone).toBeVisible();
  });

  test("fits the mobile viewport without horizontal overflow", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/");

    const visual = page.locator(".public-landing-product-visual");
    await expect(visual).toBeVisible();
    const mobileSizing = await page.evaluate(() => ({
      hasOverflow:
        document.documentElement.scrollWidth > document.documentElement.clientWidth,
      visualWidth: document
        .querySelector(".public-landing-product-visual")
        ?.getBoundingClientRect().width,
      titleFontSize: Number.parseFloat(
        getComputedStyle(document.querySelector(".public-landing-copy h1")).fontSize
      ),
    }));

    expect(mobileSizing.hasOverflow).toBe(false);
    expect(mobileSizing.visualWidth).toBeLessThanOrEqual(350);
    expect(mobileSizing.titleFontSize).toBeLessThanOrEqual(34);
  });
});
