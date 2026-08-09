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
  ensureOwnerSignedIn,
  expectAccessDeniedOrLogin,
  roles,
  signIn,
  signOut,
  submitForm,
  uniqueNationalId,
} = require("./helpers/e2e-helpers");

async function ensureScanApprovalSignature(page) {
  await ensureOwnerSignedIn(page);
  await page.goto("/Administration/Edit");
  await page.locator('[name="SignatureText"]').fill("اعتماد E2E للمسح");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click({ timeout: 2000 }).catch(() => { });
}

async function approvePermitForScan(page, permitNumber) {
  await ensureScanApprovalSignature(page);
  await submitForm(page, `/Permits/ForwardToGeneralManager/${permitNumber}`, {}, {
    tokenPath: `/Permits/Details/${permitNumber}`,
  });
  await page.goto(`/Permits/Approve/${permitNumber}`);
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
  await ensureOwnerSignedIn(page);
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
  await expect(page).toHaveURL(/\/ScanConsole/i);
  await expect(page.getByRole("heading", { name: "مركز البوابة" })).toBeVisible();

  await page.getByLabel("رمز التصريح أو الزيارة").fill("NOT-A-REAL-CODE");
  await page.getByLabel("رمز التصريح أو الزيارة").press("Enter");
  await expect(page.locator("#scanDecisionCard")).toHaveClass(/state-danger/);
  await expect(page.locator("#scanDecisionMessage")).toContainText(/غير موجود|تعذر|غير صالح/);
});

