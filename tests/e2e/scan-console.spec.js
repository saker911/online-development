const { expect, test } = require("@playwright/test");
const { execFileSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const {
  changePassword,
  createEmployeePermit,
  createUser,
  createVisit,
  ensurePermitReviewerSignedIn,
  ensureSecurityManagerSignedIn,
  ensureTenantManagerSignedIn,
  expectAccessDeniedOrLogin,
  roles,
  signIn,
  signOut,
  submitForm,
  uniqueNationalId,
} = require("./helpers/e2e-helpers");

async function ensureScanApprovalSignature(page) {
  await ensureTenantManagerSignedIn(page);
  await page.goto("/Administration/Edit");
  await page.locator('[name="SignatureText"]').fill("اعتماد E2E للمسح");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click({ timeout: 2000 }).catch(() => { });
}

async function approvePermitForScan(page, permitNumber) {
  await ensureSecurityManagerSignedIn(page);
  await ensurePermitReviewerSignedIn(page);
  await ensureScanApprovalSignature(page);
  await ensurePermitReviewerSignedIn(page);
  await submitForm(page, `/Permits/ForwardToGeneralManager/${permitNumber}`, {}, {
    tokenPath: `/Permits/Details/${permitNumber}`,
  });
  await ensureSecurityManagerSignedIn(page);
  await page.goto(`/Permits/Approve/${permitNumber}`);
  await expect(page.getByRole("heading", { name: "اعتماد التصريح" })).toBeVisible();
  await page.getByRole("button", { name: "اعتماد مباشر" }).click();
  await page.goto(`/Permits/Details/${permitNumber}`);
  await expect(page.getByText("معتمد")).toBeVisible();
}

async function approveVisitForScan(page, visitId) {
  await ensureScanApprovalSignature(page);
  await submitForm(page, `/Visits/ApproveDetained/${visitId}`, {}, {
    tokenPath: `/Visits/Details/${visitId}`,
  });
  await page.goto(`/Visits/Details/${visitId}`);
  await expect(page.locator(".badge").getByText("معتمد").first()).toBeVisible();
}

async function createReadyGateUser(page) {
  await ensureTenantManagerSignedIn(page);
  const gate = await createUser(page, { role: roles.gateSecurity });
  const password = `Aa${uniqueNationalId("8")}!`;
  await signOut(page);
  await signIn(page, gate.username, gate.temporaryPassword);
  await changePassword(page, gate.temporaryPassword, password);
  await signOut(page);
  return { ...gate, password };
}

function localDateTimeAt(dayOffset, hours, minutes) {
  const date = new Date();
  date.setDate(date.getDate() + dayOffset);
  date.setHours(hours, minutes, 0, 0);
  return date;
}

function toLocalIso(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}

function toLocalDate(date) {
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function resolveE2eDatabasePath() {
  const configuredStorageRoot = process.env.VehiclePermitSystemWeb__StorageRoot;
  if (configuredStorageRoot) {
    const configuredPath = path.join(configuredStorageRoot, ".localdata", "vehicle-permit-system.db");
    if (fs.existsSync(configuredPath)) {
      return configuredPath;
    }
  }

  const candidates = fs.readdirSync(os.tmpdir(), { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && entry.name.startsWith("VehiclePermitSystemWeb-e2e-"))
    .map((entry) => path.join(os.tmpdir(), entry.name, "storage", ".localdata", "vehicle-permit-system.db"))
    .filter((candidate) => fs.existsSync(candidate))
    .map((candidate) => ({ path: candidate, modifiedAt: fs.statSync(candidate).mtimeMs }))
    .sort((left, right) => right.modifiedAt - left.modifiedAt);

  if (candidates.length === 0) {
    throw new Error("Could not locate the Playwright SQLite database.");
  }

  return candidates[0].path;
}

function runStateTool(command, ...args) {
  const project = path.join(__dirname, "tools", "E2eStateTool", "E2eStateTool.csproj");
  const output = execFileSync("dotnet", ["run", "--project", project, "--", command, ...args], {
    cwd: path.resolve(__dirname, "../.."),
    encoding: "utf8",
    timeout: 120_000,
  });
  const jsonLine = output.trim().split(/\r?\n/).filter(Boolean).pop();
  return JSON.parse(jsonLine || "{}");
}

test("GateSecurity can open scan console and invalid scan shows denial", async ({ page }) => {
  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);
  await expect(page).toHaveURL(/\/Display\/Gate/i);
  await expect(page.getByRole("heading", { name: "مركز البوابة" })).toBeVisible();

  await page.getByLabel("رمز التصريح أو الزيارة").fill("NOT-A-REAL-CODE");
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  await expect(page.locator("#scanDecisionCard")).toHaveClass(/state-danger/);
  await expect(page.locator("#scanDecisionMessage")).toContainText(/غير موجود|تعذر|غير صالح/);
});

test("legacy gate routes converge on the unified gate workspace", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);

  await page.goto("/ScanConsole");
  await expect(page).toHaveURL(/\/Display\/Gate$/i);
  await expect(page.locator("#unifiedGateRoot")).toBeVisible();

  await page.goto("/Display/Visits");
  await expect(page).toHaveURL(/\/Display\/Gate$/i);
  await expect(page.locator("#unifiedGateRoot")).toBeVisible();
});

