namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    public static IReadOnlyList<TestScenario> GetAllScenarios()
    {
        return
        [
            Scenario(
                "Expired visitor can renew",
                fixture => ScenarioExpiredVisitorCanRenew(fixture.DbFactory, fixture.PermitService)
            ),
            Scenario(
                "Active visitor edit requires approval",
                fixture => ScenarioActiveVisitorEditRequiresApproval(fixture.PermitService)
            ),
            Scenario(
                "Active employee edit requires approval",
                fixture => ScenarioActiveEmployeeEditRequiresApproval(fixture.PermitService)
            ),
            Scenario(
                "Permit search finds records across supported fields",
                fixture =>
                    ScenarioPermitIndexSearchFindsRecordsAcrossSupportedFields(
                        fixture.Clock,
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService
                    )
            ),
            Scenario(
                "Visit date change requires approval",
                fixture => ScenarioVisitDateChangeRequiresApproval(fixture.VisitService)
            ),
            Scenario(
                "Visit companions persist and follow scan times",
                fixture => ScenarioVisitCompanionsPersistAndFollowScan(fixture.VisitService)
            ),
            Scenario(
                "Pending permits can be forwarded by department manager",
                fixture =>
                    ScenarioPendingPermitsCanBeForwardedByDepartmentManager(
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Linked general manager syncs administration settings and forwarding",
                fixture =>
                    ScenarioLinkedGeneralManagerSyncsAdministrationSettingsAndForwarding(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Administration general-manager replacement falls back when current manager leaves role",
                fixture =>
                    ScenarioAdministrationGeneralManagerReplacementFallsBackWhenCurrentManagerLeavesRole(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "General-manager existing-user assignment syncs administration and transfers previous manager",
                fixture =>
                    ScenarioGeneralManagerAssignmentExistingUserSyncsAdministrationAndTransfersPreviousManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "General-manager create-new assignment syncs administration",
                fixture =>
                    ScenarioGeneralManagerAssignmentCreateNewManagerSyncsAdministration(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Administration general-manager create-new post preserves checked active flag",
                fixture =>
                    ScenarioAdministrationAssignGeneralManagerCreateNewPostPreservesCheckedActiveFlag(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Initial system owner is created as super admin and survives upgrade",
                fixture => ScenarioInitialSystemOwnerIsCreatedAsSuperAdminAndSurvivesUpgrade()
            ),
            Scenario(
                "Legacy admin upgrade preserves marked super admin without renaming",
                fixture => ScenarioLegacyAdminUpgradePreservesMarkedSuperAdminWithoutRenaming()
            ),
            Scenario(
                "Upgrade does not promote privileged user when no super-admin flag exists",
                fixture => ScenarioUpgradeDoesNotPromotePrivilegedUserWhenNoSuperAdminFlagExists()
            ),
            Scenario(
                "Upgrade bootstrap suppresses first-run wizard for existing data",
                fixture => ScenarioUpgradeBootstrapSuppressesFirstRunWizardForExistingData()
            ),
            Scenario(
                "Legacy numeric bootstrap upgrade preserves existing username",
                fixture => ScenarioLegacyNumericBootstrapUpgradePreservesExistingUsername()
            ),
            Scenario(
                "Super admin rename keeps privileges and protected actions",
                fixture => ScenarioSuperAdminRenameKeepsPrivilegesAndProtectedActions()
            ),
            Scenario(
                "Super admin does not use hardcoded password backdoor",
                fixture => ScenarioSuperAdminDoesNotUseHardcodedPasswordBackdoor()
            ),
            Scenario(
                "Super admin can execute permit and visit approval",
                fixture =>
                    ScenarioSuperAdminCanExecutePermitAndVisitApproval(
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.VisitService,
                        fixture.AccessControlService
                    )
            ),
            Scenario(
                "Administration edit preserves automatic general-manager sync",
                fixture => ScenarioAdministrationEditPreservesAutomaticGeneralManagerSync()
            ),
            Scenario(
                "Department manager can load permit lists",
                fixture =>
                    ScenarioDepartmentManagerCanLoadPermitLists(
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Permit approval follows current department manager assignment",
                fixture =>
                    ScenarioPermitApprovalFollowsCurrentDepartmentManager(
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Department manager permit creation keeps scoped department approval route aligned",
                fixture =>
                    ScenarioDepartmentManagerPermitCreationKeepsScopedDepartmentApprovalRouteAligned(
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Visit approval follows current department manager assignment",
                fixture =>
                    ScenarioVisitApprovalFollowsCurrentDepartmentManager(
                        fixture.DbFactory,
                        fixture.VisitService
                    )
            ),
            Scenario(
                "Visit approval follows visit location instead of visited employee label",
                fixture =>
                    ScenarioVisitApprovalFollowsVisitLocationInsteadOfVisitedEmployeeLabel(
                        fixture.DbFactory,
                        fixture.VisitService
                    )
            ),
            Scenario(
                "Visit approval roles and permissions matrix is enforced",
                fixture =>
                    ScenarioVisitApprovalRolesAndPermissionsMatrixIsEnforced(
                        fixture.DbFactory,
                        fixture.VisitService,
                        fixture.AccessControlService
                    )
            ),
            Scenario(
                "Suspended visits endpoint requires authorization",
                fixture => ScenarioSuspendedVisitsEndpointRequiresAuthorization()
            ),
            Scenario(
                "Full delegation grants permit approval scope and audit",
                fixture =>
                    ScenarioFullDelegationGrantsPermitApprovalScopeAndAudit(
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.AccessControlService,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Custom visits-only delegation works",
                fixture =>
                    ScenarioCustomVisitsOnlyDelegationWorks(
                        fixture.DbFactory,
                        fixture.VisitService,
                        fixture.AccessControlService,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Custom permits-only delegation works",
                fixture =>
                    ScenarioCustomPermitsOnlyDelegationWorks(
                        fixture.DbFactory,
                        fixture.AccessControlService,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Delegation post actions bind nested editor prefix",
                fixture => ScenarioDelegationPostActionsBindNestedEditorPrefix()
            ),
            Scenario(
                "Expired delegation loses access automatically",
                fixture =>
                    ScenarioExpiredDelegationLosesAccessAutomatically(
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.AccessControlService,
                        fixture.DelegationService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Cancelled delegation loses access immediately",
                fixture =>
                    ScenarioCancelledDelegationLosesAccessImmediately(
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.AccessControlService,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Cannot delegate permissions not owned",
                fixture =>
                    ScenarioCannotDelegatePermissionsNotOwned(
                        fixture.DbFactory,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Delegation does not mutate direct permissions or department",
                fixture =>
                    ScenarioDelegationDoesNotMutateDirectPermissionsOrDepartment(
                        fixture.DbFactory,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "Scoped approvals still work with delegation",
                fixture =>
                    ScenarioScopedApprovalsStillWorkWithDelegation(
                        fixture.DbFactory,
                        fixture.VisitService,
                        fixture.AccessControlService,
                        fixture.DelegationService
                    )
            ),
            Scenario(
                "User manager assignment replaces previous manager and revokes permissions",
                fixture =>
                    ScenarioUserManagerAssignmentReplacesPreviousManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Department manager assignment syncs department and permissions",
                fixture =>
                    ScenarioDepartmentManagerAssignmentSyncsDepartmentAndPermissions(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Administration departments open general-manager wizard shows current state",
                fixture =>
                    ScenarioAdministrationDepartmentsOpenGeneralManagerWizardShowsCurrentState(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Users controller department-manager create redirects to handover when department is occupied",
                fixture =>
                    ScenarioUsersControllerCreateDepartmentManagerRedirectsToHandoverWhenDepartmentAlreadyHasManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller department-manager create stays direct when department is vacant",
                fixture =>
                    ScenarioUsersControllerCreateDepartmentManagerCreatesDirectlyWhenDepartmentHasNoManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller department-manager create shortcut preselects the department",
                fixture =>
                    ScenarioUsersControllerCreateDepartmentManagerPrefillsDepartmentFromDepartmentsShortcut(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller department-manager create shows current manager summary for occupied departments",
                fixture =>
                    ScenarioUsersControllerCreateDepartmentManagerShowsOccupiedDepartmentManagerSummary(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller general-manager create requires administration data",
                fixture =>
                    ScenarioUsersControllerCreateGeneralManagerRequiresConfiguredAdministration(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller general-manager create uses assignment workflow",
                fixture =>
                    ScenarioUsersControllerCreateGeneralManagerUsesAssignmentWorkflow(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller general-manager create redirects to transition when current manager exists",
                fixture =>
                    ScenarioUsersControllerCreateGeneralManagerRedirectsToTransitionWhenCurrentExists(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller manager creation requires general-manager actor",
                fixture =>
                    ScenarioUsersControllerManagerCreationRequiresGeneralManagerActor(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Users controller email remains optional without numeric fallback",
                fixture =>
                    ScenarioUsersControllerEmailRemainsOptionalAndDoesNotFallbackToNumericFields(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Apply role defaults during user create stays on create flow",
                fixture =>
                    ScenarioApplyRoleDefaultsDuringCreateStaysOnCreateFlow(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Administration departments prefill department handover draft from create redirect",
                fixture =>
                    ScenarioAdministrationDepartmentsPrefillsDepartmentManagerHandoverDraftFromCreateRedirect(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Department manager transfer requires replacement and keeps permissions aligned",
                fixture =>
                    ScenarioDepartmentManagerTransferRequiresReplacementAndReassignsPermissions(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Department manager handover archives retired manager and writes activity log",
                fixture =>
                    ScenarioDepartmentManagerHandoverRetirementArchivesPreviousManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Department manager handover internal transfer demotes previous manager cleanly",
                fixture =>
                    ScenarioDepartmentManagerHandoverInternalTransferDemotesPreviousManager(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Department manager handover internal transfer can reassign previous manager to another department",
                fixture =>
                    ScenarioDepartmentManagerHandoverInternalTransferCanReassignManagerToAnotherDepartment(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Department manager edit and deactivation require replacement first",
                fixture =>
                    ScenarioDepartmentManagerEditAndDeactivationRequireReplacementFirst(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Administration departments transfer screen enforces manager handover flow",
                fixture =>
                    ScenarioAdministrationDepartmentsTransferScreenEnforcesManagerHandoverFlow(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Administration handover post avoids unrelated department validation",
                fixture =>
                    ScenarioAdministrationHandoverPostAvoidsUnrelatedDepartmentValidation(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Administration general-manager post binds nested wizard request",
                fixture =>
                    ScenarioAdministrationAssignGeneralManagerPostBindsNestedWizardRequest(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Users controller lifecycle supports audit search and print",
                fixture =>
                    ScenarioUsersControllerLifecycleSupportsAuditSearchAndPrint(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "User session invalidates after account deactivation",
                fixture =>
                    ScenarioUserSessionInvalidatesAfterAccountDeactivation(fixture.UserAdminService)
            ),
            Scenario(
                "Users index uses unified action menu and mobile cards",
                fixture => ScenarioUsersIndexUsesUnifiedActionMenuAndMobileCards()
            ),
            Scenario(
                "Operational tables use hybrid icon actions",
                fixture => ScenarioOperationalTablesUseHybridIconActions()
            ),
            Scenario(
                "Administration tables use compact icon actions",
                fixture => ScenarioAdministrationTablesUseCompactIconActions()
            ),
            Scenario(
                "Gate display clears operator state and constrains wide permit details",
                fixture => ScenarioGateDisplayClearsOperatorStateAndConstrainsWideDetails()
            ),
            Scenario(
                "User editor live summary uses compact typography",
                fixture => ScenarioUserEditorLiveSummaryUsesCompactTypography()
            ),
            Scenario(
                "User editor review layout avoids empty cards and icon overlap",
                fixture => ScenarioUserEditorReviewLayoutAvoidsEmptyCardsAndIconOverlap()
            ),
            Scenario(
                "Print badges suppress browser URL header and footer",
                fixture => ScenarioPrintBadgesSuppressBrowserUrlHeaderFooter()
            ),
            Scenario(
                "Users controller wizard identity lookup and reactivate flow",
                fixture =>
                    ScenarioUsersControllerWizardIdentityLookupAndReactivateFlow(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Create, delete, and activate actions bridge to success toasts",
                fixture =>
                    ScenarioCreateDeleteAndActivateActionsBridgeToSuccessToasts(
                        fixture.DbFactory,
                        fixture.Clock,
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService
                    )
            ),
            Scenario(
                "Duplicate username validation bridges to error toast",
                fixture =>
                    ScenarioDuplicateUsernameValidationBridgesToErrorToast(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Validation toasts collapse multiple missing fields into one prompt",
                fixture => ScenarioValidationToastsCollapseMultipleMissingFieldsIntoOnePrompt()
            ),
            Scenario(
                "Toast service queues multiple notifications in order",
                fixture => ScenarioToastServiceQueuesMultipleNotificationsInOrder()
            ),
            Scenario(
                "Views contain no inline bootstrap alerts",
                fixture => ScenarioViewsContainNoInlineBootstrapAlerts()
            ),
            Scenario(
                "Shared layout keeps RTL notification shell",
                fixture => ScenarioSharedLayoutKeepsRtlNotificationShell()
            ),
            Scenario(
                "Access denied page bridges to error toast",
                fixture =>
                    ScenarioAccessDeniedPageBridgesToErrorToast(
                        fixture.PermitService,
                        fixture.VisitService,
                        fixture.ReportsDashboardService,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Super admin manual user edits persist permissions and replace the password",
                fixture =>
                    ScenarioManualUserEditPersistsPermissionsAndPassword(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "General manager cannot escalate users to privileged administrative access",
                fixture =>
                    ScenarioGeneralManagerCannotEscalateUsersToAdministrativeRolesOrPermissions(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Privileged roles persist manage delegations permission under super admin management",
                fixture =>
                    ScenarioGeneralManagerAndSystemAdminPersistManageDelegationsPermission(
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Legacy admin username can be edited and username tampering is blocked",
                fixture =>
                    ScenarioLegacyAdminUsernameCanBeEditedAndUsernameTamperingIsBlocked(
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Expired employee can renew",
                fixture => ScenarioExpiredEmployeeCanRenew(fixture.DbFactory, fixture.PermitService)
            ),
            Scenario(
                "Stopped visitor can reactivate",
                fixture => ScenarioStoppedVisitorCanReactivate(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "Stopped employee can reactivate",
                fixture =>
                    ScenarioStoppedEmployeeCanReactivate(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "Archived stopped permit can reactivate",
                fixture =>
                    ScenarioArchivedStoppedPermitCanReactivate(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Reactivate action reactivates permit and publishes success toast",
                fixture =>
                    ScenarioReactivateActionReactivatesPermitAndPublishesSuccessToast(
                        fixture.Clock,
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService
                    )
            ),
            Scenario(
                "Leave request with return allows exit and late return",
                fixture =>
                    ScenarioLeaveRequestWithReturnAllowsExitAndLateReturn(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "One-time leave request rejects past start time",
                fixture =>
                    ScenarioOneTimeLeaveRequestRejectsPastStartTime(
                        fixture.Clock,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "One-time no-return request works without end date",
                fixture =>
                    ScenarioOneTimeNoReturnRequestWorksWithoutEndDate(
                        fixture.Clock,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Entry-only one-time final exit resets next-day flow and blocks new work-hours exit",
                fixture =>
                    ScenarioEntryOnlyOneTimeFinalExitKeepsPolicyAcrossNextDay(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Leave policy service centralizes leave decisions",
                fixture =>
                    ScenarioLeavePolicyServiceCentralizesDecisions(
                        fixture.Clock,
                        fixture.ServiceProvider
                    )
            ),
            Scenario(
                "Gate policy service resolves scan decisions",
                fixture =>
                    ScenarioGatePolicyServiceResolvesDecisions(
                        fixture.Clock,
                        fixture.ServiceProvider
                    )
            ),
            Scenario(
                "Disabled leave requests use automatic employee movement",
                fixture =>
                    ScenarioDisabledLeaveRequestsUseAutomaticMovement(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Operator PIN provisioning stays separate from login password",
                fixture =>
                    ScenarioOperatorPinProvisioning(
                        fixture.UserAdminService,
                        fixture.DbFactory,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Public permit-number verification is closed and QR tokens backfill safely",
                fixture =>
                    ScenarioPublicPermitNumberVerificationIsClosedAndQrTokensBackfillSafely(
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService,
                        fixture.DbFactory,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Zebra label uses secure QR and plate only",
                fixture =>
                        ScenarioZebraLabelUsesSecureQrAndPlateOnly(
                        fixture.PermitService,
                        fixture.UserAdminService,
                        fixture.AccessControlService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Display device approval flow protects display data",
                fixture =>
                    ScenarioDisplayDeviceApprovalFlow(
                        fixture.DisplayDeviceService,
                        fixture.DbFactory,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Unified display settings use administration key and rotate links",
                fixture =>
                    ScenarioUnifiedDisplaySettingsUsesAdministrationKeyAndRotatesLinks(
                        fixture.DbFactory,
                        fixture.UserAdminService,
                        fixture.Clock
                    )
            ),
            Scenario(
                "Display sidebar has one entry and key rotation is protected",
                fixture => ScenarioDisplaySidebarHasSingleEntryAndRotationRequiresAdministration()
            ),
            Scenario(
                "Display operator endpoints are rate limited and return URLs stay local",
                fixture =>
                    ScenarioDisplayOperatorEndpointsAreRateLimitedAndReturnUrlsStayLocal(
                        fixture.UserAdminService,
                        fixture.PermitService,
                        fixture.VisitService,
                        fixture.DisplayDeviceService
                    )
            ),
            Scenario(
                "Display public links open registration for new browsers",
                fixture => ScenarioDisplayPublicLinksOpenRegistrationForNewBrowsers()
            ),
            Scenario(
                "Backup creates readable SQLite archive",
                fixture => ScenarioBackupCreatesReadableSqliteArchive()
            ),
            Scenario(
                "Backup restore verifies and restores SQLite archive",
                fixture => ScenarioBackupRestoreVerifiesAndRestoresSqliteArchive()
            ),
            Scenario(
                "Backup restore rejects corrupt archive and keeps current database",
                fixture => ScenarioBackupRestoreRejectsCorruptArchiveAndKeepsCurrentDatabase()
            ),
            Scenario(
                "Installer probe treats legacy database as upgrade candidate",
                fixture => ScenarioInstallerProbeTreatsLegacyDatabaseAsUpgradeCandidate()
            ),
            Scenario(
                "Prepare-upgrade backs up legacy database and config",
                fixture => ScenarioPrepareUpgradeBacksUpLegacyDatabaseAndConfig()
            ),
            Scenario(
                "Operator note duplicate submissions are suppressed briefly",
                fixture =>
                    ScenarioOperatorNoteDuplicateWindowSuppression(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Overnight work hours block unauthorized entry-only exit and keep overnight return deadline",
                fixture =>
                    ScenarioOvernightWorkHoursEnforceEntryOnlyPolicy(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Official workdays propagate to exit policy, daily schedule, and work-end closure",
                fixture =>
                    ScenarioOfficialWorkDaysPropagateAcrossPermitFlows(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Daily scheduled leave with return authorizes exit and keeps schedule",
                fixture =>
                    ScenarioDailyScheduledLeaveWithReturn(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Daily scheduled leave without return authorizes final exit and keeps schedule",
                fixture =>
                    ScenarioDailyScheduledLeaveWithoutReturn(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "Daily scheduled leave can be cleared while permit is inside",
                fixture =>
                    ScenarioDailyScheduledLeaveCanBeCleared(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "Duplicate permit scans are ignored within the protection window",
                fixture =>
                    ScenarioDuplicatePermitScansAreIgnored(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "Full return employee keeps base return requirement after scan cycle",
                fixture =>
                    ScenarioFullReturnEmployeeKeepsBaseReturnRequirement(
                        fixture.Clock,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "FullAccess multiple entries and exits stay allowed in the same day",
                fixture =>
                    ScenarioFullAccessMultipleEntriesAndExitsSameDayAllowed(
                        fixture.Clock,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "FullAccess never auto stops from passive reads",
                fixture => ScenarioFullAccessNeverAutoStopped(fixture.Clock, fixture.PermitService)
            ),
            Scenario(
                "FullAccess logs movements only in permit and audit logs",
                fixture =>
                    ScenarioFullAccessLogsMovementsOnly(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly multiple same-day violations count once",
                fixture =>
                    ScenarioEntryOnlyMultipleSameDayViolationsCountOnce(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly three distinct violation days escalate to stop",
                fixture =>
                    ScenarioEntryOnlyThreeDistinctViolationDaysEscalate(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly same-day return does not duplicate the violation day",
                fixture =>
                    ScenarioEntryOnlySameDayReturnDoesNotDuplicateViolationDay(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Forgot checkout auto closes the previous day with a penalty",
                fixture =>
                    ScenarioForgotToCheckoutAutoClosesPreviousDaySessionWithPenalty(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly forgot checkout recovers after work hours change",
                fixture =>
                    ScenarioEntryOnlyForgotCheckoutAfterWorkHoursChangeStillRecovers(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly stale pending exit recovers on next-day entry",
                fixture =>
                    ScenarioEntryOnlyStalePendingExitRecoversOnNextDayEntry(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly previous-day inside with current-day pending recovers",
                fixture =>
                    ScenarioEntryOnlyPreviousDayInsideWithCurrentDayPendingRecovers(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "EntryOnly overdue leave return counts as a penalty",
                fixture =>
                    ScenarioEntryOnlyOverdueLeaveReturnCountsAsPenalty(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Reading permit data does not create penalties",
                fixture =>
                    ScenarioReadingPermitDataDoesNotCreatePenalties(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
            Scenario(
                "Unauthorized exit becomes pending then closes at work end",
                fixture =>
                    ScenarioUnauthorizedExitPendingClosesAtWorkEnd(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Super admin creates the first general manager inside the selected tenant",
                fixture =>
                    ScenarioSuperAdminCreatesGeneralManagerInsideSelectedTenant(
                        fixture.DbFactory,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Pending unauthorized exit appears as a rejection in monitoring",
                fixture =>
                    ScenarioPendingUnauthorizedExitAppearsInMonitoring(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.MonitoringDashboardService
                    )
            ),
            Scenario(
                "Expired permit clears pending unauthorized exit state",
                fixture =>
                    ScenarioExpiredPermitClearsPendingUnauthorizedExit(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Pending unauthorized exit escalates to review and stop",
                fixture =>
                    ScenarioPendingUnauthorizedExitEscalatesToReviewAndStop(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Stopped permits dashboard surfaces direct action queue",
                fixture =>
                    ScenarioStoppedPermitsDashboardSurfacesDirectActionQueue(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
            Scenario(
                "Unauthorized-exit workflow dashboard categorizes all stages",
                fixture =>
                    ScenarioUnauthorizedExitWorkflowDashboardCategorizesAllStages(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
            Scenario(
                "Notification center aggregates direct-open operational queues",
                fixture =>
                    ScenarioNotificationCenterAggregatesOperationalQueues(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
            Scenario(
                "Administrative review can dismiss unresolved exit",
                fixture =>
                    ScenarioAdministrativeReviewCanDismiss(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Administrative review can confirm unresolved exit",
                fixture =>
                    ScenarioAdministrativeReviewCanConfirm(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Confirm + No keeps permit stopped and shows status message",
                fixture =>
                    ScenarioConfirmNoKeepsPermitStoppedAndPublishesStatusMessage(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService,
                        fixture.AccessControlService,
                        fixture.UserAdminService
                    )
            ),
            Scenario(
                "Reactivate later works after confirmed violation stop",
                fixture =>
                    ScenarioReactivateLaterWorksAfterConfirmedViolationStop(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Work end closure exits inside employees once",
                fixture =>
                    ScenarioWorkEndClosureExitsInsideEmployeesOnce(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Attendance grace records late attendance in activity report",
                fixture =>
                    ScenarioAttendanceGraceRecordsLateAttendance(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
            Scenario(
                "After-hours scans do not record attendance violations",
                fixture =>
                    ScenarioAfterHoursScansDoNotRecordAttendanceViolations(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService
                    )
            ),
            Scenario(
                "Work end closure waits for checkout grace and marks late checkout",
                fixture =>
                    ScenarioWorkEndClosureWaitsForCheckoutGraceAndMarksLateCheckout(
                        fixture.Clock,
                        fixture.DbFactory,
                        fixture.PermitService,
                        fixture.ReportsDashboardService
                    )
            ),
        ];
    }

    private static TestScenario Scenario(string name, Func<TestFixture, Task> execute)
    {
        return new TestScenario(name, execute);
    }
}
