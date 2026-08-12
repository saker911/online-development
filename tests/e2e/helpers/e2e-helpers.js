const { expect } = require("@playwright/test");
const fs = require("fs");
const zlib = require("zlib");

const owner = {
  username: "1234567890",
  password: "OnlineTest2026!",
};

const roles = {
  systemAdmin: "SystemAdmin",
  securityManager: "SecurityManager",
  permitReviewer: "PermitReviewer",
  generalManager: "GeneralManager",
  manager: "Manager",
  employee: "Employee",
  gateSecurity: "GateSecurity",
  receptionist: "Receptionist",
};

const allPermissionFlags = [
  "CanViewDashboard",
  "CanViewPermits",
  "CanViewVisitorPermits",
  "CanCreatePermit",
  "CanCreateVisitorPermit",
  "CanEditPermit",
  "CanEditVisitorPermit",
  "CanApprovePermit",
  "CanApproveLeaveRequest",
  "CanStopPermit",
  "CanReviewUnauthorizedExit",
  "CanViewVisits",
  "CanCreateVisit",
  "CanEditVisit",
  "CanApproveDetainedVisit",
  "CanApproveVisits",
  "CanScanOperations",
  "CanViewDisplays",
  "CanManageUsers",
  "CanManageDepartments",
  "CanManageAdministration",
  "CanManageDelegations",
];

let sequence = 0;

const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit += 1) {
    value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
  }
  return value >>> 0;
});

function uniqueSuffix() {
  sequence += 1;
  return String(Date.now() + sequence).slice(-8);
}

function uniqueNationalId(prefix = "2") {
  const normalizedPrefix = prefix === "1" || prefix === "2" ? prefix : "2";
  return `${normalizedPrefix}${uniqueSuffix()}1`.slice(0, 10).padEnd(10, "0");
}

function uniquePhone() {
  return `055${uniqueSuffix()}`.slice(0, 10).padEnd(10, "0");
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = crcTable[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

function pngChunk(type, data) {
  const typeBuffer = Buffer.from(type, "ascii");
  const lengthBuffer = Buffer.alloc(4);
  const crcBuffer = Buffer.alloc(4);
  lengthBuffer.writeUInt32BE(data.length, 0);
  crcBuffer.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])), 0);
  return Buffer.concat([lengthBuffer, typeBuffer, data, crcBuffer]);
}

function createTestPngBuffer(red, green, blue, alpha = 255) {
  const signature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
  const header = Buffer.alloc(13);
  header.writeUInt32BE(1, 0);
  header.writeUInt32BE(1, 4);
  header[8] = 8;
  header[9] = 6;
  const imageData = zlib.deflateSync(Buffer.from([0, red, green, blue, alpha]));
  return Buffer.concat([
    signature,
    pngChunk("IHDR", header),
    pngChunk("IDAT", imageData),
    pngChunk("IEND", Buffer.alloc(0)),
  ]);
}

function todayPlus(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().slice(0, 10);
}

