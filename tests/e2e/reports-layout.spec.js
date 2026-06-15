const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn } = require("./helpers/e2e-helpers");

const reportRoutes = [
  "/Reports",
  "/Reports/Permits",
  "/Reports/Visits",
  "/Reports/StoppedPermits",
  "/Reports/UnauthorizedExitWorkflow",
  "/Reports/PermitActivity",
];

test("report pages use contained responsive tables without crowded rows", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });

  for (const route of reportRoutes) {
    await page.goto(route);
    await expect(page).not.toHaveURL(/\/Account\/(Login|AccessDenied)/i);

    const layout = await page.evaluate(() => ({
      pageWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth,
      workflowLegendItems: [...document.querySelectorAll(".report-workflow-legend-item")].map(
        (item) => item.getBoundingClientRect().height
      ),
      tables: [...document.querySelectorAll(".report-scroll-table")].map((wrapper) => {
        const rect = wrapper.getBoundingClientRect();
        const table = wrapper.querySelector(".report-table");
        const rowHeights = table
          ? [...table.querySelectorAll("tbody tr")].map((row) => row.getBoundingClientRect().height)
          : [];

        return {
          left: rect.left,
          right: rect.right,
          clientWidth: wrapper.clientWidth,
          scrollWidth: wrapper.scrollWidth,
          columnCount: table?.querySelectorAll("thead th").length ?? 0,
          maxRowHeight: Math.max(0, ...rowHeights),
        };
      }),
    }));

    expect(layout.pageWidth, `${route} must not create page-level horizontal overflow`).toBeLessThanOrEqual(
      layout.viewportWidth + 1
    );

    for (const itemHeight of layout.workflowLegendItems) {
      expect(itemHeight, `${route} workflow guidance must stay compact on mobile`).toBeLessThanOrEqual(100);
    }

    for (const table of layout.tables) {
      expect(table.left, `${route} table must start inside the viewport`).toBeGreaterThanOrEqual(-1);
      expect(table.right, `${route} table must end inside the viewport`).toBeLessThanOrEqual(
        layout.viewportWidth + 1
      );
      expect(table.scrollWidth, `${route} table must scroll inside its card`).toBeGreaterThanOrEqual(
        table.clientWidth
      );
      expect(table.maxRowHeight, `${route} rows must not become vertically crowded`).toBeLessThanOrEqual(150);

      if (table.columnCount >= 7) {
        expect(table.scrollWidth, `${route} wide tables need enough horizontal space`).toBeGreaterThanOrEqual(1080);
      }
    }
  }
});
