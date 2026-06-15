using System.Text.Json;
using Microsoft.AspNetCore.Http;
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

namespace VehiclePermitSystemWeb.Services.Delegations
{
    public sealed class DelegationService : IDelegationService
    {
        private static readonly StringComparer PermissionComparer = StringComparer.Ordinal;
        private static readonly IReadOnlySet<string> DelegateablePermissions = AppPermissions
            .EditorGroups.SelectMany(group => group.Permissions)
            .Select(item => item.Key)
            .ToHashSet(PermissionComparer);

        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly ISystemClock _systemClock;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DelegationService(
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            ISystemClock systemClock,
            IHttpContextAccessor httpContextAccessor
        )
        {
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _httpContextAccessor = httpContextAccessor;
        }

        public IReadOnlyCollection<string> GetEffectivePermissions(UserAccount? user)
        {
            if (user == null || !user.IsActive)
            {
                return Array.Empty<string>();
            }

            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var effectivePermissions = GetDirectPermissions(user).ToHashSet(PermissionComparer);
            foreach (var permission in GetActiveDelegatedPermissionKeys(db, user.Username))
            {
                effectivePermissions.Add(permission);
            }

            ApplyImplicitPermissions(effectivePermissions);

            return effectivePermissions.ToList();
        }

        public bool HasEffectivePermission(UserAccount? user, string permission)
        {
            if (string.IsNullOrWhiteSpace(permission))
            {
                return false;
            }

            var permissions = GetEffectivePermissions(user);
            if (permissions.Contains(permission, PermissionComparer))
            {
                return true;
            }

            return string.Equals(
                    permission,
                    AppPermissions.ApproveDetainedVisit,
                    StringComparison.Ordinal
                ) && permissions.Contains(AppPermissions.ApproveVisits, PermissionComparer);
        }

        public DelegationExecutionContext ResolveExecutionContext(
            UserAccount? actor,
            IEnumerable<string> permissions,
            Func<UserAccount, bool> directAccessEvaluator
        )
        {
            if (actor == null || !actor.IsActive)
            {
                return DelegationExecutionContext.Denied;
            }

            var candidatePermissions = permissions
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(PermissionComparer)
                .ToList();
            if (candidatePermissions.Count == 0)
            {
                return DelegationExecutionContext.Denied;
            }

            if (
                candidatePermissions.Any(permission => UserHasDirectPermission(actor, permission))
                && directAccessEvaluator(actor)
            )
            {
                return new DelegationExecutionContext
                {
                    IsAllowed = true,
                    IsDelegated = false,
                    MatchedPermission = candidatePermissions.First(permission =>
                        UserHasDirectPermission(actor, permission)
                    ),
                };
            }

            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var activeDelegations = db
                .Delegations.AsNoTracking()
                .Include(delegation => delegation.Permissions)
                .Where(delegation =>
                    delegation.DelegateeUsername == actor.Username
                    && delegation.Status == DelegationStatuses.Active
                )
                .OrderBy(delegation => delegation.StartAt)
                .ToList();

            foreach (var delegation in activeDelegations)
            {
                var delegator = db
                    .UserAccounts.AsNoTracking()
                    .FirstOrDefault(user => user.Username == delegation.DelegatorUsername);
                if (delegator == null || !delegator.IsActive)
                {
                    continue;
                }

                foreach (var permission in candidatePermissions)
                {
                    if (!DelegationGrantsPermission(delegation, delegator, permission))
                    {
                        continue;
                    }

                    if (!directAccessEvaluator(delegator))
                    {
                        continue;
                    }

                    return new DelegationExecutionContext
                    {
                        IsAllowed = true,
                        IsDelegated = true,
                        MatchedPermission = permission,
                        Delegation = delegation,
                        Delegator = delegator,
                    };
                }
            }

            return DelegationExecutionContext.Denied;
        }

        public IEnumerable<Delegation> GetDelegations(
            string? delegatorUsername = null,
            string? delegateeUsername = null,
            string? status = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var query = db
                .Delegations.AsNoTracking()
                .Include(item => item.Permissions)
                .AsQueryable();
            if (!string.IsNullOrWhiteSpace(delegatorUsername))
            {
                query = query.Where(item => item.DelegatorUsername == delegatorUsername);
            }

            if (!string.IsNullOrWhiteSpace(delegateeUsername))
            {
                query = query.Where(item => item.DelegateeUsername == delegateeUsername);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(item => item.Status == status);
            }

            return query
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .ToList();
        }

