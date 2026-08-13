const { expect, test } = require("@playwright/test");

const owner = {
  username: "1234567890",
  password: "OnlineTest2026!",
};
let permitSequence = 0;

async function expectHomePage(page) {
  await expect(page).toHaveURL(/\/Platform$/i);
  await expect(page.locator('form[action*="/Account/Login"]')).toHaveCount(0);
}

async function completeInitialSetup(page) {
  await page.goto("/Account/Login");
  if (!/\/Account\/InitialSetup/i.test(page.url())) {
    await signInAsOwner(page);
    await expectHomePage(page);
    return;
  }

  await page.locator('[name="Username"]').fill(owner.username);
  await page.locator('[name="JobTitle"]').fill("مالك النظام");
  await page.locator('[name="FullName"]').fill("مالك النظام للاختبار");
  await page.locator('[name="PhoneNumber"]').fill("0551234567");
  const passwordInput = page.locator('[name="Password"]');
  await passwordInput.click();
  await passwordInput.pressSequentially("E");
  await expect(passwordInput).toBeFocused();
  await passwordInput.fill(owner.password);
  await page.locator('[name="ConfirmPassword"]').fill(owner.password);
  await page.locator("#setupNextButton").click();

  await page.locator('[name="OrganizationName"]').fill("جهة اختبار المتصفح");
  await page.locator('[name="AdministrationPhone"]').fill("0111234567");
  await page.locator('[name="AdministrationEmail"]').fill("e2e@example.test");
  await page.locator('[name="AdministrationAddress"]').fill("عنوان اختبار المتصفح");
  await page.locator("#setupNextButton").click();

  await page.locator("#setupSubmitButton").click();
  await expect(page.getByRole("heading", { name: /تم إعداد النظام بنجاح/ })).toBeVisible();
  await page.getByRole("link", { name: "الدخول الآن" }).click();
  await expectHomePage(page);
}

async function signInAsOwner(page) {
  await page.goto("/Account/Login");
  await page.locator('[name="username"]').fill(owner.username);
  await page.locator('[name="password"]').fill(owner.password);
  await page.getByRole("button", { name: "دخول" }).click();
}

async function ensureOwnerSignedIn(page) {
  await page.goto("/Account/Login");
  if (/\/Account\/InitialSetup/i.test(page.url())) {
    await completeInitialSetup(page);
    return;
  }

  await signInAsOwner(page);
  await expectHomePage(page);
}

function buildVisitorPermitData() {
  permitSequence += 1;
  const suffix = String(Date.now() + permitSequence).slice(-6);
  return {
    driverName: `زائر اختبار ${suffix}`,
    nationalId: `29${suffix}01`.slice(0, 10).padEnd(10, "0"),
    visitLocation: "بوابة الاختبار",
    employeePhone: `055${suffix}0`.slice(0, 10).padEnd(10, "0"),
    vehicleType: "سيارة اختبار",
    plateNumber: `E2E-${suffix}`,
  };
}

