using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageVisitQueue)]
    public sealed class QueueController : Controller
    {
        private readonly IVisitService _visitService;
        private readonly IUserAdminService _userAdminService;
        private readonly ISystemClock _systemClock;

        public QueueController(
            IVisitService visitService,
            IUserAdminService userAdminService,
            ISystemClock systemClock
        )
        {
            _visitService = visitService;
            _userAdminService = userAdminService;
            _systemClock = systemClock;
        }

        [HttpGet]
        public IActionResult Index(int? departmentId = null)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            var isWorkplaceSiteRestricted = currentUser != null
                && !currentUser.IsSuperAdmin
                && !string.Equals(
                    currentUser.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                );
            var destinations = _userAdminService
                .GetDepartments()
                .Where(department => department.IsActive && department.AcceptsVisitors)
                .OrderBy(department => department.Name)
                .Select(department => new VisitDestinationOptionViewModel
                {
                    Id = department.Id,
                    Name = department.Name,
                })
                .ToList();
            var isDepartmentRestricted = currentUser != null
                && !currentUser.IsSuperAdmin
                && (currentUser.Role == AppRoles.Receptionist || currentUser.Role == AppRoles.Manager);
            if (isDepartmentRestricted)
            {
                departmentId = destinations
                    .FirstOrDefault(destination => string.Equals(
                        destination.Name,
                        currentUser!.Department,
                        StringComparison.OrdinalIgnoreCase
                    ))
                    ?.Id;
            }
            else if (departmentId.HasValue && destinations.All(item => item.Id != departmentId.Value))
            {
                departmentId = null;
            }

            var queued = _visitService
                .GetQueueVisits()
                .Where(visit =>
                    !isWorkplaceSiteRestricted
                    || visit.WorkplaceSiteId == currentUser!.WorkplaceSiteId
                )
                .Where(visit => !departmentId.HasValue || visit.DepartmentId == departmentId.Value)
                .ToList();
            var allVisits = _visitService
                .GetAllVisits()
                .Where(visit =>
                    !isWorkplaceSiteRestricted
                    || visit.WorkplaceSiteId == currentUser!.WorkplaceSiteId
                )
                .ToList();
            var available = allVisits
                .Where(visit =>
                    visit.ArchivedAt == null
                    && string.IsNullOrWhiteSpace(visit.QueueStatus)
                    && string.Equals(visit.Status, "Inside", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        visit.ApprovalStatus,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase
                    )
                    && (!departmentId.HasValue || visit.DepartmentId == departmentId.Value)
                )
                .OrderBy(visit => visit.VisitDate)
                .Take(20)
                .Select(MapItem)
                .ToList();
            var upcoming = allVisits
                .Where(visit =>
                    visit.ArchivedAt == null
                    && visit.Status == "Active"
                    && visit.ApprovalStatus == "Approved"
                    && string.IsNullOrWhiteSpace(visit.QueueStatus)
                    && (!departmentId.HasValue || visit.DepartmentId == departmentId.Value)
                )
                .OrderBy(visit => visit.VisitDate)
                .Take(12)
                .Select(MapItem)
                .ToList();

            return View(
                new VisitQueueViewModel
                {
                    Waiting = queued
                        .Where(visit => visit.QueueStatus == Visit.QueueStatusWaiting)
                        .Select(MapItem)
                        .ToList(),
                    Called = queued
                        .Where(visit => visit.QueueStatus == Visit.QueueStatusCalled)
                        .Select(MapItem)
                        .ToList(),
                    Serving = queued
                        .Where(visit => visit.QueueStatus == Visit.QueueStatusServing)
                        .Select(MapItem)
                        .ToList(),
                    Recent = queued
                        .Where(visit =>
                            visit.QueueStatus == Visit.QueueStatusCompleted
                            || visit.QueueStatus == Visit.QueueStatusSkipped
                        )
                        .OrderByDescending(visit => visit.QueueCompletedAtUtc)
                        .Take(10)
                        .Select(MapItem)
                        .ToList(),
                    Available = available,
                    Upcoming = upcoming,
                    Destinations = destinations,
                    SelectedDepartmentId = departmentId,
                    IsDepartmentRestricted = isDepartmentRestricted,
                    UpdatedAt = _systemClock.LocalNow,
                }
            );
        }

        [HttpPost]
        public IActionResult Update(string id, string status, int? departmentId = null)
        {
            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            var visit = _visitService.GetVisitById(id);
            if (visit == null || !CanManageDepartment(visit, currentUser))
            {
                return Forbid();
            }

            var succeeded = _visitService.UpdateQueueStatus(
                id,
                status,
                User.Identity?.Name ?? string.Empty
            );
            if (succeeded)
            {
                this.ToastSuccess("تم تحديث الدور.");
            }
            else
            {
                this.ToastWarning("تعذر تنفيذ الإجراء لأن حالة الدور تغيرت.");
            }

            return RedirectToAction(nameof(Index), new { departmentId });
        }

        private static bool CanManageDepartment(Visit visit, UserAccount? currentUser)
        {
            if (currentUser == null || currentUser.IsSuperAdmin)
            {
                return currentUser != null;
            }

            if (
                !string.Equals(
                    currentUser.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
                && visit.WorkplaceSiteId != currentUser.WorkplaceSiteId
            )
            {
                return false;
            }

            if (currentUser.Role != AppRoles.Receptionist && currentUser.Role != AppRoles.Manager)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(currentUser.Department)
                && string.Equals(
                    visit.Department?.Name,
                    currentUser.Department,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private static VisitQueueItemViewModel MapItem(Visit visit) =>
            new()
            {
                VisitId = visit.VisitId,
                TicketNumber = visit.QueueTicketNumber,
                VisitorName = visit.VisitorName,
                Destination = visit.SubjectDisplay,
                DepartmentId = visit.DepartmentId,
                Location = visit.VisitLocation,
                Status = visit.QueueStatus,
                StatusDisplay = visit.QueueStatusDisplay,
                VisitDate = visit.VisitDate,
                QueuedAtUtc = visit.QueuedAtUtc,
                CalledAtUtc = visit.CalledAtUtc,
                ServiceStartedAtUtc = visit.ServiceStartedAtUtc,
                CompletedAtUtc = visit.QueueCompletedAtUtc,
                ServiceOperatorDisplayName = visit.ServiceOperatorDisplayName,
                ServiceDurationMinutes = visit.ServiceDurationMinutes,
            };
    }
}
