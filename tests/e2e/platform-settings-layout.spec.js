const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

async function expectNoHorizontalOverflow(page) {
  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
}

async function expectNaturalVerticalScrolling(page) {
  await page.evaluate(() => {
    const spacer = document.createElement("div");
    spacer.dataset.scrollTest = "true";
    spacer.style.height = "1000px";
    document.querySelector("main")?.appendChild(spacer);
  });

  const getScrollPosition = () => page.evaluate(() => Math.max(
    window.scrollY,
    document.documentElement.scrollTop,
    document.body.scrollTop,
    document.querySelector(".display-kiosk-main")?.scrollTop || 0
  ));

  await page.mouse.wheel(0, 1200);
  await expect.poll(getScrollPosition).toBeGreaterThan(0);

  await page.mouse.wheel(0, -1200);
  await expect.poll(getScrollPosition).toBe(0);
  await page.evaluate(() => document.querySelector('[data-scroll-test="true"]')?.remove());
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
    await expect(page.locator(".public-content-header")).toHaveClass(/public-landing-nav/);
    await expect(page.locator(".public-content-brand img")).toHaveAttribute("src", /\/icons\/app-icon\.svg$/);
    await expect(page.locator(".app-system-meta-kiosk, .app-system-meta-public")).toBeHidden();
    await expectNoHorizontalOverflow(page);
    await expectNaturalVerticalScrolling(page);

    const headerStyle = await page.locator(".public-content-header").evaluate((element) => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return {
        fontFamily: style.fontFamily,
        borderRadius: style.borderRadius,
        left: rect.left,
        width: rect.width,
        viewportWidth: document.documentElement.clientWidth,
      };
    });
    expect(headerStyle.fontFamily).toContain("Tajawal");
    expect(headerStyle.borderRadius).toBe("0px");
    expect(headerStyle.left).toBeCloseTo(0, 0);
    expect(headerStyle.width).toBeCloseTo(headerStyle.viewportWidth, 0);

    const brandFilter = await page.locator(".public-content-brand img").evaluate(
      (element) => getComputedStyle(element).filter
    );
    expect(brandFilter).toBe("none");

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