async function createVisitorPermit(page) {
  const permit = buildVisitorPermitData();
  await ensureOwnerSignedIn(page);

  await page.goto("/Permits/Create");
  await expect(page.getByRole("heading", { name: "إنشاء تصريح جديد" })).toBeVisible();

  await page.locator("#permitTypeSelect").selectOption("Visitor");
  await page.locator('[name="DriverName"]').fill(permit.driverName);
  await page.locator('[name="NationalId"]').fill(permit.nationalId);
  await page.locator('[name="VisitLocation"]').fill(permit.visitLocation);
  await page.locator('[name="EmployeePhone"]').fill(permit.employeePhone);
  await page.locator('[name="VehicleType"]').fill(permit.vehicleType);
  await page.locator("#PlateOriginSelect").selectOption("Foreign");
  await page.locator("#PlateNumberInput").fill(permit.plateNumber);
  await page.getByRole("button", { name: "حفظ" }).click();

  await expect(page).toHaveURL(/\/Permits\/Details\//i);
  await expect(page.getByText(permit.driverName, { exact: true })).toBeVisible();
  return permit;
}

async function expectDelegationFilterPreservesScroll(page, path, headingName, formId) {
  await ensureOwnerSignedIn(page);

  await page.goto(path);
  await expect(page.getByRole("heading", { name: headingName })).toBeVisible();

  await page.evaluate(() => window.scrollTo(0, 900));
  const beforeScroll = await page.evaluate(() => window.scrollY || window.pageYOffset || 0);

  const navigationPromise = page.waitForNavigation({ waitUntil: "domcontentloaded" });
  await page.evaluate((targetFormId) => {
    const form = document.getElementById(targetFormId);
    if (!form) {
      throw new Error(`Missing form: ${targetFormId}`);
    }

    form.requestSubmit();
  }, formId);
  await navigationPromise;

  await expect(page.getByRole("heading", { name: headingName })).toBeVisible();
  const afterScroll = await page.evaluate(() => window.scrollY || window.pageYOffset || 0);
  expect(afterScroll).toBeGreaterThanOrEqual(Math.max(beforeScroll - 100, 0));
}

test("initial setup creates owner", async ({ page }) => {
  await completeInitialSetup(page);
  await expectHomePage(page);
});

test("owner can sign in normally and open platform workspace", async ({ page }) => {
  await completeInitialSetup(page);

  await page.locator('form[action*="/Account/Logout"]').evaluate((form) => form.requestSubmit());
  await expect(page).toHaveURL(/\/o\/default/i);
  await expect(page.getByRole("heading", { name: "الدخول إلى الجهة الافتراضية" })).toBeVisible();

  await signInAsOwner(page);
  await expectHomePage(page);
});

test("tenant platform link and mobile PWA entry are ready", async ({ page }) => {
  await completeInitialSetup(page);
  await page.context().clearCookies();
  await page.setViewportSize({ width: 390, height: 844 });

  const response = await page.goto("/o/default", { waitUntil: "networkidle" });
  expect(response.status()).toBe(200);
  await expect(page.getByRole("heading", { name: "الدخول إلى الجهة الافتراضية" })).toBeVisible();
  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute("href", "/manifest.webmanifest");

  const layoutWidth = await page.evaluate(() => ({
    viewport: window.innerWidth,
    document: document.documentElement.scrollWidth,
  }));
  expect(layoutWidth.document).toBeLessThanOrEqual(layoutWidth.viewport);

  const unknownTenantResponse = await page.request.get("/o/not-a-real-tenant");
  expect(unknownTenantResponse.status()).toBe(404);

  const manifestResponse = await page.request.get("/manifest.webmanifest");
  expect(manifestResponse.status()).toBe(200);
  const manifest = await manifestResponse.json();
  expect(manifest.display).toBe("standalone");
  expect(manifest.icons.some((icon) => icon.sizes === "192x192")).toBeTruthy();
  expect(manifest.icons.some((icon) => icon.sizes === "512x512")).toBeTruthy();
});

test("owner can open administration backup page", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Administration/Backup");
  await expect(page).toHaveURL(/\/Administration\/Backup$/i);
  await expect(page.getByRole("heading", { name: "النسخ الاحتياطي" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "إنشاء نسخة الآن" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "استعادة نسخة احتياطية" })).toBeVisible();
  await expect(page.locator('form[action="/Administration/RestoreBackup"] input[name="backupFile"]')).toHaveAttribute("required", "");
});

test("owner can create a new visitor permit", async ({ page }) => {
  await createVisitorPermit(page);
});

test("owner can search for an existing permit", async ({ page }) => {
  const permit = await createVisitorPermit(page);

  await page.goto("/Permits");
  await page.locator('input[name="searchTerm"]').fill(permit.nationalId);
  await page.locator('form.permit-dashboard-search button[type="submit"]').click();

  await expect(page).toHaveURL(/\/Permits\?searchTerm=/i);
  const resultsTable = page.locator("table.permit-list-table");
  await expect(resultsTable.getByText(permit.driverName, { exact: true })).toBeVisible();
  await expect(resultsTable.getByText(permit.nationalId, { exact: true })).toBeVisible();
});

test("owner can open reports center and permits print page", async ({ page }) => {
  await createVisitorPermit(page);

  await page.goto("/Reports");
  await expect(page.getByRole("heading", { name: "اختر نوع التقرير" })).toBeVisible();
  await expect(page.getByText("فتح مركز تقارير التصاريح")).toBeVisible();

  await page.goto("/Reports/Permits");
  await expect(page.getByRole("heading", { name: "تقارير تصاريح المركبات" })).toBeVisible();
  const printReportLink = page.getByRole("link", { name: "طباعة التقرير" });
  await expect(printReportLink).toBeVisible();

  const downloadPromise = page.waitForEvent("download");
  await printReportLink.click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/\.pdf$/i);
});

test("delegation filter submit keeps scroll position", async ({ page }) => {
  await expectDelegationFilterPreservesScroll(
    page,
    "/Delegations",
    "إدارة التفويضات المؤقتة",
    "delegations-filter-form"
  );

  await expectDelegationFilterPreservesScroll(
    page,
    "/Delegations/Audit",
    "تقرير الاعتمادات والعمليات بالتفويض",
    "delegations-audit-filter-form"
  );
});

test("delegation permission groups can collapse and expand", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Delegations");
  await expect(page.getByRole("heading", { name: "إدارة التفويضات المؤقتة" })).toBeVisible();

  await page.locator('#Editor_DelegatorUsername').selectOption(owner.username);

  const groupButton = page.getByRole("button", { name: /تصاريح الموظفين/ }).first();
  const groupBody = page.locator('[data-delegation-group-body="employee-permits"]');
  const groupCount = page.locator('.delegation-permission-card.has-owned-permissions .delegation-permission-count.is-owned').first();

  await expect(groupBody).not.toBeVisible();
  await expect(groupButton).toHaveAttribute("aria-expanded", "false");
  await expect(groupCount).toBeVisible();
  await groupButton.click();
  await expect(groupBody).toBeVisible();
  await expect(groupButton).toHaveAttribute("aria-expanded", "true");
  await groupButton.click();
  await expect(groupBody).not.toBeVisible();
  await expect(groupButton).toHaveAttribute("aria-expanded", "false");
});