test("mobile gate console records a visit entry and exit without horizontal overflow", async ({ page }) => {
  test.setTimeout(150_000);
  await page.setViewportSize({ width: 390, height: 844 });

  const visit = await createVisit(page, {
    visitorName: "زائر اختبار بوابة الجوال",
  });
  await approveVisitForScan(page, visit.visitId);
  runStateTool(
    "prepare-visit-for-scan",
    resolveE2eDatabasePath(),
    visit.visitId,
    toLocalIso(new Date())
  );

  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);
  await expect(page).toHaveURL(/\/Display\/Gate/i);
  await expect(page.getByRole("heading", { name: "مركز البوابة" })).toBeVisible();
  await expect(page.getByRole("button", { name: "تشغيل كاميرا الجوال" })).toBeVisible();

  const initialMetrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(initialMetrics.scrollWidth).toBeLessThanOrEqual(initialMetrics.clientWidth + 1);

  const firstScanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(visit.visitId);
  await page.getByRole("button", { name: "تحقق" }).click();
  const firstPayload = await (await firstScanResponse).json();
  expect(firstPayload.allowed).toBeTruthy();
  await expect(page.locator("#scanDecisionCard")).toHaveClass(/state-success/);
  await expect(page.locator("#scanDecisionTitle")).toContainText(/دخول|عودة/);
  await expect(page.locator("#shiftScanCount")).toHaveText("1");

  const secondScanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(visit.visitId);
  await page.getByRole("button", { name: "تحقق" }).click();
  const secondPayload = await (await secondScanResponse).json();
  expect(secondPayload.allowed).toBeTruthy();
  await expect(page.locator("#scanDecisionTitle")).toContainText("الخروج");
  await expect(page.locator("#shiftScanCount")).toHaveText("2");
  await expect(page.locator("#shiftAllowedCount")).toHaveText("2");

  const finalMetrics = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(finalMetrics.scrollWidth).toBeLessThanOrEqual(finalMetrics.clientWidth + 1);
});

test("Receptionist without ScanOperations cannot open scan console", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  const receptionist = await createUser(page, { role: roles.receptionist });
  const password = `Aa${uniqueNationalId("8")}!`;
  await signOut(page);
  await signIn(page, receptionist.username, receptionist.temporaryPassword);
  await changePassword(page, receptionist.temporaryPassword, password);

  await expectAccessDeniedOrLogin(page, "/ScanConsole");
});

