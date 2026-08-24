const { expect, test } = require("@playwright/test");
const {
  changePassword,
  createUser,
  ensureTenantManagerSignedIn,
  expectAccessDeniedOrLogin,
  getTenantManagerAccount,
  roles,
  setUserActive,
  signIn,
  signOut,
  submitForm,
  uniqueNationalId,
  uniquePhone,
} = require("./helpers/e2e-helpers");

test("tenant manager manages users and temporary credentials", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);

  await page.goto("/Users");
  await expect(page.getByRole("heading", { name: "فريق التشغيل" })).toBeVisible();
  await expect(page.locator("#users-tenant-filter")).toHaveCount(0);
  await expect(page.locator("[data-tenant-group-header]")).toHaveCount(0);
  const manager = getTenantManagerAccount();
  await expect(page.locator("[data-user-row]")).toHaveAttribute("data-tenant-id", manager.tenantId);

  await page.setViewportSize({ width: 390, height: 844 });
  const mobileTableLayout = await page.locator(".users-index-table").evaluate((table) => {
    const firstUserRow = table.querySelector("[data-user-row]");
    const firstUserCell = firstUserRow?.querySelector("td");

    return {
      hasHorizontalOverflow: table.scrollWidth > table.clientWidth + 1,
      rowDisplay: firstUserRow ? getComputedStyle(firstUserRow).display : "",
      cellDisplay: firstUserCell ? getComputedStyle(firstUserCell).display : "",
    };
  });
  expect(mobileTableLayout).toEqual({
    hasHorizontalOverflow: false,
    rowDisplay: "block",
    cellDisplay: "block",
  });
  await page.setViewportSize({ width: 1280, height: 720 });

  const receptionist = await createUser(page, { role: roles.receptionist });
  await expect(page.getByText("كلمة المرور المؤقتة")).toBeVisible();

  await signOut(page);
  await signIn(page, receptionist.username, receptionist.temporaryPassword);
  await expect(page).toHaveURL(/\/Account\/ChangePassword/i);
  const receptionistPassword = `Aa${uniqueNationalId("7")}!`;
  await changePassword(page, receptionist.temporaryPassword, receptionistPassword);

  await signOut(page);
  await signIn(page, manager.username, manager.password, manager.tenantId);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);

  const gate = await createUser(page, { role: roles.gateSecurity });
  const reviewer = await createUser(page, { role: roles.permitReviewer });
  expect(gate.temporaryPassword).toBeTruthy();
  expect(reviewer.temporaryPassword).toBeTruthy();

  await setUserActive(page, receptionist.username, false);
  await signOut(page);
  await signIn(page, receptionist.username, receptionistPassword);
  await expect(page).toHaveURL(/\/Account\/Login/i);
  await expect(page.getByText("اسم المستخدم أو كلمة المرور غير صحيحة")).toBeVisible();

  await signIn(page, manager.username, manager.password, manager.tenantId);
  const reactivatedPassword = await setUserActive(page, receptionist.username, true);
  await signOut(page);
  await signIn(page, receptionist.username, reactivatedPassword);
  await expect(page).toHaveURL(/\/Account\/ChangePassword/i);
});

test("role permissions protect direct user and administration URLs", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  const receptionist = await createUser(page, { role: roles.receptionist });
  const receptionistPassword = `Aa${uniqueNationalId("7")}!`;

  await signOut(page);
  await signIn(page, receptionist.username, receptionist.temporaryPassword);
  await changePassword(page, receptionist.temporaryPassword, receptionistPassword);

  await expectAccessDeniedOrLogin(page, "/Users");
  await expectAccessDeniedOrLogin(page, "/Administration/Edit");
});

test("current tenant manager account is protected from normal deactivate flow", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  const manager = getTenantManagerAccount();

  await page.goto("/Users");
  await expect(page.locator("table").getByText(manager.username, { exact: true })).toBeVisible();
  await setUserActive(page, manager.username, false);
  await expect(page.getByRole("alert").getByText(/لا يمكن إيقاف حساب مالك النظام|لا يمكن إيقاف الحساب الحالي/).first()).toBeVisible();

  await signOut(page);
  await signIn(page, manager.username, manager.password, manager.tenantId);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
});

test("simplified create exposes only operational roles and applies defaults", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  await page.goto("/Users/Create");

  const accountPanelSurface = await page.locator(".ue-card[data-users-panel-container]").evaluate((panel) => ({
    background: getComputedStyle(panel).backgroundColor,
    borderWidth: getComputedStyle(panel).borderTopWidth,
    boxShadow: getComputedStyle(panel).boxShadow,
  }));
  expect(accountPanelSurface).toEqual({
    background: "rgba(0, 0, 0, 0)",
    borderWidth: "0px",
    boxShadow: "none",
  });

  await page.locator('[name="FullName"]').fill("مستخدم مسار مبسط");
  await page.locator('[name="Username"]').fill(`simple${Date.now()}`);
  await page.locator('[name="PhoneNumber"]').fill("0551239876");
  await page.getByRole("button", { name: "التالي" }).click();

  const roleSelect = page.locator("#users-role-select");
  await expect(roleSelect).toBeVisible();
  await expect(roleSelect.locator("option")).toHaveCount(5);
  await expect(roleSelect.locator(`option[value="${roles.securityManager}"]`)).toHaveCount(1);
  await expect(roleSelect.locator(`option[value="${roles.permitReviewer}"]`)).toHaveCount(1);
  await expect(roleSelect.locator(`option[value="${roles.manager}"]`)).toHaveCount(0);
  await expect(roleSelect.locator(`option[value="${roles.generalManager}"]`)).toHaveCount(0);
  await expect(roleSelect.locator(`option[value="${roles.employee}"]`)).toHaveCount(0);

  await roleSelect.selectOption(roles.permitReviewer);
  await expect(page.locator("#CanEditPermit")).toBeChecked();
  await expect(page.locator("#CanApprovePermit")).not.toBeChecked();
});

test("company administrator can only create users inside the signed-in tenant", async ({ page }) => {
  await ensureTenantManagerSignedIn(page);
  const manager = getTenantManagerAccount();
  await page.goto("/Users/Create");

  await expect(page.locator('select[name="TenantId"]')).toHaveCount(0);
  await expect(page.locator('input[type="hidden"][name="TenantId"]')).toHaveValue(manager.tenantId);
  await expect(page.locator("#users-tenant-select option")).toHaveCount(0);

  const targetUsername = `scope${Date.now()}`;
  await submitForm(page, "/Users/Create", {
    Username: targetUsername,
    FullName: "مستخدم نطاق الجهة",
    PhoneNumber: uniquePhone(),
    JobTitle: "موظف تشغيل",
    EmployeeNumber: `TENANT-${Date.now()}`,
    TenantId: "tampered-foreign-tenant",
    Role: roles.receptionist,
    IsActive: "true",
    ApplyRoleDefaults: "true",
    WizardStep: "3",
    MustChangeOperatorPin: "true",
  });

  await expect(page.getByText("بيانات الدخول المؤقتة للمستخدم الجديد")).toBeVisible();

  await signOut(page);
  await signIn(page, manager.username, manager.password, manager.tenantId);
  await page.goto("/Users");
  await page.locator("#users-table-search").fill(targetUsername);
  const createdRow = page.locator('[data-user-row]', { hasText: targetUsername }).first();
  await expect(createdRow).toBeVisible();
  await expect(createdRow).toHaveAttribute("data-tenant-id", manager.tenantId);
});
