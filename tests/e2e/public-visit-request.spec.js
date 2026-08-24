const { expect, test } = require("@playwright/test");
const {
  dateTimeLocal,
  ensureTenantManagerSignedIn,
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
  await expect(page.getByRole("heading", { name: "طلب زيارة", exact: true })).toBeVisible();
  await expect(page.locator(".public-visit-brand-mark img[data-product-mark]")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
  await expect(page.locator(".public-visit-brand-mark img[data-product-mark]")).toHaveCSS("filter", "none");

  const visitorName = `زائر ذاتي ${uniqueSuffix()}`;
  await page.locator('[name="VisitorName"]').fill(visitorName);
  await page.locator('[name="PhoneNumber"]').fill(uniquePhone());
  await page.locator('[name="VisitorEmail"]').fill(`visitor-${uniqueSuffix()}@example.com`);
  await page.locator('[name="NationalId"]').fill(uniqueNationalId("2"));
  await page.locator('[name="VisitDate"]').fill(dateTimeLocal(25));
  const hostField = page.locator('[name="VisitedPersonName"]:visible');
  if (await hostField.count()) await hostField.fill("موظف الاستقبال");
  const destination = page.locator('select[name="DepartmentId"]');
  if (await destination.count()) await destination.selectOption({ index: 1 });
  const site = page.locator('select[name="WorkplaceSiteId"]');
  if (await site.count()) await site.selectOption({ index: 1 });
  const locationField = page.locator('input[name="VisitLocation"]:visible');
  if (await locationField.count()) await locationField.fill("المبنى الرئيسي");
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

  await ensureTenantManagerSignedIn(page);
  await page.goto(`/Visits?searchTerm=${encodeURIComponent(visitorName)}`);
  await expect(page.locator("table.visit-management-table")).toContainText(visitorName);
  await expect(page.getByText("طلب ذاتي").first()).toBeVisible();
  const visitId = (await page.locator("a.permit-number-pill").first().textContent()).trim();

  await submitForm(page, `/Visits/ApproveDetained/${visitId}`, {}, { tokenPath: `/Visits/Details/${visitId}` });
  await page.context().clearCookies();
  await page.goto(statusUrl);
  await expect(page.getByRole("heading", { name: "تم اعتماد زيارتك" })).toBeVisible();
  await expect(page.locator(".public-visit-brand-mark img[data-product-mark]")).toBeVisible();
  const qr = page.locator(".public-visit-qr-frame img");
  await expect(qr).toBeVisible();
  const qrResponse = await page.request.get(await qr.getAttribute("src"));
  expect(qrResponse.status()).toBe(200);
  expect(qrResponse.headers()["content-type"]).toContain("image/svg+xml");
  expect(await qrResponse.text()).toContain("<svg");

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(publicUrl);
  const mobileLayout = await page.evaluate(() => {
    const card = document.querySelector(".public-visit-form-card")?.getBoundingClientRect();
    const versionBar = document.querySelector(".app-system-meta-kiosk");
    return {
      fitsViewport: document.documentElement.scrollWidth <= window.innerWidth + 1,
      reachesFullForm: document.documentElement.scrollHeight >= (card?.bottom ?? 0),
      versionBarHidden: !versionBar || getComputedStyle(versionBar).display === "none",
      submitWidth: document.querySelector(".public-visit-submit")?.getBoundingClientRect().width ?? 0,
    };
  });
  expect(mobileLayout.fitsViewport).toBeTruthy();
  expect(mobileLayout.reachesFullForm).toBeTruthy();
  expect(mobileLayout.versionBarHidden).toBeTruthy();
  expect(mobileLayout.submitWidth).toBeGreaterThan(250);
});

test("public visit request stays compact and aligned with application forms on desktop", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  await page.goto("/Visits");
  const publicUrl = await page.locator("#publicVisitRequestUrl").inputValue();
  await page.context().clearCookies();
  await page.setViewportSize({ width: 1600, height: 900 });
  await page.goto(publicUrl);

  const layout = await page.evaluate(() => {
    const card = document.querySelector(".public-visit-form-card")?.getBoundingClientRect();
    const controls = [...document.querySelectorAll(".public-visit-fields input")]
      .filter((input) => input.getBoundingClientRect().height > 0)
      .map((input) => input.getBoundingClientRect().height);
    return {
      fitsViewport: document.documentElement.scrollWidth <= window.innerWidth + 1,
      cardWidth: card?.width ?? 0,
      cardHeight: card?.height ?? 0,
      controls,
    };
  });

  expect(layout.fitsViewport).toBeTruthy();
  expect(layout.cardWidth).toBeLessThanOrEqual(980);
  expect(layout.cardHeight).toBeLessThan(600);
  expect(layout.controls.every((height) => height <= 42)).toBeTruthy();
});
