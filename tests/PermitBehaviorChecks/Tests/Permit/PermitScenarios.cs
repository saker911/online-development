using Microsoft.AspNetCore.Authorization;

namespace PermitBehaviorChecks;

internal static partial class ScenarioCatalog
{
    private static Task ScenarioPublicPermitNumberVerificationIsClosedAndQrTokensBackfillSafely(
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        MutableSystemClock clock
    )
    {
        var verifyByNumberMethod = typeof(PermitsController).GetMethod(
            nameof(PermitsController.VerifyByNumber)
        );
        Require(
            verifyByNumberMethod != null,
            "permit controller should expose the VerifyByNumber action"
        );
        Require(
            !verifyByNumberMethod!
                .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true)
                .Any(),
            "VerifyByNumber should no longer be exposed anonymously"
        );

        var permitNumber = CreateApprovedEmployeePermit(permitService);
        ClearPermitQrTokenDirectly(dbFactory, permitNumber);

        var backfilledToken = permitService.EnsurePermitQrToken(permitNumber, "tester");
        Require(
            !string.IsNullOrWhiteSpace(backfilledToken),
            "legacy permits should receive a QR token when it is missing"
        );
        Require(
            permitService.TryValidatePermitQrToken(
                backfilledToken,
                out var permit,
                out var status,
                out _
            )
                && permit != null
                && string.Equals(status, "authorized", StringComparison.OrdinalIgnoreCase),
            "backfilled QR token should authorize the permit"
        );

        ClearPermitQrTokenDirectly(dbFactory, permitNumber);
        var controller = CreatePermitsController(
            permitService,
            userAdminService,
            accessControlService,
            BuildPrincipal("tester", AppRoles.GeneralManager, AppPermissions.ViewPermits),
            clock
        );
        var permitWithoutToken =
            permitService.GetPermitByNumber(permitNumber, "tester")
            ?? throw new InvalidOperationException("permit not found for QR content generation");
        Require(
            string.IsNullOrWhiteSpace(permitWithoutToken.QrToken),
            "setup should clear the QR token before controller backfill"
        );

