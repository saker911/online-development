const { expect, test } = require("@playwright/test");
const fs = require("fs");
const path = require("path");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

async function capturePlatform(page, fileName) {
  if (process.env.VISUAL_CAPTURE !== "1") {
    return;
  }

  const outputDirectory = path.join(process.cwd(), "output", "playwright");
  fs.mkdirSync(outputDirectory, { recursive: true });
  await page.screenshot({ path: path.join(outputDirectory, fileName), fullPage: true });
}

test("platform owner lands in an independent administration workspace", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("vps_theme", "dark"));
  await ensureOwnerSignedIn(page);
  await expect(page).toHaveURL(/\/Platform$/i);
  await expect(page.getByRole("heading", { name: "نظرة عامة" })).toBeVisible();
  await expect(page.locator(".platform-sidebar")).toBeVisible();
  await expect(page.getByRole("button", { name: "تنبيهات المنصة" })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
  await expect(page.locator('meta[name="color-scheme"]')).toHaveAttribute("content", "light");
  await expect(page.getByRole("button", { name: /تبديل الوضع/ })).toHaveCount(0);
  await expect
    .poll(() => page.locator("body").evaluate((element) => getComputedStyle(element).fontFamily))
    .toContain("Tajawal");
  await expect(page.locator(".platform-sidebar").getByRole("link", { name: /الجهات والاشتراكات/ })).toBeVisible();
  await expect(page.getByRole("link", { name: "التصاريح", exact: true })).toHaveCount(0);
  await expect(page.getByRole("link", { name: "الزيارات", exact: true })).toHaveCount(0);
  const shellGeometry = await page.evaluate(() => {
    const brand = document.querySelector(".platform-brand")?.getBoundingClientRect();
    const topbar = document.querySelector(".platform-topbar")?.getBoundingClientRect();
    return {
      brandTop: brand?.top,
      brandHeight: brand?.height,
      topbarTop: topbar?.top,
      topbarHeight: topbar?.height,
      topbarPosition: getComputedStyle(document.querySelector(".platform-topbar")).position,
    };
  });
  expect(shellGeometry.brandTop).toBe(0);
  expect(shellGeometry.topbarTop).toBe(0);
  expect(shellGeometry.brandHeight).toBe(72);
  expect(shellGeometry.topbarHeight).toBe(72);
  expect(shellGeometry.topbarPosition).toBe("sticky");

  await page.getByRole("button", { name: "تنبيهات المنصة" }).click();
  await expect(page.getByRole("region", { name: "آخر تنبيهات المنصة" })).toBeVisible();
  await capturePlatform(page, "platform-desktop-light.png");
});

test("platform workspace remains usable on mobile", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/Platform");

  await expect(page.getByRole("heading", { name: "نظرة عامة" })).toBeVisible();
  await page.getByRole("button", { name: "فتح القائمة" }).click();
  await expect(page.locator(".platform-sidebar")).toBeVisible();
  await expect
    .poll(async () => {
      const box = await page.locator(".platform-sidebar").boundingBox();
      return (box?.x ?? 1000) + (box?.width ?? 0);
    })
    .toBeLessThanOrEqual(391);
  const sidebarBox = await page.locator(".platform-sidebar").boundingBox();
  expect(sidebarBox?.width).toBeGreaterThan(250);
  expect(sidebarBox?.x).toBeGreaterThanOrEqual(95);
  expect((sidebarBox?.x ?? 0) + (sidebarBox?.width ?? 0)).toBeLessThanOrEqual(391);

  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  expect(dimensions.documentWidth).toBeLessThanOrEqual(dimensions.viewport + 1);
  await capturePlatform(page, "platform-mobile-light.png");
});

test("platform management pages share the fixed light shell", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("vps_theme", "dark"));
  await ensureOwnerSignedIn(page);

  for (const route of ["/Tenants", "/Pricing", "/PlatformSettings"]) {
    await page.goto(route);
    await expect(page.locator("html")).toHaveAttribute("data-theme", "light");
    await expect(page.locator(".platform-sidebar")).toBeVisible();
    await expect(page.getByRole("button", { name: /تبديل الوضع/ })).toHaveCount(0);
    await capturePlatform(page, `platform-${route.slice(1).toLowerCase()}-desktop-light.png`);
  }

  await page.goto("/Tenants");
  await expect(page.getByRole("heading", { name: "إدارة الجهات" })).toBeVisible();
  await expect(page.getByRole("group", { name: "عروض سجل الجهات" })).toBeVisible();
  await expect(page.locator("#tenantSort")).toHaveValue("newest");
  await page.getByRole("button", { name: /الجديدة/ }).click();
  await expect(page.getByRole("button", { name: /الجديدة/ })).toHaveAttribute("aria-pressed", "true");
  await page.locator("#tenantClearFilters").click();
  await expect(page.getByRole("button", { name: /الكل/ })).toHaveAttribute("aria-pressed", "true");
  await capturePlatform(page, "platform-tenants-desktop-light.png");

  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  const dimensions = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    documentWidth: document.documentElement.scrollWidth,
  }));
  expect(dimensions.documentWidth).toBeLessThanOrEqual(dimensions.viewport + 1);
  await capturePlatform(page, "platform-tenants-mobile-light.png");
});
