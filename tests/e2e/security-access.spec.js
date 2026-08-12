const { expect, test } = require("@playwright/test");
const {
  changePassword,
  createEmployeePermit,
  createUser,
  ensureOwnerSignedIn,
  expectAccessDeniedOrLogin,
  roles,
  signIn,
  signOut,
  submitForm,
  uniqueNationalId,
  uniqueSuffix,
} = require("./helpers/e2e-helpers");

async function createReadyUser(page, role, options = {}) {
  const user = await createUser(page, { role, ...options });
  const password = `Aa${uniqueNationalId("9")}!`;
  await signOut(page);
  await signIn(page, user.username, user.temporaryPassword);
  await changePassword(page, user.temporaryPassword, password);
  await signOut(page);
  return { ...user, password };
}

async function createManagedDepartment(page, departmentName, managerUsername) {
  await ensureOwnerSignedIn(page);
  await submitForm(
    page,
    "/Administration/Departments",
    {
      "Department.Id": "0",
      "Department.Name": departmentName,
      "Department.IsActive": "true",
    },
    { tokenPath: "/Administration/Departments" }
  );

  await page.goto("/Administration/Departments");
  const row = page.locator("tr", { hasText: departmentName }).first();
  await expect(row).toBeVisible();
  const editHref = await row
    .getByRole("link", { name: new RegExp(`تعديل القسم ${departmentName}`) })
    .getAttribute("href");
  const editUrl = new URL(editHref, "http://127.0.0.1");
  const departmentId =
    editUrl.searchParams.get("id") ?? editUrl.pathname.match(/\/Leadership\/(\d+)/i)?.[1];
  expect(departmentId).toBeTruthy();

  await submitForm(
    page,
    "/Administration/AssignDepartmentManager",
    {
      ManagerDepartmentId: departmentId,
      ManagerUsername: managerUsername,
    },
    { tokenPath: "/Administration/Departments" }
  );
}

test("anonymous users are redirected to login for protected pages", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await signOut(page);
  await page.context().clearCookies();
  for (const path of [
    "/Users",
    "/Administration/Edit",
    "/Permits",
    "/Visits",
    "/Reports",
    "/People",
    "/People/Details/1",
    "/People/Photo/1",
    "/Attendance",
    "/Sites",
  ]) {
    await page.goto(path);
    await expect(page).toHaveURL(/\/Account\/Login/i);
  }
});

test("Receptionist, GateSecurity, and Employee direct URL permissions are enforced", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  const receptionist = await createReadyUser(page, roles.receptionist);
  await signIn(page, receptionist.username, receptionist.password);
  await expectAccessDeniedOrLogin(page, "/Users");
  await expectAccessDeniedOrLogin(page, "/Administration/Edit");
  await expectAccessDeniedOrLogin(page, "/Administration/Departments");

  await ensureOwnerSignedIn(page);
  const gate = await createReadyUser(page, roles.gateSecurity);
  await signIn(page, gate.username, gate.password);
  await expectAccessDeniedOrLogin(page, "/Users");
  await expectAccessDeniedOrLogin(page, "/Administration/Edit");

  await ensureOwnerSignedIn(page);
  const employee = await createReadyUser(page, roles.employee);
  await signIn(page, employee.username, employee.password);
  await expectAccessDeniedOrLogin(page, "/Permits/Create");
});

test("reviewer forwards employee permit and security manager approves it", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  await page.goto("/Administration/Edit");
  await page.locator('[name="SignatureText"]').fill("توقيع اعتماد المدير");
  await page.getByRole("button", { name: "حفظ البيانات" }).click();
  await page.getByRole("button", { name: "متابعة" }).click({ timeout: 2000 }).catch(() => {});

  const departmentName = `موقع اختبار الأمن ${uniqueSuffix()}`;
  const securityManager = await createReadyUser(page, roles.securityManager, {
    department: departmentName,
  });
  const reviewer = await createReadyUser(page, roles.permitReviewer, {
    department: departmentName,
  });
  const permit = await createEmployeePermit(page, {
    departmentName,
  });

  await signIn(page, reviewer.username, reviewer.password);
  await submitForm(page, `/Permits/ForwardToGeneralManager/${permit.permitNumber}`, {}, {
    tokenPath: `/Permits/Details/${permit.permitNumber}`,
  });

  await signOut(page);
  await signIn(page, securityManager.username, securityManager.password);
  await page.goto(`/Permits/Approve/${permit.permitNumber}`);
  await expect(page.getByRole("heading", { name: "اعتماد التصريح" })).toBeVisible();
});
