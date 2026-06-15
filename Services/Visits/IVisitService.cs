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

namespace VehiclePermitSystemWeb.Services.Visits
{
    public interface IVisitService
    {
        IEnumerable<Visit> GetAllVisits();
        IEnumerable<Visit> GetVisitorsAwaitingArrival();
        IEnumerable<Visit> GetVisitorsInside();
        IEnumerable<Visit> GetVisitorsCompleted();
        IEnumerable<Visit> GetSuspendedVisits();
        void AddVisit(Visit visit, string? performedBy = null);
        Visit? GetVisitById(string visitId);
        void UpdateVisit(Visit visit, string? performedBy = null, bool resetApprovalStatus = true);
        bool MarkVisitorArrived(string visitId, string? performedBy = null);
        bool MarkVisitorExited(string visitId, string? performedBy = null);
        void DeleteVisit(string visitId, string? performedBy = null);
        void SuspendVisit(string visitId, string? performedBy = null);
        void ResumeVisit(string visitId, string? performedBy = null);
        void UpdateVisitApprovalStatus(
            string visitId,
            string approvalStatus,
            string? performedBy = null
        );
        (bool allowed, string reason) RecordVisitScan(string visitId, string? scannerUser = null);
    }
}