test("valid, stopped, and rejected permit scans return expected states", async ({ page }) => {
  const permit = await createEmployeePermit(page);
  await approvePermitForScan(page, permit.permitNumber);
  const stopped = await createEmployeePermit(page);
  await approvePermitForScan(page, stopped.permitNumber);
  await submitForm(page, `/Permits/Stop/${stopped.permitNumber}`, {}, { tokenPath: `/Permits/Details/${stopped.permitNumber}` });
  const rejected = await createEmployeePermit(page);
  await ensurePermitReviewerSignedIn(page);
  await submitForm(page, `/Permits/ForwardToGeneralManager/${rejected.permitNumber}`, {}, {
    tokenPath: `/Permits/Details/${rejected.permitNumber}`,
  });
  await ensureSecurityManagerSignedIn(page);
  await submitForm(page, `/Permits/Reject/${rejected.permitNumber}`, {}, { tokenPath: `/Permits/Details/${rejected.permitNumber}` });
  await page.goto(`/Permits/Details/${rejected.permitNumber}`);
  await expect(page.locator(".status-badge").getByText("مرفوض", { exact: true })).toBeVisible();

  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);

  await page.goto("/ScanConsole");
  const approvedScanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(permit.permitNumber);
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  const approvedScan = await (await approvedScanResponse).json();
  expect(approvedScan.allowed).toBeTruthy();
  expect(approvedScan.permit?.driverName).toBe(permit.driverName);

  const stoppedScanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(stopped.permitNumber);
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  const stoppedScan = await (await stoppedScanResponse).json();
  expect(stoppedScan.allowed).toBeFalsy();
  expect(stoppedScan.permit?.approvalStatusDisplay).toMatch(/موقوف|غير نشط|منتهي/);

  const rejectedScanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(rejected.permitNumber);
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  const rejectedScan = await (await rejectedScanResponse).json();
  expect(rejectedScan.allowed).toBeFalsy();
  expect(rejectedScan.permit?.approvalStatusDisplay).toMatch(/مرفوض|غير نشط|منتهي/);
});

test("ScanConsole recovers EntryOnly stale pending exit into a new entry", async ({ page }) => {
  test.setTimeout(120_000);

  const permit = await createEmployeePermit(page, {
    driverName: "موظف دخول فقط للتعافي",
    requiresReturn: false,
  });
  await approvePermitForScan(page, permit.permitNumber);

  const databasePath = resolveE2eDatabasePath();
  const firstDayEntryAt = localDateTimeAt(-1, 10, 45);
  const firstDayPendingAt = localDateTimeAt(-1, 11, 16);
  const currentDay = toLocalDate(new Date());

  runStateTool(
    "prepare-entry-only-stale-pending",
    databasePath,
    permit.permitNumber,
    toLocalIso(firstDayEntryAt),
    toLocalIso(firstDayPendingAt)
  );

  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);
  await page.goto("/ScanConsole");

  const scanResponse = page.waitForResponse((response) =>
    response.url().includes("/api/scan/auto") && response.request().method() === "POST"
  );
  await page.getByLabel("رمز التصريح أو الزيارة").fill(permit.permitNumber);
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  const scanPayload = await (await scanResponse).json();

  expect(scanPayload.allowed).toBeTruthy();
  expect(scanPayload.reason).toBe("WorkEndEntry recorded");
  expect(scanPayload.permit?.currentState).toBe("Inside");
  expect(scanPayload.permit?.approvalStatus).toBe("Approved");
  await expect(page.getByText(/تم تسجيل دخول .* بعد إغلاق نهاية الدوام/).first()).toBeVisible();
  await expect(page.getByText("تم رفض الخروج")).toHaveCount(0);

  await page.goto(`/Permits/Details/${permit.permitNumber}`);
  await expect(page.getByText("لا توجد محاولات معلقة")).toBeVisible();

  await page.goto(`/Reports/PermitActivity?activityQuery=${permit.permitNumber}&pageSize=20`);
  await expect(page.getByText("خروج يحتاج مراجعة إدارية").first()).toBeVisible();
  await expect(page.getByText("خروج نهاية الدوام").first()).toBeVisible();
  await expect(page.getByText("تسجيل دخول").first()).toBeVisible();

  const recoveryState = runStateTool(
    "read-entry-only-recovery",
    databasePath,
    permit.permitNumber,
    currentDay
  );
  expect(recoveryState.permit.currentState).toBe("Inside");
  expect(recoveryState.permit.approvalStatus).toBe("Approved");
  expect(recoveryState.permit.pendingUnauthorizedExitAt).toBeNull();
  expect(recoveryState.permit.pendingUnauthorizedExitSequenceId || "").toBe("");
  expect(recoveryState.unauthorizedExitNeedsReviewCount).toBeGreaterThanOrEqual(1);
  expect(recoveryState.workEndExitCount).toBeGreaterThanOrEqual(1);
  expect(recoveryState.currentDayEntryCount).toBeGreaterThanOrEqual(1);
  expect(recoveryState.currentDayPendingUnauthorizedExitCount).toBe(0);
});

