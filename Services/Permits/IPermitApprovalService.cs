using System;

namespace VehiclePermitSystemWeb.Services.Permits
{
    public interface IPermitApprovalService
    {
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
