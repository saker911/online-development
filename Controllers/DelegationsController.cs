using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

namespace VehiclePermitSystemWeb.Controllers
{
    [Authorize(Policy = AppPolicies.ManageDelegations)]
    public class DelegationsController : Controller
    {
        private readonly IDelegationService _delegationService;
        private readonly IUserAdminService _userAdminService;

        public DelegationsController(
            IDelegationService delegationService,
            IUserAdminService userAdminService
        )
        {
            _delegationService = delegationService;
            _userAdminService = userAdminService;
        }

        [HttpGet]
        public IActionResult Index(
            string? status = null,
            string? delegatorUsername = null,
            string? delegateeUsername = null
        )
        {
            return View(
                BuildManagementViewModel(
                    status: status,
                    delegatorUsername: delegatorUsername,
                    delegateeUsername: delegateeUsername
                )
            );
        }

        [HttpPost]
        public IActionResult Create([Bind(Prefix = "Editor")] DelegationEditViewModel model)
        {
            if (!TryValidateEditor(model))
            {
                return View(
                    nameof(Index),
                    BuildManagementViewModel(
                        model: model,
                        status: string.Empty,
                        delegatorUsername: string.Empty,
                        delegateeUsername: string.Empty
                    )
                );
            }

            var result = _delegationService.CreateDelegation(
                ToDefinitionInput(model),
                User.Identity?.Name
            );
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                return View(
                    nameof(Index),
                    BuildManagementViewModel(
                        model: model,
                        status: string.Empty,
                        delegatorUsername: string.Empty,
                        delegateeUsername: string.Empty
                    )
                );
            }

            this.ToastSuccess("تم إنشاء التفويض بنجاح.");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var delegation = _delegationService.GetDelegation(id);
            if (delegation == null)
            {
                return NotFound();
            }

            return View(BuildManagementViewModel(model: MapToEditor(delegation)));
        }