test("mobile gate console records a visit entry and exit without horizontal overflow", async ({ page }) => {
  test.setTimeout(150_000);
  await page.setViewportSize({ width: 390, height: 844 });

  const visit = await createVisit(page, {
    visitorName: "زائر اختبار بوابة الجوال",
  });
  await approveVisitForScan(page, visit.visitId);

  const gate = await createReadyGateUser(page);
  await signIn(page, gate.username, gate.password);
  await expect(page).toHaveURL(/\/ScanConsole/i);
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
  await ensureOwnerSignedIn(page);
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
  await submitForm(page, `/Permits/ForwardToGeneralManager/${rejected.permitNumber}`, {}, {
    tokenPath: `/Permits/Details/${rejected.permitNumber}`,
  });
  await submitForm(page, `/Permits/Reject/${rejected.permitNumber}`, {}, { tokenPath: `/Permits/Details/${rejected.permitNumber}` });

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

test("display gate keeps latest result and recent operations inside fixed panels", async ({ page }) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  const permit = await createEmployeePermit(page, {
    driverName: "وليد يوسف صاحب اسم طويل جدًا لاختبار عدم تمدد بطاقة آخر نتيجة في شاشة العرض",
    departmentName: "الاتصالات وتقنية المعلومات وتشغيل الأنظمة الأمنية طويلة الاسم",
    plateNumber: "1212-LONG-PLATE-XYZ",
  });
  await approvePermitForScan(page, permit.permitNumber);

  await ensureOwnerSignedIn(page);
  const recentActivities = Array.from({ length: 28 }, (_, index) => ({
    id: 9000 + index,
    permitNumber: permit.permitNumber,
    driverName: permit.driverName,
    departmentName: permit.departmentName,
    actionLabel: index % 2 ? "خروج مسموح" : "تم السماح بالدخول",
    message: "رسالة عملية طويلة لاختبار عدم تمدد القائمة الجانبية عند تكرار التصريح نفسه.",
    occurredAtText: `2026/05/03 11:48:${String(index).padStart(2, "0")} ص`,
    source: "شاشة البوابة",
    operatorDisplay: "مشغل البوابة التجريبي",
    statusText: index % 2 ? "خروج مسموح" : "تم السماح بالدخول",
    theme: index % 2 ? "success" : "warning",
  }));
  await page.route("**/Display/GateStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        approvedEmployeesCount: 0,
        employeesOutCount: 0,
        recentActivitiesCount: recentActivities.length,
        latestActivityId: recentActivities[0].id,
        activeOperator: null,
        approvedEmployees: [],
        employeesOut: [],
        recentActivities,
        activity: recentActivities[0],
      }),
    })
  );
  await page.goto("/Display/Gate");
  await expect(page.locator("#permitDisplayRoot")).toBeVisible();

  await page.evaluate((permitData) => {
    const resultCard = document.querySelector("#permitResultCard");
    const emptyState = document.querySelector("#permitEmptyState");
    const detailsState = document.querySelector("#permitDetailsState");
    resultCard?.classList.remove("state-empty", "state-danger");
    resultCard?.classList.add("state-success");
    if (emptyState) emptyState.hidden = true;
    if (detailsState) detailsState.hidden = false;

    const setText = (selector, value) => {
      const element = document.querySelector(selector);
      if (element) element.textContent = value;
    };
    setText("#permitAccessChip", "تم السماح بالدخول");
    setText("#permitStateChip", "دخول مسموح");
    setText("#permitDriverName", permitData.driverName);
    setText("#permitMovementLabel", "تمت عودة وليد يوسف ثم تسجيل دخول جديد بنجاح مع رسالة طويلة لاختبار الالتفاف.");
    setText("#permitNumber", permitData.permitNumber);
    setText("#permitType", "تم السماح بالدخول");
    setText("#permitDepartment", permitData.departmentName);
    setText("#permitTimestamp", "11:48:34 ص");
    setText("#permitApprovalStatus", "2026-05-03");
    setText("#permitPlateNumber", permitData.plateNumber);
    setText("#permitSubject", "تصريح ماكينة دائم مع نص طويل يجب ألا يمدد بطاقة النتيجة أو يخفي المنطقة السفلية.");
    setText("#permitReason", "تمت عودة وليد يوسف. ".repeat(8));
    setText("#permitNationalId", "1044563325");
    setText("#permitPhone", "0236666666");
    setText("#permitScanSource", "بواسطة: مشغل البوابة التجريبي");

    const list = document.querySelector("#recentActivityList");
    if (list) {
      list.innerHTML = Array.from({ length: 28 }, (_, index) => `
        <article class="gate-activity-item ${index % 2 ? "success" : "warning"}">
          <div class="gate-activity-head">
            <span>2026/05/03 11:48:${String(index).padStart(2, "0")} ص</span>
            <span class="gate-activity-badge ${index % 2 ? "success" : "warning"}">${index % 2 ? "خروج مسموح" : "تم السماح بالدخول"}</span>
          </div>
          <div class="gate-activity-title">${permitData.driverName}</div>
          <div class="gate-activity-meta">رسالة عملية طويلة لاختبار عدم تمدد القائمة الجانبية عند تكرار التصريح نفسه.</div>
          <div class="gate-activity-meta">بواسطة: مشغل البوابة التجريبي</div>
        </article>
      `).join("");
    }
    document.querySelector("#barcodeScanner")?.focus({ preventScroll: true });
  }, permit);

  const metrics = await page.evaluate(() => {
    const result = document.querySelector("#permitResultCard");
    const details = document.querySelector("#permitDetailsState");
    const recent = document.querySelector("#recentActivityList");
    const alert = document.querySelector(".gate-alert-strip");
    const input = document.querySelector("#barcodeScanner");
    const resultRect = result?.getBoundingClientRect();
    const alertRect = alert?.getBoundingClientRect();
    return {
      documentHeight: document.documentElement.scrollHeight,
      viewportHeight: window.innerHeight,
      resultHeight: resultRect?.height ?? 0,
      resultVisible: !!resultRect && resultRect.top >= 0 && resultRect.bottom <= window.innerHeight,
      detailsScrollsInternally: !!details && details.scrollHeight > details.clientHeight,
      recentScrollsInternally: !!recent && recent.scrollHeight > recent.clientHeight,
      alertVisible: !!alertRect && alertRect.bottom <= window.innerHeight && alertRect.top >= 0,
      inputFocused: document.activeElement === input,
    };
  });

  expect(metrics.documentHeight).toBeLessThanOrEqual(metrics.viewportHeight + 8);
  expect(metrics.resultHeight).toBeLessThanOrEqual(360);
  expect(metrics.resultVisible).toBeTruthy();
  expect(metrics.detailsScrollsInternally).toBeTruthy();
  expect(metrics.recentScrollsInternally).toBeTruthy();
  expect(metrics.alertVisible).toBeTruthy();
  expect(metrics.inputFocused).toBeTruthy();
});

