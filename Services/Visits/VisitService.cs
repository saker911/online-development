using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
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
using VehiclePermitSystemWeb.Security;
using VehiclePermitSystemWeb.Utilities.Online;

namespace VehiclePermitSystemWeb.Services.Visits
{
    public sealed class VisitService : IVisitService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IAccessControlService _accessControl;
        private readonly IDelegationService _delegationService;
        private readonly IConfiguration _configuration;

        private sealed record WorkHoursSettings(TimeOnly StartTime, TimeOnly EndTime);

        public VisitService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IAccessControlService accessControl,
            IDelegationService delegationService,
            IConfiguration configuration
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _accessControl = accessControl;
            _delegationService = delegationService;
            _configuration = configuration;
        }

        public IEnumerable<Visit> GetAllVisits()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db).AsNoTracking().OrderByDescending(v => v.VisitDate).ToList();
        }

        public IEnumerable<Visit> GetVisitorsAwaitingArrival()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db)
                .AsNoTracking()
                .AsEnumerable()
                .Where(v =>
                    v.Status == "Active"
                    && string.Equals(
                        v.ApprovalStatus,
                        "Approved",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .OrderByDescending(v => v.VisitDate)
                .ToList();
        }

        public IEnumerable<Visit> GetVisitorsInside()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db)
                .AsNoTracking()
                .Where(v => v.Status == "Inside")
                .OrderByDescending(v => v.EntryTime)
                .ToList();
        }

        public IEnumerable<Visit> GetVisitorsCompleted()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db)
                .AsNoTracking()
                .Where(v => v.Status == "Completed")
                .OrderByDescending(v => v.ExitTime ?? v.VisitDate)
                .ToList();
        }

        public IEnumerable<Visit> GetSuspendedVisits()
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db).AsNoTracking().Where(v => v.Status == "Suspended").ToList();
        }

        public void AddVisit(Visit visit, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    PrepareVisitForSave(visit);
                    visit.VisitId = GenerateNextVisitId(db);
                    foreach (var companion in visit.Companions)
                    {
                        companion.VisitId = visit.VisitId;
                    }
                    db.Visits.Add(visit);

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "Create",
                            visit.IsDetainedVisit ? "إضافة زيارة موقوف" : "إضافة زيارة",
                            visit.IsDetainedVisit
                                ? $"تمت إضافة زيارة موقوف رقم {visit.VisitId} باسم {visit.VisitorName}."
                                : $"تمت إضافة الزيارة رقم {visit.VisitId} باسم {visit.VisitorName}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow
                        );
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public Visit? GetVisitById(string visitId)
        {
            using var db = _dbContextFactory.CreateDbContext();
            SynchronizeVisitStates(db);
            return CreateVisitQuery(db).AsNoTracking().FirstOrDefault(v => v.VisitId == visitId);
        }

        public void UpdateVisit(
            Visit visit,
            string? performedBy = null,
            bool resetApprovalStatus = true
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var existing = CreateVisitQuery(db)
                        .FirstOrDefault(v => v.VisitId == visit.VisitId);
                    if (existing == null)
                    {
                        return 0;
                    }

                    PrepareVisitForSave(visit);

                    existing.VisitorName = visit.VisitorName;
                    existing.VisitLocation = visit.VisitLocation;
                    existing.NationalId = visit.NationalId;
                    existing.PhoneNumber = visit.PhoneNumber;
                    existing.Purpose = visit.Purpose;
                    existing.HostName = visit.HostName;
                    existing.VisitedPersonName = visit.VisitedPersonName;
                    existing.VisitedPersonType = visit.VisitedPersonType;
                    existing.VisitDate = visit.VisitDate;
                    existing.EntryTime = visit.EntryTime;
                    existing.ExitTime = visit.ExitTime;
                    if (visit.ExpiresAt.HasValue)
                    {
                        existing.ExpiresAt = visit.ExpiresAt;
                    }
                    existing.Status = visit.Status;
                    if (resetApprovalStatus)
                    {
                        existing.ApprovalStatus = "Pending";
                    }

                    SyncCompanions(existing, visit.Companions);

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            existing.VisitId,
                            existing.VisitorName,
                            "Update",
                            "تعديل زيارة",
                            $"تم تحديث بيانات الزيارة رقم {existing.VisitId} الخاصة بـ {existing.VisitorName}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow
                        );
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public bool MarkVisitorArrived(string visitId, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    SynchronizeVisitStates(db);
                    var visit = CreateVisitQuery(db).FirstOrDefault(v => v.VisitId == visitId);
                    if (
                        visit == null
                        || visit.Status != "Active"
                        || !string.Equals(
                            visit.ApprovalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                        || IsVisitNoShowExpired(visit, _systemClock.LocalNow)
                    )
                    {
                        return false;
                    }

                    var occurredAt = _systemClock.LocalNow;
                    visit.EntryTime = occurredAt;
                    visit.ExpiresAt = CalculateVisitExpiration(
                        visit.EntryTime.Value,
                        GetWorkHoursSettings(db)
                    );
                    visit.Status = "Inside";
                    ApplyVisitTimesToCompanions(visit, visit.EntryTime, null);
                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "VisitorArrived",
                            "تسجيل دخول زائر",
                            $"تم تسجيل دخول الزائر {visit.VisitorName} للزيارة رقم {visit.VisitId}.",
                            "DisplayController",
                            performedBy,
                            occurredAt
                        );
                    }
                    db.SaveChanges();
                    return true;
                }
            );
        }

        public bool MarkVisitorExited(string visitId, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    SynchronizeVisitStates(db);
                    var visit = CreateVisitQuery(db).FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null || visit.Status != "Inside")
                    {
                        return false;
                    }

                    var occurredAt = _systemClock.LocalNow;
                    visit.ExitTime = occurredAt;
                    visit.ExpiresAt = visit.ExitTime;
                    visit.Status = "Completed";
                    ApplyVisitTimesToCompanions(visit, visit.EntryTime, visit.ExitTime);
                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "VisitorExited",
                            "تسجيل خروج زائر",
                            $"تم تسجيل خروج الزائر {visit.VisitorName} من الزيارة رقم {visit.VisitId}.",
                            "DisplayController",
                            performedBy,
                            occurredAt
                        );
                    }
                    db.SaveChanges();
                    return true;
                }
            );
        }

        public void DeleteVisit(string visitId, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var visit = CreateVisitQuery(db).FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null)
                    {
                        return 0;
                    }

                    // Archive the visit instead of hard-deleting
                    visit.ArchivedAt = _systemClock.LocalNow;
                    visit.Status = "Completed";
                    visit.ExpiresAt = visit.ArchivedAt;

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "Archive",
                            "أرشفة زيارة",
                            $"تمت أرشفة الزيارة رقم {visit.VisitId} الخاصة بـ {visit.VisitorName}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow
                        );
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public void SuspendVisit(string visitId, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var visit = db.Visits.FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null)
                    {
                        return 0;
                    }

                    visit.Status = "Suspended";

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "Suspend",
                            "إيقاف زيارة",
                            $"تم إيقاف الزيارة رقم {visit.VisitId} الخاصة بـ {visit.VisitorName}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow
                        );
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public void ResumeVisit(string visitId, string? performedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var visit = db.Visits.FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null)
                    {
                        return 0;
                    }

                    visit.Status = "Active";

                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            "Resume",
                            "استئناف زيارة",
                            $"تم استئناف الزيارة رقم {visit.VisitId} الخاصة بـ {visit.VisitorName}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow
                        );
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public void UpdateVisitApprovalStatus(
            string visitId,
            string approvalStatus,
            string? performedBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            ExecuteInTransaction(
                db,
                () =>
                {
                    var visit = db.Visits.FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null)
                    {
                        return 0;
                    }
                    // enforce approver permissions
                    if (!string.IsNullOrWhiteSpace(performedBy))
                    {
                        var approver = db.UserAccounts.FirstOrDefault(u =>
                            u.Username == performedBy
                        );
                        var approvalContext = _delegationService.ResolveExecutionContext(
                            approver,
                            visit.IsDetainedVisit
                                ? new[]
                                {
                                    AppPermissions.ApproveVisits,
                                    AppPermissions.ApproveDetainedVisit,
                                }
                                : new[] { AppPermissions.ApproveVisits },
                            delegator => CanApproveVisitDirectly(db, visit, delegator)
                        );
                        if (!approvalContext.IsAllowed)
                        {
                            return 0;
                        }

                        var isApproved = string.Equals(
                            approvalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        );
                        var delegatedSuffix = approvalContext.IsDelegated
                            ? $" بتفويض من {approvalContext.Delegator?.DisplayName ?? approvalContext.Delegator?.Username}"
                            : string.Empty;

                        if (
                            !string.Equals(
                                approvalStatus,
                                "Approved",
                                StringComparison.OrdinalIgnoreCase
                            )
                            && !string.Equals(
                                approvalStatus,
                                "Rejected",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            return 0;
                        }

                        visit.ApprovalStatus = approvalStatus;
                        if (
                            string.Equals(
                                approvalStatus,
                                "Rejected",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            visit.Status = "Suspended";
                        }
                        else if (
                            string.Equals(
                                visit.Status,
                                "Suspended",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            visit.Status = "Active";
                        }

                        RecordUserActivity(
                            db,
                            visit.VisitId,
                            visit.VisitorName,
                            isApproved ? "Approve" : "Reject",
                            isApproved ? "اعتماد زيارة" : "رفض زيارة",
                            isApproved
                                ? $"تم اعتماد الزيارة رقم {visit.VisitId} الخاصة بـ {visit.VisitorName}{delegatedSuffix}."
                                : $"تم رفض الزيارة رقم {visit.VisitId} الخاصة بـ {visit.VisitorName}{delegatedSuffix}.",
                            "VisitsController",
                            performedBy,
                            _systemClock.LocalNow,
                            performedBy,
                            approvalContext.IsDelegated,
                            approvalContext.Delegator?.Username,
                            approvalContext.Delegation?.Id,
                            visit.VisitId
                        );

                        if (approvalContext.IsDelegated)
                        {
                            db.AuditLogs.Add(
                                new AuditLog
                                {
                                    Username = performedBy,
                                    ActualActorUsername = performedBy,
                                    ActionType = "DelegatedApproval",
                                    ActionLabel = isApproved
                                        ? "اعتماد زيارة بتفويض"
                                        : "رفض زيارة بتفويض",
                                    EntityType = "Visit",
                                    EntityId = visit.VisitId,
                                    Message = isApproved
                                        ? $"تم اعتماد الزيارة بواسطة {performedBy} بتفويض من {approvalContext.Delegator?.Username}."
                                        : $"تم رفض الزيارة بواسطة {performedBy} بتفويض من {approvalContext.Delegator?.Username}.",
                                    Source = nameof(VisitService),
                                    RecordedBy = performedBy,
                                    OccurredAt = _systemClock.UtcNow,
                                    Success = true,
                                    ActedUnderDelegation = true,
                                    DelegatedFromUsername =
                                        approvalContext.Delegator?.Username ?? string.Empty,
                                    DelegationId = approvalContext.Delegation?.Id,
                                    AfterJson = System.Text.Json.JsonSerializer.Serialize(
                                        new
                                        {
                                            visit.VisitId,
                                            visit.VisitorName,
                                            approvalStatus,
                                            ActualActorUsername = performedBy,
                                            DelegatedFromUsername = approvalContext
                                                .Delegator
                                                ?.Username,
                                            approvalContext.Delegation?.Id,
                                        }
                                    ),
                                }
                            );
                        }

                        db.SaveChanges();
                        return 0;
                    }

                    if (
                        !string.Equals(
                            approvalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                        && !string.Equals(
                            approvalStatus,
                            "Rejected",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return 0;
                    }

                    visit.ApprovalStatus = approvalStatus;
                    if (
                        string.Equals(
                            approvalStatus,
                            "Rejected",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        visit.Status = "Suspended";
                    }
                    else if (
                        string.Equals(visit.Status, "Suspended", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        visit.Status = "Active";
                    }

                    db.SaveChanges();
                    return 0;
                }
            );
        }

        public (bool allowed, string reason) RecordVisitScan(
            string visitId,
            string? scannerUser = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            return ExecuteInTransaction(
                db,
                () =>
                {
                    var workHours = GetWorkHoursSettings(db);
                    var now = _systemClock.LocalNow;
                    var visit = CreateVisitQuery(db).FirstOrDefault(v => v.VisitId == visitId);
                    if (visit == null)
                    {
                        return (false, "visit_not_found");
                    }

                    if (visit.ExpiresAt.HasValue && visit.ExpiresAt.Value <= now)
                    {
                        visit.Status = "Completed";
                        visit.ExitTime ??= visit.ExpiresAt;
                        db.SaveChanges();
                        return (false, "visit_expired");
                    }

                    if (
                        !string.Equals(
                            visit.ApprovalStatus,
                            "Approved",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return (false, "visit_not_approved");
                    }

                    if (visit.Status == "Active")
                    {
                        var entryTime = now;
                        visit.EntryTime = entryTime;
                        visit.ExpiresAt = CalculateVisitExpiration(entryTime, workHours);
                        visit.Status = "Inside";
                        ApplyVisitTimesToCompanions(visit, entryTime, null);
                        db.SaveChanges();
                        return (true, "entry_recorded");
                    }

                    if (visit.Status == "Inside")
                    {
                        visit.ExitTime = now;
                        visit.ExpiresAt = visit.ExitTime;
                        visit.Status = "Completed";
                        ApplyVisitTimesToCompanions(visit, visit.EntryTime, now);
                        db.SaveChanges();
                        return (true, "exit_recorded");
                    }

                    return (false, "not_allowed");
                }
            );
        }

        private void PrepareVisitForSave(Visit visit)
        {
            if (visit.VisitDate == default)
            {
                visit.VisitDate = _systemClock.LocalNow;
            }

            visit.VisitorName = (visit.VisitorName ?? string.Empty).Trim();
            visit.VisitLocation = (visit.VisitLocation ?? string.Empty).Trim();
            visit.NationalId = NormalizeVisitNationalId(visit.NationalId);
            visit.PhoneNumber = NormalizeVisitPhoneNumber(visit.PhoneNumber);
            visit.Purpose = (visit.Purpose ?? string.Empty).Trim();
            visit.HostName = (visit.HostName ?? string.Empty).Trim();
            visit.VisitedPersonName = string.IsNullOrWhiteSpace(visit.VisitedPersonName)
                ? visit.HostName
                : visit.VisitedPersonName.Trim();
            visit.VisitedPersonType = NormalizeVisitedPersonType(
                visit.VisitedPersonType,
                visit.Purpose,
                OnlineEditionSettings.SimplifiedVisits(_configuration)
            );
            visit.HostName = visit.VisitedPersonName;
            visit.Status = string.IsNullOrWhiteSpace(visit.Status) ? "Active" : visit.Status.Trim();
            visit.ApprovalStatus = "Pending";
            visit.Companions = SanitizeCompanions(visit.Companions);
        }

        private static string NormalizeVisitedPersonType(
            string? type,
            string? purpose,
            bool simplifiedVisits
        )
        {
            if (simplifiedVisits)
            {
                if (
                    string.Equals(
                        type,
                        Visit.VisitedPersonTypeEmployee,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return Visit.VisitedPersonTypeEmployee;
                }

                if (
                    string.Equals(
                        type,
                        Visit.VisitedPersonTypeOther,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return Visit.VisitedPersonTypeOther;
                }

                return Visit.VisitedPersonTypeHost;
            }

            if (
                string.Equals(
                    type,
                    Visit.VisitedPersonTypeDetained,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Visit.VisitedPersonTypeDetained;
            }

            if (
                string.Equals(
                    type,
                    Visit.VisitedPersonTypePrisoner,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Visit.VisitedPersonTypePrisoner;
            }

            if (
                string.Equals(
                    type,
                    Visit.VisitedPersonTypeEmployee,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Visit.VisitedPersonTypeEmployee;
            }

            if (
                string.Equals(
                    type,
                    Visit.VisitedPersonTypeOther,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Visit.VisitedPersonTypeOther;
            }

            var normalizedPurpose = purpose ?? string.Empty;
            if (
                normalizedPurpose.Contains("موقوف", StringComparison.OrdinalIgnoreCase)
                || normalizedPurpose.Contains("توقيف", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Visit.VisitedPersonTypeDetained;
            }

            if (
                normalizedPurpose.Contains("مسجون", StringComparison.OrdinalIgnoreCase)
                || normalizedPurpose.Contains("سجين", StringComparison.OrdinalIgnoreCase)
                || normalizedPurpose.Contains("نزيل", StringComparison.OrdinalIgnoreCase)
            )
            {
                return Visit.VisitedPersonTypePrisoner;
            }

            return Visit.VisitedPersonTypeHost;
        }

        private static List<VisitCompanion> SanitizeCompanions(
            IEnumerable<VisitCompanion>? companions
        )
        {
            return (companions ?? Enumerable.Empty<VisitCompanion>())
                .Where(c => c != null)
                .Select(
                    (companion, index) =>
                        new VisitCompanion
                        {
                            Id = companion.Id,
                            VisitId = companion.VisitId,
                            FullName = (companion.FullName ?? string.Empty).Trim(),
                            NationalId = NormalizeVisitNationalId(companion.NationalId),
                            PhoneNumber = NormalizeVisitPhoneNumber(companion.PhoneNumber),
                            Relationship = (companion.Relationship ?? string.Empty).Trim(),
                            SortOrder = index,
                            EntryTime = companion.EntryTime,
                            ExitTime = companion.ExitTime,
                        }
                )
                .Where(c => !c.IsEmpty)
                .ToList();
        }

        private static void SyncCompanions(
            Visit existingVisit,
            IEnumerable<VisitCompanion>? incomingCompanions
        )
        {
            var sanitizedCompanions = SanitizeCompanions(incomingCompanions);
            var incomingById = sanitizedCompanions.Where(c => c.Id > 0).ToDictionary(c => c.Id);

            var companionsToRemove = existingVisit
                .Companions.Where(c => c.Id > 0 && !incomingById.ContainsKey(c.Id))
                .ToList();

            foreach (var companion in companionsToRemove)
            {
                existingVisit.Companions.Remove(companion);
            }

            foreach (var incomingCompanion in sanitizedCompanions)
            {
                if (incomingCompanion.Id > 0)
                {
                    var existingCompanion = existingVisit.Companions.FirstOrDefault(c =>
                        c.Id == incomingCompanion.Id
                    );
                    if (existingCompanion != null)
                    {
                        existingCompanion.FullName = incomingCompanion.FullName;
                        existingCompanion.NationalId = incomingCompanion.NationalId;
                        existingCompanion.PhoneNumber = incomingCompanion.PhoneNumber;
                        existingCompanion.Relationship = incomingCompanion.Relationship;
                        existingCompanion.SortOrder = incomingCompanion.SortOrder;
                        continue;
                    }
                }

                existingVisit.Companions.Add(
                    new VisitCompanion
                    {
                        VisitId = existingVisit.VisitId,
                        FullName = incomingCompanion.FullName,
                        NationalId = incomingCompanion.NationalId,
                        PhoneNumber = incomingCompanion.PhoneNumber,
                        Relationship = incomingCompanion.Relationship,
                        SortOrder = incomingCompanion.SortOrder,
                        EntryTime = existingVisit.EntryTime,
                        ExitTime = existingVisit.ExitTime,
                    }
                );
            }
        }

        private static void ApplyVisitTimesToCompanions(
            Visit visit,
            DateTime? entryTime,
            DateTime? exitTime
        )
        {
            foreach (var companion in visit.Companions)
            {
                if (entryTime.HasValue && !companion.EntryTime.HasValue)
                {
                    companion.EntryTime = entryTime;
                }

                if (exitTime.HasValue)
                {
                    companion.ExitTime = exitTime;
                }
            }
        }

        private static string GenerateNextVisitId(ApplicationDbContext db)
        {
            var nextNumber =
                db.Visits.IgnoreQueryFilters().AsEnumerable()
                    .Select(v => int.TryParse(v.VisitId.TrimStart('V'), out var value) ? value : 0)
                    .DefaultIfEmpty(0)
                    .Max() + 1;
            return $"V{nextNumber}";
        }

        private void SynchronizeVisitStates(ApplicationDbContext db)
        {
            var now = _systemClock.LocalNow;
            var workHours = GetWorkHoursSettings(db);
            var visits = db
                .Visits.Where(visit =>
                    visit.Status == "Active"
                    || visit.Status == "Inside"
                    || (visit.Status == "Completed" && !visit.ExpiresAt.HasValue)
                )
                .ToList();
            var changed = false;

            foreach (var visit in visits)
            {
                if (EnsureVisitExpiration(visit, workHours))
                {
                    changed = true;
                }

                if (visit.Status == "Inside")
                {
                    var expirationTime =
                        visit.ExpiresAt
                        ?? CalculateVisitExpiration(visit.EntryTime ?? visit.VisitDate, workHours);
                    if (!visit.ExpiresAt.HasValue)
                    {
                        visit.ExpiresAt = expirationTime;
                        changed = true;
                    }

                    if (expirationTime <= now)
                    {
                        visit.Status = "Completed";
                        visit.ExitTime ??= expirationTime;
                        ApplyVisitTimesToCompanions(visit, visit.EntryTime, visit.ExitTime);
                        changed = true;
                    }
                }

                if (
                    visit.Status == "Active"
                    && visit.ExpiresAt.HasValue
                    && visit.ExpiresAt.Value <= now
                )
                {
                    visit.Status = "Completed";
                    visit.ExitTime ??= visit.ExpiresAt;
                    ApplyVisitTimesToCompanions(visit, visit.EntryTime, visit.ExitTime);
                    changed = true;
                }

                if (IsVisitNoShowExpired(visit, now))
                {
                    visit.Status = "Completed";
                    visit.ExitTime ??= visit.VisitDate.AddMinutes(20);
                    ApplyVisitTimesToCompanions(visit, visit.EntryTime, visit.ExitTime);
                    changed = true;
                }
            }

            if (changed)
            {
                db.SaveChanges();
            }
        }

        private static IQueryable<Visit> CreateVisitQuery(ApplicationDbContext db)
        {
            return db.Visits.Include(v => v.Companions);
        }

        private static bool IsVisitNoShowExpired(Visit visit, DateTime now)
        {
            return visit.Status == "Active"
                && !visit.EntryTime.HasValue
                && visit.VisitDate.AddMinutes(20) <= now;
        }

        private static bool EnsureVisitExpiration(Visit visit, WorkHoursSettings workHours)
        {
            if (visit.ExpiresAt.HasValue)
            {
                return false;
            }

            DateTime? expirationTime = visit.Status switch
            {
                "Inside" => CalculateVisitExpiration(visit.EntryTime ?? visit.VisitDate, workHours),
                "Completed" => CalculateVisitExpiration(
                    visit.EntryTime ?? visit.VisitDate,
                    workHours
                ),
                _ => null,
            };

            if (!expirationTime.HasValue)
            {
                return false;
            }

            visit.ExpiresAt = expirationTime;
            return true;
        }

        private static DateTime CalculateVisitExpiration(
            DateTime referenceTime,
            WorkHoursSettings workHours
        )
        {
            var workEndToday = referenceTime.Date.Add(workHours.EndTime.ToTimeSpan());
            return referenceTime <= workEndToday
                ? workEndToday
                : referenceTime.Date.AddDays(1).Add(workHours.EndTime.ToTimeSpan());
        }

        private WorkHoursSettings GetWorkHoursSettings()
        {
            using var db = _dbContextFactory.CreateDbContext();
            return GetWorkHoursSettings(db);
        }

        private static WorkHoursSettings GetWorkHoursSettings(ApplicationDbContext db)
        {
            var settings = db.AdministrationSettings.AsNoTracking().FirstOrDefault();
            return new WorkHoursSettings(
                settings?.WorkStartTime ?? new TimeOnly(8, 0),
                settings?.WorkEndTime ?? new TimeOnly(16, 0)
            );
        }

        private static string NormalizeVisitNationalId(string? nationalId)
        {
            var digits = new string((nationalId ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length <= 10 ? digits : digits[..10];
        }

        private static string NormalizeVisitPhoneNumber(string? phoneNumber)
        {
            var digits = new string((phoneNumber ?? string.Empty).Where(char.IsDigit).ToArray());
            return digits.Length <= 10 ? digits : digits[..10];
        }

        private static void RecordUserActivity(
            ApplicationDbContext db,
            string username,
            string displayName,
            string actionType,
            string actionLabel,
            string message,
            string source,
            string? recordedBy,
            DateTime occurredAt,
            string? actualActorUsername = null,
            bool actedUnderDelegation = false,
            string? delegatedFromUsername = null,
            int? delegationId = null,
            string? entityId = null
        )
        {
            db.UserActivities.Add(
                new UserActivity
                {
                    Username = username,
                    DisplayName = displayName,
                    ActionType = actionType,
                    ActionLabel = actionLabel,
                    Message = message,
                    Source = source,
                    RecordedBy = recordedBy ?? string.Empty,
                    OccurredAt = occurredAt,
                }
            );

            try
            {
                db.AuditLogs.Add(
                    new AuditLog
                    {
                        Username = username,
                        ActualActorUsername = actualActorUsername ?? recordedBy ?? username,
                        ActionType = actionType,
                        ActionLabel = actionLabel,
                        EntityType = "Visit",
                        EntityId = entityId ?? string.Empty,
                        Message = message,
                        Source = source,
                        RecordedBy = recordedBy ?? string.Empty,
                        OccurredAt = occurredAt,
                        Success = true,
                        ActedUnderDelegation = actedUnderDelegation,
                        DelegatedFromUsername = delegatedFromUsername ?? string.Empty,
                        DelegationId = delegationId,
                    }
                );
            }
            catch
            {
                // ignore audit failures
            }
        }

        private static bool CanApproveVisitDirectly(
            ApplicationDbContext db,
            Visit visit,
            UserAccount? user
        )
        {
            if (visit == null || user == null || !user.IsActive)
            {
                return false;
            }

            if (AppRoles.IsSuperAdmin(user))
            {
                return true;
            }

            if (
                string.Equals(
                    user.Role,
                    AppRoles.GeneralManager,
                    StringComparison.OrdinalIgnoreCase
                )
                || string.Equals(
                    user.Role,
                    AppRoles.SecurityManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return true;
            }

            var canApproveCurrentVisit =
                user.CanApproveVisits || (visit.IsDetainedVisit && user.CanApproveDetainedVisit);
            if (!canApproveCurrentVisit)
            {
                return false;
            }

            if (
                !string.Equals(
                    user.Role,
                    AppRoles.DepartmentManager,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }

            var scopedDepartment = NormalizeText(user.Department);
            if (string.IsNullOrWhiteSpace(scopedDepartment))
            {
                return false;
            }

            var managedDepartments = db
                .Departments.AsNoTracking()
                .Where(department =>
                    department.IsActive && department.ManagerUsername == user.Username
                )
                .Select(department => department.Name)
                .AsEnumerable()
                .Select(NormalizeText)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!managedDepartments.Contains(scopedDepartment))
            {
                return false;
            }

            var normalizedVisitLocation = NormalizeText(visit.VisitLocation ?? visit.HostName);
            return normalizedVisitLocation.Contains(
                scopedDepartment,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string NormalizeText(string? value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static T ExecuteInTransaction<T>(ApplicationDbContext db, Func<T> action)
        {
            using var transaction = db.Database.BeginTransaction();
            try
            {
                var result = action();
                transaction.Commit();
                return result;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