        var buildQrContent = typeof(PermitsController).GetMethod(
            "BuildPermitQrContent",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        Require(
            buildQrContent != null,
            "permit controller should expose BuildPermitQrContent privately"
        );

        var qrContent =
            buildQrContent!.Invoke(controller, new object[] { permitWithoutToken }) as string;
        Require(
            !string.IsNullOrWhiteSpace(qrContent)
                && qrContent!.Contains("/Permits/Verify", StringComparison.OrdinalIgnoreCase),
            "QR content should resolve to token verification links"
        );
        Require(
            !qrContent!.Contains("VerifyByNumber", StringComparison.OrdinalIgnoreCase),
            "QR content should stop falling back to permit-number verification links"
        );

        var persistedPermit =
            permitService.GetPermitByNumber(permitNumber, "tester")
            ?? throw new InvalidOperationException("permit not found after QR content generation");
        Require(
            !string.IsNullOrWhiteSpace(persistedPermit.QrToken),
            "QR content generation should persist a missing QR token"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioZebraLabelUsesPublicPermitCodeAndPlateOnly(
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService,
        MutableSystemClock clock
    )
    {
        var permitNumber = CreateApprovedEmployeePermit(permitService);
        var permit =
            permitService.GetPermitByNumber(permitNumber, "tester")
            ?? throw new InvalidOperationException("permit not found for Zebra label test");

        var buildContent = typeof(PermitsController).GetMethod(
            "BuildZebraLabelContent",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
        );
        Require(
            buildContent != null,
            "permit controller should build Zebra label content centrally"
        );

        var labelContent = buildContent!.Invoke(null, new object[] { permit });
        Require(labelContent != null, "Zebra label content should be created for approved permits");

        var contentType = labelContent!.GetType();
        var contentProperties = contentType
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Require(
            contentProperties.SequenceEqual(new[] { "BarcodeValue", "PlateNumberDisplay" }),
            "Zebra label visible content should be limited to barcode value and plate number"
        );

        var barcodeValue =
            contentType.GetProperty("BarcodeValue")?.GetValue(labelContent) as string;
        var plateNumberDisplay =
            contentType.GetProperty("PlateNumberDisplay")?.GetValue(labelContent) as string;
        Require(
            string.Equals(barcodeValue, permit.PublicPermitCode, StringComparison.Ordinal),
            "Zebra label barcode should use PublicPermitCode"
        );
        Require(
            string.Equals(plateNumberDisplay, permit.PlateNumberDisplay, StringComparison.Ordinal),
            "Zebra label should show the permit plate number"
        );
        Require(
            !string.Equals(plateNumberDisplay, permit.DriverName, StringComparison.Ordinal)
                && !string.Equals(
                    plateNumberDisplay,
                    permit.DepartmentName,
                    StringComparison.Ordinal
                ),
            "Zebra label should not expose permit holder or department as visible text"
        );

        var controller = CreatePermitsController(
            permitService,
            userAdminService,
            accessControlService,
            BuildPrincipal("tester", AppRoles.GeneralManager, AppPermissions.ViewPermits),
            clock
        );
#pragma warning disable CA1416
        var result = controller.PrintZebraLabel(permitNumber) as FileContentResult;
#pragma warning restore CA1416
        Require(result != null, "PrintZebraLabel should return a PDF file for approved permits");
        Require(
            string.Equals(
                result!.ContentType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase
            ),
            "PrintZebraLabel should return a PDF content type"
        );
        Require(
            result.FileContents.Length > 1000,
            "PrintZebraLabel should generate a non-empty PDF"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioExpiredVisitorCanRenew(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        var permitNumber = CreateApprovedVisitorPermit(permitService, "Visitor expired");

        ExpirePermitDirectly(dbFactory, permitNumber);

        var archivedBeforeEdit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("visitor permit not found after final exit");

        Require(
            string.Equals(
                archivedBeforeEdit.ApprovalStatus,
                "Expired",
                StringComparison.OrdinalIgnoreCase
            ),
            "visitor permit should be expired before edit"
        );
        Require(
            archivedBeforeEdit.ArchivedAt.HasValue,
            "visitor permit should be archived before edit"
        );

        var renewedExpiry = AppClock.LocalNow.AddDays(7);

        permitService.UpdatePermit(
            new Permit
            {
                PermitNumber = permitNumber,
                PermitType = Permit.PermitTypeVisitor,
                DriverName = archivedBeforeEdit.DriverName,
                NationalId = archivedBeforeEdit.NationalId,
                VehicleType = archivedBeforeEdit.VehicleType,
                PlateNumber = archivedBeforeEdit.PlateNumber,
                DepartmentName = archivedBeforeEdit.DepartmentName,
                VisitLocation = archivedBeforeEdit.VisitLocation,
                EmployeePhone = "0551111111",
                RequiresReturn = false,
                ExpiresAt = renewedExpiry,
                PermitDate = archivedBeforeEdit.PermitDate,
            }
        );

        var renewedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("visitor permit not found after renewal edit");

        Require(
            string.Equals(
                renewedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "renewing an expired visitor permit should reactivate it"
        );
        Require(
            !renewedPermit.ArchivedAt.HasValue,
            "renewed visitor permit should clear archive marker"
        );
        Require(
            renewedPermit.ExpiresAt.HasValue && renewedPermit.ExpiresAt.Value > AppClock.LocalNow,
            "renewed visitor permit should have a future expiry"
        );

        var reopenedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reopenedScan.allowed, "renewed visitor permit should scan again");

        return Task.CompletedTask;
    }

    private static Task ScenarioActiveVisitorEditRequiresApproval(IPermitService permitService)
    {
        var permitNumber = CreateApprovedVisitorPermit(permitService, "Visitor active");

        permitService.UpdatePermit(
            new Permit
            {
                PermitNumber = permitNumber,
                PermitType = Permit.PermitTypeVisitor,
                DriverName = "Visitor active edited",
                NationalId = "2876543210",
                VehicleType = "SUV",
                PlateNumber = "JKL1234",
                DepartmentName = "بوابة المركبات",
                VisitLocation = "بوابة المركبات",
                EmployeePhone = "0552222222",
                RequiresReturn = false,
                ExpiresAt = AppClock.LocalNow.AddHours(12),
                PermitDate = AppClock.LocalNow,
            }
        );

        var editedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("visitor permit not found after active edit");

        Require(
            string.Equals(
                editedPermit.ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "editing an active visitor permit should move it back to pending"
        );

        permitService.UpdatePermitApprovalStatus(permitNumber, "Approved");
        var scan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(scan.allowed, "approved visitor permit should scan again after edit");

        return Task.CompletedTask;
    }

    private static Task ScenarioActiveEmployeeEditRequiresApproval(IPermitService permitService)
    {
        var permitNumber = CreateApprovedEmployeePermit(permitService);

        permitService.UpdatePermit(
            new Permit
            {
                PermitNumber = permitNumber,
                PermitType = Permit.PermitTypePermanent,
                DriverName = "Employee active edited",
                NationalId = "1234567890",
                VehicleType = "Sedan",
                PlateNumber = "MNO5678",
                DepartmentName = "الإدارة العامة",
                ManagerName = "مدير النظام",
                EmployeePhone = "0553333333",
                PermitDate = AppClock.LocalNow.AddHours(-1),
                ExpiresAt = AppClock.LocalNow.AddHours(1),
                RequiresReturn = true,
            }
        );

        var editedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("employee permit not found after edit");

        Require(
            string.Equals(
                editedPermit.ApprovalStatus,
                "Pending",
                StringComparison.OrdinalIgnoreCase
            ),
            "editing an active employee permit should move it back to pending"
        );

        permitService.UpdatePermitApprovalStatus(permitNumber, "Approved");
        var scan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(scan.allowed, "approved employee permit should scan again after edit");

        return Task.CompletedTask;
    }

    private static Task ScenarioExpiredEmployeeCanRenew(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        var permitNumber = CreateApprovedEmployeePermit(permitService);
        ExpirePermitDirectly(dbFactory, permitNumber);

        var expiredBeforeEdit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("employee permit not found after expiration");

        Require(
            string.Equals(
                expiredBeforeEdit.ApprovalStatus,
                "Expired",
                StringComparison.OrdinalIgnoreCase
            ),
            "employee permit should be expired before renewal"
        );
        Require(
            expiredBeforeEdit.ArchivedAt.HasValue,
            "employee permit should be archived before renewal"
        );

        var renewedExpiry = AppClock.LocalNow.AddDays(14);
        permitService.UpdatePermit(
            new Permit
            {
                PermitNumber = permitNumber,
                PermitType = Permit.PermitTypePermanent,
                DriverName = expiredBeforeEdit.DriverName,
                NationalId = expiredBeforeEdit.NationalId,
                VehicleType = expiredBeforeEdit.VehicleType,
                PlateNumber = expiredBeforeEdit.PlateNumber,
                DepartmentName = expiredBeforeEdit.DepartmentName,
                ManagerName = expiredBeforeEdit.ManagerName,
                EmployeePhone = expiredBeforeEdit.EmployeePhone,
                PermitDate = expiredBeforeEdit.PermitDate,
                ExpiresAt = renewedExpiry,
                RequiresReturn = expiredBeforeEdit.RequiresReturn,
            }
        );

        var renewedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("employee permit not found after renewal edit");

        Require(
            string.Equals(
                renewedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "renewing an expired employee permit should reactivate it"
        );
        Require(
            !renewedPermit.ArchivedAt.HasValue,
            "renewed employee permit should clear archive marker"
        );
        Require(
            renewedPermit.ExpiresAt.HasValue && renewedPermit.ExpiresAt.Value > AppClock.LocalNow,
            "renewed employee permit should have a future expiry"
        );

        var reopenedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reopenedScan.allowed, "renewed employee permit should scan again");

        return Task.CompletedTask;
    }

    private static Task ScenarioStoppedVisitorCanReactivate(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        var permitNumber = CreateApprovedVisitorPermit(permitService, "Visitor stopped");

        Require(
            permitService.StopPermit(permitNumber, "tester"),
            "visitor permit should stop successfully"
        );

        var stoppedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("visitor permit not found after stop");
        Require(
            string.Equals(
                stoppedPermit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "stopped visitor should remain stopped"
        );
        Require(
            !stoppedPermit.ArchivedAt.HasValue,
            "manual stop should not archive visitor permit"
        );

        var blockedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!blockedScan.allowed, "stopped visitor permit must not scan while stopped");

        Require(
            permitService.ReactivatePermit(permitNumber, "tester"),
            "stopped visitor should reactivate successfully"
        );

        var reactivatedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("visitor permit not found after reactivate");
        Require(
            string.Equals(
                reactivatedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "reactivated visitor should return to approved"
        );
        Require(
            !reactivatedPermit.ArchivedAt.HasValue,
            "reactivated visitor should remain unarchived"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var reopenedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reopenedScan.allowed, "reactivated visitor permit should scan again");

        return Task.CompletedTask;
    }

    private static Task ScenarioStoppedEmployeeCanReactivate(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        var permitNumber = CreateApprovedEmployeePermit(permitService);

        Require(
            permitService.StopPermit(permitNumber, "tester"),
            "employee permit should stop successfully"
        );

        var stoppedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("employee permit not found after stop");
        Require(
            string.Equals(
                stoppedPermit.ApprovalStatus,
                "Stopped",
                StringComparison.OrdinalIgnoreCase
            ),
            "stopped employee should remain stopped"
        );
        Require(
            !stoppedPermit.ArchivedAt.HasValue,
            "manual stop should not archive employee permit"
        );

        var blockedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(!blockedScan.allowed, "stopped employee permit must not scan while stopped");

        Require(
            permitService.ReactivatePermit(permitNumber, "tester"),
            "stopped employee should reactivate successfully"
        );

        var reactivatedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("employee permit not found after reactivate");
        Require(
            string.Equals(
                reactivatedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "reactivated employee should return to approved"
        );
        Require(
            !reactivatedPermit.ArchivedAt.HasValue,
            "reactivated employee should remain unarchived"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var reopenedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reopenedScan.allowed, "reactivated employee permit should scan again");

        return Task.CompletedTask;
    }

    private static Task ScenarioArchivedStoppedPermitCanReactivate(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        var permitNumber = CreateApprovedVisitorPermit(permitService, "Visitor archived");
        StopAndArchivePermitDirectly(dbFactory, permitNumber);

        Require(
            permitService.ReactivatePermit(permitNumber, "tester"),
            "stopped archived permit should reactivate"
        );

        var reactivatedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after reactivate attempt");
        Require(
            string.Equals(
                reactivatedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "reactivated stopped permit should return to approved"
        );
        Require(
            !reactivatedPermit.ArchivedAt.HasValue,
            "reactivated stopped permit should clear archive marker"
        );
        Require(
            !string.IsNullOrWhiteSpace(reactivatedPermit.QrToken),
            "reactivated stopped permit should restore a QR token when missing"
        );
        Require(
            permitService.TryValidatePermitQrToken(
                reactivatedPermit.QrToken,
                out _,
                out var qrStatus,
                out _
            ) && string.Equals(qrStatus, "authorized", StringComparison.OrdinalIgnoreCase),
            "reactivated stopped permit should expose an authorized QR token"
        );

        clock.Advance(TimeSpan.FromSeconds(6));
        var reopenedScan = permitService.RecordPermitScan(permitNumber, "tester");
        Require(reopenedScan.allowed, "reactivated archived stopped permit should scan again");

        return Task.CompletedTask;
    }

    private static Task ScenarioReactivateActionReactivatesPermitAndPublishesSuccessToast(
        MutableSystemClock clock,
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService
    )
    {
        var permitNumber = CreateApprovedEmployeePermit(permitService);
        Require(permitService.StopPermit(permitNumber, "tester"), "setup should stop the permit");

        var controller = CreatePermitsController(
            permitService,
            userAdminService,
            accessControlService,
            BuildPrincipal("tester", AppRoles.GeneralManager, AppPermissions.StopPermit),
            clock
        );

        var result = controller.Reactivate(permitNumber);
        Require(
            result is RedirectToActionResult { ActionName: nameof(PermitsController.Index) },
            "reactivate action should redirect back to the permits index when no local return URL is supplied"
        );
        Require(
            HasQueuedToast(controller.TempData, "تمت إعادة تفعيل التصريح بنجاح", "success"),
            "reactivate action should publish the success toast"
        );

        var reactivatedPermit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after controller reactivation"
            );
        Require(
            string.Equals(
                reactivatedPermit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "reactivate action should return the permit to approved"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioPermitIndexSearchFindsRecordsAcrossSupportedFields(
        MutableSystemClock clock,
        IPermitService permitService,
        IUserAdminService userAdminService,
        IAccessControlService accessControlService
    )
    {
        var permit = new Permit
        {
            PermitType = Permit.PermitTypePermanent,
            DriverName = "بحث التحديث",
            NationalId = "1234567890",
            VehicleType = "Sedan",
            PlateNumber = "SRC1234",
            DepartmentName = "الإدارة العامة",
            ManagerName = "Tester",
            EmployeePhone = "0551234567",
            RequiresReturn = true,
            AccessMode = Permit.AccessModeFullAccess,
            PermitDate = clock.LocalNow,
            ExpiresAt = clock.LocalNow.AddDays(3),
        };

        permitService.AddPermit(permit);
        permitService.UpdatePermitApprovalStatus(permit.PermitNumber, "Approved");

        var controller = CreatePermitsController(
            permitService,
            userAdminService,
            accessControlService,
            BuildPrincipal("tester", AppRoles.GeneralManager, AppPermissions.ViewPermits),
            clock
        );

        RequirePermitSearchContainsPermit(
            controller,
            permit.DriverName,
            permit.PermitNumber,
            "name search should find the permit"
        );
        RequirePermitSearchContainsPermit(
            controller,
            "١٢٣٤٥٦٧٨٩٠",
            permit.PermitNumber,
            "national-id search should find the permit when Arabic digits are entered"
        );
        RequirePermitSearchContainsPermit(
            controller,
            permit.PermitNumber,
            permit.PermitNumber,
            "permit-number search should find the permit"
        );

        if (int.TryParse(permit.PermitNumber[7..], out var legacyPermitNumber))
        {
            RequirePermitSearchContainsPermit(
                controller,
                $"P{legacyPermitNumber}",
                permit.PermitNumber,
                "legacy short permit-number search should still find the permit"
            );
        }

        return Task.CompletedTask;
    }

    private static void RequirePermitSearchContainsPermit(
        PermitsController controller,
        string searchTerm,
        string permitNumber,
        string expectation
    )
    {
        if (
            controller.Index(searchTerm)
            is not Microsoft.AspNetCore.Mvc.ViewResult
            {
                Model: VehiclePermitSystemWeb.Models.ViewModels.Permits.PermitIndexViewModel model
            }
        )
        {
            throw new InvalidOperationException(
                $"{expectation}: search should return a permit index view"
            );
        }

        Require(
            model.Permits.Any(item =>
                string.Equals(item.PermitNumber, permitNumber, StringComparison.OrdinalIgnoreCase)
            ),
            expectation
        );
    }

    private static Task ScenarioLeaveRequestWithReturnAllowsExitAndLateReturn(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 9, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before leave request");

        var leaveStartAt = clock.LocalNow.AddMinutes(1);
        var leaveEndAt = clock.LocalNow.AddMinutes(30);
        var submitted = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: true,
            leaveReason: "مراجعة خارجية",
            leaveStartAt,
            leaveEndAt,
            "tester"
        );
        Require(submitted, "leave request should be accepted for inside approved employee");

        var permitAfterRequest =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after leave request");
        Require(
            permitAfterRequest.PendingExitRequest,
            "leave request should remain pending before exit"
        );
        Require(
            permitAfterRequest.ExpectedReturnTime == leaveEndAt,
            "leave request should persist the requested return deadline"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 9, 10, 0));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "leave request should allow authorized exit inside the window");

        var permitAfterExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after authorized exit");
        Require(
            string.Equals(
                permitAfterExit.ApprovalStatus,
                "Out",
                StringComparison.OrdinalIgnoreCase
            ),
            "authorized leave exit should mark permit as out"
        );
        Require(
            !permitAfterExit.PendingExitRequest,
            "authorized exit should clear pending leave request state"
        );
        Require(
            permitAfterExit.ExpectedReturnTime == leaveEndAt,
            "authorized exit should retain the requested return deadline"
        );
        Require(
            permitAfterExit.LeaveWindowStartAt == leaveStartAt,
            "authorized exit should retain the leave window start while awaiting return"
        );
        Require(
            permitAfterExit.LeaveWindowEndAt == leaveEndAt,
            "authorized exit should retain the leave window end while awaiting return"
        );
        Require(
            string.Equals(permitAfterExit.LeaveReason, "مراجعة خارجية", StringComparison.Ordinal),
            "authorized exit should retain the leave reason while awaiting return"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 9, 46, 0));
        var lateReturn = permitService.RecordPermitScan(permitNumber, "tester");
        Require(lateReturn.allowed, "late return should still be recorded as entry");

        var permitAfterReturn =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after late return");
        Require(
            string.Equals(
                permitAfterReturn.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "late return should restore permit to approved state"
        );
        Require(
            string.Equals(
                permitAfterReturn.CurrentState,
                "Inside",
                StringComparison.OrdinalIgnoreCase
            ),
            "late return should bring permit back inside"
        );
        Require(
            !permitAfterReturn.ExpectedReturnTime.HasValue,
            "late return should clear the expected return deadline"
        );
        Require(
            !permitAfterReturn.RequiresReturn,
            "temporary leave return should not change the permit base return setting"
        );

        var latestActivity = permitService.GetPermitActivities(permitNumber).FirstOrDefault();
        Require(latestActivity != null, "late return should produce a permit activity");
        Require(
            string.Equals(
                latestActivity!.ActionType,
                "LateReturn",
                StringComparison.OrdinalIgnoreCase
            ),
            "late return should be recorded with the LateReturn action"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioOneTimeLeaveRequestRejectsPastStartTime(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            entry.allowed,
            "employee permit should enter before validating the leave start time"
        );

        var submitted = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: true,
            leaveReason: "محاولة قديمة",
            leaveStartAt: clock.LocalNow.AddMinutes(-10),
            leaveEndAt: clock.LocalNow.AddMinutes(20),
            performedBy: "tester"
        );
        Require(!submitted, "one-time leave request should reject a start time in the past");

        var permitAfterAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after rejecting a past leave start"
            );
        Require(
            !permitAfterAttempt.PendingExitRequest,
            "rejecting a past leave start should not leave a pending request on the permit"
        );
        Require(
            !permitAfterAttempt.LeaveWindowStartAt.HasValue,
            "rejecting a past leave start should not persist the leave window start"
        );
        Require(
            !permitAfterAttempt.LeaveWindowEndAt.HasValue,
            "rejecting a past leave start should not persist the leave window end"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioEntryOnlyOneTimeFinalExitKeepsPolicyAcrossNextDay(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        SetAdministrationWorkHours(
            dbFactory,
            new TimeOnly(8, 0),
            new TimeOnly(16, 0),
            5,
            "Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 16, 8, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var initialEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(initialEntry.allowed, "entry-only permit should allow the initial entry");

        var submitted = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: false,
            leaveReason: "خروج نهائي مؤقت",
            leaveStartAt: clock.LocalNow.AddMinutes(1),
            leaveEndAt: null,
            performedBy: "tester"
        );
        Require(
            submitted,
            "one-time final exit request should be accepted for an inside entry-only permit"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 16, 8, 10, 0));
        var authorizedExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            authorizedExit.allowed,
            "authorized final exit should be allowed inside the leave window"
        );

        var permitAfterAuthorizedExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after one-time final exit");
        Require(
            string.Equals(
                permitAfterAuthorizedExit.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "permit should remain outside after the one-time final exit"
        );
        Require(
            string.Equals(
                permitAfterAuthorizedExit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "one-time final exit should keep the base permit approved"
        );
        Require(
            !permitAfterAuthorizedExit.PendingExitRequest,
            "authorized final exit should clear the pending leave request"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 17, 9, 0, 0));
        var nextDayEntry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(nextDayEntry.allowed, "entry-only permit should allow re-entry on the next day");

        clock.Advance(TimeSpan.FromSeconds(6));
        var nextDayUnauthorizedExit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(
            !nextDayUnauthorizedExit.allowed,
            "entry-only permit should still block exit without a fresh leave request on the next day"
        );
        Require(
            string.Equals(
                nextDayUnauthorizedExit.reason,
                "PendingUnauthorizedExit",
                StringComparison.OrdinalIgnoreCase
            ),
            "entry-only permit should open a pending unauthorized exit workflow after the next-day exit attempt"
        );

        var permitAfterNextDayAttempt =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException(
                "permit not found after next-day unauthorized exit attempt"
            );
        Require(
            permitAfterNextDayAttempt.PendingUnauthorizedExitAt.HasValue,
            "next-day unauthorized exit should create a pending unauthorized exit marker"
        );
        Require(
            !permitAfterNextDayAttempt.PendingExitRequest,
            "next-day unauthorized exit should not resurrect the old leave request"
        );

        ResetStandardAdministrationSchedule(dbFactory);

        return Task.CompletedTask;
    }

    private static Task ScenarioOneTimeNoReturnRequestWorksWithoutEndDate(
        MutableSystemClock clock,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: false,
            expiresAt: clock.LocalNow.AddDays(7)
        );

        var entry = permitService.RecordPermitScan(permitNumber, "tester");
        Require(entry.allowed, "employee permit should enter before the no-return request");

        var leaveStartAt = clock.LocalNow.AddMinutes(2);
        var submitted = permitService.SubmitLeaveRequest(
            permitNumber,
            requiresReturn: false,
            leaveReason: "خروج نهائي بلا نهاية",
            leaveStartAt: leaveStartAt,
            leaveEndAt: null,
            performedBy: "tester"
        );
        Require(submitted, "one-time no-return request should be accepted without an end date");

        var permitAfterRequest =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after no-return request");
        Require(
            permitAfterRequest.PendingExitRequest,
            "no-return request should stay pending before the employee exits"
        );
        Require(
            permitAfterRequest.LeaveWindowStartAt == leaveStartAt,
            "no-return request should keep the configured start time"
        );
        Require(
            !permitAfterRequest.LeaveWindowEndAt.HasValue,
            "no-return request should not store an end date"
        );
        Require(
            !permitAfterRequest.ExpectedReturnTime.HasValue,
            "no-return request should not store an expected return time"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 5, 0));
        var exit = permitService.RecordPermitScan(permitNumber, "tester");
        Require(exit.allowed, "no-return request should allow the final exit after the start time");

        var permitAfterExit =
            permitService.GetPermitByNumber(permitNumber)
            ?? throw new InvalidOperationException("permit not found after no-return final exit");
        Require(
            string.Equals(
                permitAfterExit.CurrentState,
                "Outside",
                StringComparison.OrdinalIgnoreCase
            ),
            "no-return final exit should leave the permit outside"
        );
        Require(
            string.Equals(
                permitAfterExit.ApprovalStatus,
                "Approved",
                StringComparison.OrdinalIgnoreCase
            ),
            "no-return final exit should keep the base permit approved"
        );
        Require(
            !permitAfterExit.PendingExitRequest,
            "no-return final exit should clear the pending request"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioLeavePolicyServiceCentralizesDecisions(
        MutableSystemClock clock,
        IServiceProvider serviceProvider
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 13, 9, 15, 0));

        var leavePolicyService = serviceProvider.GetRequiredService<ILeavePolicyService>();
        const string officialWorkDaysCsv = "Sunday,Monday,Tuesday,Wednesday,Thursday";
        var workStartTime = new TimeOnly(8, 0);
        var workEndTime = new TimeOnly(16, 0);

        var permit = new Permit
        {
            PermitNumber = "PERMIT-LEAVE-POLICY",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            HasDailyLeaveSchedule = true,
            DailyLeaveScheduleStartDate = new DateTime(2026, 4, 13),
            DailyLeaveScheduleEndDate = new DateTime(2026, 4, 13),
            DailyLeaveScheduleExitMinutes = 9 * 60,
            DailyLeaveScheduleReturnMinutes = 10 * 60,
            DailyLeaveScheduleRequiresReturn = true,
            LeaveWindowStartAt = clock.LocalNow.AddMinutes(-5),
            LeaveWindowEndAt = clock.LocalNow.AddMinutes(20),
            PendingExitRequest = true,
            ExpectedReturnTime = clock.LocalNow.AddMinutes(20),
        };

        Require(
            leavePolicyService.CanExitNow(
                permit,
                clock.LocalNow,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            ),
            "policy service should allow exit while a leave request is active"
        );
        Require(
            leavePolicyService.RequiresReturn(permit, clock.LocalNow, officialWorkDaysCsv),
            "policy service should mark the active leave as requiring a return"
        );
        Require(
            leavePolicyService.HasActiveLeave(permit, clock.LocalNow, officialWorkDaysCsv),
            "policy service should report the active leave as active"
        );
        Require(
            string.Equals(
                leavePolicyService.ResolveLeaveSource(permit, clock.LocalNow, officialWorkDaysCsv),
                "LeaveRequest",
                StringComparison.OrdinalIgnoreCase
            ),
            "policy service should resolve a pending leave request as the leave source"
        );
        Require(
            leavePolicyService.GetExpectedReturn(
                permit,
                clock.LocalNow,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            ) == permit.ExpectedReturnTime,
            "policy service should return the configured expected return time"
        );
        Require(
            leavePolicyService.IsLeaveWindowActive(permit, clock.LocalNow),
            "policy service should report the leave window as active"
        );
        Require(
            leavePolicyService.IsLateReturn(
                permit,
                clock.LocalNow,
                TimeSpan.FromMinutes(5),
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            ) == false,
            "policy service should not mark an on-time leave as late"
        );
        Require(
            leavePolicyService.IsDailyLeaveScheduleActive(
                permit,
                new DateTime(2026, 4, 13, 9, 30, 0),
                officialWorkDaysCsv
            ),
            "policy service should activate the daily leave schedule inside the configured window"
        );

        var entryOnlyPermit = new Permit
        {
            PermitNumber = "PERMIT-ENTRY-ONLY",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            RequiresReturn = false,
        };
        Require(
            !leavePolicyService.CanExitNow(
                entryOnlyPermit,
                clock.LocalNow,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            ),
            "policy service should block an entry-only exit during work hours without leave"
        );
        Require(
            string.Equals(
                leavePolicyService.ResolveLeaveSource(
                    entryOnlyPermit,
                    clock.LocalNow,
                    officialWorkDaysCsv
                ),
                "None",
                StringComparison.OrdinalIgnoreCase
            ),
            "policy service should resolve no active leave for the entry-only permit"
        );

        leavePolicyService.ClearLeaveRequestState(permit);
        Require(
            !permit.PendingExitRequest
                && !permit.ExpectedReturnTime.HasValue
                && !permit.LeaveWindowStartAt.HasValue
                && !permit.LeaveWindowEndAt.HasValue,
            "clearing a leave request should remove the pending request and return window"
        );

        leavePolicyService.ResetLeaveState(permit);
        Require(
            !permit.PendingExitRequest
                && !permit.HasDailyLeaveSchedule
                && !permit.DailyLeaveScheduleStartDate.HasValue
                && !permit.DailyLeaveScheduleEndDate.HasValue,
            "resetting leave state should clear both ad hoc and daily schedule data"
        );

        Require(
            !leavePolicyService.RequiresReturn(permit, clock.LocalNow, officialWorkDaysCsv),
            "a full-access permanent permit should not regain a mandatory return after reset"
        );
        Require(
            leavePolicyService.CanExitNow(
                permit,
                clock.LocalNow,
                workStartTime,
                workEndTime,
                officialWorkDaysCsv
            ),
            "a full-access permanent permit should remain freely exitable after reset"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioGatePolicyServiceResolvesDecisions(
        MutableSystemClock clock,
        IServiceProvider serviceProvider
    )
    {
        var gatePolicyService = serviceProvider.GetRequiredService<IGatePolicyService>();

        var outsidePermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-ENTRY",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Outside",
        };
        var outsideDecision = gatePolicyService.ResolveDecision(outsidePermit, clock.LocalNow);
        Require(
            outsideDecision.Outcome == GateDecisionOutcome.AllowEntry,
            "outside permit should resolve to entry"
        );
        Require(
            gatePolicyService.CanEnter(outsidePermit, clock.LocalNow),
            "outside permit should be enterable"
        );

        var exitPermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-EXIT",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            AccessMode = Permit.AccessModeFullAccess,
            RequiresReturn = true,
        };
        var exitDecision = gatePolicyService.ResolveDecision(exitPermit, clock.LocalNow);
        Require(
            exitDecision.Outcome == GateDecisionOutcome.AllowFinalExit,
            "full-access permit should resolve to a final exit"
        );
        Require(
            gatePolicyService.CanExit(exitPermit, clock.LocalNow),
            "full-access permit should be allowed to exit"
        );

        var finalExitPermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-FINAL",
            PermitType = Permit.PermitTypeVisitor,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            RequiresReturn = false,
        };
        var finalExitDecision = gatePolicyService.ResolveDecision(finalExitPermit, clock.LocalNow);
        Require(
            finalExitDecision.Outcome == GateDecisionOutcome.AllowFinalExit,
            "visitor permit should resolve to a final exit"
        );

        var blockedLeavePermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-LEAVE",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            AccessMode = Permit.AccessModeEntryOnly,
            RequiresReturn = false,
            PendingExitRequest = true,
            LeaveWindowStartAt = clock.LocalNow.AddMinutes(10),
            LeaveWindowEndAt = clock.LocalNow.AddMinutes(20),
            ExpectedReturnTime = clock.LocalNow.AddMinutes(20),
        };
        var blockedLeaveDecision = gatePolicyService.ResolveDecision(
            blockedLeavePermit,
            clock.LocalNow
        );
        Require(
            blockedLeaveDecision.Outcome == GateDecisionOutcome.DenyNoPermission,
            "leave request before its window should deny with no permission"
        );
        Require(
            string.Equals(
                blockedLeaveDecision.ReasonCode,
                "LeaveWindowNotStarted",
                StringComparison.OrdinalIgnoreCase
            ),
            "leave window not started should be preserved as the reason code"
        );

        clock.SetLocalNow(new DateTime(2026, 4, 13, 10, 0, 0));
        var pendingReviewPermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-PENDING",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            RequiresReturn = false,
            PendingUnauthorizedExitAt = clock.LocalNow.AddMinutes(-1),
            PendingUnauthorizedExitSequenceId = Guid.NewGuid().ToString("N"),
        };
        var pendingReviewDecision = gatePolicyService.ResolveDecision(
            pendingReviewPermit,
            clock.LocalNow
        );
        Require(
            pendingReviewDecision.Outcome == GateDecisionOutcome.DenyPendingReview,
            "pending unauthorized exit should resolve to pending review"
        );
        Require(
            gatePolicyService.IsBlockedByPendingUnauthorizedExit(
                pendingReviewPermit,
                clock.LocalNow
            ),
            "pending unauthorized exit should be reported as blocked"
        );

        var waitForClosurePermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-WAIT",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Approved",
            CurrentState = "Inside",
            RequiresReturn = false,
        };
        var waitDecision = gatePolicyService.ResolveDecision(waitForClosurePermit, clock.LocalNow);
        Require(
            waitDecision.Outcome == GateDecisionOutcome.WaitForWorkEndClosure,
            "entry-only permit during work hours should wait for work-end closure"
        );
        Require(
            gatePolicyService.ShouldWaitForWorkEndClosure(waitForClosurePermit, clock.LocalNow),
            "wait-for-closure helper should match the resolved decision"
        );

        var stoppedPermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-STOPPED",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Stopped",
            CurrentState = "Inside",
        };
        Require(
            gatePolicyService.ResolveDecision(stoppedPermit, clock.LocalNow).Outcome
                == GateDecisionOutcome.DenyStopped,
            "stopped permit should resolve to denied stopped"
        );

        var expiredPermit = new Permit
        {
            PermitNumber = "PERMIT-GATE-EXPIRED",
            PermitType = Permit.PermitTypePermanent,
            ApprovalStatus = "Expired",
            CurrentState = "Inside",
        };
        Require(
            gatePolicyService.ResolveDecision(expiredPermit, clock.LocalNow).Outcome
                == GateDecisionOutcome.DenyExpired,
            "expired permit should resolve to denied expired"
        );

        return Task.CompletedTask;
    }

