const { expect, test } = require("@playwright/test");
const {
  createEmployeePermit,
  createVisitorPermit,
  downloadPdfAndAssert,
  ensureOwnerSignedIn,
  submitForm,
  todayPlus,
  uniqueNationalId,
  uniquePhone,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

async function ensureAdministrationSignature(page) {
  await ensureOwnerSignedIn(page);
  await page.goto("/Administration/Edit");
  await expect(page.getByRole("heading", { name: /بيانات الإدارة/ })).toBeVisible();
  await page.locator('[name="OrganizationName"]').fill("جهة اختبار القبول");
  await page.locator('[name="DepartmentName"]').fill("إدارة اختبار القبول");
  await page.locator('[name="SignatureText"]').fill("اعتماد إلكتروني لاختبارات E2E");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click({ timeout: 2000 }).catch(() => { });
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();
}

async function approvePermit(page, permitNumber) {
  await ensureAdministrationSignature(page);
  await page.goto(`/Permits/Approve/${permitNumber}`);
  await expect(page.getByRole("heading", { name: "اعتماد التصريح" })).toBeVisible();
  await page.getByRole("button", { name: "اعتماد مباشر" }).click();
}

test("create permits and search by national ID, permit number, and plate number", async ({ page }) => {
  const employeePermit = await createEmployeePermit(page);
  const visitorPermit = await createVisitorPermit(page);

  for (const searchTerm of [
    { query: employeePermit.nationalId, expected: employeePermit.driverName },
    { query: employeePermit.permitNumber, expected: employeePermit.driverName },
    { query: employeePermit.plateNumber, expected: employeePermit.driverName },
  ]) {
    await page.goto("/Permits");
    await page.locator('input[name="searchTerm"]').fill(searchTerm.query);
    await page.locator('form.permit-dashboard-search button[type="submit"]').click();
    await expect(page).toHaveURL(/\/Permits\?searchTerm=/i);
    await expect(page.locator("table.permit-list-table")).toContainText(searchTerm.expected);
  }
});

test("duplicate national ID or plate shows conflict warning", async ({ page }) => {
  const permit = await createVisitorPermit(page);

  await submitForm(page, "/Permits/Create", {
    PermitType: "Visitor",
    RequiresReturn: "true",
    DriverName: `تكرار ${uniqueSuffix()}`,
    NationalId: permit.nationalId,
    VisitLocation: "بوابة الاختبار",
    EmployeePhone: uniquePhone(),
    VehicleType: "سيارة اختبار",
    PlateOrigin: "Foreign",
    PlateNumber: permit.plateNumber,
  });

  await expect(page.getByText("تم العثور على تصريح مسجل مسبقًا")).toBeVisible();
  await expect(
    page.getByRole("cell", { name: permit.permitNumber, exact: true })
  ).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  const conflictLayout = await page.evaluate(() => {
    const table = document.querySelector(".app-inline-note-warning table");
    const wrapper = table?.closest(".table-responsive");
    const rect = wrapper?.getBoundingClientRect();

    return {
      pageWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth,
      tableWidth: table?.getBoundingClientRect().width ?? 0,
      wrapperLeft: rect?.left ?? Number.NEGATIVE_INFINITY,
      wrapperRight: rect?.right ?? Number.POSITIVE_INFINITY,
      wrapperClientWidth: wrapper?.clientWidth ?? 0,
      wrapperScrollWidth: wrapper?.scrollWidth ?? 0,
    };
  });

  expect(conflictLayout.pageWidth).toBeLessThanOrEqual(conflictLayout.viewportWidth + 1);
  expect(conflictLayout.wrapperLeft).toBeGreaterThanOrEqual(-1);
  expect(conflictLayout.wrapperRight).toBeLessThanOrEqual(conflictLayout.viewportWidth + 1);
  expect(conflictLayout.tableWidth).toBeGreaterThanOrEqual(960);
  expect(conflictLayout.wrapperScrollWidth).toBeGreaterThan(conflictLayout.wrapperClientWidth);
});

test("edit, approve, reject, stop, reactivate, print, and verify permit", async ({ page }) => {
  const permit = await createEmployeePermit(page);
  const editedName = `${permit.driverName} معدل`;

  await page.goto(`/Permits/Edit/${permit.permitNumber}`);
  await page.locator('[name="DriverName"]').fill(editedName);
  await page.locator('[name="NationalId"]').fill(permit.nationalId);
  await page.locator("#DepartmentNamePickerInput").fill(permit.departmentName);
  await page.locator('[name="DepartmentName"]').evaluate((input, value) => {
    input.value = value;
  }, permit.departmentName);
  await page.locator('[name="ManagerName"]').evaluate((input, value) => {
    input.value = value;
  }, permit.managerName);
  await page.locator('[name="EmployeePhone"]').fill(permit.employeePhone);
  await page.locator('[name="ExpiresAt"]').fill(todayPlus(8));
  await page.locator('[name="VehicleType"]').fill(permit.vehicleType);
  await page.locator('[name="PlateNumber"]').fill(permit.plateNumber);
  await page.getByRole("button", { name: "حفظ التعديلات" }).click();
  await expect(page).toHaveURL(/\/Permits/i);

  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.getByRole("link", { name: "طباعة ملصق Zebra" })).toHaveCount(0);

  await approvePermit(page, permit.permitNumber);
  await page.goto("/Permits");
  const zebraIndexLink = page.locator(`a[aria-label="طباعة ملصق Zebra للتصريح ${permit.permitNumber}"]`);
  await expect(zebraIndexLink).toHaveAttribute(
    "href",
    `/Permits/PrintZebraLabel/${permit.permitNumber}`
  );

  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.getByText("معتمد")).toBeVisible();
  await expect(page.getByRole("link", { name: "طباعة ملصق Zebra" })).toBeVisible();
  await expect(page.getByRole("link", { name: "عرض باركود التصريح" })).toBeVisible();

  await downloadPdfAndAssert(page, `/Permits/Print/${permit.permitNumber}`);
  await downloadPdfAndAssert(page, `/Permits/PrintZebraLabel/${permit.permitNumber}`);

  const barcodeResponse = await page.goto(`/Permits/Barcode/${permit.permitNumber}`);
  expect(barcodeResponse?.ok()).toBeTruthy();
  expect(barcodeResponse?.headers()["content-type"] ?? "").toMatch(/image/i);

  await page.goto(`/ScanConsole?id=${encodeURIComponent(permit.permitNumber)}`);
  await expect(page.getByRole("heading", { name: "باركود التصريح المعتمد" })).toBeVisible();
  await expect(page.getByText(permit.permitNumber, { exact: true }).first()).toBeVisible();

  await page.goto(`/Permits/VerifyByNumber/${permit.permitNumber}`);
  await expect(page.getByRole("heading", { name: /تصريح/ })).toBeVisible();
  await expect(page.getByText(/التصريح فعال ومصرح به/)).toBeVisible();
  await expect(page.getByText(editedName, { exact: false })).toBeVisible();

  await submitForm(page, `/Permits/Stop/${permit.permitNumber}`, {}, { tokenPath: `/Permits/Details/${permit.permitNumber}` });
  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.locator(".status-badge").getByText("موقوف", { exact: true }).first()).toBeVisible();

  await submitForm(page, `/Permits/Reactivate/${permit.permitNumber}`, {}, { tokenPath: `/Permits/Details/${permit.permitNumber}` });
  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.locator(".status-badge").getByText("معتمد", { exact: true }).first()).toBeVisible();

});

test("reject permit workflow marks permit rejected", async ({ page }) => {
  const permit = await createVisitorPermit(page, {
    nationalId: uniqueNationalId("8"),
  });
  await submitForm(page, `/Permits/Reject/${permit.permitNumber}`, {}, { tokenPath: `/Permits/Details/${permit.permitNumber}` });
  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.getByText("مرفوض")).toBeVisible();
  await expect(page.getByRole("link", { name: "طباعة ملصق Zebra" })).toHaveCount(0);
});
