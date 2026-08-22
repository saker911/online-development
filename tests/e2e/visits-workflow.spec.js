const { expect, test } = require("@playwright/test");
const {
  createVisit,
  createTestPngBuffer,
  dateTimeLocal,
  downloadPdfBufferAndAssert,
  ensureOwnerSignedIn,
  submitForm,
  updateAdministrationBranding,
  uniqueNationalId,
  uniquePhone,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

test("create visits, companions, search, and edit", async ({ page }) => {
  const visit = await createVisit(page);
  const visitWithCompanion = await createVisit(page, {
    companions: [
      {
        fullName: "مرافق اختبار",
        nationalId: uniqueNationalId("7"),
        phoneNumber: uniquePhone(),
        relationship: "زميل",
      },
    ],
  });

  await page.goto(`/Visits/Details/${visitWithCompanion.visitId}`);
  await expect(page.getByText("مرافق اختبار")).toBeVisible();

  for (const searchTerm of [visit.visitorName, visit.nationalId]) {
    await page.goto("/Visits");
    await page.locator('input[name="searchTerm"]').fill(searchTerm);
    await page.locator('form.permit-dashboard-search button[type="submit"]').click();
    await expect(page.locator("table.permit-list-table")).toContainText(searchTerm);
  }

  await page.goto(`/Visits/Edit/${visit.visitId}`);
  await page.locator('[name="VisitorName"]').fill(`${visit.visitorName} معدل`);
  const locationField = page.locator('input[name="VisitLocation"]:visible');
  if (await locationField.count()) await locationField.fill(visit.visitLocation);
  const nationalIdField = page.locator('input[name="NationalId"]:visible');
  if (await nationalIdField.count()) await nationalIdField.fill(visit.nationalId);
  await page.locator('[name="PhoneNumber"]').fill(visit.phoneNumber);
  await page.locator('[name="Purpose"]').fill(visit.purpose);
  const visitedPersonType = page.locator('select[name="VisitedPersonType"]');
  if (await visitedPersonType.count()) {
    await visitedPersonType.selectOption(visit.visitedPersonType);
    await page.locator('input[name="VisitedPersonName"]:visible').fill(visit.visitedPersonName);
  }
  await page.locator('[name="VisitDate"]').fill(dateTimeLocal(20));
  await page.getByRole("button", { name: "حفظ التعديلات" }).click();
  await expect(page).toHaveURL(/\/Visits/i);
});

test("visit search table stays aligned and scrolls inside its card on narrow screens", async ({ page }) => {
  const visit = await createVisit(page, {
    visitorName: `زائر اختبار محاذاة طويل ${uniqueSuffix()}`,
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/Visits?searchTerm=${encodeURIComponent(visit.visitorName)}`);
  await expect(page.locator("table.visit-management-table")).toContainText(visit.visitorName);

  const layout = await page.evaluate(() => {
    const card = document.querySelector(".visit-table-shell");
    const scroller = card?.querySelector(".table-responsive");
    const visitorCell = card?.querySelector('tbody td[data-label="الزائر"]');
    const holder = visitorCell?.querySelector(".permit-holder-cell");
    const actions = card?.querySelector(".record-row-actions");
    const cardRect = card?.getBoundingClientRect();
    const cellRect = visitorCell?.getBoundingClientRect();
    const holderRect = holder?.getBoundingClientRect();

    return {
      bodyFitsViewport: document.documentElement.scrollWidth <= window.innerWidth + 1,
      cardFitsViewport: Boolean(cardRect && cardRect.left >= -1 && cardRect.right <= window.innerWidth + 1),
      hasInternalScroll: Boolean(scroller && scroller.scrollWidth > scroller.clientWidth),
      visitorCenterOffset: cellRect && holderRect
        ? Math.abs((cellRect.left + cellRect.right - holderRect.left - holderRect.right) / 2)
        : Number.POSITIVE_INFINITY,
      actionsHeight: actions?.getBoundingClientRect().height ?? Number.POSITIVE_INFINITY,
    };
  });

  expect(layout.bodyFitsViewport).toBeTruthy();
  expect(layout.cardFitsViewport).toBeTruthy();
  expect(layout.hasInternalScroll).toBeTruthy();
  expect(layout.visitorCenterOffset).toBeLessThan(2);
  expect(layout.actionsHeight).toBeLessThanOrEqual(30);
});

test("visit validation rejects old visit date", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Visits/Create");
  const firstOptionValue = async (name) => {
    const select = page.locator(`select[name="${name}"]`);
    if (!(await select.count())) return "";
    return select.locator("option").evaluateAll((options) =>
      options.map((option) => option.value).find((value) => value) ?? ""
    );
  };
  const departmentId = await firstOptionValue("DepartmentId");
  const workplaceSiteId = await firstOptionValue("WorkplaceSiteId");
  const form = {
    VisitorName: "زائر بتاريخ قديم",
    VisitLocation: "مكتب الزيارات",
    NationalId: uniqueNationalId("6"),
    PhoneNumber: uniquePhone(),
    Purpose: "اختبار رفض التاريخ",
    VisitedPersonType: "Host",
    VisitedPersonName: "مضيف اختبار",
    VisitDate: dateTimeLocal(-120),
    Status: "Active",
  };
  if (departmentId) form.DepartmentId = departmentId;
  if (workplaceSiteId) form.WorkplaceSiteId = workplaceSiteId;
  await submitForm(page, "/Visits/Create", {
    ...form,
  });

  await expect(page.getByRole("alert").getByText(/يجب أن يكون موعد الزيارة|بعده مباشرة|قديم/).first()).toBeVisible();
});

test("suspend, resume, approve, reject, and print visits", async ({ page }) => {
  await updateAdministrationBranding(page, { signatureText: "" });

  const visit = await createVisit(page, {
    visitedPersonType: "Detained",
    purpose: "زيارة موقوف للاختبار",
  });

  await submitForm(page, `/Visits/ApproveDetained/${visit.visitId}`, {}, { tokenPath: `/Visits/Details/${visit.visitId}` });
  await expect(page.getByText(/تعذر اعتماد الزيارة/).first()).toBeVisible();
  await page.goto(`/Visits/Details/${visit.visitId}`);
  await expect(page.locator(".badge").getByText("معتمد").first()).toHaveCount(0);

  await updateAdministrationBranding(page, {
    organizationName: `جهة بطاقة زيارة ${uniqueSuffix()}`,
    departmentName: "إدارة الزيارات",
    signatureText: "اعتماد إلكتروني لاختبار بطاقة الزائر",
    logoFile: {
      name: "e2e-logo.png",
      mimeType: "image/png",
      buffer: createTestPngBuffer(25, 96, 170),
    },
    signatureFile: {
      name: "e2e-signature.png",
      mimeType: "image/png",
      buffer: createTestPngBuffer(20, 132, 72),
    },
  });

  await submitForm(page, `/Visits/Suspend/${visit.visitId}`, {}, { tokenPath: `/Visits/Details/${visit.visitId}` });
  await page.goto(`/Visits/Details/${visit.visitId}`);
  await expect(page.locator(".badge").getByText("موقوف").first()).toBeVisible();

  await submitForm(page, `/Visits/Resume/${visit.visitId}`, {}, { tokenPath: `/Visits/Details/${visit.visitId}` });
  await page.goto(`/Visits/Details/${visit.visitId}`);
  await expect(page.locator(".badge").getByText("بانتظار الوصول").first()).toBeVisible();

  await submitForm(page, `/Visits/ApproveDetained/${visit.visitId}`, {}, { tokenPath: `/Visits/Details/${visit.visitId}` });
  await page.goto(`/Visits/Details/${visit.visitId}`);
  await expect(page.locator(".badge").getByText("معتمد").first()).toBeVisible();
  const pdfBuffer = await downloadPdfBufferAndAssert(page, `/Visits/Print/${visit.visitId}`);
  const embeddedImageCount = pdfBuffer.toString("latin1").match(/\/Subtype\s*\/Image/g)?.length ?? 0;
  expect(embeddedImageCount).toBeGreaterThanOrEqual(2);

  const rejected = await createVisit(page, {
    visitedPersonType: "Detained",
    purpose: "زيارة موقوف للرفض",
  });
  await submitForm(page, `/Visits/RejectDetained/${rejected.visitId}`, {}, { tokenPath: `/Visits/Details/${rejected.visitId}` });
  await page.goto(`/Visits/Details/${rejected.visitId}`);
  await expect(page.locator(".badge").getByText("مرفوض").first()).toBeVisible();

  const blocked = await page.goto(`/Visits/Print/${rejected.visitId}`);
  const contentType = blocked?.headers()["content-type"] ?? "";
  expect(contentType).not.toMatch(/pdf|octet-stream/i);
  await expect(page.getByText(/ليس لديك صلاحية للوصول|AccessDenied|غير مصرح/).first()).toBeVisible();
});