    private static Task ScenarioOperatorPinProvisioning(
        IUserAdminService userAdminService,
        IDbContextFactory<ApplicationDbContext> dbFactory
    )
    {
        const string username = "1888888888";
        const string badgeCode = "GATE-1888888888";

        var created = userAdminService.CreateUser(
            new UserAccount
            {
                Username = username,
                DisplayName = "موظف بوابة اختبار",
                FullName = "موظف بوابة اختبار",
                Department = "الأمن",
                JobTitle = "موظف بوابة",
                PhoneNumber = "0558888888",
                Role = AppRoles.GateSecurity,
                IsActive = true,
                CanScanOperations = true,
                CanViewDisplays = true,
            },
            "123456"
        );
        Require(created, "scan-capable user should be created successfully");

        var provisionedPin = userAdminService.ConfigureOperatorCredentials(
            username,
            badgeCode,
            null,
            true
        );
        Require(
            !string.IsNullOrWhiteSpace(provisionedPin)
                && provisionedPin.Length == 6
                && provisionedPin.All(char.IsDigit)
                && !string.IsNullOrWhiteSpace(provisionedPin),
            "scan-capable user should receive the initial temporary operator PIN"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var storedUser = db.UserAccounts.Single(user => user.Username == username);
            Require(
                !string.IsNullOrWhiteSpace(storedUser.PasswordHash),
                "login password hash should be stored"
            );
            Require(
                !string.IsNullOrWhiteSpace(storedUser.OperatorPinHash),
                "operator PIN hash should be stored"
            );
            Require(
                !string.Equals(
                    storedUser.PasswordHash,
                    storedUser.OperatorPinHash,
                    StringComparison.Ordinal
                ),
                "login password and operator PIN must use separate hashes"
            );
        }

        var signInResult = userAdminService.SwitchDisplayOperator(
            "gate-default",
            badgeCode,
            provisionedPin!,
            "test-user"
        );
        Require(
            signInResult.Success,
            "generated operator PIN should be accepted for display login"
        );
        Require(
            signInResult.RequiresPinChange,
            "default operator PIN should force first-time change"
        );

        var unchangedPinResult = userAdminService.ChangeDisplayOperatorPin(
            "gate-default",
            provisionedPin!,
            provisionedPin!,
            out var unchangedPinErrorCode
        );
        Require(!unchangedPinResult, "operator PIN change should reject reusing the same PIN");
        Require(
            string.Equals(
                unchangedPinErrorCode,
                "operator_pin_unchanged",
                StringComparison.Ordinal
            ),
            "operator PIN change should return an unchanged error code when the new PIN matches the current PIN"
        );

        var changeResult = userAdminService.ChangeDisplayOperatorPin(
            "gate-default",
            provisionedPin!,
            "654321",
            out var errorCode
        );
        Require(changeResult, "operator PIN should be changeable after first login");
        Require(
            string.IsNullOrWhiteSpace(errorCode),
            "operator PIN change should not return an error"
        );

        var oldPinResult = userAdminService.SwitchDisplayOperator(
            "gate-default-2",
            badgeCode,
            provisionedPin!,
            "test-user"
        );
        Require(
            !oldPinResult.Success,
            "old generated operator PIN should stop working after change"
        );

        var newPinResult = userAdminService.SwitchDisplayOperator(
            "gate-default-2",
            badgeCode,
            "654321",
            "test-user"
        );
        Require(newPinResult.Success, "new operator PIN should work after change");

        return Task.CompletedTask;
    }

