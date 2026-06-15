# Changelog

## 2026-06-16
- Added an isolated PostgreSQL provider path for the online edition while keeping SQLite as the local development default.
- Added the initial PostgreSQL EF Core migration, automatic migration bootstrap, environment-based connection guidance, and an Arabic execution schedule for the online product.
- Upgraded EF Core and the Npgsql provider to `8.0.11`, verified that the dependency graph contains no known vulnerable packages, and retained transitional local-time compatibility until the planned UTC normalization phase.
- Removed personal-looking credentials and names from automated test fixtures used by the online-development repository.

## 2026-04-23
- Reserved `1.0.7` as the next local test version by updating the application assembly/package version in `VehiclePermitSystemWeb.csproj` and moving the Windows packaging entrypoints (`package-release.ps1`, `package-all.ps1`, `build-setup.ps1`, `package-update-test.ps1`) to `1.0.7` by default.
- Updated the Windows packaging operator guide so the published command examples now target `1.0.7` consistently for local release, installer, full deliverables, and update-test package generation.
- Documented the latest completed work and the remaining delivery tracks in dedicated project documentation, with the current remainder explicitly grouped under interface polish, dynamic behavior refinement, security hardening, and preparing a stable server-ready working release.

## 2026-04-22
- Added a super-admin-only `نقل المدير الحالي` workflow to departments management so current manager handover now runs through a numbered popup flow instead of the old open-ended manager-assignment card. The new flow captures `تثبيت/تكليف`, whether the replacement is an existing account or a brand-new manager, and the outgoing manager disposition (`تقاعد`, `استقالة`, `إنهاء تكليف`, `نقل خارجي`, `نقل داخلي`).
- Extended department-manager handover business rules so the previous manager now loses the source-department manager powers immediately and is processed according to the selected exit action: retirement/resignation/external transfer archive the account by deactivating it while preserving activity history, end-assignment returns the user to the same department in a non-manager role, and internal transfer can either demote the user into another department or reassign them as the manager of another empty department.
- Added executable behavior coverage for the new manager-handover paths, including retirement archival with activity logging, internal transfer with demotion and permission removal, and internal transfer that reassigns the outgoing manager to another department as its new manager; `run-permit-behavior-checks` now passes with the new scenarios included.
- Fixed the temporary delegations editor so the selected `المفوِّض` and `المفوَّض إليه` values now post correctly back to `DelegationsController` even though the form is rendered from the nested `Editor` model, which removes the false `اختر المفوَّض إليه` validation error that was appearing after changing dates or submitting an already selected delegatee.
- Updated the delegations editor picker behavior so the same user is no longer available simultaneously in both `المفوِّض` and `المفوَّض إليه`: once one side is selected, that username is hidden/disabled on the opposite side, and server-side validation now also rejects self-delegation with a field-level Arabic error if someone bypasses the UI.
- Updated the Windows Inno Setup installer so it now creates `C:\ProgramData\Vehicle Permit System` and `C:\ProgramData\Vehicle Permit System\data` explicitly through the `[Dirs]` section, with `Permissions: users-modify` applied to the SQLite data directory to allow writable `db`, `wal`, and `shm` files from first launch without any post-install PowerShell or manual ACL steps.
- Rebuilt the Windows installer successfully after the ACL change using `packaging\windows\build-setup.ps1 -Version 1.0.4`, producing a fresh `VehiclePermitSystem-Setup-1.0.4.exe` plus timestamped setup output in the `publish\Releases\VehiclePermitSystem-1.0.4-win-x64\installer` folder.
- Validation popups now collapse repeated required-field failures into one smart toast instead of stacking one popup per missing field: multiple missing required fields show `أكمل الحقول الإلزامية المطلوبة.` while a single missing field still keeps its specific Arabic message such as the missing date/phone/identity field; the shared layout no longer emits any fallback validation toast when `ModelState` is clean.
- Revalidated installer upgrade safety with the available Windows packages by exercising the `1.0.3 -> 1.0.4` setup path through the installer scripts: the upgrade-preparation phase created database and runtime-config backups under `C:\ProgramData\Vehicle Permit System\backups\installer-upgrades`, the service came back on `http://localhost:5000`, and the temporary test installation was removed afterward.
- Rebuilt the corrupted top section of `Views/Shared/_Layout.cshtml` after the toast-layout refactor, keeping notification aggregation local to the layout while restoring a valid `head/body` structure, the shared toast container, and a clean `dotnet build`.
- Resolved the lingering editor-side `CS0246` lookup failure in `UsersController` for `ToastNotificationItem` by changing that controller's TempData queue serialization to a private local payload record with the same JSON shape; runtime behavior stayed unchanged while the file's Roslyn diagnostics returned clean.
- Cleaned up repeated and mixed-language validation feedback, starting with the users editor: secondary actions such as `إعادة صلاحيات الدور` and temporary reset buttons no longer trigger full-form validation, the duplicate page-level user-editor popup layer was removed, optional user-editor fields no longer generate implicit English `required` errors, and remaining default validation annotations used by live models were explicitly Arabicized.
- Completed the second phase of notification unification at the controller source level: remaining user/admin/report/permit feedback paths were migrated off legacy page-message keys into the shared toast queue, including protected super-admin actions, permit reactivation, and daily-schedule clearing, so controller behavior now matches the single shared popup renderer instead of relying on bridge-only fallback behavior.
- Stabilized the `PermitBehaviorChecks` controller harness for request-scoped toast flows by wiring request services/TempData consistently and updating toast assertions to read the shared queued notification payload with web JSON defaults; final validation finished green with both `dotnet build` and `dotnet run --project .\tests\PermitBehaviorChecks\PermitBehaviorChecks.csproj` passing after the direct-controller migration.
- Unified operational feedback around the new shared toast notification path: login/display/scan flows now publish through the same popup system, remaining inline Bootstrap alert banners were removed from Razor views, hidden validation summaries are bridged into toasts, `AccessDenied` now raises the same error toast on entry, and the shared layout remains the single notification renderer in RTL.
- Added missing success feedback for delete actions that previously completed silently: permit deletion and visit deletion now emit the same unified popup success message style as the rest of the system.
- Expanded behavior checks for notifications by covering success-toast bridging on create/delete/activate flows, duplicate-username error toasts from model-state validation, ordered toast queue behavior, the absence of inline Bootstrap alerts in views, and the RTL notification shell in the shared layout; `dotnet build` and `run-permit-behavior-checks` both passed after the change.
- Hardened the permits index search after update installs by normalizing comparison values across the supported search fields, so lookups by permit holder name, permit number, and national ID continue to work even when the entered identity number uses Arabic digits; added a behavior-check regression that exercises those search paths.
- Fixed the printed permit-activity report layout so the table column count now matches the rendered headers and row cells, and localized confusing raw activity classifications/source labels into clearer Arabic display values in the PDF output.
- Updated the unauthorized-exit workflow decision path so confirming a violation now asks whether the permit should also be reactivated immediately; choosing `لا` leaves the permit stopped with a clear status message, while choosing `نعم` confirms the violation and then reactivates the permit in the same flow.
- Clarified the `Unauthorized Exit Workflow` action language and feedback: the review action now reads `اعتماد المخالفة فقط`, the reactivation action now reads `إعادة تفعيل التصريح`, the page explains that confirming a violation does not reactivate the permit, and both review resolution plus reactivation now emit the shared popup toast style with distinct success/failure messages.
- Fixed the users create flow so it stays on the real create screen instead of reusing `Edit.cshtml`, which had been making new-user attempts look like editing an existing account; the users editor warnings/errors now surface as the same popup-style notifications used elsewhere in the UI.
- Produced and validated a new Windows release package `1.0.4` locally: `validate-gate` and `PermitBehaviorChecks` both passed, `package-all.ps1` built the ZIP and Inno Setup installer successfully, and `package-update-test.ps1` generated update-test notes in the `deliverables` folder for trying the package on another device.
- Reviewed the Windows packaging files for the current release cycle, aligned the packaging README with the live super-admin bootstrap behavior, and advanced the packaging script defaults to `1.0.4` for the next distributable build.
- Strengthened the visual centering of the `بيانات الحساب` card on `بياناتي`: the card header and the user-data stack now sit in a centered inner column, and LTR values like username/phone/email are centered inside that card instead of inheriting left alignment.
- Expanded `بياناتي` to use the full available content width and added a self-service operator action for accounts with scan/gate access: the page now exposes `إعادة تعيين PIN المؤقت` directly from the gate-access credential card, with success/error feedback shown on the profile page after reset.
- Rebuilt `بياناتي` to match the read-only user presentation style more closely: a professional top summary panel, three information cards (`بيانات الحساب`, `بيانات الاعتماد`, `الربط الإداري`), and grouped permission boxes that are display-only. The profile now surfaces account status, email, manager link, login-password status, and gate PIN status for users who hold scan/operation access.
- Refreshed `بياناتي` and `تغيير الرقم السري` into modern colored card layouts and added a visible list of the permissions owned by the current account.
- Separated the signed-in account from the user-management editor: `بياناتي` is now the dedicated self page, and `Users/Edit` is reserved for managing other users only.
- Fixed the department dropdown clipping inside the users editor, removed the duplicate username field from edit-mode account details, and moved the visible username badge next to the hero avatar.
- Added a final display-side guard so any readonly duplicate username field stays hidden in the edit account row.
- Removed the extra readonly helper text under the username field and converted the user-status control into a smaller activation toggle with clearer active/inactive feedback.
- Refreshed the current users edit page layout to use the page width better, tighten empty spacing, reduce oversized fields and buttons, center helper text, and improve responsive collapse without changing permissions or behavior.
- Matched the normal users edit page to the account-display sizing by keeping four top account fields visible and making the status/role/department strip use equal-width columns.
- Centered the content inside the self-edit readonly account cards so the label, value, and helper text are visually aligned in the middle.
- Locked the self-edit identity fields in the users editor to display-only mode and added a server-side restore guard so users cannot change those account data fields from `بياناتي`.
- Reduced the main account field sizing in the users editor to a more compact, better-aligned layout while keeping the overall page width unchanged.
- Restored the previous larger `بياناتي` appearance and rolled back the user-editor card sizing reduction so the account data area no longer feels artificially shrunken.
- Matched the users edit screen width to the personal `بياناتي` page and converted the profile summary into fixed-size data cards so both screens now use the same visual scale and more stable box sizing.
- Self-edit now keeps the permissions card visible as read-only, and accounts that already have gate/scan access can still update their own temporary gate PIN without regaining administrative permission editing.
- Fixed the users editor client-side permission refresh script after the self-edit layout changes.
- Refined the users edit screen layout to use a wider, more balanced card arrangement with less oversized administrative controls.
- Editing the current signed-in account now allows personal data and password updates only; role, department, activation state, permissions, manager binding, and gate settings are read-only and enforced server-side.
- Replaced the previous Arabic `SystemAdmin` label with `مشرف نظام` in runtime role-display helpers and the user editor UI.
- Added a sovereign `Super Admin` identity model based on `UserAccount.IsSuperAdmin` instead of relying on role text alone.
- Fresh installs now create the first bootstrap account as the protected system owner account, with forced password change at first sign-in.
- Existing installations now normalize to exactly one super admin during SQLite bootstrap upgrades without creating duplicate admin accounts and without breaking the preserved password.
- Login/auth wiring now emits a dedicated `super_admin` claim and uses super-admin-aware display naming in account/profile flows.
- User-management flows now protect the super admin from ordinary deactivation and temporary password reset actions, while allowing protected username rename with reference migration.
- Direct access-control checks now treat the super admin as a full-authority bypass for permit and visit visibility/approval scope.
- UI updates now surface the system-owner state in initial setup, user editing, user listing, and protected permit actions.
- Added executable regression coverage for fresh install super-admin creation, legacy admin promotion during upgrade, no duplicate bootstrap admin creation, password preservation across upgrade, protected rename with retained privileges, and survival across future upgrades.

