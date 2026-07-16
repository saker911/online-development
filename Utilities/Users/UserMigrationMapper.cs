using System.Collections.Concurrent;
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
using VehiclePermitSystemWeb.Services.Administration;
using VehiclePermitSystemWeb.Services.Audit;
using VehiclePermitSystemWeb.Services.Backup;
using VehiclePermitSystemWeb.Services.Bootstrap;
using VehiclePermitSystemWeb.Services.Common;
using VehiclePermitSystemWeb.Services.Delegations;
using VehiclePermitSystemWeb.Services.Gate;
using VehiclePermitSystemWeb.Services.Management;
using VehiclePermitSystemWeb.Services.Notifications;
using VehiclePermitSystemWeb.Services.Permits;
using VehiclePermitSystemWeb.Services.Reports;
using VehiclePermitSystemWeb.Services.Users;
using VehiclePermitSystemWeb.Services.Visits;

namespace VehiclePermitSystemWeb.Utilities.Users
{
    public static class UserMigrationMapper
    {
        public static void MigrateUsernameReferences(
            ApplicationDbContext db,
            string oldUsername,
            string newUsername,
            string newDisplayName
        )
        {
            foreach (
                var externalLogin in db.ExternalUserLogins.IgnoreQueryFilters().Where(login =>
                    login.Username == oldUsername
                )
            )
            {
                externalLogin.Username = newUsername;
            }

            foreach (
                var account in db.UserAccounts.Where(account =>
                    account.ManagerUsername == oldUsername
                )
            )
            {
                account.ManagerUsername = newUsername;
            }

            foreach (
                var department in db.Departments.Where(department =>
                    department.ManagerUsername == oldUsername
                )
            )
            {
                department.ManagerUsername = newUsername;
                department.ManagerDisplayName = newDisplayName;
            }

            var settings = AdministrationSettingsService.GetAdministrationSettingsRecord(db, 1);
            if (settings != null && settings.GeneralManagerUsername == oldUsername)
            {
                settings.GeneralManagerUsername = newUsername;
                settings.ManagerName = newDisplayName;
            }

            foreach (
                var delegation in db.Delegations.Where(delegation =>
                    delegation.DelegatorUsername == oldUsername
                    || delegation.DelegateeUsername == oldUsername
                    || delegation.CreatedByUsername == oldUsername
                    || delegation.LastUpdatedByUsername == oldUsername
                    || delegation.CancelledByUsername == oldUsername
                )
            )
            {
                if (delegation.DelegatorUsername == oldUsername)
                {
                    delegation.DelegatorUsername = newUsername;
                }

                if (delegation.DelegateeUsername == oldUsername)
                {
                    delegation.DelegateeUsername = newUsername;
                }

                if (delegation.CreatedByUsername == oldUsername)
                {
                    delegation.CreatedByUsername = newUsername;
                }

                if (delegation.LastUpdatedByUsername == oldUsername)
                {
                    delegation.LastUpdatedByUsername = newUsername;
                }

                if (delegation.CancelledByUsername == oldUsername)
                {
                    delegation.CancelledByUsername = newUsername;
                }
            }

            foreach (
                var activity in db.UserActivities.Where(activity =>
                    activity.Username == oldUsername || activity.RecordedBy == oldUsername
                )
            )
            {
                if (activity.Username == oldUsername)
                {
                    activity.Username = newUsername;
                }

                if (activity.RecordedBy == oldUsername)
                {
                    activity.RecordedBy = newUsername;
                }
            }

            foreach (
                var auditLog in db.AuditLogs.Where(auditLog =>
                    auditLog.Username == oldUsername
                    || auditLog.RecordedBy == oldUsername
                    || auditLog.ActualActorUsername == oldUsername
                    || auditLog.DelegatedFromUsername == oldUsername
                )
            )
            {
                if (auditLog.Username == oldUsername)
                {
                    auditLog.Username = newUsername;
                }

                if (auditLog.RecordedBy == oldUsername)
                {
                    auditLog.RecordedBy = newUsername;
                }

                if (auditLog.ActualActorUsername == oldUsername)
                {
                    auditLog.ActualActorUsername = newUsername;
                }

                if (auditLog.DelegatedFromUsername == oldUsername)
                {
                    auditLog.DelegatedFromUsername = newUsername;
                }
            }

            foreach (
                var permitActivity in db.PermitActivities.Where(activity =>
                    activity.RecordedBy == oldUsername
                    || activity.GateOperatorAccount == oldUsername
                )
            )
            {
                if (permitActivity.RecordedBy == oldUsername)
                {
                    permitActivity.RecordedBy = newUsername;
                }

                if (permitActivity.GateOperatorAccount == oldUsername)
                {
                    permitActivity.GateOperatorAccount = newUsername;
                }
            }

            foreach (var permit in db.Permits.Where(permit => permit.CreatedBy == oldUsername))
            {
                permit.CreatedBy = newUsername;
            }

            foreach (
                var visit in db.Visits.Where(visit => visit.VisitApproverUsername == oldUsername)
            )
            {
                visit.VisitApproverUsername = newUsername;
            }

            foreach (
                var session in db.SessionRecords.Where(session => session.Username == oldUsername)
            )
            {
                session.Username = newUsername;
            }
        }

        public static void SyncDisplayOperatorSessionIdentity(
            ConcurrentDictionary<string, DisplayOperatorSessionInfo> displayOperatorSessions,
            string oldUsername,
            string newUsername,
            string displayName
        )
        {
            foreach (var entry in displayOperatorSessions)
            {
                if (
                    !string.Equals(
                        entry.Value.Username,
                        oldUsername,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                entry.Value.Username = newUsername;
                entry.Value.DisplayName = displayName;
            }
        }
    }
}
