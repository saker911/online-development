const { expect, test } = require("@playwright/test");
const { ensureOwnerSignedIn, uniqueSuffix } = require("./helpers/e2e-helpers");

function ownerFields(suffix, index = 0) {
  const digits = `${suffix}${String(index).padStart(2, "0")}`.replace(/\D/g, "");
  return {
    OwnerFullName: `مدير الجهة ${suffix} ${index}`,
    OwnerUsername: `tenant${digits.slice(-12).padStart(12, "0")}`,
    OwnerEmail: `tenant-${suffix}-${index}@example.test`,
    OwnerPhoneNumber: `05${digits.slice(-8).padStart(8, "0")}`,
  };
}

async function fillOwnerFields(page, suffix, index = 0) {
  const owner = ownerFields(suffix, index);
  await page.locator('[name="OwnerFullName"]').fill(owner.OwnerFullName);
  await page.locator('[name="OwnerUsername"]').fill(owner.OwnerUsername);
  await page.locator('[name="OwnerEmail"]').fill(owner.OwnerEmail);
  await page.locator('[name="OwnerPhoneNumber"]').fill(owner.OwnerPhoneNumber);
}

test("owner can stop and safely delete a tenant", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Tenants");
  await expect(page.getByRole("heading", { name: "إدارة الجهات" })).toBeVisible();

  const suffix = uniqueSuffix();
  const tenantName = `جهة حذف ${suffix}`;

  await page.getByRole("button", { name: "إضافة جهة" }).click();
  await page.locator('[name="Name"]').fill(tenantName);
  await page.locator('[name="DepartmentName"]').fill("الإدارة العامة");
  await fillOwnerFields(page, suffix);
  await page.getByRole("button", { name: "إضافة الجهة" }).click();

  let row = page.getByRole("row", { name: new RegExp(tenantName) });
  await expect(row).toBeVisible();
  await expect(row.getByText("1 مستخدم", { exact: true }).first()).toBeVisible();
  await row.locator(".tenant-users-directory summary").click();
  await expect(row.getByText(`مدير الجهة ${suffix} 0`, { exact: true })).toBeVisible();
  await expect(row.getByText(ownerFields(suffix).OwnerUsername, { exact: true })).toBeVisible();
  await expect(row.getByText("مدير عام", { exact: true })).toBeVisible();
  await expect(row.getByRole("button", { name: "حذف" })).toBeDisabled();

  await row.getByRole("button", { name: "إيقاف" }).click();
  row = page.getByRole("row", { name: new RegExp(tenantName) });
  await expect(row.getByText("الحسابات محجوبة")).toBeVisible();

  await row.getByRole("button", { name: "حذف" }).click();
  const dialog = page.locator("#tenantDeleteDialog");
  await expect(dialog).toBeVisible();
  const reason = dialog.getByLabel("سبب الحذف");
  const acknowledgement = dialog.getByLabel(/أفهم أن حذف الجهة/);
  const deleteButton = dialog.getByRole("button", { name: "حذف نهائي" });
  await reason.fill("حذف جهة اختبارية");
  await expect(deleteButton).toBeDisabled();
  await acknowledgement.check();
  await expect(deleteButton).toBeEnabled();
  await deleteButton.click();

  await expect(page.getByRole("row", { name: new RegExp(tenantName) })).toHaveCount(0);
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
  const tenantNames = Array.from(
    { length: 11 },
    (_, index) => `جهة ترقيم ${index + 1} ${suffix}`
  );

  for (const [index, tenantName] of tenantNames.entries()) {
    const response = await page.request.post("/Tenants/Create", {
      form: {
        __RequestVerificationToken: token,
        Name: tenantName,
        DepartmentName: "الإدارة العامة",
        SubscriptionStatus: "Active",
        PlanName: "أساسية",
        ...ownerFields(suffix, index + 1),
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

  const uniquelyMatchingTenantName = tenantNames[5];
  await page.locator("#tenantSearch").fill(uniquelyMatchingTenantName);
  await expect(page.locator("#tenantVisibleCount")).toHaveText("1");
  await expect(page.locator("#tenantPaginationShell")).toBeHidden();
  await expect(page.getByRole("row", { name: new RegExp(uniquelyMatchingTenantName) })).toBeVisible();
});

test("owner controls tenant services and disabled public routes stay closed", async ({
  page,
  request,
}) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Tenants");

  const suffix = uniqueSuffix();
  const tenantName = `جهة خدمات ${suffix}`;
  await page.getByRole("button", { name: "إضافة جهة" }).click();
  await page.locator('[name="Name"]').fill(tenantName);
  await page.locator('[name="DepartmentName"]').fill("الإدارة العامة");
  await fillOwnerFields(page, suffix);
  await page.locator('input[type="checkbox"][name="VisitsServiceEnabled"]').uncheck();

  await expect(
    page.locator('input[type="checkbox"][name="SelfServiceEnabled"]')
  ).toBeDisabled();
  await expect(
    page.locator('input[type="checkbox"][name="QueueServiceEnabled"]')
  ).toBeDisabled();
  await page.getByRole("button", { name: "إضافة الجهة" }).click();

  await page.locator("#tenantSearch").fill(tenantName);
  let row = page.getByRole("row", { name: new RegExp(tenantName) });
  await expect(row.getByText("2 من 5 خدمات مفعلة")).toBeVisible();
  const tenantPath = (await row.locator("code").filter({ hasText: "/o/" }).first().innerText()).trim();
  expect((await request.get(`${tenantPath}/visit-request`)).status()).toBe(404);

  await row.getByRole("link", { name: "تعديل" }).click();
  await page.locator('input[type="checkbox"][name="VisitsServiceEnabled"]').check();
  await page.locator('input[type="checkbox"][name="SelfServiceEnabled"]').check();
  await page.getByRole("button", { name: "حفظ التعديل" }).click();
  const validationErrors = await page
    .locator(".field-validation-error")
    .allTextContents();
  expect(validationErrors.filter((message) => message.trim()), "tenant update validation errors").toEqual([]);

  await page.locator("#tenantSearch").fill(tenantName);
  row = page.getByRole("row", { name: new RegExp(tenantName) });
  await expect(row.getByText("4 من 5 خدمات مفعلة")).toBeVisible();
  expect((await request.get(`${tenantPath}/visit-request`)).status()).toBe(200);

  await row.getByRole("button", { name: "إيقاف" }).click();
  await page.locator("#tenantSearch").fill(tenantName);
  row = page.getByRole("row", { name: new RegExp(tenantName) });
  await row.getByRole("button", { name: "حذف" }).click();
  const dialog = page.locator("#tenantDeleteDialog");
  await dialog.getByLabel("سبب الحذف").fill("تنظيف بيانات الاختبار");
  await dialog.getByLabel(/أفهم أن حذف الجهة/).check();
  await dialog.getByRole("button", { name: "حذف نهائي" }).click();
  await expect(page.getByRole("row", { name: new RegExp(tenantName) })).toHaveCount(0);
});