        public Delegation? GetDelegation(int id)
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);
            return db
                .Delegations.AsNoTracking()
                .Include(item => item.Permissions)
                .FirstOrDefault(item => item.Id == id);
        }

        public DelegationOperationResult CreateDelegation(
            DelegationDefinitionInput input,
            string? createdBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var validationError = ValidateDefinition(db, input, excludeDelegationId: null);
            if (validationError != null)
            {
                return validationError;
            }

            var now = _systemClock.LocalNow;
            var normalizedPermissions = NormalizePermissionKeys(input.PermissionKeys);
            var delegation = new Delegation
            {
                DelegationNumber = $"DLG-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                DelegatorUsername = input.DelegatorUsername.Trim(),
                DelegateeUsername = input.DelegateeUsername.Trim(),
                ScopeType = NormalizeScopeType(input.ScopeType),
                StartAt = input.StartAt,
                EndAt = input.EndAt,
                TimeZoneId = NormalizeTimeZoneId(input.TimeZoneId),
                Status = DetermineStatus(input.StartAt, input.EndAt, now),
                Notes = (input.Notes ?? string.Empty).Trim(),
                CreatedByUsername = NormalizeAuditUsername(createdBy, input.DelegatorUsername),
                CreatedAt = now,
                UpdatedAt = now,
                LastUpdatedByUsername = NormalizeAuditUsername(createdBy, input.DelegatorUsername),
                ActivatedAt = now >= input.StartAt && now <= input.EndAt ? now : null,
            };

            foreach (var permission in normalizedPermissions)
            {
                delegation.Permissions.Add(new DelegationPermission { PermissionKey = permission });
            }

            db.Delegations.Add(delegation);
            RecordDelegationAudit(
                db,
                delegation,
                "DelegationCreated",
                "إنشاء تفويض",
                $"تم إنشاء تفويض من {delegation.DelegatorUsername} إلى {delegation.DelegateeUsername}.",
                delegation.CreatedByUsername,
                beforeJson: string.Empty,
                afterJson: BuildDelegationSnapshot(delegation)
            );

            if (delegation.Status == DelegationStatuses.Active)
            {
                RecordDelegationAudit(
                    db,
                    delegation,
                    "DelegationActivated",
                    "تفعيل تفويض",
                    $"تم تفعيل التفويض {delegation.DelegationNumber}.",
                    delegation.CreatedByUsername,
                    afterJson: BuildDelegationSnapshot(delegation)
                );
            }

            db.SaveChanges();
            return DelegationOperationResult.Success(CloneDelegation(db, delegation.Id));
        }

        public DelegationOperationResult UpdateDelegation(
            int id,
            DelegationDefinitionInput input,
            string? updatedBy = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var delegation = db
                .Delegations.Include(item => item.Permissions)
                .FirstOrDefault(item => item.Id == id);
            if (delegation == null)
            {
                return DelegationOperationResult.Failure(
                    "delegation_not_found",
                    "التفويض غير موجود."
                );
            }

            if (
                delegation.Status == DelegationStatuses.Cancelled
                || delegation.Status == DelegationStatuses.Expired
            )
            {
                return DelegationOperationResult.Failure(
                    "delegation_locked",
                    "لا يمكن تعديل تفويض منتهٍ أو ملغى."
                );
            }

            var validationError = ValidateDefinition(db, input, excludeDelegationId: id);
            if (validationError != null)
            {
                return validationError;
            }

            var beforeJson = BuildDelegationSnapshot(delegation);
            delegation.DelegatorUsername = input.DelegatorUsername.Trim();
            delegation.DelegateeUsername = input.DelegateeUsername.Trim();
            delegation.ScopeType = NormalizeScopeType(input.ScopeType);
            delegation.StartAt = input.StartAt;
            delegation.EndAt = input.EndAt;
            delegation.TimeZoneId = NormalizeTimeZoneId(input.TimeZoneId);
            delegation.Notes = (input.Notes ?? string.Empty).Trim();
            delegation.UpdatedAt = _systemClock.LocalNow;
            delegation.LastUpdatedByUsername = NormalizeAuditUsername(
                updatedBy,
                delegation.DelegatorUsername
            );
            delegation.Status = DetermineStatus(
                delegation.StartAt,
                delegation.EndAt,
                _systemClock.LocalNow
            );
            if (delegation.Status == DelegationStatuses.Active && !delegation.ActivatedAt.HasValue)
            {
                delegation.ActivatedAt = _systemClock.LocalNow;
            }

            delegation.Permissions.Clear();
            foreach (var permission in NormalizePermissionKeys(input.PermissionKeys))
            {
                delegation.Permissions.Add(new DelegationPermission { PermissionKey = permission });
            }

            RecordDelegationAudit(
                db,
                delegation,
                "DelegationUpdated",
                "تحديث تفويض",
                $"تم تحديث التفويض {delegation.DelegationNumber}.",
                delegation.LastUpdatedByUsername,
                beforeJson,
                BuildDelegationSnapshot(delegation)
            );

            db.SaveChanges();
            return DelegationOperationResult.Success(CloneDelegation(db, delegation.Id));
        }

        public bool CancelDelegation(int id, string? cancelledBy = null, string? reason = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var delegation = db
                .Delegations.Include(item => item.Permissions)
                .FirstOrDefault(item => item.Id == id);
            if (delegation == null || delegation.Status == DelegationStatuses.Cancelled)
            {
                return false;
            }

            var beforeJson = BuildDelegationSnapshot(delegation);
            delegation.Status = DelegationStatuses.Cancelled;
            delegation.CancelledAt = _systemClock.LocalNow;
            delegation.CancelledByUsername = NormalizeAuditUsername(
                cancelledBy,
                delegation.DelegatorUsername
            );
            delegation.CancelReason = (reason ?? string.Empty).Trim();
            delegation.UpdatedAt = _systemClock.LocalNow;
            delegation.LastUpdatedByUsername = delegation.CancelledByUsername;

            RecordDelegationAudit(
                db,
                delegation,
                "DelegationCancelled",
                "إلغاء تفويض",
                $"تم إلغاء التفويض {delegation.DelegationNumber}.",
                delegation.CancelledByUsername,
                beforeJson,
                BuildDelegationSnapshot(delegation)
            );

            db.SaveChanges();
            return true;
        }

        public bool ExtendDelegation(int id, DateTime newEndAt, string? updatedBy = null)
        {
            using var db = _dbContextFactory.CreateDbContext();
            RefreshStatuses(db);

            var delegation = db
                .Delegations.Include(item => item.Permissions)
                .FirstOrDefault(item => item.Id == id);
            if (delegation == null || delegation.Status == DelegationStatuses.Cancelled)
            {
                return false;
            }

            if (newEndAt <= delegation.StartAt || newEndAt <= delegation.EndAt)
            {
                return false;
            }

            var beforeJson = BuildDelegationSnapshot(delegation);
            delegation.EndAt = newEndAt;
            delegation.UpdatedAt = _systemClock.LocalNow;
            delegation.LastUpdatedByUsername = NormalizeAuditUsername(
                updatedBy,
                delegation.DelegatorUsername
            );
            delegation.Status = DetermineStatus(
                delegation.StartAt,
                delegation.EndAt,
                _systemClock.LocalNow
            );

            RecordDelegationAudit(
                db,
                delegation,
                "DelegationUpdated",
                "تمديد تفويض",
                $"تم تمديد التفويض {delegation.DelegationNumber} إلى {VehiclePermitSystemWeb.Utilities.Dates.DateHelper.ToGregorianDateTime12(newEndAt)}.",
                delegation.LastUpdatedByUsername,
                beforeJson,
                BuildDelegationSnapshot(delegation)
            );

            db.SaveChanges();
            return true;
        }

        public IEnumerable<AuditLog> GetDelegatedActionAuditLogs(
            string? actualActorUsername = null,
            string? delegatedFromUsername = null,
            DateTime? fromDate = null,
            DateTime? toDate = null
        )
        {
            using var db = _dbContextFactory.CreateDbContext();
            var query = db.AuditLogs.AsNoTracking().Where(log => log.ActedUnderDelegation);
            if (!string.IsNullOrWhiteSpace(actualActorUsername))
            {
                query = query.Where(log => log.ActualActorUsername == actualActorUsername);
            }

            if (!string.IsNullOrWhiteSpace(delegatedFromUsername))
            {
                query = query.Where(log => log.DelegatedFromUsername == delegatedFromUsername);
            }

            if (fromDate.HasValue)
            {
                query = query.Where(log => log.OccurredAt >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(log => log.OccurredAt <= toDate.Value);
            }

            return query
                .OrderByDescending(log => log.OccurredAt)
                .ThenByDescending(log => log.Id)
                .ToList();
        }

        private static IEnumerable<string> GetDirectPermissions(UserAccount user)
        {
            return AppPermissions.GetGrantedPermissions(user);
        }

        private IEnumerable<string> GetActiveDelegatedPermissionKeys(
            ApplicationDbContext db,
            string delegateeUsername
        )
        {
            var activeDelegations = db
                .Delegations.AsNoTracking()
                .Include(delegation => delegation.Permissions)
                .Where(delegation =>
                    delegation.DelegateeUsername == delegateeUsername
                    && delegation.Status == DelegationStatuses.Active
                )
                .OrderBy(delegation => delegation.StartAt)
                .ToList();

            foreach (var delegation in activeDelegations)
            {
                var delegator = db
                    .UserAccounts.AsNoTracking()
                    .FirstOrDefault(user => user.Username == delegation.DelegatorUsername);
                if (delegator == null || !delegator.IsActive)
                {
                    continue;
                }

                foreach (var permission in GetGrantedPermissionKeys(delegation, delegator))
                {
                    yield return permission;
                }
            }
        }

        private static IEnumerable<string> GetGrantedPermissionKeys(
            Delegation delegation,
            UserAccount delegator
        )
        {
            var directPermissions = GetDirectPermissions(delegator)
                .Where(permission => DelegateablePermissions.Contains(permission))
                .ToHashSet(PermissionComparer);

            if (
                string.Equals(
                    delegation.ScopeType,
                    DelegationScopeTypes.Full,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return directPermissions;
            }

            return delegation
                .Permissions.Select(item => item.PermissionKey)
                .Where(permission => directPermissions.Contains(permission));
        }

        private static bool UserHasDirectPermission(UserAccount user, string permission)
        {
            var directPermissions = GetDirectPermissions(user).ToHashSet(PermissionComparer);
            if (directPermissions.Contains(permission))
            {
                return true;
            }

            return string.Equals(
                    permission,
                    AppPermissions.ApproveDetainedVisit,
                    StringComparison.Ordinal
                ) && directPermissions.Contains(AppPermissions.ApproveVisits);
        }

        private static bool DelegationGrantsPermission(
            Delegation delegation,
            UserAccount delegator,
            string permission
        )
        {
            var grantedPermissions = GetGrantedPermissionKeys(delegation, delegator)
                .ToHashSet(PermissionComparer);
            if (grantedPermissions.Contains(permission))
            {
                return true;
            }

            return string.Equals(
                    permission,
                    AppPermissions.ApproveDetainedVisit,
                    StringComparison.Ordinal
                ) && grantedPermissions.Contains(AppPermissions.ApproveVisits);
        }

        private static void ApplyImplicitPermissions(ISet<string> permissions)
        {
            if (
                permissions.Contains(AppPermissions.CreateVisit)
                || permissions.Contains(AppPermissions.EditVisit)
                || permissions.Contains(AppPermissions.ApproveVisits)
                || permissions.Contains(AppPermissions.ApproveDetainedVisit)
            )
            {
                permissions.Add(AppPermissions.ViewVisits);
            }

            if (
                permissions.Contains(AppPermissions.CreatePermit)
                || permissions.Contains(AppPermissions.EditPermit)
                || permissions.Contains(AppPermissions.ApprovePermit)
                || permissions.Contains(AppPermissions.ApproveLeaveRequest)
                || permissions.Contains(AppPermissions.StopPermit)
                || permissions.Contains(AppPermissions.ReviewUnauthorizedExit)
            )
            {
                permissions.Add(AppPermissions.ViewPermits);
            }

            if (
                permissions.Contains(AppPermissions.CreateVisitorPermit)
                || permissions.Contains(AppPermissions.EditVisitorPermit)
            )
            {
                permissions.Add(AppPermissions.ViewVisitorPermits);
            }
        }

        private DelegationOperationResult? ValidateDefinition(
            ApplicationDbContext db,
            DelegationDefinitionInput input,
            int? excludeDelegationId
        )
        {
            var delegatorUsername = (input.DelegatorUsername ?? string.Empty).Trim();
            var delegateeUsername = (input.DelegateeUsername ?? string.Empty).Trim();
            if (
                string.IsNullOrWhiteSpace(delegatorUsername)
                || string.IsNullOrWhiteSpace(delegateeUsername)
            )
            {
                return DelegationOperationResult.Failure(
                    "delegation_users_required",
                    "يجب اختيار المفوِّض والمفوَّض إليه."
                );
            }

            if (
                string.Equals(
                    delegatorUsername,
                    delegateeUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return DelegationOperationResult.Failure(
                    "delegation_self_not_allowed",
                    "لا يمكن إنشاء تفويض لنفس المستخدم."
                );
            }

            if (input.EndAt <= input.StartAt)
            {
                return DelegationOperationResult.Failure(
                    "delegation_invalid_dates",
                    "يجب أن تكون نهاية التفويض بعد البداية."
                );
            }

            var delegator = db
                .UserAccounts.AsNoTracking()
                .FirstOrDefault(user => user.Username == delegatorUsername);
            var delegatee = db
                .UserAccounts.AsNoTracking()
                .FirstOrDefault(user => user.Username == delegateeUsername);
            if (
                delegator == null
                || !delegator.IsActive
                || delegatee == null
                || !delegatee.IsActive
            )
            {
                return DelegationOperationResult.Failure(
                    "delegation_user_not_found",
                    "المستخدم المختار غير موجود أو غير نشط."
                );
            }

            var reverseOverlapExists = db
                .Delegations.AsNoTracking()
                .Any(item =>
                    item.Id != excludeDelegationId
                    && item.Status != DelegationStatuses.Cancelled
                    && item.DelegatorUsername == delegateeUsername
                    && item.DelegateeUsername == delegatorUsername
                    && item.StartAt < input.EndAt
                    && item.EndAt > input.StartAt
                );
            if (reverseOverlapExists)
            {
                return DelegationOperationResult.Failure(
                    "delegation_circular_conflict",
                    "يوجد تفويض عكسي متداخل زمنيًا بين نفس المستخدمين."
                );
            }

            var normalizedScopeType = NormalizeScopeType(input.ScopeType);
            var directPermissions = GetDirectPermissions(delegator)
                .Where(permission => DelegateablePermissions.Contains(permission))
                .ToHashSet(PermissionComparer);
            if (normalizedScopeType == DelegationScopeTypes.Custom)
            {
                var selectedPermissions = NormalizePermissionKeys(input.PermissionKeys);
                if (selectedPermissions.Count == 0)
                {
                    return DelegationOperationResult.Failure(
                        "delegation_permissions_required",
                        "يجب اختيار صلاحية واحدة على الأقل في التفويض المخصص."
                    );
                }

                if (selectedPermissions.Any(permission => !directPermissions.Contains(permission)))
                {
                    return DelegationOperationResult.Failure(
                        "delegation_permission_not_owned",
                        "لا يمكن تفويض صلاحية لا يملكها المفوِّض مباشرة."
                    );
                }
            }

            return null;
        }

        private void RefreshStatuses(ApplicationDbContext db)
        {
            var now = _systemClock.LocalNow;
            var delegations = db
                .Delegations.Include(item => item.Permissions)
                .Where(item => item.Status != DelegationStatuses.Cancelled)
                .ToList();
            var hasChanges = false;

            foreach (var delegation in delegations)
            {
                var nextStatus = DetermineStatus(delegation.StartAt, delegation.EndAt, now);
                if (
                    string.Equals(delegation.Status, nextStatus, StringComparison.OrdinalIgnoreCase)
                )
                {
                    continue;
                }

                var beforeJson = BuildDelegationSnapshot(delegation);
                delegation.Status = nextStatus;
                delegation.UpdatedAt = now;
                delegation.LastUpdatedByUsername = delegation.LastUpdatedByUsername;
                if (nextStatus == DelegationStatuses.Active && !delegation.ActivatedAt.HasValue)
                {
                    delegation.ActivatedAt = now;
                }

                if (nextStatus == DelegationStatuses.Expired)
                {
                    RecordDelegationAudit(
                        db,
                        delegation,
                        "DelegationExpired",
                        "انتهاء تفويض",
                        $"انتهى التفويض {delegation.DelegationNumber} تلقائيًا.",
                        delegation.LastUpdatedByUsername,
                        beforeJson,
                        BuildDelegationSnapshot(delegation)
                    );
                }
                else if (nextStatus == DelegationStatuses.Active)
                {
                    RecordDelegationAudit(
                        db,
                        delegation,
                        "DelegationActivated",
                        "تفعيل تفويض",
                        $"تم تفعيل التفويض {delegation.DelegationNumber} تلقائيًا.",
                        delegation.LastUpdatedByUsername,
                        beforeJson,
                        BuildDelegationSnapshot(delegation)
                    );
                }

                hasChanges = true;
            }

            if (hasChanges)
            {
                db.SaveChanges();
            }
        }

        private void RecordDelegationAudit(
            ApplicationDbContext db,
            Delegation delegation,
            string actionType,
            string actionLabel,
            string message,
            string? recordedBy,
            string? beforeJson = null,
            string? afterJson = null
        )
        {
            db.AuditLogs.Add(
                new AuditLog
                {
                    Username = delegation.DelegateeUsername,
                    ActualActorUsername = delegation.DelegateeUsername,
                    ActionType = actionType,
                    ActionLabel = actionLabel,
                    EntityType = "Delegation",
                    EntityId = delegation.DelegationNumber,
                    Message = message,
                    Source = nameof(DelegationService),
                    RecordedBy = NormalizeAuditUsername(recordedBy, delegation.DelegatorUsername),
                    OccurredAt = _systemClock.UtcNow,
                    IpAddress = ResolveIpAddress(),
                    Success = true,
                    DelegatedFromUsername = delegation.DelegatorUsername,
                    DelegationId = delegation.Id,
                    BeforeJson = beforeJson ?? string.Empty,
                    AfterJson = afterJson ?? string.Empty,
                }
            );
        }

        private static string NormalizeScopeType(string? scopeType)
        {
            return string.Equals(
                scopeType,
                DelegationScopeTypes.Full,
                StringComparison.OrdinalIgnoreCase
            )
                ? DelegationScopeTypes.Full
                : DelegationScopeTypes.Custom;
        }

        private static string NormalizeTimeZoneId(string? timeZoneId)
        {
            return (timeZoneId ?? string.Empty).Trim();
        }

        private static IReadOnlyList<string> NormalizePermissionKeys(
            IEnumerable<string>? permissionKeys
        )
        {
            return (permissionKeys ?? Array.Empty<string>())
                .Select(permission => (permission ?? string.Empty).Trim())
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Where(permission => DelegateablePermissions.Contains(permission))
                .Distinct(PermissionComparer)
                .ToList();
        }

        private static string DetermineStatus(DateTime startAt, DateTime endAt, DateTime now)
        {
            if (now < startAt)
            {
                return DelegationStatuses.Scheduled;
            }

            if (now > endAt)
            {
                return DelegationStatuses.Expired;
            }

            return DelegationStatuses.Active;
        }

        private static string NormalizeAuditUsername(string? username, string fallback)
        {
            return string.IsNullOrWhiteSpace(username) ? fallback : username.Trim();
        }

        private static string BuildDelegationSnapshot(Delegation delegation)
        {
            return JsonSerializer.Serialize(
                new
                {
                    delegation.Id,
                    delegation.DelegationNumber,
                    delegation.DelegatorUsername,
                    delegation.DelegateeUsername,
                    delegation.ScopeType,
                    delegation.StartAt,
                    delegation.EndAt,
                    delegation.TimeZoneId,
                    delegation.Status,
                    delegation.Notes,
                    delegation.CreatedByUsername,
                    delegation.CreatedAt,
                    delegation.UpdatedAt,
                    delegation.LastUpdatedByUsername,
                    delegation.ActivatedAt,
                    delegation.CancelledAt,
                    delegation.CancelledByUsername,
                    delegation.CancelReason,
                    PermissionKeys = delegation
                        .Permissions.Select(item => item.PermissionKey)
                        .OrderBy(item => item)
                        .ToList(),
                }
            );
        }

        private Delegation CloneDelegation(ApplicationDbContext db, int id)
        {
            return db
                .Delegations.AsNoTracking()
                .Include(item => item.Permissions)
                .First(item => item.Id == id);
        }

        private string ResolveIpAddress()
        {
            return _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
                ?? string.Empty;
        }
    }
}