test("repeated approved permit scans do not stretch scan console layout", async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const permit = await createEmployeePermit(page, {
    driverName: "اسم طويل لاختبار ثبات شاشة المسح عند تكرار الدخول والخروج الكامل",
    departmentName: "إدارة طويلة الاسم لاختبار الالتفاف الآمن داخل بطاقة آخر نتيجة",
    plateNumber: "LONG-PLATE-1234567890",
  });
  await approvePermitForScan(page, permit.permitNumber);

  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);
  await page.goto("/ScanConsole");

  for (let index = 0; index < 6; index += 1) {
    const scanResponse = page.waitForResponse((response) =>
      response.url().includes("/api/scan/auto") && response.request().method() === "POST"
    );
    await page.getByLabel("رمز التصريح أو الزيارة").fill(permit.permitNumber);
    await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
    await scanResponse;
    await expect(page.getByLabel("رمز التصريح أو الزيارة")).toBeFocused();
  }

  const metrics = await page.evaluate(() => {
    const card = document.querySelector("#scannedPermitCard");
    const input = document.querySelector(".scan-input");
    const rect = card?.getBoundingClientRect();
    return {
      documentHeight: document.documentElement.scrollHeight,
      viewportHeight: window.innerHeight,
      cardHeight: rect?.height ?? 0,
      cardVisible: !!rect && rect.top < window.innerHeight && rect.bottom > 0,
      cardScrollsInternally: !!card && card.scrollHeight > card.clientHeight,
      inputFocused: document.activeElement === input,
    };
  });

  expect(metrics.documentHeight).toBeLessThanOrEqual(metrics.viewportHeight + 120);
  expect(metrics.cardHeight).toBeLessThanOrEqual(360);
  expect(metrics.cardVisible).toBeTruthy();
  expect(metrics.cardScrollsInternally).toBeFalsy();
  expect(metrics.inputFocused).toBeTruthy();
});

test("unified gate keeps results and recent operations responsive on desktop", async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const permit = await createEmployeePermit(page, {
    driverName: "وليد يوسف صاحب اسم طويل جدًا لاختبار عدم تمدد بطاقة آخر نتيجة في شاشة العرض",
    departmentName: "الاتصالات وتقنية المعلومات وتشغيل الأنظمة الأمنية طويلة الاسم",
    plateNumber: "1212-LONG-PLATE-XYZ",
  });
  await approvePermitForScan(page, permit.permitNumber);

  await ensureTenantManagerSignedIn(page);
  await page.goto("/Display/Gate");
  await expect(page.locator("#unifiedGateRoot")).toBeVisible();

  await page.evaluate((permitData) => {
    const decision = document.querySelector("#scanDecisionCard");
    decision?.classList.remove("state-idle", "state-danger");
    decision?.classList.add("state-success");
    document.querySelector("#scanDecisionTitle").textContent = "دخول مسموح";
    document.querySelector("#scanDecisionMessage").textContent = "تمت عودة وليد يوسف ثم تسجيل دخول جديد بنجاح مع رسالة طويلة لاختبار الالتفاف.";
    document.querySelector("#scanDecisionCode").textContent = permitData.permitNumber;

    const detailsCard = document.querySelector("#scannedPermitCard");
    detailsCard?.classList.remove("d-none");
    const details = document.querySelector("#scannedPermitDetails");
    details?.classList.remove("d-none");
    if (details) {
      details.innerHTML = Array.from({ length: 12 }, (_, index) => `
        <div class="gate-mobile-detail">
          <span>بيان ${index + 1}</span>
          <strong>${permitData.driverName} ${permitData.departmentName}</strong>
        </div>
      `).join("");
    }

    const list = document.querySelector("#scanRecentActivityList");
    if (list) {
      list.innerHTML = Array.from({ length: 28 }, (_, index) => `
        <article class="gate-mobile-activity ${index % 2 ? "is-allowed" : "is-denied"}">
          <div><strong>${permitData.driverName}</strong><span>${index % 2 ? "خروج مسموح" : "تم السماح بالدخول"}</span></div>
          <time>11:48 ص</time>
        </article>
      `).join("");
    }
    document.querySelector(".scan-input")?.focus({ preventScroll: true });
  }, permit);

  const metrics = await page.evaluate(() => {
    const result = document.querySelector("#scanDecisionCard");
    const details = document.querySelector("#scannedPermitDetails");
    const recent = document.querySelector("#scanRecentActivityList");
    const input = document.querySelector(".scan-input");
    const resultRect = result?.getBoundingClientRect();
    return {
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: document.documentElement.clientWidth,
      resultHeight: resultRect?.height ?? 0,
      resultVisible: !!resultRect && resultRect.top >= 0,
      detailsContained: !!details && details.scrollWidth <= details.clientWidth + 1,
      recentScrollsInternally: !!recent && recent.scrollHeight > recent.clientHeight,
      inputFocused: document.activeElement === input,
    };
  });

  expect(metrics.scrollWidth).toBeLessThanOrEqual(metrics.clientWidth + 1);
  expect(metrics.resultHeight).toBeLessThanOrEqual(360);
  expect(metrics.resultVisible).toBeTruthy();
  expect(metrics.detailsContained).toBeTruthy();
  expect(metrics.recentScrollsInternally).toBeTruthy();
  expect(metrics.inputFocused).toBeTruthy();
});

