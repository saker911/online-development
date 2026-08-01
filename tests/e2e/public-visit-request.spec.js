const { expect, test } = require("@playwright/test");
const {
  dateTimeLocal,
  ensureOwnerSignedIn,
  submitForm,
  updateAdministrationBranding,
  uniqueNationalId,
  uniquePhone,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

test("visitor submits a public request, follows status, and receives QR only after approval", async ({ page }) => {
  await updateAdministrationBranding(page, {
    signatureText: "اعتماد إلكتروني لطلبات الزيارة الذاتية",
  });
  await page.goto("/Visits");

  const publicUrl = await page.locator("#publicVisitRequestUrl").inputValue();
  expect(publicUrl).toMatch(/\/o\/[^/]+\/visit-request$/i);

  await page.context().clearCookies();
  await page.goto(publicUrl);
  await expect(page.getByRole("heading", { name: "أرسل طلبك قبل الوصول" })).toBeVisible();

  const visitorName = `زائر ذاتي ${uniqueSuffix()}`;
  await page.locator('[name="VisitorName"]').fill(visitorName);
  await page.locator('[name="PhoneNumber"]').fill(uniquePhone());
  await page.locator('[name="NationalId"]').fill(uniqueNationalId("2"));
  await page.locator('[name="VisitDate"]').fill(dateTimeLocal(25));
  await page.locator('[name="VisitedPersonName"]').fill("موظف الاستقبال");
  await page.locator('[name="VisitLocation"]').fill("المبنى الرئيسي");
  await page.locator('[name="Purpose"]').fill("موعد عمل تجريبي");
  await page.getByRole("checkbox", { name: /أوافق على استخدام البيانات/ }).check();
  await page.getByRole("button", { name: "إرسال الطلب" }).click();

  await expect(page).toHaveURL(/\/visit-request\/status\?token=/i);
  await expect(page.getByRole("heading", { name: "طلبك قيد المراجعة" })).toBeVisible();
  await expect(page.locator(".public-visit-qr-frame")).toHaveCount(0);
  const statusUrl = page.url();

  const invalidTenantUrl = statusUrl.replace(/\/o\/[^/]+\//i, "/o/not-a-real-tenant/");
  const invalidResponse = await page.request.get(invalidTenantUrl);
  expect(invalidResponse.status()).toBe(404);

  await ensureOwnerSignedIn(page);
  await page.goto(`/Visits?searchTerm=${encodeURIComponent(visitorName)}`);
  await expect(page.locator("table.visit-management-table")).toContainText(visitorName);
  await expect(page.getByText("طلب ذاتي").first()).toBeVisible();
  const visitId = (await page.locator("a.permit-number-pill").first().textContent()).trim();

  await submitForm(page, `/Visits/ApproveDetained/${visitId}`, {}, { tokenPath: `/Visits/Details/${visitId}` });
  await page.context().clearCookies();
  await page.goto(statusUrl);
  await expect(page.getByRole("heading", { name: "تم اعتماد زيارتك" })).toBeVisible();
  const qr = page.locator(".public-visit-qr-frame img");
  await expect(qr).toBeVisible();
  const qrResponse = await page.request.get(await qr.getAttribute("src"));
  expect(qrResponse.status()).toBe(200);
  expect(qrResponse.headers()["content-type"]).toContain("image/svg+xml");
  expect(await qrResponse.text()).toContain("<svg");

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(publicUrl);
  const mobileLayout = await page.evaluate(() => ({
    fitsViewport: document.documentElement.scrollWidth <= window.innerWidth + 1,
    submitWidth: document.querySelector(".public-visit-submit")?.getBoundingClientRect().width ?? 0,
  }));
  expect(mobileLayout.fitsViewport).toBeTruthy();
  expect(mobileLayout.submitWidth).toBeGreaterThan(250);
});
