const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

const operationalTablePages = [
  "/Permits",
  "/Visits",
  "/Users",
  "/Delegations",
  "/Administration/Departments",
  "/Administration/Backup",
  "/Administration/DisplaySettings",
  "/Administration/DisplayDevices",
];

test("operational tables stay inside their page and use internal scrolling on narrow screens", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });

  for (const route of operationalTablePages) {
    await page.goto(route);
    await expect(page).not.toHaveURL(/\/Account\/(Login|AccessDenied)/i);

    const layout = await page.evaluate(() => {
      const wrappers = [...document.querySelectorAll(".table-responsive")];

      return {
        bodyWidth: document.documentElement.scrollWidth,
        viewportWidth: window.innerWidth,
        wrappers: wrappers.map((wrapper) => {
          const rect = wrapper.getBoundingClientRect();
          return {
            left: rect.left,
            right: rect.right,
            clientWidth: wrapper.clientWidth,
            scrollWidth: wrapper.scrollWidth,
          };
        }),
      };
    });

    expect(layout.bodyWidth, `${route} must not create page-level horizontal overflow`).toBeLessThanOrEqual(
      layout.viewportWidth + 1
    );

    for (const wrapper of layout.wrappers) {
      expect(wrapper.left, `${route} table wrapper must start inside the viewport`).toBeGreaterThanOrEqual(-1);
      expect(wrapper.right, `${route} table wrapper must end inside the viewport`).toBeLessThanOrEqual(
        layout.viewportWidth + 1
      );
      expect(wrapper.clientWidth, `${route} table wrapper must have a usable width`).toBeGreaterThan(0);
      expect(wrapper.scrollWidth, `${route} table content width must be measurable`).toBeGreaterThanOrEqual(
        wrapper.clientWidth
      );
    }
  }
});