    private static void ClearPermitQrTokenDirectly(
        IDbContextFactory<ApplicationDbContext> dbFactory,
        string permitNumber
    )
    {
        using var db = dbFactory.CreateDbContext();
        var permit = db.Permits.FirstOrDefault(entry => entry.PermitNumber == permitNumber);
        if (permit == null)
        {
            throw new InvalidOperationException("permit not found while clearing QR token");
        }

        permit.QrToken = string.Empty;
        db.SaveChanges();
    }

    private static Task ScenarioOperatorNoteDuplicateWindowSuppression(
        MutableSystemClock clock,
        IDbContextFactory<ApplicationDbContext> dbFactory,
        IPermitService permitService
    )
    {
        clock.SetLocalNow(new DateTime(2026, 4, 22, 19, 0, 0));

        var permitNumber = CreateApprovedEmployeePermit(
            permitService,
            requiresReturn: true,
            expiresAt: clock.LocalNow.AddDays(1)
        );
        const string noteText = "ملاحظة بوابة مكررة";

        Require(
            permitService.AddOperatorNote(permitNumber, noteText, "gate-user"),
            "first operator note should be recorded successfully"
        );
        Require(
            permitService.AddOperatorNote(permitNumber, noteText, "gate-user"),
            "duplicate operator note inside the suppression window should return success"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var duplicateWindowActivities = db
                .PermitActivities.AsNoTracking()
                .Where(activity =>
                    activity.PermitNumber == permitNumber
                    && activity.ActionType == "OperatorNote"
                    && activity.Message == noteText
                    && activity.RecordedBy == "gate-user"
                )
                .ToList();

            Require(
                duplicateWindowActivities.Count == 1,
                "duplicate operator note inside the suppression window should not create a second activity"
            );
        }

        clock.Advance(TimeSpan.FromSeconds(6));
        Require(
            permitService.AddOperatorNote(permitNumber, noteText, "gate-user"),
            "same operator note should still be accepted after the suppression window"
        );

        using (var db = dbFactory.CreateDbContext())
        {
            var activitiesAfterWindow = db
                .PermitActivities.AsNoTracking()
                .Where(activity =>
                    activity.PermitNumber == permitNumber
                    && activity.ActionType == "OperatorNote"
                    && activity.Message == noteText
                    && activity.RecordedBy == "gate-user"
                )
                .ToList();

            Require(
                activitiesAfterWindow.Count == 2,
                "same operator note should be recorded again after the suppression window ends"
            );
        }

        return Task.CompletedTask;
    }
}