test("display gate preserves QR verification URL punctuation when scanning", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  const activeDisplayOperator = {
    username: "gate-url-scanner",
    displayName: "مشغل رابط QR",
    signedInAtText: "10:30:00",
    mustChangePin: false,
  };
  const scannedUrl = "http://127.0.0.1:5001/Permits/VerifyByNumber/PERMIT-00003?source=gate&mode=qr";
  let submittedBody = null;

  await page.route("**/Display/GateStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        approvedEmployeesCount: 0,
        employeesOutCount: 0,
        recentActivitiesCount: 0,
        latestActivityId: 0,
        activeOperator: activeDisplayOperator,
        approvedEmployees: [],
        employeesOut: [],
        recentActivities: [],
        activity: null,
      }),
    })
  );
  await page.route("**/Display/GateOperatorStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ operatorSession: activeDisplayOperator }),
    })
  );
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
  await expect(page.locator("#permitDisplayRoot")).toBeVisible();
  await expect(page.locator("#activeOperatorName")).toHaveText(activeDisplayOperator.displayName);

  await page.locator("#barcodeScanner").focus();
  await page.keyboard.type(scannedUrl);
  await page.keyboard.press("Enter");

  await expect.poll(() => submittedBody?.identifier).toBe(scannedUrl);
});

test("display gate refreshes stale security token and retries operator login after recovery", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  let activeDisplayOperator = null;

  await page.route("**/Display/GateStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        approvedEmployeesCount: 0,
        employeesOutCount: 0,
        recentActivitiesCount: 0,
        latestActivityId: 0,
        activeOperator: activeDisplayOperator,
        approvedEmployees: [],
        employeesOut: [],
        recentActivities: [],
        activity: null,
      }),
    })
  );
  await page.route("**/Display/GateOperatorStatus**", (route) =>
    route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ operatorSession: activeDisplayOperator }),
    })
  );

  await page.goto("/Display/Gate");
  await expect(page.locator("#permitDisplayRoot")).toBeVisible();

  let refreshedPageRequests = 0;
  await page.route("**/Display/Gate", (route) => {
    refreshedPageRequests += 1;
    return route.fulfill({
      status: 200,
      contentType: "text/html",
      body: '<!doctype html><input name="__RequestVerificationToken" value="RECOVERED-TOKEN" />',
    });
  });

  const switchTokens = [];
  await page.route("**/Display/SwitchOperator", (route) => {
    switchTokens.push(route.request().headers()["requestverificationtoken"] || "");
    if (switchTokens.length === 1) {
      return route.fulfill({
        status: 400,
        contentType: "text/html",
        body: "stale antiforgery token",
      });
    }

    activeDisplayOperator = {
      username: "gate-recovered",
      displayName: "مشغل التعافي",
      signedInAtText: "10:30:00",
      mustChangePin: false,
    };

    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        success: true,
        errorCode: null,
        message: "تم تسجيل دخول المشغل.",
        requiresPinChange: false,
        previousOperator: null,
        currentOperator: activeDisplayOperator,
      }),
    });
  });

  await page.locator("#openOperatorModalButton").click();
  await page.evaluate(() => {
    document.querySelector("#operatorBadgeInput").value = "GATE-RECOVERY";
    document.querySelector("#operatorPinInput").value = "123456";
  });
  await page.locator("#submitOperatorLoginButton").click();

  await expect(page.locator("#activeOperatorName")).toHaveText("مشغل التعافي");
  expect(refreshedPageRequests).toBe(1);
  expect(switchTokens).toHaveLength(2);
  expect(switchTokens[1]).toBe("RECOVERED-TOKEN");
});
