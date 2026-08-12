using Microsoft.EntityFrameworkCore;
using VehiclePermitSystemWeb.Data;
using VehiclePermitSystemWeb.Models.ViewModels.Workplace;

namespace VehiclePermitSystemWeb.Services.Workplace
{
    public sealed class EmergencyService : IEmergencyService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IWorkplaceDirectoryService _directory;
        private readonly ISystemClock _clock;

        public EmergencyService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            IWorkplaceDirectoryService directory,
            ISystemClock clock
        )
        {
            _dbContextFactory = dbContextFactory;
            _directory = directory;
            _clock = clock;
        }

        public EmergencyDashboardViewModel BuildDashboard(int? siteId)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var sites = _directory.GetLocationOptions();
            var selectedSiteId = siteId ?? sites.FirstOrDefault()?.SiteId;
            var active = selectedSiteId.HasValue
                ? db.EmergencySessions.AsNoTracking()
                    .Include(item => item.WorkplaceSite)
                    .Include(item => item.Members)
                    .Where(item => item.WorkplaceSiteId == selectedSiteId
                        && item.Status == EmergencySessionStatuses.Active)
                    .OrderByDescending(item => item.StartedAtUtc)
                    .FirstOrDefault()
                : null;
            var recent = db.EmergencySessions.AsNoTracking()
                .Include(item => item.WorkplaceSite)
                .Include(item => item.Members)
                .OrderByDescending(item => item.StartedAtUtc)
                .Take(10)
                .ToList();

            return new EmergencyDashboardViewModel
            {
                SelectedSiteId = selectedSiteId,
                Sites = sites,
                ActiveSession = active == null ? null : MapSession(active),
                RecentSessions = recent.Select(item => new EmergencySessionSummaryViewModel
                {
                    Id = item.Id,
                    SiteName = item.WorkplaceSite?.Name ?? "موقع",
                    StatusDisplay = item.Status == EmergencySessionStatuses.Active ? "نشطة" : "مكتملة",
                    TotalCount = item.Members.Count,
                    StartedAtUtc = item.StartedAtUtc,
                    EndedAtUtc = item.EndedAtUtc,
                }).ToList(),
            };
        }

        public bool StartSession(int siteId, string actor, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var site = db.WorkplaceSites.AsNoTracking()
                .FirstOrDefault(item => item.Id == siteId && item.IsActive);
            if (site == null)
            {
                error = "الموقع غير موجود أو متوقف.";
                return false;
            }
            if (db.EmergencySessions.Any(item => item.WorkplaceSiteId == siteId
                && item.Status == EmergencySessionStatuses.Active))
            {
                error = "توجد جلسة طوارئ نشطة لهذا الموقع.";
                return false;
            }

            var presence = _directory.BuildAttendanceDashboard(null, null).PresentPeople
                .Where(item => string.Equals(item.Location, site.Name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Destination, site.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var session = new EmergencySession
            {
                WorkplaceSiteId = site.Id,
                StartedBy = actor,
                StartedAtUtc = _clock.UtcNow,
                Members = presence.Select(item => new EmergencySessionMember
                {
                    PersonProfileId = item.PersonId,
                    FullName = item.FullName,
                    PersonType = item.PersonType,
                    Reference = item.Reference,
                    Location = item.Location,
                }).ToList(),
            };
            db.EmergencySessions.Add(session);
            db.SaveChanges();
            return true;
        }

        public bool UpdateMember(int sessionId, int memberId, string status, string actor, out string error)
        {
            error = string.Empty;
            if (!EmergencyMemberStatuses.Supported.Contains(status))
            {
                error = "حالة التحقق غير صالحة.";
                return false;
            }
            using var db = _dbContextFactory.CreateDbContext();
            var member = db.EmergencySessionMembers.Include(item => item.EmergencySession)
                .FirstOrDefault(item => item.Id == memberId && item.EmergencySessionId == sessionId);
            if (member?.EmergencySession?.Status != EmergencySessionStatuses.Active)
            {
                error = "جلسة الطوارئ غير نشطة.";
                return false;
            }
            member.Status = status;
            member.UpdatedBy = actor;
            member.UpdatedAtUtc = _clock.UtcNow;
            db.SaveChanges();
            return true;
        }

        public bool CompleteSession(int sessionId, string actor, out string error)
        {
            error = string.Empty;
            using var db = _dbContextFactory.CreateDbContext();
            var session = db.EmergencySessions.FirstOrDefault(item => item.Id == sessionId);
            if (session == null || session.Status != EmergencySessionStatuses.Active)
            {
                error = "جلسة الطوارئ غير نشطة.";
                return false;
            }
            session.Status = EmergencySessionStatuses.Completed;
            session.EndedBy = actor;
            session.EndedAtUtc = _clock.UtcNow;
            db.SaveChanges();
            return true;
        }

        private static EmergencySessionViewModel MapSession(EmergencySession session)
        {
            var members = session.Members.OrderBy(item => item.Status)
                .ThenBy(item => item.FullName).Select(item => new EmergencyMemberViewModel
                {
                    Id = item.Id,
                    FullName = item.FullName,
                    PersonTypeDisplay = PersonTypes.DisplayName(item.PersonType),
                    Reference = item.Reference,
                    Location = item.Location,
                    Status = item.Status,
                    StatusDisplay = EmergencyMemberStatuses.DisplayName(item.Status),
                }).ToList();
            return new EmergencySessionViewModel
            {
                Id = session.Id,
                SiteId = session.WorkplaceSiteId,
                SiteName = session.WorkplaceSite?.Name ?? "موقع",
                StartedAtUtc = session.StartedAtUtc,
                StartedBy = session.StartedBy,
                TotalCount = members.Count,
                SafeCount = members.Count(item => item.Status == EmergencyMemberStatuses.Safe),
                AssistanceCount = members.Count(item => item.Status == EmergencyMemberStatuses.Assistance),
                MissingCount = members.Count(item => item.Status == EmergencyMemberStatuses.Missing),
                PendingCount = members.Count(item => item.Status == EmergencyMemberStatuses.Pending),
                Members = members,
            };
        }
    }
}
