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

namespace VehiclePermitSystemWeb.Services.Users
{
    public sealed class UserSessionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly TimeSpan _sessionIdleTimeout = TimeSpan.FromMinutes(15);

        public UserSessionService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
        }

        public string CreateSession(string username)
        {
            using var db = _dbContextFactory.CreateDbContext();
            var sessionId = Guid.NewGuid().ToString();
            var now = _systemClock.UtcNow;
            db.SessionRecords.Add(
                new SessionRecord
                {
                    SessionId = sessionId,
                    Username = username,
                    ExpiresAtUtc = now.Add(_sessionIdleTimeout),
                    LastActivityUtc = now,
                }
            );
            db.SaveChanges();
            return sessionId;
        }

        public bool ValidateSession(string sessionId, out string? username)
        {
            username = null;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var session = db.SessionRecords.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null)
            {
                return false;
            }

            if (session.ExpiresAtUtc < _systemClock.UtcNow)
            {
                db.SessionRecords.Remove(session);
                db.SaveChanges();
                return false;
            }

            var activeUserExists = db.UserAccounts.AsNoTracking()
                .Any(user => user.Username == session.Username && user.IsActive);
            if (!activeUserExists)
            {
                db.SessionRecords.Remove(session);
                db.SaveChanges();
                return false;
            }

            username = session.Username;
            return true;
        }

        public bool RefreshSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var session = db.SessionRecords.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null)
            {
                return false;
            }

            var now = _systemClock.UtcNow;
            session.ExpiresAtUtc = now.Add(_sessionIdleTimeout);
            session.LastActivityUtc = now;
            db.SaveChanges();
            return true;
        }

        public void RemoveSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            using var db = _dbContextFactory.CreateDbContext();
            var session = db.SessionRecords.FirstOrDefault(s => s.SessionId == sessionId);
            if (session == null)
            {
                return;
            }

            db.SessionRecords.Remove(session);
            db.SaveChanges();
        }

    }
}
