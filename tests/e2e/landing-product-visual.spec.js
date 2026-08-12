const { test, expect } = require("@playwright/test");
const { completeInitialSetup } = require("./helpers/e2e-helpers");

test.describe("Landing product visual", () => {
  test.beforeEach(async ({ page }) => {
    await completeInitialSetup(page);
    await page.context().clearCookies();
  });

  test("shows the approved interactive product image on the official landing page", async ({ page }) => {
    await page.goto("/");

    const hero = page.locator(".public-landing-hero");
    const visual = page.locator(".public-landing-product-visual");
    const stage = visual.locator(".landing-hero-visual-stage");
    const image = visual.locator(".landing-hero-visual-image");
    const scanBeam = visual.locator(".landing-hero-scan-beam");

    await expect(hero).toBeVisible();
    await expect(visual).toBeVisible();
    await expect(stage).toBeVisible();
    await expect(image).toBeVisible();
    await expect(scanBeam).toBeVisible();

    await expect.poll(() => image.evaluate((node) => ({
      complete: node.complete,
      width: node.naturalWidth,
      height: node.naturalHeight,
    }))).toEqual({ complete: true, width: 1672, height: 941 });

    const desktopSizing = await page.evaluate(() => ({
      viewportWidth: document.documentElement.clientWidth,
      heroWidth: document.querySelector(".public-landing-hero")?.getBoundingClientRect().width,
      visualWidth: document
        .querySelector(".public-landing-product-visual")
        ?.getBoundingClientRect().width,
      titleFontSize: Number.parseFloat(
        getComputedStyle(document.querySelector(".public-landing-copy h1")).fontSize
      ),
    }));

    expect(Math.abs(desktopSizing.heroWidth - desktopSizing.viewportWidth)).toBeLessThanOrEqual(1);
    expect(Math.abs(desktopSizing.visualWidth - desktopSizing.viewportWidth)).toBeLessThanOrEqual(1);
    expect(desktopSizing.titleFontSize).toBeLessThanOrEqual(47);

    await page.mouse.move(180, 320);
    await expect.poll(() => stage.evaluate((node) =>
      getComputedStyle(node).getPropertyValue("--hero-shift-x").trim()
    )).not.toBe("0px");

    await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));

    await expect(stage).toBeVisible();
    await expect(image).toBeVisible();
  });

  test("fits the mobile viewport without horizontal overflow", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/");

    const visual = page.locator(".public-landing-product-visual");
    await expect(visual).toBeVisible();
    await expect(visual.locator(".landing-hero-visual-image")).toBeVisible();
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
    expect(mobileSizing.visualWidth).toBeLessThanOrEqual(390);
    expect(mobileSizing.titleFontSize).toBeLessThanOrEqual(36);
  });
});
