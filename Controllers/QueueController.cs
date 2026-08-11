using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VehiclePermitSystemWeb.Models.Entities;
using VehiclePermitSystemWeb.Models.ViewModels.Visits;
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageVisitQueue)]
    public sealed class QueueController : Controller
    {
        private readonly IVisitService _visitService;
        private readonly ISystemClock _systemClock;

        public QueueController(IVisitService visitService, ISystemClock systemClock)
        {
            _visitService = visitService;
            _systemClock = systemClock;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var queued = _visitService.GetQueueVisits().ToList();
            var available = _visitService
                .GetAllVisits()
                .Where(visit =>
                    visit.ArchivedAt == null
                    && string.IsNullOrWhiteSpace(visit.QueueStatus)
                    && string.Equals(visit.Status, "Active", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        visit.ApprovalStatus,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderBy(visit => visit.VisitDate)
                .Take(20)
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
                    UpdatedAt = _systemClock.LocalNow,
                }
            );
        }

        [HttpPost]
        public IActionResult Update(string id, string status)
        {
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

            return RedirectToAction(nameof(Index));
        }

        private static VisitQueueItemViewModel MapItem(Visit visit) =>
            new()
            {
                VisitId = visit.VisitId,
                TicketNumber = visit.QueueTicketNumber,
                VisitorName = visit.VisitorName,
                Destination = visit.SubjectDisplay,
                Location = visit.VisitLocation,
                Status = visit.QueueStatus,
                StatusDisplay = visit.QueueStatusDisplay,
                VisitDate = visit.VisitDate,
                QueuedAtUtc = visit.QueuedAtUtc,
                CalledAtUtc = visit.CalledAtUtc,
                ServiceStartedAtUtc = visit.ServiceStartedAtUtc,
                CompletedAtUtc = visit.QueueCompletedAtUtc,
            };
    }
}