function dateTimeLocal(minutesFromNow) {
  const date = new Date(Date.now() + minutesFromNow * 60_000);
  const pad = (value) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

async function expectHomePage(page) {
  await expect(page).toHaveURL(/\/($|Home(\/Index)?$|Dashboard$)/i);
  await expect(page.getByText("تسجيل الدخول", { exact: true })).toHaveCount(0);
}

async function completeInitialSetup(page) {
  await page.goto("/Account/Login");
  if (!/\/Account\/InitialSetup/i.test(page.url())) {
    return;
  }

  await page.locator('[name="Username"]').fill(owner.username);
  await page.locator('[name="JobTitle"]').fill("مالك النظام");
  await page.locator('[name="FullName"]').fill("مالك النظام للاختبار");
  await page.locator('[name="PhoneNumber"]').fill("0551234567");
  await page.locator('[name="Password"]').fill(owner.password);
  await page.locator('[name="ConfirmPassword"]').fill(owner.password);
  await page.locator("#setupNextButton").click();

  await page.locator('[name="OrganizationName"]').fill("جهة اختبار المتصفح");
  await page.locator('[name="logoFile"]').setInputFiles({
    name: "initial-organization-logo.png",
    mimeType: "image/png",
    buffer: createTestPngBuffer(14, 116, 144),
  });
  await page.locator('[name="AdministrationPhone"]').fill("0111234567");
  await page.locator('[name="AdministrationEmail"]').fill("e2e@example.test");
  await page.locator('[name="AdministrationAddress"]').fill("عنوان اختبار المتصفح");
  await page.locator("#setupNextButton").click();

  await page.locator("#setupSubmitButton").click();
  await expect(page.getByRole("heading", { name: /تم إعداد النظام بنجاح/ })).toBeVisible();
  await expect(page.locator(".login-page-watermark")).toHaveAttribute(
    "src",
    /\/icons\/brand-mark\.svg\?v=/i
  );
  await page.getByRole("link", { name: "الدخول الآن" }).click();
  await expectHomePage(page);
}

async function signIn(page, username = owner.username, password = owner.password) {
  await page.goto("/Account/Login");
  await page.locator('[name="username"]').fill(username);
  await page.locator('[name="password"]').fill(password);
  await page.getByRole("button", { name: "دخول" }).click();
}

async function signOut(page) {
  await page.context().clearCookies();
  await page.goto("/Account/Login");
}

async function ensureOwnerSignedIn(page) {
  await page.goto("/Account/Login");
  if (/\/Account\/InitialSetup/i.test(page.url())) {
    await completeInitialSetup(page);
    return;
  }

  if (!/\/Account\/Login/i.test(page.url())) {
    await page.context().clearCookies();
    await page.goto("/Account/Login");
  }

  await signIn(page, owner.username, owner.password);
  await expectHomePage(page);
}

async function updateAdministrationBranding(page, options = {}) {
  await ensureOwnerSignedIn(page);
  await page.goto("/Administration/Edit");
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();

  if (options.organizationName !== undefined) {
    await page.locator('[name="OrganizationName"]').fill(options.organizationName);
  }
  if (options.departmentName !== undefined) {
    await page.locator('[name="DepartmentName"]').fill(options.departmentName);
  }
  if (options.signatureText !== undefined) {
    await page.locator('[name="SignatureText"]').fill(options.signatureText);
  }
  if (options.logoFile) {
    await page.locator('input[name="logoFile"]').setInputFiles(options.logoFile);
  }
  if (options.signatureFile) {
    await page.locator('input[name="signatureFile"]').setInputFiles(options.signatureFile);
  }

  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  const continueButton = page.getByRole("button", { name: "متابعة" });
  await continueButton
    .waitFor({ state: "visible", timeout: 2000 })
    .then(() => continueButton.click())
    .catch(() => null);
  await expect(page.getByRole("heading", { name: /تعديل بيانات الإدارة/ })).toBeVisible();
}

async function getAntiforgeryToken(page, path) {
  await page.goto(path);
  return page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
}

async function submitForm(page, path, form, options = {}) {
  const tokenPath = options.tokenPath ?? path;
  const token = await getAntiforgeryToken(page, tokenPath);
  const navigationPromise = page.waitForNavigation({ waitUntil: "domcontentloaded" }).catch(() => null);
  await page.evaluate(
    ({ action, fields }) => {
      const form = document.createElement("form");
      form.method = "post";
      form.action = action;
      for (const [name, value] of Object.entries(fields)) {
        const input = document.createElement("input");
        input.type = "hidden";
        input.name = name;
        input.value = value == null ? "" : String(value);
        form.appendChild(input);
      }
      document.body.appendChild(form);
      form.submit();
    },
    {
      action: path,
      fields: {
        __RequestVerificationToken: token,
        ...form,
      },
    }
  );
  await navigationPromise;
}

function withPermissionFlags(granted = []) {
  const form = {};
  for (const flag of granted) {
    if (!allPermissionFlags.includes(flag)) {
      throw new Error(`Unknown permission flag: ${flag}`);
    }
    form[flag] = "true";
  }
  return form;
}

async function readTemporaryPassword(page) {
  const input = page.locator('section:has-text("بيانات") input[readonly]').first();
  await expect(input).toBeVisible();
  const value = await input.inputValue();
  expect(value).toBeTruthy();
  return value;
}

async function createUser(page, options = {}) {
  await ensureOwnerSignedIn(page);
  const suffix = uniqueSuffix();
  const username = options.username ?? uniqueNationalId("3");
  const role = options.role ?? roles.receptionist;
  const applyRoleDefaults = options.applyRoleDefaults ?? true;
  const fullName = options.fullName ?? `مستخدم اختبار ${suffix}`;

  const form = {
    Username: username,
    FullName: fullName,
    PhoneNumber: options.phoneNumber ?? uniquePhone(),
    JobTitle: options.jobTitle ?? "مستخدم اختبار",
    Email: options.email ?? "",
    EmployeeNumber: options.employeeNumber ?? `E2E-${suffix}`,
    Department: options.department ?? "",
    Role: role,
    IsActive: options.isActive === false ? "false" : "true",
    ApplyRoleDefaults: applyRoleDefaults ? "true" : "false",
    WizardStep: "3",
    ManagerUsername: options.managerUsername ?? "",
    AutoBindManager: options.autoBindManager ? "true" : "false",
    MustChangeOperatorPin: "true",
    TemporaryOperatorPin: options.temporaryOperatorPin ?? "",
    OperatorBadgeCode: options.operatorBadgeCode ?? "",
    ...(!applyRoleDefaults ? withPermissionFlags(options.permissions ?? []) : {}),
  };

  await submitForm(page, "/Users/Create", form);
  const temporaryPassword = await readTemporaryPassword(page);
  return { username, fullName, role, temporaryPassword };
}

async function changePassword(page, currentPassword, newPassword) {
  await page.goto("/Account/ChangePassword?forced=true");
  await page.locator('[name="CurrentPassword"]').fill(currentPassword);
  await page.locator('[name="NewPassword"]').fill(newPassword);
  await page.locator('[name="ConfirmPassword"]').fill(newPassword);
  await page.getByRole("button", { name: "حفظ كلمة المرور" }).click();
  await expect(page).not.toHaveURL(/\/Account\/ChangePassword/i);
}

async function setUserActive(page, username, active) {
  await ensureOwnerSignedIn(page);
  const action = active ? "Activate" : "Deactivate";
  await submitForm(page, `/Users/${action}/${username}`, {}, { tokenPath: "/Users" });
  if (active) {
    return readTemporaryPassword(page);
  }
  return null;
}

function buildVisitorPermitData(overrides = {}) {
  const suffix = uniqueSuffix();
  return {
    driverName: overrides.driverName ?? `زائر اختبار ${suffix}`,
    nationalId: overrides.nationalId ?? uniqueNationalId("4"),
    visitLocation: overrides.visitLocation ?? "بوابة الاختبار",
    employeePhone: overrides.employeePhone ?? uniquePhone(),
    vehicleType: overrides.vehicleType ?? "سيارة اختبار",
    plateNumber: overrides.plateNumber ?? `E2E-${suffix}`,
  };
}

function buildEmployeePermitData(overrides = {}) {
  const suffix = uniqueSuffix();
  return {
    driverName: overrides.driverName ?? `موظف اختبار ${suffix}`,
    nationalId: overrides.nationalId ?? uniqueNationalId("5"),
    departmentName: overrides.departmentName ?? "قسم اختبار",
    managerName: overrides.managerName ?? "مدير اختبار",
    employeePhone: overrides.employeePhone ?? uniquePhone(),
    vehicleType: overrides.vehicleType ?? "مركبة اختبار",
    plateNumber: overrides.plateNumber ?? `EMP-${suffix}`,
    permitDate: overrides.permitDate ?? todayPlus(0),
    expiresAt: overrides.expiresAt ?? todayPlus(7),
    requiresReturn: overrides.requiresReturn,
  };
}

async function createPermit(page, data, type) {
  await ensureOwnerSignedIn(page);
  const form = {
    PermitType: type,
    RequiresReturn: data.requiresReturn === false ? "false" : "true",
    DriverName: data.driverName,
    NationalId: data.nationalId,
    DepartmentName: data.departmentName ?? "",
    VisitLocation: data.visitLocation ?? "",
    ManagerName: data.managerName ?? "",
    EmployeePhone: data.employeePhone,
    PermitDate: data.permitDate ?? "",
    ExpiresAt: data.expiresAt ?? "",
    VehicleType: data.vehicleType,
    PlateOrigin: "Foreign",
    PlateNumber: data.plateNumber,
  };

  await submitForm(page, "/Permits/Create", form);
  await expect(page).toHaveURL(/\/Permits\/Details\//i);
  await expect(page.getByText(data.driverName, { exact: true })).toBeVisible();
  const match = page.url().match(/\/Permits\/Details\/([^/?#]+)/i);
  return { ...data, permitNumber: match ? decodeURIComponent(match[1]) : "" };
}

async function createVisitorPermit(page, overrides = {}) {
  return createPermit(page, buildVisitorPermitData(overrides), "Visitor");
}

async function createEmployeePermit(page, overrides = {}) {
  return createPermit(page, buildEmployeePermitData(overrides), "Permanent");
}

async function createVisit(page, overrides = {}) {
  await ensureOwnerSignedIn(page);
  const suffix = uniqueSuffix();
  const data = {
    visitorName: overrides.visitorName ?? `زائر زيارة ${suffix}`,
    visitLocation: overrides.visitLocation ?? "مكتب الزيارات",
    nationalId: overrides.nationalId ?? uniqueNationalId("6"),
    phoneNumber: overrides.phoneNumber ?? uniquePhone(),
    purpose: overrides.purpose ?? "اختبار زيارة",
    visitedPersonType: overrides.visitedPersonType ?? "Host",
    visitedPersonName: overrides.visitedPersonName ?? "مضيف اختبار",
    visitDate: overrides.visitDate ?? dateTimeLocal(10),
    companions: overrides.companions ?? [],
  };

  const form = {
    VisitorName: data.visitorName,
    VisitLocation: data.visitLocation,
    NationalId: data.nationalId,
    PhoneNumber: data.phoneNumber,
    Purpose: data.purpose,
    VisitedPersonType: data.visitedPersonType,
    VisitedPersonName: data.visitedPersonName,
    VisitDate: data.visitDate,
    Status: "Active",
  };
  data.companions.forEach((companion, index) => {
    form[`Companions[${index}].FullName`] = companion.fullName;
    form[`Companions[${index}].NationalId`] = companion.nationalId;
    form[`Companions[${index}].PhoneNumber`] = companion.phoneNumber;
    form[`Companions[${index}].Relationship`] = companion.relationship ?? "مرافق";
  });

  await submitForm(page, "/Visits/Create", form);
  await expect(page).toHaveURL(/\/Visits/i);
  await expect(page.locator("table").getByText(data.visitorName, { exact: true })).toBeVisible();
  const row = page.locator("tr", { hasText: data.visitorName }).first();
  const detailsHref = await row.locator('a[href*="/Visits/Details/"]').first().getAttribute("href");
  const visitId = detailsHref?.match(/\/Visits\/Details\/([^/?#]+)/i)?.[1] ?? "";
  return { ...data, visitId };
}

async function expectAccessDeniedOrLogin(page, path) {
  await page.goto(path);
  await expect(async () => {
    expect(page.url()).toMatch(/\/Account\/Login|\/Home\/AccessDenied|\/Error/i);
  }).toPass();
}

async function downloadPdfAndAssert(page, pathOrLocator) {
  if (typeof pathOrLocator === "string") {
    const downloadPromise = page.waitForEvent("download", { timeout: 2000 }).catch(() => null);
    const response = await page.goto(pathOrLocator).catch((error) => {
      if (!/Download is starting/i.test(error.message)) {
        throw error;
      }
      return null;
    });
    const download = await downloadPromise;
    if (download) {
      expect(download.suggestedFilename()).toMatch(/\.pdf$|\.zip$/i);
      return;
    }

    expect(response?.ok()).toBeTruthy();
    const contentType = response?.headers()["content-type"] ?? "";
    expect(contentType).toMatch(/pdf|octet-stream/i);
    return;
  }

  const downloadPromise = page.waitForEvent("download");
  await pathOrLocator.click();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toMatch(/\.pdf$|\.zip$/i);
}

async function downloadPdfBufferAndAssert(page, path) {
  const downloadPromise = page.waitForEvent("download", { timeout: 2000 }).catch(() => null);
  const response = await page.goto(path).catch((error) => {
    if (!/Download is starting/i.test(error.message)) {
      throw error;
    }
    return null;
  });
  const download = await downloadPromise;
  if (download) {
    expect(download.suggestedFilename()).toMatch(/\.pdf$/i);
    const downloadPath = await download.path();
    expect(downloadPath).toBeTruthy();
    return fs.readFileSync(downloadPath);
  }

  expect(response?.ok()).toBeTruthy();
  const contentType = response?.headers()["content-type"] ?? "";
  expect(contentType).toMatch(/pdf|octet-stream/i);
  const buffer = await response.body();
  expect(buffer.length).toBeGreaterThan(1000);
  return buffer;
}

module.exports = {
  owner,
  roles,
  allPermissionFlags,
  uniqueSuffix,
  uniqueNationalId,
  uniquePhone,
  todayPlus,
  dateTimeLocal,
  createTestPngBuffer,
  completeInitialSetup,
  signIn,
  signOut,
  ensureOwnerSignedIn,
  updateAdministrationBranding,
  createUser,
  changePassword,
  setUserActive,
  createVisitorPermit,
  createEmployeePermit,
  createVisit,
  expectAccessDeniedOrLogin,
  downloadPdfAndAssert,
  downloadPdfBufferAndAssert,
  submitForm,
  getAntiforgeryToken,
};