        [HttpPost]
        public IActionResult Edit(int id, [Bind(Prefix = "Editor")] DelegationEditViewModel model)
        {
            model.Id = id;
            model.IsEditMode = true;
            if (!TryValidateEditor(model))
            {
                return View(BuildManagementViewModel(model: model));
            }

            var result = _delegationService.UpdateDelegation(
                id,
                ToDefinitionInput(model),
                User.Identity?.Name
            );
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, result.Message);
                return View(BuildManagementViewModel(model: model));
            }

            this.ToastSuccess("تم تحديث التفويض بنجاح.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Cancel(int id, string? reason = null)
        {
            if (!_delegationService.CancelDelegation(id, User.Identity?.Name, reason))
            {
                this.ToastError("تعذر إلغاء التفويض المطلوب.");
                return RedirectToAction(nameof(Index));
            }

            this.ToastSuccess("تم إلغاء التفويض.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Extend(int id, DateTime newEndAt)
        {
            if (!_delegationService.ExtendDelegation(id, newEndAt, User.Identity?.Name))
            {
                this.ToastError("تعذر تمديد التفويض. تحقق من التاريخ الجديد.");
                return RedirectToAction(nameof(Index));
            }

            this.ToastSuccess("تم تمديد التفويض بنجاح.");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public IActionResult Audit(
            string? actualActorUsername = null,
            string? delegatedFromUsername = null,
            DateTime? fromDate = null,
            DateTime? toDate = null
        )
        {
            var users = GetVisibleUsers().OrderBy(user => user.DisplayName).ToList();
            var entries = _delegationService
                .GetDelegatedActionAuditLogs(
                    actualActorUsername,
                    delegatedFromUsername,
                    fromDate,
                    toDate
                )
                .ToList();
            entries = FilterProtectedEntries(entries).ToList();

            return View(
                new DelegatedActionReportViewModel
                {
                    Entries = entries,
                    AvailableUsers = users,
                    UserDisplayNames = users.ToDictionary(
                        user => user.Username,
                        user =>
                            string.IsNullOrWhiteSpace(user.DisplayName)
                                ? user.Username
                                : user.DisplayName,
                        StringComparer.OrdinalIgnoreCase
                    ),
                    ActualActorUsername = actualActorUsername ?? string.Empty,
                    DelegatedFromUsername = delegatedFromUsername ?? string.Empty,
                    FromDate = fromDate,
                    ToDate = toDate,
                }
            );
        }

        private DelegationManagementViewModel BuildManagementViewModel(
            DelegationEditViewModel? model = null,
            string? status = null,
            string? delegatorUsername = null,
            string? delegateeUsername = null
        )
        {
            var users = GetVisibleUsers().OrderBy(user => user.DisplayName).ToList();
            var delegations = _delegationService
                .GetDelegations(delegatorUsername, delegateeUsername, status)
                .ToList();

            return new DelegationManagementViewModel
            {
                Editor = model ?? new DelegationEditViewModel(),
                Delegations = delegations,
                AvailableUsers = users,
                DelegatorPermissionKeys = users.ToDictionary(
                    user => user.Username,
                    user =>
                        AppPermissions
                            .GetGrantedPermissions(user)
                            .OrderBy(permission => permission)
                            .ToList(),
                    StringComparer.OrdinalIgnoreCase
                ),
                UserDisplayNames = users.ToDictionary(
                    user => user.Username,
                    user =>
                        string.IsNullOrWhiteSpace(user.DisplayName)
                            ? user.Username
                            : user.DisplayName,
                    StringComparer.OrdinalIgnoreCase
                ),
                StatusCounts = delegations
                    .GroupBy(item => item.Status)
                    .OrderBy(group => group.Key)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Count(),
                        StringComparer.OrdinalIgnoreCase
                    ),
                StatusFilter = status ?? string.Empty,
                DelegatorFilter = delegatorUsername ?? string.Empty,
                DelegateeFilter = delegateeUsername ?? string.Empty,
            };
        }

        private IEnumerable<UserAccount> GetVisibleUsers()
        {
            var users = _userAdminService.GetAllUsers(User.IsSuperAdmin());
            if (User.IsSuperAdmin())
            {
                return users;
            }

            users = users.Where(user => !user.IsSuperAdmin);
            if (User.IsInRole(AppRoles.GeneralManager))
            {
                return users;
            }

            var currentUser = _userAdminService.GetUserAccount(User.Identity?.Name ?? string.Empty);
            return currentUser == null
                ? Enumerable.Empty<UserAccount>()
                : users.Where(user => user.WorkplaceSiteId == currentUser.WorkplaceSiteId);
        }

        private IEnumerable<AuditLog> FilterProtectedEntries(IEnumerable<AuditLog> entries)
        {
            if (User.IsSuperAdmin())
            {
                return entries;
            }

            var protectedUsernames = _userAdminService
                .GetAllUsers(User.IsSuperAdmin())
                .Where(user => user.IsSuperAdmin)
                .Select(user => user.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return entries.Where(entry =>
                !protectedUsernames.Contains(entry.Username)
                && !protectedUsernames.Contains(entry.RecordedBy)
                && !protectedUsernames.Contains(entry.ActualActorUsername)
                && !protectedUsernames.Contains(entry.DelegatedFromUsername)
            );
        }

        private bool TryValidateEditor(DelegationEditViewModel model)
        {
            const string delegatorFieldKey = "Editor.DelegatorUsername";
            const string delegateeFieldKey = "Editor.DelegateeUsername";
            const string endAtFieldKey = "Editor.EndAt";
            const string permissionsFieldKey = "Editor.SelectedPermissions";

            model.DelegatorUsername = (model.DelegatorUsername ?? string.Empty).Trim();
            model.DelegateeUsername = (model.DelegateeUsername ?? string.Empty).Trim();
            model.Notes = (model.Notes ?? string.Empty).Trim();
            model.TimeZoneId = (model.TimeZoneId ?? string.Empty).Trim();
            model.SelectedPermissions =
                model
                    .SelectedPermissions?.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
                ?? new List<string>();

            if (string.IsNullOrWhiteSpace(model.DelegatorUsername))
            {
                ModelState.AddModelError(delegatorFieldKey, "اختر المفوِّض.");
            }

            if (string.IsNullOrWhiteSpace(model.DelegateeUsername))
            {
                ModelState.AddModelError(delegateeFieldKey, "اختر المفوَّض إليه.");
            }

            var visibleUsernames = GetVisibleUsers()
                .Select(user => user.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (
                !string.IsNullOrWhiteSpace(model.DelegatorUsername)
                && !visibleUsernames.Contains(model.DelegatorUsername)
            )
            {
                ModelState.AddModelError(
                    delegatorFieldKey,
                    "المفوِّض لا يتبع موقعك التشغيلي."
                );
            }
            if (
                !string.IsNullOrWhiteSpace(model.DelegateeUsername)
                && !visibleUsernames.Contains(model.DelegateeUsername)
            )
            {
                ModelState.AddModelError(
                    delegateeFieldKey,
                    "المفوَّض إليه لا يتبع موقعك التشغيلي."
                );
            }

            if (
                !string.IsNullOrWhiteSpace(model.DelegatorUsername)
                && !string.IsNullOrWhiteSpace(model.DelegateeUsername)
                && string.Equals(
                    model.DelegatorUsername,
                    model.DelegateeUsername,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                ModelState.AddModelError(
                    delegateeFieldKey,
                    "لا يمكن اختيار نفس المستخدم في خانتي المفوِّض والمفوَّض إليه."
                );
            }

            if (model.EndAt <= model.StartAt)
            {
                ModelState.AddModelError(endAtFieldKey, "يجب أن يكون تاريخ النهاية بعد البداية.");
            }

            if (
                string.Equals(
                    model.ScopeType,
                    DelegationScopeTypes.Custom,
                    StringComparison.OrdinalIgnoreCase
                )
                && model.SelectedPermissions.Count == 0
            )
            {
                ModelState.AddModelError(
                    permissionsFieldKey,
                    "اختر صلاحية واحدة على الأقل في التفويض المخصص."
                );
            }

            return ModelState.IsValid;
        }

        private static DelegationDefinitionInput ToDefinitionInput(DelegationEditViewModel model)
        {
            return new DelegationDefinitionInput
            {
                DelegatorUsername = model.DelegatorUsername,
                DelegateeUsername = model.DelegateeUsername,
                ScopeType = model.ScopeType,
                PermissionKeys = model.SelectedPermissions,
                StartAt = model.StartAt,
                EndAt = model.EndAt,
                TimeZoneId = model.TimeZoneId,
                Notes = model.Notes,
            };
        }

        private static DelegationEditViewModel MapToEditor(Delegation delegation)
        {
            return new DelegationEditViewModel
            {
                Id = delegation.Id,
                IsEditMode = true,
                DelegatorUsername = delegation.DelegatorUsername,
                DelegateeUsername = delegation.DelegateeUsername,
                ScopeType = delegation.ScopeType,
                SelectedPermissions = delegation
                    .Permissions.Select(item => item.PermissionKey)
                    .OrderBy(item => item)
                    .ToList(),
                StartAt = delegation.StartAt,
                EndAt = delegation.EndAt,
                TimeZoneId = delegation.TimeZoneId,
                Notes = delegation.Notes,
            };
        }
    }
}