## 2026-04-16
- Fixed JavaScript error `escapeHtml is not defined` in `Views/ScanConsole/Index.cshtml` by adding a robust `escapeHtml` helper and using it consistently when rendering permit data in the client.
- Fixed plate number input validation regex to a safer and browser-compatible pattern in:
  - `Views/Permits/Create.cshtml`
  - `Views/Permits/Edit.cshtml`
  The new pattern accepts Arabic letters, ASCII and Arabic-Indic digits, spaces, and hyphens: `^[A-Za-z\u0621-\u064A0-9\u0660-\u0669\u06F0-\u06F9\s-]{3,12}$`.
- Added `OfficialWorkDaysCsv` property to `Models/AdministrationSettings` and UI in `Views/Administration/Edit.cshtml` to manage the official work days (Saturday..Friday) with checkboxes and saving support.
- Enforced presence of manager's signature (image or signature text) in administration settings before allowing approvals:
  - Approving permits (`PermitsController.Approve`) now blocks and shows a clear error if signature is missing.
  - Approving/recording leave requests (`PermitsController.LeaveRequest` POST) now blocks and shows a clear error if signature is missing.
- Cleaned notification dropdown items in `Views/Shared/_Layout.cshtml` by removing duplicate icons/images inside menu items and keeping the toolbar icons only; this simplifies and declutters the UI.

Notes:
- Database schema mapping updated to include `OfficialWorkDaysCsv` (max length 256) in `Data/ApplicationDbContext`.
- All changes follow safe defaults and preserve backward compatibility of stored permits.

Please run the application and test the modified screens (Administration, Permits create/edit, Scan Console, Notifications) to verify visuals and behavior.
