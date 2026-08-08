const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn, uniqueSuffix } = require("./helpers/e2e-helpers");

test("owner can stop and safely delete a tenant", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Tenants");
  await expect(page.getByRole("heading", { name: "إدارة الجهات" })).toBeVisible();

  const suffix = uniqueSuffix();
  const tenantName = `جهة حذف ${suffix}`;
  const tenantId = `delete-${suffix}`;

  await page.getByRole("button", { name: "إضافة جهة" }).click();
  await page.locator('[name="Name"]').fill(tenantName);
  await page.locator('[name="TenantId"]').fill(tenantId);
  await page.locator('[name="Slug"]').fill(tenantId);
  await page.locator('[name="DepartmentName"]').fill("الإدارة العامة");
  await page.getByRole("button", { name: "إضافة الجهة" }).click();

  let row = page.getByRole("row", { name: new RegExp(tenantId) });
  await expect(row).toBeVisible();
  await expect(row.getByRole("button", { name: "حذف" })).toBeDisabled();

  await row.getByRole("button", { name: "إيقاف" }).click();
  row = page.getByRole("row", { name: new RegExp(tenantId) });
  await expect(row.getByText("الحسابات محجوبة")).toBeVisible();

  await row.getByRole("button", { name: "حذف" }).click();
  const dialog = page.locator("#tenantDeleteDialog");
  await expect(dialog).toBeVisible();
  const confirmation = dialog.getByLabel("اكتب اسم الجهة للتأكيد");
  const deleteButton = dialog.getByRole("button", { name: "حذف نهائي" });
  await confirmation.fill("اسم غير مطابق");
  await expect(deleteButton).toBeDisabled();
  await confirmation.fill(tenantName);
  await expect(deleteButton).toBeEnabled();
  await deleteButton.click();

  await expect(page.getByRole("row", { name: new RegExp(tenantId) })).toHaveCount(0);
  await expect(page.getByText("تم حذف الجهة وجميع بياناتها نهائيًا.")).toBeVisible();
});

test("tenant registry remains orderly on mobile", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/Tenants");

  const metrics = await page.evaluate(() => {
    const firstRow = document.querySelector("[data-tenant-row]");
    const firstCell = firstRow?.querySelector("td");
    return {
      viewport: window.innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      rowDisplay: firstRow ? getComputedStyle(firstRow).display : "",
      cellDisplay: firstCell ? getComputedStyle(firstCell).display : "",
    };
  });

  expect(metrics.documentWidth).toBeLessThanOrEqual(metrics.viewport + 1);
  expect(metrics.rowDisplay).toBe("block");
  expect(metrics.cellDisplay).toBe("grid");
});

test("tenant registry keeps every desktop row on one table line", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/Tenants");

  const metrics = await page.locator("[data-tenant-row]").first().evaluate((row) => {
    const cells = Array.from(row.cells);
    const headers = Array.from(row.closest("table").tHead.rows[0].cells);
    const rect = (element) => {
      const bounds = element.getBoundingClientRect();
      return {
        left: Math.round(bounds.left),
        right: Math.round(bounds.right),
      };
    };

    return {
      displays: cells.map((cell) => getComputedStyle(cell).display),
      tops: cells.map((cell) => Math.round(cell.getBoundingClientRect().top)),
      heights: cells.map((cell) => Math.round(cell.getBoundingClientRect().height)),
      headerColumns: headers.map(rect),
      rowColumns: cells.map(rect),
    };
  });

  expect(new Set(metrics.displays)).toEqual(new Set(["table-cell"]));
  expect(new Set(metrics.tops).size).toBe(1);
  expect(new Set(metrics.heights).size).toBe(1);
  expect(metrics.rowColumns).toEqual(metrics.headerColumns);
});

test("tenant registry paginates large result sets and preserves filtering", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Tenants");

  const token = await page
    .locator('form[action$="/Tenants/Create"] input[name="__RequestVerificationToken"]')
    .inputValue();
  const suffix = uniqueSuffix();
  const tenantIds = Array.from({ length: 11 }, (_, index) => `page-${suffix}-${index + 1}`);

  for (const [index, tenantId] of tenantIds.entries()) {
    const response = await page.request.post("/Tenants/Create", {
      form: {
        __RequestVerificationToken: token,
        TenantId: tenantId,
        Name: `جهة ترقيم ${index + 1} ${suffix}`,
        Slug: tenantId,
        DepartmentName: "الإدارة العامة",
        SubscriptionStatus: "Active",
        PlanName: "أساسية",
      },
    });
    expect(response.ok()).toBeTruthy();
  }

  await page.goto("/Tenants");
  await expect(page.locator("#tenantPaginationShell")).toBeVisible();
  await expect(page.locator("#tenantPageInfo")).toContainText("صفحة 1 من 2");
  await expect(page.getByRole("button", { name: "الانتقال إلى الصفحة 2" })).toBeVisible();

  let visibleRows = await page.locator("[data-tenant-row]").evaluateAll((rows) =>
    rows.filter((row) => !row.hidden).length
  );
  expect(visibleRows).toBe(10);

  await page.getByRole("button", { name: "الانتقال إلى الصفحة 2" }).click();
  await expect(page.locator("#tenantPageInfo")).toContainText("صفحة 2 من 2");
  visibleRows = await page.locator("[data-tenant-row]").evaluateAll((rows) =>
    rows.filter((row) => !row.hidden).length
  );
  expect(visibleRows).toBe(2);

  const uniquelyMatchingTenantId = tenantIds[5];
  await page.locator("#tenantSearch").fill(uniquelyMatchingTenantId);
  await expect(page.locator("#tenantVisibleCount")).toHaveText("1");
  await expect(page.locator("#tenantPaginationShell")).toBeHidden();
  await expect(page.getByRole("row", { name: new RegExp(uniquelyMatchingTenantId) })).toBeVisible();
});
