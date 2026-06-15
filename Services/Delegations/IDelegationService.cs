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

namespace VehiclePermitSystemWeb.Services.Delegations
{
    public sealed class DelegationExecutionContext
    {
        public bool IsAllowed { get; init; }
        public bool IsDelegated { get; init; }
        public string MatchedPermission { get; init; } = string.Empty;
        public Delegation? Delegation { get; init; }
        public UserAccount? Delegator { get; init; }

        public static DelegationExecutionContext Denied { get; } = new();
    }

    public sealed class DelegationDefinitionInput
    {
        public string DelegatorUsername { get; set; } = string.Empty;
        public string DelegateeUsername { get; set; } = string.Empty;
        public string ScopeType { get; set; } = DelegationScopeTypes.Custom;
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public string TimeZoneId { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public IReadOnlyCollection<string> PermissionKeys { get; set; } = Array.Empty<string>();
    }

    public sealed class DelegationOperationResult
    {
        public bool Succeeded { get; init; }
        public string ErrorCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
        public Delegation? Delegation { get; init; }

        public static DelegationOperationResult Success(Delegation delegation) =>
            new() { Succeeded = true, Delegation = delegation };

        public static DelegationOperationResult Failure(string errorCode, string message) =>
            new()
            {
                Succeeded = false,
                ErrorCode = errorCode,
                Message = message,
            };
    }

    public interface IDelegationService
    {
        IReadOnlyCollection<string> GetEffectivePermissions(UserAccount? user);
        bool HasEffectivePermission(UserAccount? user, string permission);
        DelegationExecutionContext ResolveExecutionContext(
            UserAccount? actor,
            IEnumerable<string> permissions,
            Func<UserAccount, bool> directAccessEvaluator
        );
        IEnumerable<Delegation> GetDelegations(
            string? delegatorUsername = null,
            string? delegateeUsername = null,
            string? status = null
        );
        Delegation? GetDelegation(int id);
        DelegationOperationResult CreateDelegation(
            DelegationDefinitionInput input,
            string? createdBy = null
        );
        DelegationOperationResult UpdateDelegation(
            int id,
            DelegationDefinitionInput input,
            string? updatedBy = null
        );
        bool CancelDelegation(int id, string? cancelledBy = null, string? reason = null);
        bool ExtendDelegation(int id, DateTime newEndAt, string? updatedBy = null);
        IEnumerable<AuditLog> GetDelegatedActionAuditLogs(
            string? actualActorUsername = null,
            string? delegatedFromUsername = null,
            DateTime? fromDate = null,
            DateTime? toDate = null
        );
    }
}
