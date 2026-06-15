const { expect, test } = require("@playwright/test");
const {
  changePassword,
  createUser,
  ensureOwnerSignedIn,
  expectAccessDeniedOrLogin,
  owner,
  roles,
  setUserActive,
  signIn,
  signOut,
  uniqueNationalId,
} = require("./helpers/e2e-helpers");

test("owner manages users and temporary credentials", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Users");
  await expect(page.getByRole("heading", { name: "المستخدمون والصلاحيات" })).toBeVisible();

  const receptionist = await createUser(page, { role: roles.receptionist });
  await expect(page.getByText("كلمة المرور المؤقتة")).toBeVisible();

  await signOut(page);
  await signIn(page, receptionist.username, receptionist.temporaryPassword);
  await expect(page).toHaveURL(/\/Account\/ChangePassword/i);
  const receptionistPassword = `Aa${uniqueNationalId("7")}!`;
  await changePassword(page, receptionist.temporaryPassword, receptionistPassword);

  await signOut(page);
  await signIn(page, owner.username, owner.password);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);

  const gate = await createUser(page, { role: roles.gateSecurity });
  const manager = await createUser(page, { role: roles.manager });
  expect(gate.temporaryPassword).toBeTruthy();
  expect(manager.temporaryPassword).toBeTruthy();

  await setUserActive(page, receptionist.username, false);
  await signOut(page);
  await signIn(page, receptionist.username, receptionistPassword);
  await expect(page).toHaveURL(/\/Account\/Login/i);
  await expect(page.getByText("اسم المستخدم أو كلمة المرور غير صحيحة")).toBeVisible();

  await signIn(page, owner.username, owner.password);
  const reactivatedPassword = await setUserActive(page, receptionist.username, true);
  await signOut(page);
  await signIn(page, receptionist.username, reactivatedPassword);
  await expect(page).toHaveURL(/\/Account\/ChangePassword/i);
});

test("role permissions protect direct user and administration URLs", async ({ page }) => {
  await ensureOwnerSignedIn(page);
  const receptionist = await createUser(page, { role: roles.receptionist });
  const receptionistPassword = `Aa${uniqueNationalId("7")}!`;

  await signOut(page);
  await signIn(page, receptionist.username, receptionist.temporaryPassword);
  await changePassword(page, receptionist.temporaryPassword, receptionistPassword);

  await expectAccessDeniedOrLogin(page, "/Users");
  await expectAccessDeniedOrLogin(page, "/Administration/Edit");
});

test("owner account is protected from normal deactivate flow", async ({ page }) => {
  await ensureOwnerSignedIn(page);

  await page.goto("/Users");
  await expect(page.locator("table").getByText(owner.username, { exact: true })).toBeVisible();
  await setUserActive(page, owner.username, false);
  await expect(page.getByRole("alert").getByText(/لا يمكن إيقاف حساب مالك النظام|لا يمكن إيقاف الحساب الحالي/).first()).toBeVisible();

  await signOut(page);
  await signIn(page, owner.username, owner.password);
  await expect(page).not.toHaveURL(/\/Account\/Login/i);
});
