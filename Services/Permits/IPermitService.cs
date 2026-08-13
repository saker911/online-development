using VehiclePermitSystemWeb.Models.DTOs;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Account;
using VehiclePermitSystemWeb.Models.ViewModels.Backup;
using VehiclePermitSystemWeb.Models.ViewModels.Delegations;
using VehiclePermitSystemWeb.Models.ViewModels.Departments;
using VehiclePermitSystemWeb.Models.ViewModels.Display;
using VehiclePermitSystemWeb.Models.ViewModels.Permits;
using VehiclePermitSystemWeb.Models.ViewModels.Reports;
using VehiclePermitSystemWeb.Models.ViewModels.Scan;
using VehiclePermitSystemWeb.Models.ViewModels.Users;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;

namespace VehiclePermitSystemWeb.Services.Permits
{
    public interface IPermitService
    {
        IEnumerable<Permit> GetPendingPermits(string? username = null);
        IEnumerable<Permit> GetAllPermits(string? username = null);
        IEnumerable<Permit> GetVisiblePermits(string? username = null);
        IEnumerable<Permit> GetApprovedPermitsForDisplay();
        IEnumerable<Permit> GetEmployeesOutForDisplay();
        IEnumerable<PermitActivity> GetRecentPermitActivities(
            int take = 20,
            string? username = null
        );
        IEnumerable<PermitActivity> GetRecentPermitActivitiesForDevice(
            string deviceId,
            int take = 20,
            string? username = null
        );
        PermitActivity? GetLatestPermitActivity();
        IEnumerable<PermitActivity> GetPermitActivities(
            string permitNumber,
            string? username = null
        );
        IEnumerable<PermitActivity> GetSequencedPermitActivities(string? username = null);
        List<PermitConflictViewModel> FindPermitConflicts(
            Permit permit,
            string? excludePermitNumber = null
        );
        Permit? GetPermitByNumber(string permitNumber, string? username = null);
        void AddPermit(Permit permit, string? performedBy = null);
        void UpdatePermit(Permit permit, string? performedBy = null);
        void DeletePermit(string permitNumber, string? performedBy = null);
        void CancelExpiredPermits(string? performedBy = null);
        bool TryValidatePermitQrToken(
            string token,
            out Permit? permit,
            out string status,
            out string message
        );
        string EnsurePermitQrToken(string permitNumber, string? performedBy = null);
        (bool allowed, string reason) RecordPermitScan(
            string permitNumber,
            string? recordedBy = null,
            bool overrideEntry = false,
            PermitScanAuditContext? auditContext = null
        );
        bool MarkPermitDeparted(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        );
        bool MarkPermitReturned(
            string permitNumber,
            string? recordedBy = null,
            PermitScanAuditContext? auditContext = null
        );
        bool StopPermit(string permitNumber, string? performedBy = null);
        bool ReactivatePermit(string permitNumber, string? performedBy = null);
        bool ClearDailyLeaveSchedule(string permitNumber, string? performedBy = null);
        bool ResolveUnauthorizedExitReview(
            string sequenceId,
            bool confirmViolation,
            string? performedBy = null
        );
        bool AddOperatorNote(
            string permitNumber,
            string noteText,
            string? performedBy = null,
            PermitScanAuditContext? auditContext = null
        );
        int ClosePermitsAtWorkEnd(string? performedBy = null);
        bool SubmitLeaveRequest(
            string permitNumber,
            bool requiresReturn,
            string leaveReason,
            DateTime? leaveStartAt,
            DateTime? leaveEndAt,
            string? performedBy = null,
            bool isDailySchedule = false,
            DateTime? scheduleStartDate = null,
            DateTime? scheduleEndDate = null,
            int? dailyExitMinutes = null,
            int? dailyReturnMinutes = null
        );
        void UpdatePermitApprovalStatus(
            string permitNumber,
            string approvalStatus,
            string? performedBy = null,
            bool? requiresReturn = null,
            DateTime? expectedReturnTime = null,
            bool? pendingExitRequest = null,
            string? leaveReason = null
        );
        bool ForwardPermitToGeneralManager(string permitNumber, string? performedBy = null);
    }
}