test("display gate preserves QR verification URL punctuation when scanning", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);

  const scannedUrl = "http://127.0.0.1:5001/Permits/VerifyByNumber/PERMIT-00003?source=gate&mode=qr";
  let submittedBody = null;
  await page.route("**/api/scan/auto", async (route) => {
    submittedBody = route.request().postDataJSON();
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        allowed: true,
        reason: "Entry recorded",
        identifier: submittedBody.identifier,
        scanMode: "permit",
        displayName: "ريم سالم العنزي",
        permit: {
          permitNumber: "PERMIT-00003",
          publicPermitCode: "PERMIT-00003",
          driverName: "ريم سالم العنزي",
          permitTypeDisplay: "موظف",
          approvalStatusDisplay: "معتمد",
          departmentName: "الإدارة العامة",
          locationDisplay: "الإدارة العامة",
          subject: "تصريح مركبة دائم",
          nationalId: "1333333333",
          vehicleType: "شانجان",
          plateNumberDisplay: "ط ط ط 2222",
          employeePhone: "0503333333",
          permitDateText: "2026-05-09",
          expiresAtText: "2027-05-09",
          approvalStatus: "Approved",
          currentState: "Inside",
          requiresReturn: true,
          pendingExitRequest: false,
        },
      }),
    });
  });

  await page.goto("/Display/Gate");
  await expect(page.locator("#unifiedGateRoot")).toBeVisible();

  await page.locator(".scan-input").fill(scannedUrl);
  await page.locator(".scan-input").press("Enter");

  await expect.poll(() => submittedBody?.identifier).toBe(scannedUrl);
});

test("display gate uses the signed-in account without a temporary operator PIN", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);

  await page.route("**/Display/GateStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        approvedEmployeesCount: 0,
        employeesOutCount: 0,
        recentActivitiesCount: 0,
        latestActivityId: 0,
        activeOperator: {
          username: "1234567890",
          displayName: "مالك النظام",
          signedInAtText: "10:30:00",
          mustChangePin: false,
        },
        approvedEmployees: [],
        employeesOut: [],
        recentActivities: [],
        activity: null,
      }),
    })
  );
  await page.goto("/Display/Gate?operatorMode=true");
  await expect(page.locator("#unifiedGateRoot")).toBeVisible();
  await expect(page.locator("#scanConsoleSubmitButton")).toBeEnabled();
  await expect(page.locator("#scanConsoleCameraButton")).toBeEnabled();
  await expect(page.locator("#operatorModal")).toHaveCount(0);
  await expect(page.getByText("الرقم المؤقت", { exact: false })).toHaveCount(0);
});
