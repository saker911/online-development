const { expect, test } = require("@playwright/test");
const {
  dateTimeLocal,
  ensureOwnerSignedIn,
  uniquePhone,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

async function saveWorkflow(page) {
  await page.getByRole("button", { name: "حفظ وتطبيق المسار" }).click();
  await page.getByRole("button", { name: "متابعة" }).click();
  await expect(page.getByText("تم حفظ مسار الزيارة وتطبيقه على رابط الزوار.")).toBeVisible();
}

test("owner publishes the express visitor flow and the public form follows it", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/VisitorWorkflow");

  await expect(page.getByRole("heading", { name: "مسار الزيارة" })).toBeVisible();
  await page.locator('input[name="TemplateKey"][value="Express"]').check();
  await expect(page.locator('input[type="checkbox"][name="ShowNationalId"]')).not.toBeChecked();
  await expect(page.locator('input[type="checkbox"][name="ShowVisitLocation"]')).not.toBeChecked();
  await expect(page.locator('input[type="checkbox"][name="ShowHostName"]')).toBeChecked();
  await expect(page.locator('input[type="checkbox"][name="ShowPurpose"]')).toBeChecked();
  await saveWorkflow(page);

  await page.goto("/Visits");
  const publicUrl = await page.locator("#publicVisitRequestUrl").inputValue();
  await page.context().clearCookies();
  await page.goto(publicUrl);

  await expect(page.locator('[name="NationalId"]')).toHaveCount(0);
  await expect(page.locator('[name="VisitLocation"]')).toHaveCount(0);
  await expect(page.locator('[name="VisitedPersonName"]')).toBeVisible();
  await expect(page.locator('[name="Purpose"]')).toBeVisible();

  await page.locator('[name="VisitorName"]').fill(`زائر سريع ${uniqueSuffix()}`);
  await page.locator('[name="PhoneNumber"]').fill(uniquePhone());
  await page.locator('[name="VisitDate"]').fill(dateTimeLocal(10));
  await page.locator('[name="VisitedPersonName"]').fill("موظف الاستقبال");
  await page.locator('[name="Purpose"]').fill("زيارة عمل سريعة");
  await page.getByRole("checkbox", { name: /أوافق على استخدام البيانات/ }).check();
  await page.getByRole("button", { name: "إرسال الطلب" }).click();
  await expect(page.getByRole("heading", { name: "طلبك قيد المراجعة" })).toBeVisible();

  await ensureOwnerSignedIn(page);
  await page.goto("/VisitorWorkflow");
  await page.locator('input[name="TemplateKey"][value="Standard"]').check();
  await saveWorkflow(page);
});

test("visitor workflow editor stays readable on mobile and dark mode", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/VisitorWorkflow");
  await page.evaluate(() => document.documentElement.setAttribute("data-theme", "dark"));

  await expect(page.locator(".visitor-workflow-templates label")).toHaveCount(3);
  const metrics = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    background: getComputedStyle(document.body).backgroundColor,
  }));
  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth + 1);
  expect(metrics.background).toBe("rgb(13, 13, 13)");
});
